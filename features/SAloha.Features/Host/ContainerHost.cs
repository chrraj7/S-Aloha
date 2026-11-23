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

/// <summary>
/// Hôte du conteneur <c>features</c> : jeton du conteneur (OPT-10) et contrôle de santé (OPT-01).
/// Il n'exécute encore aucune fonctionnalité : chacune s'inscrit ici comme <see cref="IContainerFeature"/>.
/// </summary>
public static class ContainerHost
{
    public const int MinTokenLength = 32;

    /// <summary>Valeur d'exemple de <c>docker-compose.demo.yml</c>, connue de tous : refusée hors démonstration (S7).</summary>
    public const string SampleToken = "CHANGEZ-CE-JETON-DU-CONTENEUR-FEATURES-DE-32-CARACTERES-MINIMUM";

    public static void Configure(WebApplicationBuilder builder)
    {
        // Les fonctionnalités s'inscrivent ici (services.AddSingleton<IContainerFeature, …>) :
        // le tri assisté des tickets à l'étape 3 du chantier F14, la CMDB assistée à l'étape 5.
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
