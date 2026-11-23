using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SAloha.Api.Core.Features;

/// <summary>
/// Contrôle de santé des conteneurs des fonctionnalités optionnelles (OPT-01) :
/// <c>GET {adresse}/health</c>, qui répond <c>{ "version": "…", "features": ["…"] }</c>.
/// Le conteneur <c>features</c> exige son jeton (<c>Features:Token</c>, OPT-10) ; le conteneur
/// <c>mcp</c> n'en a pas (ADR-0024). Le dernier résultat est gardé pour la page d'administration.
/// </summary>
public class ContainerProbe(HttpClient http, IConfiguration config)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    private static readonly ConcurrentDictionary<string, ContainerState> last = new();

    private string? UrlOf(string container) => (container == FeatureCatalog.McpContainer ? config["Mcp:Url"] : config["Features:Url"])?.Trim().TrimEnd('/');

    public bool Configured(string container) => !string.IsNullOrEmpty(UrlOf(container));

    /// <summary>Dernier contrôle connu, sans appel réseau ; null s'il n'y en a pas encore.</summary>
    public static ContainerState? Last(string container) => last.GetValueOrDefault(container);

    public async Task<ContainerState> ProbeAsync(string container, CancellationToken ct = default)
    {
        var url = UrlOf(container);
        var state = string.IsNullOrEmpty(url)
            ? new ContainerState(container, false, false, false, null, [], "Adresse non configurée.", DateTime.UtcNow)
            : await CallAsync(container, url, ct);
        last[container] = state;
        return state;
    }

    private async Task<ContainerState> CallAsync(string container, string url, CancellationToken ct)
    {
        ContainerState Fail(bool reachable, string message) => new(container, true, reachable, false, null, [], message, DateTime.UtcNow);
        if (!Uri.TryCreate(url + "/health", UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return Fail(false, "Adresse invalide : une URL http(s).");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        var token = config["Features:Token"];
        if (container == FeatureCatalog.FeaturesContainer && !string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            using var response = await http.SendAsync(request, timeout.Token);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return Fail(true, "Jeton refusé par le conteneur.");
            if (!response.IsSuccessStatusCode) return Fail(true, $"Réponse {(int)response.StatusCode} au contrôle de santé.");
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(timeout.Token), cancellationToken: timeout.Token);
            var root = json.RootElement;
            var version = root.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var features = root.TryGetProperty("features", out var f) && f.ValueKind == JsonValueKind.Array
                ? f.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToArray() : [];
            return new ContainerState(container, true, true, true, version, features,
                $"Joignable{(version is null ? "" : $", version {version}")}.", DateTime.UtcNow);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Fail(false, $"Injoignable : pas de réponse en {Timeout.TotalSeconds:0} s.");
        }
        catch (HttpRequestException ex)
        {
            return Fail(false, $"Injoignable : {ex.Message.Split('\n')[0]}");
        }
        catch (JsonException)
        {
            return Fail(true, "Réponse illisible au contrôle de santé.");
        }
    }
}
