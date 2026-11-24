using SAloha.Features.Host;
using SAloha.Features.LanguageModel;

namespace SAloha.Features.AssistedCmdb;

/// <summary>
/// CMDB assistée (clé <c>assisted-cmdb</c>, docs/reference/cmdb-assistee.md). Une analyse lancée par un gestionnaire
/// arrive par le fil (<c>analysis.requested</c>, CMA-01) ; le conteneur lit ses CI (la sélection, ou le périmètre page
/// après page), les soumet au modèle de langage par lots, une requête par tâche et par lot, et dépose les propositions
/// retenues (OPT-14), qu'un gestionnaire décide (CMA-03). Il rend compte après chaque lot ; un modèle en panne met
/// l'analyse en échec, motif donné (OPT-28). Renommer, Normaliser, Reclasser et Criticité vont par lots de CI (CMA-02) ;
/// Relier ne soumet que les CI dont la description cite un autre CI de l'analyse ; Fusionner, les paires candidates de toute
/// l'analyse, présélectionnées ici, par requêtes de 50 (CMA-06).
/// </summary>
public class AssistedCmdb(ApiClient api, LanguageModelClient model, ILogger<AssistedCmdb> log) : IContainerFeature, IEventHandler
{
    public const string FeatureKey = "assisted-cmdb";
    /// <summary>CI par requête au modèle : assez pour comparer, assez peu pour une réponse courte et un délai borné.</summary>
    public const int BatchSize = 20;
    public static readonly string[] Supported =
        [CmdbTasks.Rename, CmdbTasks.Normalize, CmdbTasks.Reclassify, CmdbTasks.Criticality, Relations.Task, Duplicates.Task];

    public string Key => FeatureKey;
    public string Feature => FeatureKey;
    public IReadOnlyCollection<string> Types { get; } = ["analysis.requested"];

    public async Task HandleAsync(FeatureEvent e, CancellationToken ct)
    {
        if (e.EntityType != "cmdb-analysis") return;
        var analysis = await api.AnalysisAsync(e.EntityId, ct);
        // Rejoué après un redémarrage, ou déjà prise : une analyse qui n'est plus en file ne se refait pas.
        if (analysis is null || analysis.Status != "En file") return;
        if (!await api.ReportAnalysisAsync(analysis.Id, new AnalysisReport("En cours"), ct)) return;

        int proposals = 0, rejected = 0;
        // Une requête au modèle : la sortie validée, ou null (non conforme deux fois : comptée, on continue) ; un modèle en
        // panne ou trop lent arrête l'analyse (OPT-28).
        async Task<System.Text.Json.JsonElement?> AskAsync(string task, string instructions, string input, System.Text.Json.JsonElement schema,
            Pseudonymizer pseudo)
        {
            var result = await model.RunAsync(new LanguageModelTask(FeatureKey, task, CmdbTasks.PromptVersion, instructions, input, schema), pseudo, ct);
            rejected += result.Call.Rejected;
            if (result.Call.Outcome is LanguageModelClient.Error or LanguageModelClient.Timeout)
                throw new ModelFailed(result.Call.Error ?? "Modèle de langage en échec.");
            return result.Output;
        }
        async Task ProposeAsync(int ci, string field, string value, string reasons)
        {
            if (await api.ProposeAsync(new ProposalInput(FeatureKey, "ci", ci, field, value, reasons, [], null), ct)) proposals++;
            else rejected++;
        }
        Task KeepAliveAsync() => api.ReportAnalysisAsync(analysis.Id, new AnalysisReport("En cours", null, proposals, rejected), ct);

        try
        {
            var tasks = analysis.Tasks.Where(Supported.Contains).ToList();
            var naming = tasks.Contains(CmdbTasks.Rename) ? await api.NamingAsync(ct) : [];
            var named = naming.Select(n => n.CiType).ToHashSet();
            var items = await ItemsAsync(analysis, ct);
            var titles = items.ToDictionary(c => c.Id, c => c.Title ?? "");
            var perBatch = tasks.Where(t => t != Duplicates.Task && t != Relations.Task).ToList();
            foreach (var batch in perBatch.Count == 0 ? [] : items.Chunk(BatchSize))
            {
                foreach (var task in perBatch)
                {
                    // CMA-04 : sans convention pour un type, Renommer ne s'y applique pas ; Criticité ne vise que les applications sans criticité.
                    var subject = task switch
                    {
                        CmdbTasks.Rename => batch.Where(c => named.Contains(c.CiType ?? "")).ToList(),
                        CmdbTasks.Criticality => batch.Where(CmdbTasks.NeedsCriticality).ToList(),
                        _ => batch.ToList(),
                    };
                    if (subject.Count == 0) continue;
                    var pseudo = new Pseudonymizer();
                    var output = await AskAsync(task, CmdbTasks.Instructions(task, naming.Where(n => subject.Any(c => c.CiType == n.CiType)).ToList()),
                        CmdbTasks.Input(subject, titles, pseudo), CmdbTasks.SchemaOf(task), pseudo);
                    if (output is not { } valid) continue;
                    var (kept, refused) = CmdbTasks.Read(task, valid, subject.ToDictionary(c => c.Id), named);
                    rejected += refused;
                    foreach (var p in kept) await ProposeAsync(p.Ci, p.Field, p.Value, p.Reasons);
                }
                // Le compte rendu de chaque lot garde l'analyse vivante côté API.
                await KeepAliveAsync();
            }
            if (tasks.Contains(Relations.Task))
            {
                // CMA-02 : seuls les CI dont la description cite un autre CI de l'analyse, avec ces seules cibles.
                var analysed = items.Select(c => c.Id).ToHashSet();
                foreach (var chunk in Relations.Subjects(items).Chunk(Relations.SourcesPerRequest))
                {
                    var pseudo = new Pseudonymizer();
                    if (await AskAsync(Relations.Task, Relations.Instructions, Relations.Input(chunk, titles, pseudo), Relations.Schema, pseudo) is { } output)
                    {
                        var (kept, refused) = Relations.Read(output, chunk, analysed);
                        rejected += refused;
                        foreach (var r in kept) await ProposeAsync(r.Source, "relation", $"{r.Target}|{r.Type}", r.Reasons);
                    }
                    await KeepAliveAsync();
                }
            }
            if (tasks.Contains(Duplicates.Task))
            {
                // CMA-06 : le modèle ne juge que les paires présélectionnées, jamais une paire rejetée.
                var declined = await api.DeclinedPairsAsync(analysis.PerimeterId, ct);
                var byId = items.ToDictionary(c => c.Id);
                foreach (var chunk in Duplicates.Candidates(items, declined).Chunk(Duplicates.PairsPerRequest))
                {
                    var pseudo = new Pseudonymizer();
                    if (await AskAsync(Duplicates.Task, Duplicates.Instructions, Duplicates.Input(chunk, byId, titles, pseudo), Duplicates.Schema, pseudo) is { } output)
                    {
                        var (kept, refused) = Duplicates.Read(output, chunk);
                        rejected += refused;
                        foreach (var m in kept) await ProposeAsync(m.Duplicate, "merge", m.Keep.ToString(), m.Reasons);
                    }
                    await KeepAliveAsync();
                }
            }
            await api.ReportAnalysisAsync(analysis.Id, new AnalysisReport("Terminée", null, proposals, rejected), ct);
        }
        catch (ModelFailed failure)
        {
            await api.ReportAnalysisAsync(analysis.Id, new AnalysisReport("En échec", failure.Message, proposals, rejected), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException && !ct.IsCancellationRequested)
        {
            log.LogWarning("Analyse {Id} en échec : {Error}", analysis.Id, ex.Message.Split('\n')[0]);
            await api.ReportAnalysisAsync(analysis.Id, new AnalysisReport("En échec", $"Lecture ou dépôt impossible : {ex.Message.Split('\n')[0]}",
                proposals, rejected), ct);
        }
    }

    /// <summary>Le modèle en panne ou trop lent : l'analyse s'arrête, en échec, avec ce motif (OPT-28).</summary>
    private sealed class ModelFailed(string reason) : Exception(reason);

    /// <summary>Les CI de l'analyse : la sélection (par 50), ou le périmètre page après page.</summary>
    private async Task<List<CiRecord>> ItemsAsync(AnalysisData analysis, CancellationToken ct)
    {
        var items = new List<CiRecord>();
        if (analysis.ConfigurationItemIds is { } ids)
        {
            foreach (var chunk in ids.Chunk(50)) items.AddRange(await api.CiRecordsAsync(chunk, ct));
            // Une sélection ne sort jamais du périmètre de l'analyse (CMA-07).
            return items.Where(c => c.PerimeterId == analysis.PerimeterId && c.Status != "Retiré").ToList();
        }
        for (var after = 0; ;)
        {
            var page = await api.PerimeterItemsAsync(analysis.PerimeterId, after, ct);
            items.AddRange(page.Where(c => c.PerimeterId == analysis.PerimeterId));
            if (page.Count == 0) return items;
            after = page[^1].Id;
        }
    }
}
