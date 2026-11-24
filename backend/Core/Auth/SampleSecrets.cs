namespace SAloha.Api.Core.Auth;

/// <summary>
/// S7 : hors développement, l'API refuse de démarrer avec les valeurs d'exemple livrées dans
/// <c>appsettings.json</c> et <c>docker-compose.demo.yml</c> (clé JWT, clé du coffre, jeton du conteneur
/// <c>features</c>, mots de passe) — une clé JWT connue de tous permet de
/// fabriquer des jetons valides —, ni avec une clé JWT trop courte ou un mot de passe d'annuaire vide. Le mode démonstration, public par construction, l'accepte
/// explicitement (<c>Security:AllowSampleSecrets</c>) et le journalise en avertissement.
/// </summary>
public static class SampleSecrets
{
    public const string SampleJwtKey = "CHANGEZ-CETTE-CLE-SECRETE-DE-64-CARACTERES-MINIMUM-0123456789ab";
    private const string SampleBindPassword = "MotDePasseServiceAccount";
    private const string SampleDbPassword = "ChangeMe_Str0ng!";
    public const string SampleVaultKey = "CHANGEZ-CETTE-CLE-DU-COFFRE-DE-32-CARACTERES-MINIMUM-0123456789";
    /// <summary>Jeton d'exemple du conteneur <c>features</c> (OPT-10), le même que celui que le conteneur refuse.</summary>
    public const string SampleFeaturesToken = "CHANGEZ-CE-JETON-DU-CONTENEUR-FEATURES-DE-32-CARACTERES-MINIMUM";

    /// <summary>Réglages restés à leur valeur d'exemple, ou trop faibles.</summary>
    public static IReadOnlyList<string> Problems(IConfiguration config)
    {
        var problems = new List<string>();
        var key = config["Jwt:Key"] ?? "";
        if (key == SampleJwtKey) problems.Add("Jwt:Key vaut la clé d'exemple");
        else if (key.Length < 64) problems.Add("Jwt:Key fait moins de 64 caractères");
        // CFG-45 : la clé d'exemple du coffre rendrait les secrets de scan déchiffrables par tous.
        if (config["Discovery:VaultKey"] == SampleVaultKey) problems.Add("Discovery:VaultKey vaut la clé d'exemple");
        // OPT-10 : le jeton d'exemple ouvrirait le port interne à quiconque lit docker-compose.demo.yml.
        if (config["Features:Token"] == SampleFeaturesToken) problems.Add("Features:Token vaut le jeton d'exemple");
        // S1 : vide dans le compose à défaut de LDAP_BIND_PASSWORD. Un bind simple sans mot de passe est un bind
        // anonyme (RFC 4513 § 5.1.2), que certains annuaires acceptent : il ne vaut jamais le compte de service.
        var bindPassword = config["Ldap:BindPassword"];
        if (bindPassword == SampleBindPassword) problems.Add("Ldap:BindPassword vaut le mot de passe d'exemple");
        else if (string.IsNullOrEmpty(bindPassword)) problems.Add("Ldap:BindPassword est vide");
        if ((config.GetConnectionString("Default") ?? "").Contains(SampleDbPassword, StringComparison.Ordinal))
            problems.Add("ConnectionStrings:Default contient le mot de passe d'exemple");
        return problems;
    }

    /// <summary>Contrôle au démarrage ; lève une exception explicite si le démarrage doit être refusé.</summary>
    public static void Check(IConfiguration config, IHostEnvironment env, ILogger logger)
    {
        if (env.IsDevelopment()) return;
        var problems = Problems(config);
        if (problems.Count == 0) return;
        var list = string.Join(" ; ", problems);
        if (config.GetValue<bool>("Security:AllowSampleSecrets"))
        {
            logger.LogWarning("Valeurs d'exemple acceptées (Security:AllowSampleSecrets, mode démonstration) : {Problems}. Jamais en production.", list);
            return;
        }
        throw new InvalidOperationException(
            $"Démarrage refusé : {list}. Fournir de vrais secrets (variables d'environnement ou secrets de l'orchestrateur) — docs/reference/securite.md.");
    }
}
