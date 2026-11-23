using SAloha.Features.Host;

namespace SAloha.Features.TicketTriage;

/// <summary>
/// Tri assisté des tickets (clé <c>assisted-ticket-triage</c>, docs/reference/tri-assiste-des-tickets.md).
/// Après chaque enregistrement d'un incident (fil d'événements, TRI-02), il tient à jour son index des incidents
/// ouverts (TRI-06), en tire les pannes probables du périmètre (TRI-05 à TRI-07), qu'il signale à l'API, puis lit
/// l'incident, ses CI reliés et ses voisins résolus dans le seul périmètre de l'incident (TRI-11) et dépose ses
/// suggestions en propositions (OPT-14), que l'agent décide. Rien n'est jamais appliqué seul.
/// </summary>
public class TicketTriage(ApiClient api, EmbeddingClient embeddings, ILogger<TicketTriage> log) : IContainerFeature, IEventHandler
{
    public const string FeatureKey = "assisted-ticket-triage";
    public const int EmbedBatch = 32;
    public string Key => FeatureKey;
    public string Feature => FeatureKey;
    public IReadOnlyCollection<string> Types { get; } = ["incident.created", "incident.updated", "incident.resolved", "incident.deleted"];

    /// <summary>Catalogue des services et catégories : référentiels relus une fois par minute au plus.</summary>
    private static readonly TimeSpan ReferenceAge = TimeSpan.FromMinutes(1);
    private (DateTime At, List<ServiceData> Services, List<CategoryData> Categories)? reference;

    /// <summary>Pannes probables signalées à l'API, par périmètre : les membres de chaque groupe ouvert.</summary>
    private readonly Dictionary<int, List<IReadOnlyList<int>>> signaled = [];

    public TriageSettings Settings { get; } = new();
    public CorrelationSettings Correlation { get; } = new();
    public OpenIncidents Index { get; } = new();

    /// <summary>OPT-06 : des événements ont été purgés depuis le curseur ; l'index repart d'un état complet.</summary>
    public async Task ResetAsync(CancellationToken ct)
    {
        await RebuildAsync(ct);
        foreach (var perimeter in Index.Perimeters) await CorrelateAsync(perimeter, ct);
    }

    public async Task HandleAsync(FeatureEvent e, CancellationToken ct)
    {
        if (e.EntityType != "incident") return;
        // Premier événement depuis le démarrage : l'index se reconstruit depuis l'API (TRI-06), puis les pannes
        // de tous les périmètres se recalculent ; l'API ne renotifie pas un groupe qu'elle connaît.
        if (!Index.Built) await ResetAsync(ct);
        var read = e.Type == "incident.deleted" ? null : await api.IncidentAsync(e.EntityId, ct);
        var before = Index.Get(e.EntityId)?.PerimeterId;
        await UpdateIndexAsync(e.EntityId, read, ct);
        var perimeter = read?.Incident.PerimeterId ?? before ?? e.PerimeterId;
        var groups = perimeter is int p ? await CorrelateAsync(p, ct) : [];
        if (before is int moved && moved != perimeter) await CorrelateAsync(moved, ct);

        var incident = read?.Incident;
        // TRI-01 : un incident supprimé entre-temps, ou qui n'est plus Nouveau ni En cours, n'est pas trié.
        if (read is null || incident is null || !TriageRules.InScope.Contains(incident.Status) || string.IsNullOrWhiteSpace(incident.Title)) return;

        var similar = await api.SimilarAsync(new SimilarQuery(incident.Title, incident.Description, [incident.PerimeterId], incident.Id,
            Settings.Neighbours), ct);
        var (services, categories) = await ReferenceAsync(ct);
        var cis = await api.ConfigurationItemsAsync(read.ConfigurationItemIds, ct);
        var outage = groups.FirstOrDefault(g => g.Members.Contains(incident.Id));
        foreach (var s in TriageRules.Suggest(incident, similar, services, categories, cis, Settings, outage))
            await ProposeAsync(incident.Id, s, ct);
    }

    private async Task ProposeAsync(int incidentId, Suggestion s, CancellationToken ct)
    {
        var accepted = await api.ProposeAsync(new ProposalInput(FeatureKey, "incident", incidentId, s.Field, s.Value, s.Reasons, s.Cited, s.Confidence), ct);
        if (!accepted) log.LogInformation("Tri assisté : proposition {Field} refusée par l'API pour l'incident {Id}.", s.Field, incidentId);
    }

    /// <summary>TRI-06 : un incident ouvert entre ou reste dans l'index (son vecteur recalculé si son texte change) ; sinon il en sort.</summary>
    private async Task UpdateIndexAsync(int id, IncidentRead? read, CancellationToken ct)
    {
        if (read is null || !OpenIncidents.Open.Contains(read.Incident.Status))
        {
            Index.Remove(id);
            return;
        }
        var text = OpenEntry.TextOf(read.Incident);
        var vector = Index.Get(id) is { } known && known.Text == text ? known.Vector : null;
        vector ??= (await embeddings.EmbedAsync([text], ct))?[0];
        Index.Upsert(OpenEntry.From(read, vector));
    }

    /// <summary>TRI-06 : l'index se reconstruit depuis l'API, page après page, les vecteurs par lots.</summary>
    private async Task RebuildAsync(CancellationToken ct)
    {
        var reads = new List<IncidentRead>();
        int? after = 0;
        while (after is int from)
        {
            var page = await api.OpenIncidentsAsync(from, ct);
            reads.AddRange(page.Items);
            after = page.Next;
        }
        var entries = new List<OpenEntry>();
        foreach (var batch in reads.Chunk(EmbedBatch))
        {
            var vectors = await embeddings.EmbedAsync(batch.Select(r => OpenEntry.TextOf(r.Incident)).ToList(), ct);
            entries.AddRange(batch.Select((r, i) => OpenEntry.From(r, vectors?[i])));
        }
        Index.Replace(entries);
        log.LogInformation("Tri assisté : index des incidents ouverts reconstruit, {Count} incidents{Vectors}.", entries.Count,
            entries.Any(e => e.Vector is null) && entries.Count > 0 ? " (sans le sens pour une partie : service d'embeddings indisponible)" : "");
    }

    /// <summary>
    /// TRI-05 à TRI-07 : les pannes probables d'un périmètre, comparées à celles déjà signalées. Un groupe nouveau ou
    /// changé est signalé à l'API, et ses incidents reçoivent l'impact Élevé proposé (TRI-04) ; un groupe défait est
    /// signalé fermé.
    /// </summary>
    private async Task<List<OutageGroup>> CorrelateAsync(int perimeter, CancellationToken ct)
    {
        var (services, _) = await ReferenceAsync(ct);
        var titles = services.Where(s => s.Title is not null).ToDictionary(s => s.Id, s => s.Title!);
        var groups = OpenIncidents.Outages(Index.InPerimeter(perimeter), Correlation, titles);
        var known = signaled.GetValueOrDefault(perimeter) ?? [];
        foreach (var g in groups.Where(g => !known.Any(k => k.SequenceEqual(g.Members))))
        {
            await api.SignalOutageAsync(new OutageSignal(FeatureKey, g.Members, g.Basis, g.ServiceId, g.Reasons), ct);
            foreach (var member in g.Members.Select(Index.Get).OfType<OpenEntry>().Where(m => TriageRules.InScope.Contains(m.Status)))
                if (TriageRules.OutageImpact(member.Id, member.Impact, g) is { } impact) await ProposeAsync(member.Id, impact, ct);
        }
        foreach (var gone in known.Where(k => !groups.Any(g => g.Members.Intersect(k).Any())))
            await api.SignalOutageAsync(new OutageSignal(FeatureKey, gone, null, null, null, Closed: true), ct);
        signaled[perimeter] = groups.Select(g => g.Members).ToList();
        return groups;
    }

    private async Task<(List<ServiceData>, List<CategoryData>)> ReferenceAsync(CancellationToken ct)
    {
        if (reference is { } r && DateTime.UtcNow - r.At < ReferenceAge) return (r.Services, r.Categories);
        var services = await api.ServicesAsync(ct);
        var categories = await api.CategoriesAsync("incident", ct);
        reference = (DateTime.UtcNow, services, categories);
        return (services, categories);
    }
}
