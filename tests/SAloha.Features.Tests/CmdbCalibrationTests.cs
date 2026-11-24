using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using SAloha.Features.AssistedCmdb;
using SAloha.Features.Host;
using Xunit.Abstractions;

namespace SAloha.Features.Tests;

/// <summary>
/// CMA-11 : jeu d'essai de la CMDB assistée (<c>demo/donnees/cmdb-assistee.json</c>). Toujours : le jeu est cohérent avec la
/// présélection du conteneur (doublons et pièges présélectionnés, relations citées, criticités à proposer). Sur demande, la
/// mesure : le vrai conteneur, une fausse API qui sert le jeu, et un vrai modèle de langage, désigné par l'environnement :
/// <code>
/// CMA_CALIBRATION_URL=http://localhost:11434/v1 CMA_CALIBRATION_MODEL=qwen2.5:7b \
///   dotnet test tests/SAloha.Features.Tests --filter CmdbCalibration --logger "console;verbosity=detailed"
/// </code>
/// <c>CMA_CALIBRATION_TIMEOUT</c> (secondes par appel, 300), <c>CMA_CALIBRATION_REPORT</c> (fichier Markdown du compte rendu).
/// </summary>
public class CmdbCalibrationTests(ITestOutputHelper output)
{
    private static readonly string[] Tasks =
        [CmdbTasks.Rename, CmdbTasks.Normalize, CmdbTasks.Reclassify, Duplicates.Task, Relations.Task, CmdbTasks.Criticality];

    /// <summary>Le jeu, cherché depuis le dossier du test jusqu'à la racine du dépôt.</summary>
    private static JsonElement Data()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "demo", "donnees", "cmdb-assistee.json");
            if (File.Exists(path)) return JsonDocument.Parse(File.ReadAllText(path)).RootElement;
        }
        throw new FileNotFoundException("demo/donnees/cmdb-assistee.json introuvable.");
    }

    private static string? S(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static List<CiRecord> Items(JsonElement data) => data.GetProperty("ci").EnumerateArray().Select(c => new CiRecord(
        c.GetProperty("id").GetInt32(), 7, $"CI-2026-{c.GetProperty("id").GetInt32():0000}", S(c, "title"), S(c, "description"), "En service",
        S(c, "ciType"), S(c, "environment"), S(c, "operatingSystem"), S(c, "vendor"), S(c, "model"), null, null, null, null, [], [],
        S(c, "criticality"))).ToList();

    private static HashSet<string> Strings(JsonElement list) => list.EnumerateArray().Select(v => v.GetString()!.Trim().ToLowerInvariant()).ToHashSet();
    private static (int, int) Pair(int a, int b) => a < b ? (a, b) : (b, a);
    private static HashSet<(int, int)> Pairs(JsonElement list) =>
        list.EnumerateArray().Select(p => Pair(p[0].GetInt32(), p[1].GetInt32())).ToHashSet();

    [Fact]
    public void CMA_11_le_jeu_d_essai_compte_50_CI_et_ses_reponses_attendues_suivent_la_preselection()
    {
        var data = Data();
        var items = Items(data);
        var byId = items.ToDictionary(c => c.Id);
        Assert.True(items.Count >= 50, $"{items.Count} CI : 50 au moins (CMA-11).");
        Assert.Equal(items.Count, byId.Count);
        var expected = data.GetProperty("attendu");
        var named = data.GetProperty("conventions").EnumerateArray().Select(n => n.GetProperty("ciType").GetString()).ToHashSet();

        // Renommer ne vise que les types qui ont une convention ; Normaliser, ses trois champs ; Reclasser, un type de la liste.
        foreach (var r in expected.GetProperty("rename").EnumerateObject()) Assert.Contains(byId[int.Parse(r.Name)].CiType, named);
        foreach (var n in expected.GetProperty("normalize").EnumerateObject())
            foreach (var f in n.Value.EnumerateObject())
            {
                Assert.Contains(f.Name, CmdbTasks.NormalizedFields);
                Assert.True(byId.ContainsKey(int.Parse(n.Name)));
            }
        foreach (var r in expected.GetProperty("reclassify").EnumerateObject())
            Assert.All(r.Value.EnumerateArray(), t => Assert.Contains(t.GetString(), CmdbTasks.CiTypes));

        // Fusionner : chaque doublon attendu, et chaque piège, est une paire que le conteneur soumet (CMA-06).
        var candidates = Duplicates.Candidates(items, new HashSet<(int, int)>()).Select(p => Pair(p.A, p.B)).ToHashSet();
        var merges = Pairs(expected.GetProperty("merge"));
        var traps = Pairs(expected.GetProperty("pieges"));
        Assert.Empty(merges.Where(p => !candidates.Contains(p)));
        Assert.Empty(traps.Where(p => !candidates.Contains(p)));
        Assert.Empty(merges.Intersect(traps));

        // Relier : chaque relation attendue part d'un CI dont la description cite la cible.
        var subjects = Relations.Subjects(items).ToDictionary(s => s.Source.Id, s => s.Targets.Select(t => t.Id).ToHashSet());
        foreach (var r in expected.GetProperty("relate").EnumerateArray())
        {
            var (source, target) = (r.GetProperty("source").GetInt32(), r.GetProperty("cible").GetInt32());
            Assert.True(subjects.TryGetValue(source, out var cited) && cited.Contains(target), $"{source} ne cite pas {target}.");
            Assert.All(r.GetProperty("types").EnumerateArray(), t => Assert.Contains(t.GetString(), Relations.Types));
            if (r.TryGetProperty("inverse", out var inverse)) Assert.All(inverse.EnumerateArray(), t => Assert.Contains(t.GetString(), Relations.Types));
        }

        // Criticité : exactement les applications sans criticité, chacune avec ses réponses admises.
        var critical = expected.GetProperty("criticality").EnumerateObject().ToDictionary(c => int.Parse(c.Name), c => c.Value);
        Assert.Equal(items.Where(CmdbTasks.NeedsCriticality).Select(c => c.Id).Order(), critical.Keys.Order());
        Assert.All(critical.Values, v => Assert.All(v.EnumerateArray(), c => Assert.Contains(c.GetString(), CmdbTasks.Criticalities)));
    }

    /// <summary>Une tâche mesurée : propositions faites et justes (précision), réponses attendues et trouvées (rappel).</summary>
    public record TaskScore(string Task, int Proposed, int Right, int Expected, int Found, int Calls, int NonConforming, double AverageSeconds,
        List<string>? Wrong = null)
    {
        public double Precision => Proposed == 0 ? 0 : (double)Right / Proposed;
        public double Recall => Expected == 0 ? 0 : (double)Found / Expected;
    }

    /// <summary>Note les propositions déposées par le conteneur au regard des réponses attendues du jeu.</summary>
    public static List<TaskScore> Score(JsonElement expected, IReadOnlyList<JsonElement> proposals, IReadOnlyList<JsonElement> calls)
    {
        static string Norm(string? v) => (v ?? "").Trim().ToLowerInvariant();
        var scores = new List<TaskScore>();
        foreach (var task in Tasks)
        {
            int proposed = 0, right = 0, expectedCount;
            var found = new HashSet<string>();
            var wrong = new List<string>();
            string[] fields = task switch
            {
                CmdbTasks.Rename => ["title"], CmdbTasks.Normalize => CmdbTasks.NormalizedFields, CmdbTasks.Reclassify => ["ciType"],
                CmdbTasks.Criticality => ["criticality"], Duplicates.Task => ["merge"], _ => ["relation"],
            };
            foreach (var p in proposals.Where(p => fields.Contains(p.GetProperty("field").GetString())))
            {
                proposed++;
                var (ci, field, after) = (p.GetProperty("targetId").GetInt32(), p.GetProperty("field").GetString()!, p.GetProperty("after").GetString()!);
                string? key = null;
                switch (task)
                {
                    case Duplicates.Task:
                        var pair = Pair(ci, int.Parse(after));
                        if (Pairs(expected.GetProperty("merge")).Contains(pair)) key = $"{pair}";
                        break;
                    case Relations.Task:
                        // La relation attendue, ou la même vue de la cible avec un type admis (« inverse »).
                        var (target, type) = (int.Parse(after.Split('|')[0]), after.Split('|')[1]);
                        static bool Has(JsonElement r, string name, string type) =>
                            r.TryGetProperty(name, out var list) && list.EnumerateArray().Any(t => t.GetString() == type);
                        var match = expected.GetProperty("relate").EnumerateArray().FirstOrDefault(r =>
                            (r.GetProperty("source").GetInt32() == ci && r.GetProperty("cible").GetInt32() == target && Has(r, "types", type))
                            || (r.GetProperty("source").GetInt32() == target && r.GetProperty("cible").GetInt32() == ci && Has(r, "inverse", type)));
                        if (match.ValueKind == JsonValueKind.Object) key = $"{match.GetProperty("source").GetInt32()}>{match.GetProperty("cible").GetInt32()}";
                        break;
                    case CmdbTasks.Normalize:
                        if (expected.GetProperty("normalize").TryGetProperty($"{ci}", out var byField) && byField.TryGetProperty(field, out var admitted)
                            && Strings(admitted).Contains(Norm(after)))
                            key = $"{ci}.{field}";
                        break;
                    default:
                        if (expected.GetProperty(task).TryGetProperty($"{ci}", out var values) && Strings(values).Contains(Norm(after))) key = $"{ci}";
                        break;
                }
                if (key is null)
                {
                    wrong.Add($"{ci} {field} → {after} ({(p.TryGetProperty("reasons", out var r) ? r.GetString() : "")})");
                    continue;
                }
                right++;
                found.Add(key);
            }
            expectedCount = task switch
            {
                Duplicates.Task => expected.GetProperty("merge").GetArrayLength(),
                Relations.Task => expected.GetProperty("relate").GetArrayLength(),
                CmdbTasks.Normalize => expected.GetProperty("normalize").EnumerateObject().Sum(c => c.Value.EnumerateObject().Count()),
                _ => expected.GetProperty(task).EnumerateObject().Count(),
            };
            var traced = calls.Where(c => c.GetProperty("task").GetString() == task).ToList();
            scores.Add(new TaskScore(task, proposed, right, expectedCount, found.Count, traced.Count, traced.Sum(c => c.GetProperty("rejected").GetInt32()),
                traced.Count == 0 ? 0 : traced.Average(c => c.GetProperty("durationMs").GetInt32()) / 1000.0, wrong));
        }
        return scores;
    }

    [Fact]
    public void CMA_11_la_notation_compte_juste_ce_qui_est_admis_et_faux_le_reste()
    {
        JsonElement P(int ci, string field, string after) =>
            JsonSerializer.SerializeToElement(new { targetType = "ci", targetId = ci, field, after }, JsonSerializerOptions.Web);
        JsonElement C(string task, int rejected, int ms) => JsonSerializer.SerializeToElement(new { task, rejected, durationMs = ms }, JsonSerializerOptions.Web);
        var scores = Score(Data().GetProperty("attendu"),
        [
            P(2, "title", "srv-paie-01"), P(1, "title", "SRV-PAIE-001"),                       // admise (casse ignorée) ; CI déjà conforme
            P(2, "operatingSystem", "Windows Server 2019 Standard"), P(6, "vendor", "Dell Technologies"),
            P(2, "merge", "1"), P(25, "merge", "19"),                                          // doublon ; piège
            P(19, "relation", "15|Dépend de"), P(12, "relation", "1|Dépend de"),               // type admis ; type non admis
            P(3, "relation", "22|Héberge"), P(15, "relation", "19|Dépend de"),                 // même relation vue de la cible ; sens faux
            P(23, "criticality", "Mineure"),
        ], [C("rename", 1, 2000), C("rename", 0, 4000)]);
        var by = scores.ToDictionary(s => s.Task);
        Assert.Equal((2, 1, 12, 1), (by["rename"].Proposed, by["rename"].Right, by["rename"].Expected, by["rename"].Found));
        Assert.Equal((2, 1, 1, 3.0), (by["rename"].Calls, by["rename"].NonConforming, by["normalize"].Right, by["rename"].AverageSeconds));
        Assert.Equal((2, 1, 0.5), (by["merge"].Proposed, by["merge"].Right, by["merge"].Precision));
        Assert.Equal((4, 2, 17, 2), (by["relate"].Proposed, by["relate"].Right, by["relate"].Expected, by["relate"].Found));
        Assert.Equal((1, 1.0), (by["criticality"].Right, by["criticality"].Precision));
        Assert.Equal(0, by["reclassify"].Proposed);
    }

    [Fact]
    public async Task CMA_11_mesure_de_chaque_tache_sur_un_modele_reel()
    {
        var url = Environment.GetEnvironmentVariable("CMA_CALIBRATION_URL");
        if (string.IsNullOrWhiteSpace(url))
        {
            output.WriteLine("CMA_CALIBRATION_URL absente : mesure non faite (voir la documentation de la classe).");
            return;
        }
        var data = Data();
        var api = new FakeCmdbApi();
        await api.InitializeAsync();
        try
        {
            await MeasureAsync(api, data, url);
        }
        finally
        {
            await api.DisposeAsync();
        }
    }

    private async Task MeasureAsync(FakeCmdbApi api, JsonElement data, string url)
    {
        api.ModelUrl = url.TrimEnd('/');
        api.ModelName = Environment.GetEnvironmentVariable("CMA_CALIBRATION_MODEL") ?? "qwen2.5:7b";
        api.ModelTimeoutSeconds = int.TryParse(Environment.GetEnvironmentVariable("CMA_CALIBRATION_TIMEOUT"), out var t) ? t : 300;
        api.Items = [.. Items(data).Select(c => (object)c)];
        api.Naming = [.. data.GetProperty("conventions").EnumerateArray().Select(n => (object)n)];
        api.Analyses[1] = new { id = 1, perimeterId = 7, tasks = Tasks, configurationItemIds = (int[]?)null, status = "En file", itemCount = api.Items.Length };
        api.Events.Enqueue(new { id = 1L, type = "analysis.requested", entityType = "cmdb-analysis", entityId = 1, perimeterId = 7, at = DateTime.UtcNow });

        var clock = Stopwatch.StartNew();
        using var container = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Features:Token", FakeInternalApi.Token);
            b.UseSetting("Api:Url", api.Url);
            b.UseSetting("Events:PollMilliseconds", "200");
        });
        container.CreateClient();
        JsonElement report = default;
        while (clock.Elapsed < TimeSpan.FromHours(3))
        {
            report = api.Reports.Where(r => r.Id == 1).Select(r => r.Report).LastOrDefault();
            if (report.ValueKind == JsonValueKind.Object && report.GetProperty("status").GetString() is "Terminée" or "En échec") break;
            await Task.Delay(1000);
        }
        clock.Stop();
        Assert.True(report.ValueKind == JsonValueKind.Object, "Aucun compte rendu de l'analyse.");
        Assert.Equal("Terminée", report.GetProperty("status").GetString());

        var scores = Score(data.GetProperty("attendu"), [.. api.Proposals], [.. api.Calls]);
        // Mémoire du modèle chargé, quand le serveur est Ollama (GET /api/ps).
        string memory = "non mesurée";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var ps = await http.GetFromJsonAsync<JsonElement>(url.TrimEnd('/')[..^3] + "/api/ps");
            if (ps.GetProperty("models").EnumerateArray().FirstOrDefault() is { ValueKind: JsonValueKind.Object } m)
                memory = $"{m.GetProperty("size").GetInt64() / 1e9:0.0} Go (dont {m.GetProperty("size_vram").GetInt64() / 1e9:0.0} Go en mémoire vidéo)";
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or KeyNotFoundException or InvalidOperationException)
        {
        }

        var fr = CultureInfo.GetCultureInfo("fr-FR");
        var md = new StringBuilder();
        md.AppendLine($"Modèle `{api.ModelName}`, {api.Items.Length} CI, analyse de {clock.Elapsed.TotalMinutes.ToString("0.0", fr)} min, mémoire du modèle : {memory}.");
        md.AppendLine();
        md.AppendLine("| Tâche | Proposées | Justes | Précision | Attendues | Trouvées | Rappel | Appels | Réponses écartées | Appel moyen |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        foreach (var s in scores)
            md.AppendLine($"| {s.Task} | {s.Proposed} | {s.Right} | {(s.Precision * 100).ToString("0", fr)} % | {s.Expected} | {s.Found} | "
                + $"{(s.Recall * 100).ToString("0", fr)} % | {s.Calls} | {s.NonConforming} | {s.AverageSeconds.ToString("0.0", fr)} s |");
        md.AppendLine();
        var low = string.Join(", ", scores.Where(s => s.Precision < 0.7).Select(s => s.Task));
        md.AppendLine($"Sous 70 % de précision (coupées par défaut, CMA-11) : {(low.Length > 0 ? low : "aucune")}.");
        md.AppendLine();
        md.AppendLine("Propositions hors des réponses admises :");
        foreach (var s in scores.Where(s => s.Wrong is { Count: > 0 }))
            foreach (var w in s.Wrong!) md.AppendLine($"- {s.Task} : {w}");
        output.WriteLine(md.ToString());
        if (Environment.GetEnvironmentVariable("CMA_CALIBRATION_REPORT") is { Length: > 0 } file) await File.WriteAllTextAsync(file, md.ToString());
    }
}
