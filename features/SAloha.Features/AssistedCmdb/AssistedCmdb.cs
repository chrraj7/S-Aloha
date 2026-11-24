using SAloha.Features.Host;
using SAloha.Features.LanguageModel;

namespace SAloha.Features.AssistedCmdb;

/// <summary>
/// CMDB assistée (clé <c>assisted-cmdb</c>, docs/reference/cmdb-assistee.md). Une analyse lancée par un gestionnaire
/// arrive par le fil (<c>analysis.requested</c>, CMA-01) ; le conteneur lit ses CI (la sélection, ou le périmètre page
/// après page), les soumet au modèle de langage par lots, une requête par tâche et par lot, et dépose les propositions
/// retenues (OPT-14), qu'un gestionnaire décide (CMA-03). Il rend compte après chaque lot ; un modèle en panne met
/// l'analyse en échec, motif donné (OPT-28). Aujourd'hui : Renommer, Normaliser, Reclasser (CMA-02, étape 5a), et Fusionner
/// (CMA-06, étape 5b) : les paires candidates de toute l'analyse, présélectionnées ici, partent par requêtes de 50.
/// </summary>
public class AssistedCmdb(ApiClient api, LanguageModelClient model, ILogger<AssistedCmdb> log) : IContainerFeature, IEventHandler
{
    public const string FeatureKey = "assisted-cmdb";
    /// <summary>CI par requête au modèle : assez pour comparer, assez peu pour une réponse courte et un délai borné.</summary>
    public const int BatchSize = 20;
    public static readonly string[] Supported = [CmdbTasks.Rename, CmdbTasks.Normalize, CmdbTasks.Reclassify, Duplicates.Task];

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
        try
        {
            var tasks = analysis.Tasks.Where(Supported.Contains).ToList();
            var naming = tasks.Contains(CmdbTasks.Rename) ? await api.NamingAsync(ct) : [];
            var named = naming.Select(n => n.CiType).ToHashSet();
            var items = await ItemsAsync(analysis, ct);
            var titles = items.ToDictionary(c => c.Id, c => c.Title ?? "");
            var perBatch = tasks.Where(t => t != Duplicates.Task).ToList();
            foreach (var batch in perBatch.Count == 0 ? [] : items.Chunk(BatchSize))
            {
                foreach (var task in perBatch)
                {
                    // CMA-04 : sans convention pour un type, Renommer ne s'y applique pas.
                    var subject = task == CmdbTasks.Rename ? batch.Where(c => named.Contains(c.CiType ?? "")).ToList() : batch.ToList();
                    if (subject.Count == 0) continue;
                    var pseudo = new Pseudonymizer();
                    var input = CmdbTasks.Input(subject, titles, pseudo);
                    var job = new LanguageModelTask(FeatureKey, task, CmdbTasks.PromptVersion,
                        CmdbTasks.Instructions(task, naming.Where(n => subject.Any(c => c.CiType == n.CiType)).ToList()), input, CmdbTasks.SchemaOf(task));
                    var result = await model.RunAsync(job, pseudo, ct);
                    rejected += result.Call.Rejected;
                    if (result.Call.Outcome is LanguageModelClient.Error or LanguageModelClient.Timeout)
                    {
                        await api.ReportAnalysisAsync(analysis.Id, new AnalysisReport("En échec", result.Call.Error ?? "Modèle de langage en échec.",
                            proposals, rejected), ct);
                        return;
                    }
                    if (result.Output is not { } output) continue;   // non conforme deux fois : comptée, lot suivant
                    var (kept, refused) = CmdbTasks.Read(task, output, subject.ToDictionary(c => c.Id), named);
                    rejected += refused;
                    foreach (var p in kept)
                    {
                        if (await api.ProposeAsync(new ProposalInput(FeatureKey, "ci", p.Ci, p.Field, p.Value, p.Reasons, [], null), ct)) proposals++;
                        else rejected++;
                    }
                }
                // Le compte rendu de chaque lot garde l'analyse vivante côté API.
                await api.ReportAnalysisAsync(analysis.Id, new AnalysisReport("En cours", null, proposals, rejected), ct);
            }
            if (tasks.Contains(Duplicates.Task))
            {
                // CMA-06 : le modèle ne juge que les paires présélectionnées, jamais une paire rejetée.
                var declined = await api.DeclinedPairsAsync(analysis.PerimeterId, ct);
                var byId = items.ToDictionary(c => c.Id);
                foreach (var chunk in Duplicates.Candidates(items, declined).Chunk(Duplicates.PairsPerRequest))
                {
                    var pseudo = new Pseudonymizer();
                    var job = new LanguageModelTask(FeatureKey, Duplicates.Task, CmdbTasks.PromptVersion, Duplicates.Instructions,
                        Duplicates.Input(chunk, byId, titles, pseudo), Duplicates.Schema);
                    var result = await model.RunAsync(job, pseudo, ct);
                    rejected += result.Call.Rejected;
                    if (result.Call.Outcome is LanguageModelClient.Error or LanguageModelClient.Timeout)
                    {
                        await api.ReportAnalysisAsync(analysis.Id, new AnalysisReport("En échec", result.Call.Error ?? "Modèle de langage en échec.",
                            proposals, rejected), ct);
                        return;
                    }
                    if (result.Output is { } output)
                    {
                        var (kept, refused) = Duplicates.Read(output, chunk);
                        rejected += refused;
                        foreach (var m in kept)
                        {
                            if (await api.ProposeAsync(new ProposalInput(FeatureKey, "ci", m.Duplicate, "merge", m.Keep.ToString(), m.Reasons, [], null), ct))
                                proposals++;
                            else rejected++;
                        }
                    }
                    await api.ReportAnalysisAsync(analysis.Id, new AnalysisReport("En cours", null, proposals, rejected), ct);
                }
            }
            await api.ReportAnalysisAsync(analysis.Id, new AnalysisReport("Terminée", null, proposals, rejected), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException && !ct.IsCancellationRequested)
        {
            log.LogWarning("Analyse {Id} en échec : {Error}", analysis.Id, ex.Message.Split('\n')[0]);
            await api.ReportAnalysisAsync(analysis.Id, new AnalysisReport("En échec", $"Lecture ou dépôt impossible : {ex.Message.Split('\n')[0]}",
                proposals, rejected), ct);
        }
    }

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
