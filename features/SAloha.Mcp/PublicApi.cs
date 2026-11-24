using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SAloha.Mcp;

/// <summary>Réponse d'une route publique de l'API : son statut et, en cas de succès, son corps JSON.</summary>
public sealed record ApiAnswer(HttpStatusCode Status, JsonNode? Body)
{
    public bool Ok => (int)Status is >= 200 and < 300;
}

/// <summary>
/// MCP-02 : client des routes <b>publiques de lecture</b> de l'API (<c>Api:Url</c>, son port public), toujours avec le
/// jeton personnel de l'utilisateur de la requête, jamais avec un jeton propre : le conteneur n'en a pas. Seule la
/// méthode GET existe ici. Le jeton n'est jamais journalisé.
/// </summary>
public class PublicApi
{
    /// <summary>Réponse de l'API lue en entier au plus : au-delà, l'appel échoue plutôt que d'occuper la mémoire.</summary>
    private const long MaxResponseBytes = 16 * 1024 * 1024;
    private readonly HttpClient http;
    private readonly IHttpContextAccessor context;
    private readonly ILogger<PublicApi> logger;

    public PublicApi(HttpClient http, IConfiguration config, IHttpContextAccessor context, ILogger<PublicApi> logger)
    {
        (this.http, this.context, this.logger) = (http, context, logger);
        if (UrlOf(config) is { } url) http.BaseAddress = url;
        http.MaxResponseContentBufferSize = MaxResponseBytes;
    }

    /// <summary><c>Api:Url</c>, adresse HTTP(S) absolue de l'API ; null si absente ou invalide.</summary>
    public static Uri? UrlOf(IConfiguration config)
    {
        var url = config["Api:Url"]?.Trim().TrimEnd('/');
        return !string.IsNullOrEmpty(url) && Uri.TryCreate(url + "/", UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri : null;
    }

    /// <summary>
    /// MCP-01, MCP-03, MCP-04 : le jeton présenté, tel que l'API le reconnaît (<c>GET /api/me/tokens/current</c>). Statut
    /// à rendre au client : 404 quand le serveur MCP est coupé, 401 pour un jeton absent ou refusé, 503 quand l'API ou
    /// l'annuaire ne répond pas.
    /// </summary>
    public async Task<(int Status, McpCaller? Caller)> CurrentAsync(string? authorization, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "api/me/tokens/current");
            if (authorization is not null) req.Headers.TryAddWithoutValidation("Authorization", authorization);
            using var res = await http.SendAsync(req, ct);
            if (res.StatusCode == HttpStatusCode.OK
                && await res.Content.ReadFromJsonAsync<JsonObject>(ct) is { } body
                && body["id"] is JsonValue id && id.TryGetValue<int>(out var tokenId)
                && body["username"] is JsonValue user && user.TryGetValue<string>(out var username))
                return (StatusCodes.Status200OK, new McpCaller(tokenId, username));
            return (res.StatusCode switch
            {
                HttpStatusCode.NotFound => StatusCodes.Status404NotFound,
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => StatusCodes.Status401Unauthorized,
                _ => StatusCodes.Status503ServiceUnavailable,
            }, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || ex is TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("API injoignable pour reconnaître un jeton : {Error}", ex.Message.Split('\n')[0]);
            return (StatusCodes.Status503ServiceUnavailable, null);
        }
    }

    /// <summary>MCP-02 : une route publique de lecture, avec le jeton de l'utilisateur de la requête en cours.</summary>
    public async Task<ApiAnswer> GetAsync(string path, CancellationToken ct)
    {
        var authorization = context.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authorization)) throw new InvalidOperationException("Aucun jeton d'utilisateur à relayer.");
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.TryAddWithoutValidation("Authorization", authorization);
        using var res = await http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) return new ApiAnswer(res.StatusCode, null);
        var text = await res.Content.ReadAsStringAsync(ct);
        return new ApiAnswer(res.StatusCode, text.Length == 0 ? null : JsonNode.Parse(text));
    }
}
