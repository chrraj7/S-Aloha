using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using SAloha.Api.Core.Auth;
using SAloha.Api.Modules.ServiceConfigurationManagement.Discovery;

namespace SAloha.Api.Tests;

/// <summary>
/// S1 : <c>docker-compose.yml</c> ne porte aucun secret d'exemple (valeurs vides, fournies par
/// l'environnement) ; seul <c>docker-compose.demo.yml</c>, public par construction, les garde,
/// et ce sont bien celles que S7 reconnaît pour les accepter en démonstration.
/// </summary>
public class ComposeSecretsTests
{
    // Valeur de SampleSecrets.SampleDbPassword, privée.
    private const string SampleDbPassword = "ChangeMe_Str0ng!";

    private static string Read(string name)
    {
        // Les tests s'exécutent depuis bin/… : on remonte jusqu'à la racine du dépôt.
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, name);
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException(name);
    }

    [Fact]
    public void S1_docker_compose_ne_porte_aucun_secret_d_exemple()
    {
        var compose = Read("docker-compose.yml");
        Assert.DoesNotContain(SampleSecrets.SampleJwtKey, compose);
        Assert.DoesNotContain(SampleSecrets.SampleVaultKey, compose);
        Assert.DoesNotContain(SampleDbPassword, compose);
        Assert.Contains("${POSTGRES_PASSWORD:-}", compose);
        Assert.Contains("${JWT_KEY:-}", compose);
        Assert.Contains("${DISCOVERY_VAULT_KEY:-}", compose);
    }

    [Fact]
    public void S1_la_demonstration_garde_les_valeurs_d_exemple_reconnues_par_S7()
    {
        var demo = Read("docker-compose.demo.yml");
        Assert.Contains(SampleSecrets.SampleJwtKey, demo);
        Assert.Contains(SampleSecrets.SampleVaultKey, demo);
        Assert.Contains(SampleDbPassword, demo);
        Assert.Contains("Security__AllowSampleSecrets: \"true\"", demo);
    }

    [Fact]
    public void OPT_10_le_jeton_d_exemple_du_conteneur_ne_vit_que_dans_la_demonstration_et_S7_le_refuse()
    {
        var compose = Read("docker-compose.yml");
        Assert.DoesNotContain(SampleSecrets.SampleFeaturesToken, compose);
        Assert.Contains("Features__Token: \"${FEATURES_TOKEN:-}\"", compose);
        Assert.Contains("profiles: [\"fonctionnalites\"]", compose);
        // OPT-11 : le port interne de l'API ne se publie jamais.
        Assert.DoesNotContain("8081:", compose);
        // En démonstration, l'API et le conteneur reçoivent le même jeton d'exemple.
        var demo = Read("docker-compose.demo.yml");
        Assert.Equal(2, Regex.Matches(demo, Regex.Escape($"Features__Token: \"{SampleSecrets.SampleFeaturesToken}\"")).Count);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = new string('x', 64), ["Ldap:BindPassword"] = "vrai-secret", ["ConnectionStrings:Default"] = "Host=db;Password=autre",
            ["Features:Token"] = SampleSecrets.SampleFeaturesToken
        }).Build();
        Assert.Contains("Features:Token", Assert.Single(SampleSecrets.Problems(config)));
    }

    [Fact]
    public void S1_un_coffre_laisse_vide_par_le_compose_n_empeche_pas_le_demarrage_mais_refuse_d_enregistrer()
    {
        // Ce que donne docker-compose.yml sans DISCOVERY_VAULT_KEY : une valeur vide, qui
        // l'emporte sur celle d'appsettings.json.
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = new string('x', 64), ["Ldap:BindPassword"] = "vrai-secret", ["ConnectionStrings:Default"] = "Host=db;Password=autre",
            ["Discovery:VaultKey"] = ""
        }).Build();
        Assert.Empty(SampleSecrets.Problems(config));
        var vault = new CredentialVault(config);
        Assert.False(vault.Configured);
        Assert.Throws<InvalidOperationException>(() => vault.Protect("secret"));
    }
}
