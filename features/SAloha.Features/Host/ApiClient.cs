using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace SAloha.Features.Host;

// Vues de l'API publique lues sur le port interne (OPT-13, ADR-0016) : seuls les champs dont le
// conteneur se sert ; les autres sont ignorés à la lecture.
public record IncidentData(int Id, int PerimeterId, string? Reference, string? Title, string? Description, string? Status,
    string? Category, int? ServiceId, string? Impact, string? OwnerType, string? OwnerId, string? OwnerDisplayName);
public record IncidentRead(IncidentData Incident, List<int> ConfigurationItemIds);
public record Neighbour(double Score, bool Lexical, bool Semantic, IncidentData Incident);
public record SimilarResult(bool Semantic, List<Neighbour> Neighbours);
public record SimilarQuery(string? Title, string? Description, int[] Perimeters, int? ExcludeId, int Limit);
public record ServiceData(int Id, string? Reference, string? Title, string? Status, string? Criticality);
public record CiData(int Id, string? Reference, string? Title, string? Criticality);
public record CategoryData(string Name, bool Active);
public record FeatureEvent(long Id, string Type, string EntityType, int EntityId, int? PerimeterId, DateTime At);
public record EventsPage(List<FeatureEvent> Events, long Next, bool Gap);
public record CitedRef(string Type, int Id);
public record ProposalInput(string Feature, string TargetType, int TargetId, string Field, string After, string Reasons,
    IReadOnlyList<CitedRef> Cited, double? Confidence);

/// <summary>
/// Client du port interne de l'API (<c>Api:Url</c>, <c>http://api:8081</c>), avec le jeton du conteneur
/// (OPT-10). Le jeton n'est jamais journalisé. Une réponse inattendue lève une exception, que la boucle
/// d'événements journalise avant de réessayer.
/// </summary>
public class ApiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient http;

    public ApiClient(HttpClient http, IConfiguration config)
    {
        this.http = http;
        var url = config["Api:Url"]?.Trim().TrimEnd('/');
        Configured = !string.IsNullOrEmpty(url) && Uri.TryCreate(url + "/", UriKind.Absolute, out _);
        if (Configured) http.BaseAddress = new Uri(url + "/");
        var token = config["Features:Token"];
        if (!string.IsNullOrEmpty(token)) http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary><c>Api:Url</c> donnée : sans elle, le conteneur ne lit rien et ne propose rien.</summary>
    public bool Configured { get; }

    /// <summary>OPT-12 : une page du fil ; null si l'API le refuse (403 : aucune fonctionnalité activée ne le lit).</summary>
    public async Task<EventsPage?> EventsAsync(long after, int limit, CancellationToken ct)
    {
        using var res = await http.GetAsync($"internal/events?after={after}&limit={limit}", ct);
        if (res.StatusCode == HttpStatusCode.Forbidden) return null;
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<EventsPage>(Json, ct);
    }

    /// <summary>Un incident et ses CI reliés ; null s'il n'existe plus.</summary>
    public async Task<IncidentRead?> IncidentAsync(int id, CancellationToken ct)
    {
        using var res = await http.GetAsync($"internal/incidents/{id}", ct);
        if (res.StatusCode == HttpStatusCode.NotFound) return null;
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<IncidentRead>(Json, ct);
    }

    public async Task<SimilarResult> SimilarAsync(SimilarQuery query, CancellationToken ct)
    {
        using var res = await http.PostAsJsonAsync("internal/search/similar", query, Json, ct);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<SimilarResult>(Json, ct))!;
    }

    public async Task<List<CiData>> ConfigurationItemsAsync(IReadOnlyCollection<int> ids, CancellationToken ct) =>
        ids.Count == 0 ? [] : (await http.GetFromJsonAsync<List<CiData>>($"internal/configuration-items?ids={string.Join(',', ids)}", Json, ct))!;

    public async Task<List<ServiceData>> ServicesAsync(CancellationToken ct) =>
        (await http.GetFromJsonAsync<List<ServiceData>>("internal/services", Json, ct))!;

    public async Task<List<CategoryData>> CategoriesAsync(string practice, CancellationToken ct) =>
        (await http.GetFromJsonAsync<List<CategoryData>>($"internal/categories?practice={practice}", Json, ct))!;

    /// <summary>OPT-14 : dépôt d'une proposition ; faux si l'API la refuse (champ, valeur, cible, fonctionnalité coupée).</summary>
    public async Task<bool> ProposeAsync(ProposalInput proposal, CancellationToken ct)
    {
        using var res = await http.PostAsJsonAsync("internal/proposals", proposal, Json, ct);
        return res.IsSuccessStatusCode;
    }
}
