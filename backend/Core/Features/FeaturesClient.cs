using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace SAloha.Api.Core.Features;

/// <summary>
/// OPT-16 : appels de l'API vers le conteneur <c>features</c>, réservés aux réponses qu'un écran attend
/// (suggestions à la saisie, TRI-02). Délai de 2 secondes au plus ; un échec, un délai dépassé ou un
/// conteneur absent rendent une réponse vide (<c>null</c>), journalisée, jamais une erreur à l'écran.
/// Le reste du travail du conteneur passe par le fil d'événements (OPT-12), jamais par ces appels.
/// </summary>
public class FeaturesClient(HttpClient http, IConfiguration config, IFeatureGate gate, ILogger<FeaturesClient> logger)
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    /// <summary>Taille maximale d'une réponse du conteneur (réglée sur le client HTTP).</summary>
    public const int MaxResponseBytes = 1 << 20;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// <c>POST {Features:Url}{path}</c> pour la fonctionnalité <paramref name="feature"/>, avec le jeton du
    /// conteneur. Fonctionnalité coupée ou d'un autre conteneur : aucun appel (OPT-04). Le corps et le jeton
    /// ne sont jamais journalisés, seulement le chemin et la cause.
    /// </summary>
    public async Task<T?> PostAsync<T>(string feature, string path, object body, CancellationToken ct = default) where T : class
    {
        if (FeatureCatalog.ByKey(feature)?.Container != FeatureCatalog.FeaturesContainer)
            throw new ArgumentException($"Fonctionnalité inconnue du conteneur features : {feature}", nameof(feature));
        if (!await gate.IsEnabledAsync(feature, ct)) return null;

        var url = config["Features:Url"]?.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url + path, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            logger.LogWarning("Conteneur features : appel {Path} ({Feature}) sans réponse, adresse absente ou invalide (Features:Url).", path, feature);
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(body, options: Json) };
        var token = config["Features:Token"];
        if (!string.IsNullOrEmpty(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        var started = DateTime.UtcNow;
        string? failure;
        try
        {
            using var response = await http.SendAsync(request, timeout.Token);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<T>(Json, timeout.Token);
                if (result is not null) return result;
                failure = "réponse vide";
            }
            else failure = $"réponse {(int)response.StatusCode}";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // L'écran a renoncé (nouvelle frappe, page quittée) : rien à signaler.
            return null;
        }
        catch (OperationCanceledException)
        {
            failure = $"délai de {Timeout.TotalSeconds:0} s dépassé";
        }
        catch (HttpRequestException ex)
        {
            failure = $"injoignable ({ex.HttpRequestError})";
        }
        catch (JsonException)
        {
            failure = "réponse illisible";
        }
        logger.LogWarning("Conteneur features : appel {Path} ({Feature}) sans réponse, {Failure}, après {Elapsed} ms ; réponse vide rendue.",
            path, feature, failure, (int)(DateTime.UtcNow - started).TotalMilliseconds);
        return null;
    }
}
