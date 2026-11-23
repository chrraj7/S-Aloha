using SAloha.Features.Host;

namespace SAloha.Features.TicketTriage;

/// <summary>
/// Tri assisté des tickets (clé <c>assisted-ticket-triage</c>, docs/reference/tri-assiste-des-tickets.md).
/// Après chaque enregistrement d'un incident (fil d'événements, TRI-02), il lit l'incident, ses CI reliés
/// et ses voisins résolus dans le seul périmètre de l'incident (TRI-11), puis dépose ses suggestions en
/// propositions (OPT-14), que l'agent décide. Rien n'est jamais appliqué seul.
/// </summary>
public class TicketTriage(ApiClient api, ILogger<TicketTriage> log) : IContainerFeature, IEventHandler
{
    public const string FeatureKey = "assisted-ticket-triage";
    public string Key => FeatureKey;
    public string Feature => FeatureKey;
    public IReadOnlyCollection<string> Types { get; } = ["incident.created", "incident.updated"];

    /// <summary>Catalogue des services et catégories : référentiels relus une fois par minute au plus.</summary>
    private static readonly TimeSpan ReferenceAge = TimeSpan.FromMinutes(1);
    private (DateTime At, List<ServiceData> Services, List<CategoryData> Categories)? reference;

    public TriageSettings Settings { get; } = new();

    public async Task HandleAsync(FeatureEvent e, CancellationToken ct)
    {
        if (e.EntityType != "incident") return;
        var read = await api.IncidentAsync(e.EntityId, ct);
        var incident = read?.Incident;
        // TRI-01 : un incident supprimé entre-temps, ou qui n'est plus Nouveau ni En cours, n'est pas trié.
        if (read is null || incident is null || !TriageRules.InScope.Contains(incident.Status) || string.IsNullOrWhiteSpace(incident.Title)) return;

        var similar = await api.SimilarAsync(new SimilarQuery(incident.Title, incident.Description, [incident.PerimeterId], incident.Id,
            Settings.Neighbours), ct);
        var (services, categories) = await ReferenceAsync(ct);
        var cis = await api.ConfigurationItemsAsync(read.ConfigurationItemIds, ct);
        foreach (var s in TriageRules.Suggest(incident, similar, services, categories, cis, Settings))
        {
            var accepted = await api.ProposeAsync(new ProposalInput(FeatureKey, "incident", incident.Id, s.Field, s.Value, s.Reasons, s.Cited,
                s.Confidence), ct);
            if (!accepted) log.LogInformation("Tri assisté : proposition {Field} refusée par l'API pour l'incident {Id}.", s.Field, incident.Id);
        }
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
