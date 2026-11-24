using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace SAloha.Features.Host;

// Vues de l'API publique lues sur le port interne (OPT-13, ADR-0016) : seuls les champs dont le
// conteneur se sert ; les autres sont ignorés à la lecture.
public record IncidentData(int Id, int PerimeterId, string? Reference, string? Title, string? Description, string? Status,
    string? Category, int? ServiceId, string? Impact, string? OwnerType, string? OwnerId, string? OwnerDisplayName,
    DateTime CreatedAt = default);
public record IncidentRead(IncidentData Incident, List<int> ConfigurationItemIds);
public record OpenPage(List<IncidentRead> Items, int? Next);
/// <summary>TRI-07 : groupe signalé à l'API ; <c>Closed</c> : il s'est défait.</summary>
public record OutageSignal(string Feature, IReadOnlyList<int> Members, string? Basis, int? ServiceId, string? Reasons, bool Closed = false);
public record Neighbour(double Score, bool Lexical, bool Semantic, IncidentData Incident);
public record SimilarResult(bool Semantic, List<Neighbour> Neighbours);
public record SimilarQuery(string? Title, string? Description, int[] Perimeters, int? ExcludeId, int Limit);
public record ServiceData(int Id, string? Reference, string? Title, string? Status, string? Criticality);
public record CiData(int Id, string? Reference, string? Title, string? Criticality);
public record CategoryData(string Name, bool Active);
public record FeatureEvent(long Id, string Type, string EntityType, int EntityId, int? PerimeterId, DateTime At);
public record EventsPage(List<FeatureEvent> Events, long Next, bool Gap);
public record CitedRef(string Type, int Id);
/// <summary>TRI-13 : réglages du tri, tels que l'Admin les a fixés.</summary>
public record TriageSettingsData(int Neighbours, int MinVoters, int MinSharePercent, int CorrelationWindowMinutes, int OutageThreshold,
    double CorrelationMinSimilarity, bool SuggestCategory, bool SuggestService, bool SuggestGroup, bool SuggestImpact, bool SuggestOutage);
public record ProposalInput(string Feature, string TargetType, int TargetId, string Field, string After, string Reasons,
    IReadOnlyList<CitedRef> Cited, double? Confidence);
/// <summary>CMA-01 : une analyse de la CMDB assistée, telle que l'API la remet ; <c>ConfigurationItemIds</c> null : tout le périmètre.</summary>
public record AnalysisData(int Id, int PerimeterId, List<string> Tasks, List<int>? ConfigurationItemIds, string Status, int ItemCount);
public record AnalysisReport(string Status, string? Reason = null, int? ProposalCount = null, int? RejectedCount = null);
public record CiRelationRead(int SourceId, int TargetId, string? Type);
/// <summary>Un CI dans la vue publique, champs utiles à la CMDB assistée (CMA-07) et ceux qu'elle déclare au pseudonymiseur.</summary>
public record CiRecord(int Id, int PerimeterId, string? Reference, string? Title, string? Description, string? Status, string? CiType,
    string? Environment, string? OperatingSystem, string? Vendor, string? Model, string? SerialNumber, string? IpAddress, string? MacAddress,
    string? OwnerId, List<CiRelationRead>? Outgoing, List<CiRelationRead>? Incoming, string? Criticality = null);
/// <summary>CMA-04 : convention de nommage d'un type de CI, tenue par les gestionnaires.</summary>
public record NamingRule(string CiType, string Description, List<string>? Examples);

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

    /// <summary>TRI-13 : réglages d'une fonctionnalité ; null si l'API n'en sert pas (ancienne version, 403, 404).</summary>
    public async Task<TriageSettingsData?> TriageSettingsAsync(CancellationToken ct)
    {
        using var res = await http.GetAsync("internal/features/assisted-ticket-triage/settings", ct);
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadFromJsonAsync<TriageSettingsData>(Json, ct);
    }

    /// <summary>
    /// OPT-23 : réglages du modèle de langage, clé comprise ; null si l'API les refuse (403 : aucune fonctionnalité qui
    /// s'en sert n'est activée). Relus à chaque appel du modèle, jamais écrits.
    /// </summary>
    public async Task<LanguageModel.LanguageModelSettingsData?> LanguageModelSettingsAsync(CancellationToken ct)
    {
        using var res = await http.GetAsync("internal/language-model", ct);
        if (res.StatusCode == HttpStatusCode.Forbidden) return null;
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<LanguageModel.LanguageModelSettingsData>(Json, ct);
    }

    /// <summary>OPT-27 : trace d'un appel au modèle ; faux si l'API la refuse (fonctionnalité coupée, champ hors bornes).</summary>
    public async Task<bool> TraceLanguageModelCallAsync(LanguageModel.LanguageModelCall call, CancellationToken ct)
    {
        using var res = await http.PostAsJsonAsync("internal/language-model/calls", call, Json, ct);
        return res.IsSuccessStatusCode;
    }

    /// <summary>CMA-01 : une analyse annoncée par le fil ; null si elle n'existe plus.</summary>
    public async Task<AnalysisData?> AnalysisAsync(int id, CancellationToken ct)
    {
        using var res = await http.GetAsync($"internal/assisted-cmdb/analyses/{id}", ct);
        if (res.StatusCode == HttpStatusCode.NotFound) return null;
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<AnalysisData>(Json, ct);
    }

    /// <summary>CMA-01 : compte rendu d'une analyse ; faux si l'API le refuse (analyse finie entre-temps).</summary>
    public async Task<bool> ReportAnalysisAsync(int id, AnalysisReport report, CancellationToken ct)
    {
        using var res = await http.PutAsJsonAsync($"internal/assisted-cmdb/analyses/{id}", report, Json, ct);
        return res.IsSuccessStatusCode;
    }

    /// <summary>CMA-01 : une page des CI d'un périmètre (hors retirés), après l'identifiant donné.</summary>
    public async Task<List<CiRecord>> PerimeterItemsAsync(int perimeter, int after, CancellationToken ct) =>
        (await http.GetFromJsonAsync<List<CiRecord>>($"internal/configuration-items?perimeter={perimeter}&after={after}", Json, ct))!;

    /// <summary>Des CI désignés, dans la vue complète (50 par appel au plus).</summary>
    public async Task<List<CiRecord>> CiRecordsAsync(IReadOnlyCollection<int> ids, CancellationToken ct) =>
        ids.Count == 0 ? [] : (await http.GetFromJsonAsync<List<CiRecord>>($"internal/configuration-items?ids={string.Join(',', ids)}", Json, ct))!;

    /// <summary>CMA-04 : conventions de nommage ; aucune si l'API n'en sert pas (404).</summary>
    public async Task<List<NamingRule>> NamingAsync(CancellationToken ct)
    {
        using var res = await http.GetAsync("internal/assisted-cmdb/naming", ct);
        if (res.StatusCode == HttpStatusCode.NotFound) return [];
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<List<NamingRule>>(Json, ct) ?? [];
    }

    /// <summary>
    /// CMA-06 : paires du périmètre dont la fusion a été rejetée, identifiants dans l'ordre croissant. Sans elles, l'analyse
    /// échoue : une paire rejetée n'est plus jamais soumise au modèle.
    /// </summary>
    public async Task<HashSet<(int, int)>> DeclinedPairsAsync(int perimeter, CancellationToken ct) =>
        ((await http.GetFromJsonAsync<List<int[]>>($"internal/assisted-cmdb/declined-pairs?perimeter={perimeter}", Json, ct)) ?? [])
            .Where(p => p.Length == 2).Select(p => (Math.Min(p[0], p[1]), Math.Max(p[0], p[1]))).ToHashSet();

    /// <summary>CMA-08 : une tâche de découverte finie et les actifs qu'elle laisse au sas sans rapprochement ; null si elle a disparu.</summary>
    public async Task<AssistedCmdb.StagingRead?> StagingAsync(int job, CancellationToken ct)
    {
        using var res = await http.GetAsync($"internal/discovery/staging?job={job}", ct);
        if (res.StatusCode == HttpStatusCode.NotFound) return null;
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<AssistedCmdb.StagingRead>(Json, ct);
    }

    /// <summary>TRI-06 : une page des incidents ouverts, avec leurs CI reliés.</summary>
    public async Task<OpenPage> OpenIncidentsAsync(int after, CancellationToken ct) =>
        (await http.GetFromJsonAsync<OpenPage>($"internal/incidents/open?after={after}&limit=500", Json, ct))!;

    /// <summary>TRI-07 : signalement d'une panne probable, formée, changée ou défaite.</summary>
    public async Task<bool> SignalOutageAsync(OutageSignal signal, CancellationToken ct)
    {
        using var res = await http.PostAsJsonAsync("internal/outages", signal, Json, ct);
        return res.IsSuccessStatusCode;
    }

    /// <summary>OPT-14 : dépôt d'une proposition ; faux si l'API la refuse (champ, valeur, cible, fonctionnalité coupée).</summary>
    public async Task<bool> ProposeAsync(ProposalInput proposal, CancellationToken ct)
    {
        using var res = await http.PostAsJsonAsync("internal/proposals", proposal, Json, ct);
        return res.IsSuccessStatusCode;
    }
}
