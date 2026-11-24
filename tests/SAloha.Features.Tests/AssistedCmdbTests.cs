using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using SAloha.Features.AssistedCmdb;
using SAloha.Features.Host;
using SAloha.Features.LanguageModel;

namespace SAloha.Features.Tests;

/// <summary>CMA-07, CMA-09 : l'entrée d'un lot et la lecture des propositions, en fonctions pures.</summary>
public class CmdbTasksTests
{
    private static CiRecord Ci(int id, string title, string type = "Serveur", string? os = null, string? description = null, string? ip = null,
        string? serial = null, int perimeter = 7) =>
        new(id, perimeter, $"CI-{id}", title, description, "En service", type, "Production", os, null, null, serial, ip, null, "hery.rakoto",
            [new CiRelationRead(id, 3, "Héberge")], []);

    [Fact]
    public void CMA_07_seuls_les_champs_utiles_partent_et_les_valeurs_sensibles_sont_masquees()
    {
        var pseudo = new Pseudonymizer();
        var ci = Ci(1, "srvpaie1", os: "Win2019 std", description: "Paie, joignable en 10.1.2.3, série SN-7781-AX. " + new string('x', 1200),
            ip: "10.1.2.3", serial: "SN-7781-AX");
        var input = pseudo.Apply(CmdbTasks.Input([ci], new Dictionary<int, string> { [1] = "srvpaie1", [3] = "base-paie" }, pseudo));
        foreach (var secret in new[] { "10.1.2.3", "SN-7781-AX", "hery.rakoto" }) Assert.DoesNotContain(secret, input);
        var item = JsonDocument.Parse(input).RootElement[0];
        Assert.Equal(["id", "nom", "type", "environnement", "systeme", "constructeur", "modele", "description", "relations"],
            item.EnumerateObject().Select(p => p.Name));
        Assert.Equal("Héberge base-paie", item.GetProperty("relations")[0].GetString());
        Assert.True(item.GetProperty("description").GetString()!.Length <= 1000);
    }

    [Fact]
    public void CMA_09_une_proposition_hors_du_lot_vide_ou_sans_convention_est_ecartee_et_comptee()
    {
        var batch = new[] { Ci(1, "srvpaie1", os: "Win2019 std"), Ci(2, "base-paie", type: "Base de données") }.ToDictionary(c => c.Id);
        var normalize = JsonDocument.Parse("""
            { "propositions": [
              { "ci": 1, "champ": "operatingSystem", "valeur": "Windows Server 2019 Standard", "motif": "Écriture normalisée." },
              { "ci": 1, "champ": "operatingSystem", "valeur": "Windows 2019", "motif": "Second avis : ignoré." },
              { "ci": 2, "champ": "vendor", "valeur": "  ", "motif": "Vide : écarté." },
              { "ci": 99, "champ": "model", "valeur": "DL380", "motif": "Hors du lot : écarté." } ] }
            """).RootElement;
        var (kept, rejected) = CmdbTasks.Read(CmdbTasks.Normalize, normalize, batch, new HashSet<string>());
        Assert.Equal([new CmdbProposal(1, "operatingSystem", "Windows Server 2019 Standard", "Écriture normalisée.")], kept);
        Assert.Equal(2, rejected);

        // Renommer un type sans convention : écarté ; garder le même nom : ignoré.
        var rename = JsonDocument.Parse("""
            { "propositions": [ { "ci": 1, "nom": "SRV-PAIE-01", "motif": "Convention des serveurs." },
              { "ci": 2, "nom": "BDD-PAIE-01", "motif": "Pas de convention : écarté." }, { "ci": 1, "nom": "srvpaie1", "motif": "Inchangé." } ] }
            """).RootElement;
        (kept, rejected) = CmdbTasks.Read(CmdbTasks.Rename, rename, batch, new HashSet<string> { "Serveur" });
        Assert.Equal([("title", "SRV-PAIE-01")], kept.Select(k => (k.Field, k.Value)));
        Assert.Equal(1, rejected);
        // Le schéma borne les types et les champs (OPT-25) : un type hors liste n'atteint jamais la lecture.
        Assert.NotNull(SchemaCheck.Error(JsonDocument.Parse("""{ "propositions": [ { "ci": 1, "type": "Imprimante", "motif": "-" } ] }""").RootElement,
            CmdbTasks.ReclassifySchema));
    }
}

/// <summary>Port interne factice de l'API et modèle factice : une analyse, ses CI, les conventions, les propositions et les comptes rendus.</summary>
public sealed class FakeCmdbApi : IAsyncLifetime
{
    private WebApplication app = null!;
    public string Url { get; private set; } = "";
    public readonly ConcurrentQueue<object> Events = new();
    public readonly ConcurrentDictionary<int, object> Analyses = new();
    public readonly ConcurrentQueue<(int Id, JsonElement Report)> Reports = new();
    public readonly ConcurrentQueue<JsonElement> Proposals = new();
    public readonly ConcurrentQueue<JsonElement> ModelRequests = new();
    public volatile object[] Items = [];
    public volatile object[] Naming = [];
    /// <summary>Réponse du modèle selon la tâche (nom du schéma demandé) ; absente : panne (500).</summary>
    public readonly ConcurrentDictionary<string, string> Replies = new();

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0));
        app = builder.Build();
        app.MapGet("/internal/events", (long after) =>
        {
            var list = Events.Select(e => JsonSerializer.SerializeToElement(e, JsonSerializerOptions.Web)).Where(e => e.GetProperty("id").GetInt64() > after).ToList();
            return Results.Json(new { events = list, next = list.Count > 0 ? list[^1].GetProperty("id").GetInt64() : after, gap = false });
        });
        app.MapGet("/internal/assisted-cmdb/analyses/{id:int}", (int id) => Analyses.TryGetValue(id, out var a) ? Results.Json(a) : Results.NotFound());
        app.MapPut("/internal/assisted-cmdb/analyses/{id:int}", async (int id, HttpContext c) =>
        {
            Reports.Enqueue((id, await JsonSerializer.DeserializeAsync<JsonElement>(c.Request.Body)));
            return Results.Json(new { id });
        });
        app.MapGet("/internal/configuration-items", (int? perimeter, int? after, string? ids) =>
        {
            var all = Items.Select(i => JsonSerializer.SerializeToElement(i, JsonSerializerOptions.Web)).ToList();
            if (ids is not null)
            {
                var wanted = ids.Split(',').Select(int.Parse).ToHashSet();
                return Results.Json(all.Where(i => wanted.Contains(i.GetProperty("id").GetInt32())));
            }
            return Results.Json(all.Where(i => i.GetProperty("id").GetInt32() > (after ?? 0)));
        });
        app.MapGet("/internal/assisted-cmdb/naming", () => Results.Json(Naming));
        app.MapPost("/internal/proposals", async (HttpContext c) =>
        {
            Proposals.Enqueue(await JsonSerializer.DeserializeAsync<JsonElement>(c.Request.Body));
            return Results.Json(new { id = Proposals.Count, created = true }, statusCode: 201);
        });
        app.MapGet("/internal/language-model", () => Results.Json(new
        {
            enabled = true, location = "Dans le SI", url = Url + "/v1", model = "qwen2.5:7b", key = (string?)null, timeoutSeconds = 30, maxResponseKb = 64,
        }));
        app.MapPost("/internal/language-model/calls", () => Results.Json(new { id = 1 }, statusCode: 201));
        app.MapPost("/v1/chat/completions", async (HttpContext c) =>
        {
            var body = await JsonSerializer.DeserializeAsync<JsonElement>(c.Request.Body);
            ModelRequests.Enqueue(body);
            var task = body.GetProperty("response_format").GetProperty("json_schema").GetProperty("name").GetString()!;
            return Replies.TryGetValue(task, out var content)
                ? Results.Json(new { choices = new[] { new { message = new { role = "assistant", content } } } })
                : Results.StatusCode(500);
        });
        await app.StartAsync();
        Url = app.Urls.Single();
    }

    public async Task DisposeAsync() => await app.DisposeAsync();
}

/// <summary>CMDB assistée de bout en bout dans le conteneur : une analyse, Renommer, Normaliser, Reclasser (CMA-01, CMA-02, CMA-07, CMA-09).</summary>
public class AssistedCmdbTests(FakeCmdbApi api) : IClassFixture<FakeCmdbApi>
{
    private static long eventId;

    private WebApplicationFactory<Program> Container() => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
    {
        b.UseSetting("Features:Token", FakeInternalApi.Token);
        b.UseSetting("Api:Url", api.Url);
        b.UseSetting("Events:PollMilliseconds", "50");
        b.UseSetting("Events:IdleMilliseconds", "100");
    });

    private static object Item(int id, string title, string type, string? os = null, string? description = null, string? ip = null, string? serial = null,
        int perimeter = 7) => new
    {
        id, perimeterId = perimeter, reference = $"CI-{id}", title, description, status = "En service", ciType = type, environment = "Production",
        operatingSystem = os, vendor = (string?)null, model = (string?)null, serialNumber = serial, ipAddress = ip, macAddress = (string?)null,
        ownerId = "hery.rakoto", outgoing = Array.Empty<object>(), incoming = Array.Empty<object>(),
    };

    private void Analysis(int id, string[] tasks, string status = "En file", int perimeter = 7)
    {
        api.Analyses[id] = new { id, perimeterId = perimeter, tasks, configurationItemIds = (int[]?)null, status, itemCount = 3 };
        api.Events.Enqueue(new { id = Interlocked.Increment(ref eventId), type = "analysis.requested", entityType = "cmdb-analysis", entityId = id,
            perimeterId = perimeter, at = DateTime.UtcNow });
    }

    private async Task<JsonElement?> FinalReport(int id)
    {
        for (var i = 0; i < 200; i++)
        {
            var last = api.Reports.Where(r => r.Id == id).Select(r => r.Report).LastOrDefault();
            if (last.ValueKind == JsonValueKind.Object && last.GetProperty("status").GetString() is "Terminée" or "En échec") return last;
            await Task.Delay(50);
        }
        return null;
    }

    [Fact]
    public async Task CMA_02_CMA_07_CMA_09_une_analyse_propose_renommer_normaliser_reclasser_sans_rien_envoyer_de_sensible()
    {
        api.Items =
        [
            Item(1, "srvpaie1", "Serveur", os: "Win2019 std", description: "Paie, joignable en 10.1.2.3, série SN-7781-AX.", ip: "10.1.2.3", serial: "SN-7781-AX"),
            Item(2, "Serveur paie prod", "Autre", os: "Windows Server 2019 Standard"),
            Item(3, "base-paie", "Base de données"),
            Item(4, "srv-usine-07", "Serveur", perimeter: 8),   // d'un autre périmètre : jamais envoyé (CMA-07)
        ];
        api.Naming = [new { ciType = "Serveur", description = "SRV-<RÔLE>-<NN>, en majuscules", examples = new[] { "SRV-PAIE-01", "SRV-MAIL-02" } }];
        api.Replies["rename"] = """{ "propositions": [ { "ci": 1, "nom": "SRV-PAIE-01", "motif": "Convention des serveurs." }, { "ci": 3, "nom": "BDD-PAIE", "motif": "Hors du lot." } ] }""";
        api.Replies["normalize"] = """
            { "propositions": [ { "ci": 1, "champ": "operatingSystem", "valeur": "Windows Server 2019 Standard", "motif": "Écriture normalisée." },
              { "ci": 2, "champ": "operatingSystem", "valeur": "Windows Server 2019 Standard", "motif": "Déjà ainsi : ignoré." },
              { "ci": 99, "champ": "model", "valeur": "DL380", "motif": "Inconnu." } ] }
            """;
        api.Replies["reclassify"] = """{ "propositions": [ { "ci": 2, "type": "Serveur", "motif": "Un serveur, d'après son nom et son système." } ] }""";
        Analysis(501, ["rename", "normalize", "reclassify"]);

        using var container = Container();
        container.CreateClient();
        var report = await FinalReport(501);
        Assert.NotNull(report);
        Assert.Equal(("Terminée", 3, 2), (report!.Value.GetProperty("status").GetString(), report.Value.GetProperty("proposalCount").GetInt32(),
            report.Value.GetProperty("rejectedCount").GetInt32()));
        Assert.Equal("En cours", api.Reports.First(r => r.Id == 501).Report.GetProperty("status").GetString());

        var proposals = api.Proposals.Where(p => p.GetProperty("feature").GetString() == "assisted-cmdb")
            .Select(p => (p.GetProperty("targetType").GetString(), p.GetProperty("targetId").GetInt32(), p.GetProperty("field").GetString(), p.GetProperty("after").GetString()))
            .ToList();
        Assert.Equal([("ci", 1, "title", "SRV-PAIE-01"), ("ci", 1, "operatingSystem", "Windows Server 2019 Standard"), ("ci", 2, "ciType", "Serveur")], proposals);

        // CMA-07 : ni adresse ni numéro de série en clair, aucun CI d'un autre périmètre ; Renommer ne voit que les types qui ont une convention.
        var requests = api.ModelRequests.Select(r => (Task: r.GetProperty("response_format").GetProperty("json_schema").GetProperty("name").GetString(),
            User: r.GetProperty("messages")[1].GetProperty("content").GetString()!)).ToList();
        Assert.Equal(["rename", "normalize", "reclassify"], requests.Select(r => r.Task));
        foreach (var (_, user) in requests)
            foreach (var secret in new[] { "10.1.2.3", "SN-7781-AX", "hery.rakoto", "srv-usine-07" }) Assert.DoesNotContain(secret, user);
        Assert.DoesNotContain("base-paie", requests[0].User);
        Assert.Contains("SRV-PAIE-01", api.ModelRequests.First().GetProperty("messages")[0].GetProperty("content").GetString());   // la convention
    }

    [Fact]
    public async Task OPT_28_un_modele_en_panne_met_l_analyse_en_echec_motif_donne_et_une_analyse_finie_ne_se_refait_pas()
    {
        api.Items = [Item(11, "srvmail2", "Serveur", os: "W2k19")];
        api.Replies.Clear();   // le modèle répond 500
        Analysis(502, ["normalize"]);
        Analysis(503, ["normalize"], status: "Terminée");
        using var container = Container();
        container.CreateClient();
        var report = await FinalReport(502);
        Assert.NotNull(report);
        Assert.Equal("En échec", report!.Value.GetProperty("status").GetString());
        Assert.Contains("HTTP 500", report.Value.GetProperty("reason").GetString());
        await Task.Delay(300);
        Assert.DoesNotContain(api.Reports, r => r.Id == 503);
    }
}
