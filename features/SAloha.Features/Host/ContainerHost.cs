using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace SAloha.Features.Host;

/// <summary>
/// Une fonctionnalité exécutée par ce conteneur. Sa clé est celle du catalogue de l'API
/// (<c>FeatureCatalog</c>) : le contrôle de santé la déclare, et l'API la rend disponible (OPT-01).
/// </summary>
public interface IContainerFeature
{
    string Key { get; }
}

/// <summary>Une fonctionnalité qui sert des routes à l'API (OPT-16 : réponses attendues par un écran), derrière le jeton.</summary>
public interface IEndpointFeature
{
    void MapEndpoints(IEndpointRouteBuilder app);
}

/// <summary>
/// Hôte du conteneur <c>features</c> : jeton du conteneur (OPT-10), contrôle de santé (OPT-01), client du
/// port interne de l'API et fil d'événements (OPT-12). Chaque fonctionnalité s'inscrit ici comme
/// <see cref="IContainerFeature"/> : le tri assisté des tickets.
/// </summary>
public static class ContainerHost
{
    public const int MinTokenLength = 32;

    /// <summary>Valeur d'exemple de <c>docker-compose.demo.yml</c>, connue de tous : refusée hors démonstration (S7).</summary>
    public const string SampleToken = "CHANGEZ-CE-JETON-DU-CONTENEUR-FEATURES-DE-32-CARACTERES-MINIMUM";

    public static void Configure(WebApplicationBuilder builder)
    {
        // Port interne de l'API (Api:Url), avec le jeton du conteneur ; puis le fil d'événements (OPT-12).
        builder.Services.AddHttpClient<ApiClient>(c => c.Timeout = TimeSpan.FromSeconds(30));
        // Service d'embeddings du compose (Embeddings:Url) : le sens de la corrélation du tri (TRI-06).
        builder.Services.AddHttpClient<EmbeddingClient>();
        builder.Services.AddSingleton<EventLoop>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<EventLoop>());
        // Une fonctionnalité s'inscrit comme IContainerFeature (déclarée par /health) et, si elle lit le fil,
        // comme IEventHandler. La CMDB assistée viendra à l'étape 5 du chantier F14.
        builder.Services.AddSingleton<TicketTriage.TicketTriage>();
        builder.Services.AddSingleton<IContainerFeature>(sp => sp.GetRequiredService<TicketTriage.TicketTriage>());
        builder.Services.AddSingleton<IEventHandler>(sp => sp.GetRequiredService<TicketTriage.TicketTriage>());
    }

    public static void Map(WebApplication app)
    {
        CheckToken(app.Configuration, app.Environment, app.Logger);
        var expected = Encoding.UTF8.GetBytes(app.Configuration["Features:Token"] ?? "");
        // OPT-10 : chaque appel de l'API porte le jeton ; sans lui, rien ne répond, pas même la santé.
        app.Use(async (http, next) =>
        {
            if (!Authorized(http.Request.Headers.Authorization.ToString(), expected))
            {
                http.Response.StatusCode = StatusCodes.Status401Unauthorized;
                http.Response.Headers.WWWAuthenticate = "Bearer";
                return;
            }
            await next();
        });
        // OPT-01 : version et fonctionnalités déclarées ; l'API attend la réponse 3 s au plus.
        app.MapGet("/health", (IEnumerable<IContainerFeature> features) =>
            Results.Json(new { version = Version, features = features.Select(f => f.Key).Distinct().ToArray() }));
        // OPT-16 : routes appelées par l'API pour un écran (suggestions à la saisie, TRI-02).
        foreach (var feature in app.Services.GetServices<IContainerFeature>().OfType<IEndpointFeature>().Distinct())
            feature.MapEndpoints(app);
    }

    /// <summary>Version de l'image (ADM-21), sans le commit que le SDK ajoute après « + ».</summary>
    public static string Version { get; } =
        (typeof(ContainerHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "").Split('+')[0];

    /// <summary>Jeton présenté conforme : 32 caractères au moins de part et d'autre, comparé en temps constant.</summary>
    public static bool Authorized(string header, byte[] expected)
    {
        if (expected.Length < MinTokenLength || !header.StartsWith("Bearer ", StringComparison.Ordinal)) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(header["Bearer ".Length..]), expected);
    }

    /// <summary>
    /// Au démarrage : jeton absent ou trop court, le conteneur démarre mais n'ouvre rien (401), et le dit ;
    /// jeton d'exemple hors développement, démarrage refusé, sauf en démonstration (<c>Security:AllowSampleSecrets</c>).
    /// Le jeton n'est jamais journalisé, seulement le nom du réglage.
    /// </summary>
    public static void CheckToken(IConfiguration config, IHostEnvironment env, ILogger logger)
    {
        var token = config["Features:Token"] ?? "";
        if (token.Length < MinTokenLength)
        {
            logger.LogError("Features:Token absent ou de moins de {Min} caractères : le conteneur refuse tout appel (401).", MinTokenLength);
            return;
        }
        if (token != SampleToken || env.IsDevelopment()) return;
        if (config.GetValue<bool>("Security:AllowSampleSecrets"))
        {
            logger.LogWarning("Features:Token vaut le jeton d'exemple, accepté (Security:AllowSampleSecrets, mode démonstration). Jamais en production.");
            return;
        }
        throw new InvalidOperationException(
            "Démarrage refusé : Features:Token vaut le jeton d'exemple. Fournir un vrai secret, le même qu'à l'API — docs/reference/securite.md.");
    }
}
