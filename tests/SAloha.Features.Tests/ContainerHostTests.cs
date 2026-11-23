using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using SAloha.Features.Host;

namespace SAloha.Features.Tests;

/// <summary>Hôte du conteneur « features » : jeton du conteneur (OPT-10), contrôle de santé (OPT-01).</summary>
public class ContainerHostTests
{
    private const string Token = "jeton-du-conteneur-de-test-0123456789";   // 32 caractères au moins

    private sealed class FakeFeature(string key) : IContainerFeature
    {
        public string Key { get; } = key;
    }

    private static WebApplicationFactory<Program> Factory(string? token, string environment = "Production", bool allowSample = false,
        params string[] features) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment(environment);
            b.UseSetting("Features:Token", token ?? "");
            b.UseSetting("Security:AllowSampleSecrets", allowSample ? "true" : "false");
            b.ConfigureServices(s =>
            {
                foreach (var key in features) s.AddSingleton<IContainerFeature>(new FakeFeature(key));
            });
        });

    private static Task<HttpResponseMessage> Health(WebApplicationFactory<Program> factory, string? bearer)
    {
        var client = factory.CreateClient();
        if (bearer is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return client.GetAsync("/health");
    }

    [Fact]
    public async Task OPT_01_le_controle_de_sante_rend_la_version_et_les_fonctionnalites_declarees()
    {
        // Joignable, jeton reconnu ; le tri assisté des tickets est déclaré par sa clé, celle du catalogue de l'API.
        using (var host = Factory(Token))
        {
            var res = await Health(host, Token);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
            Assert.False(string.IsNullOrEmpty(body.GetProperty("version").GetString()));
            Assert.DoesNotContain("+", body.GetProperty("version").GetString());
            Assert.Equal(["assisted-ticket-triage"], body.GetProperty("features").EnumerateArray().Select(e => e.GetString()));
        }
        // Une fonctionnalité de plus s'ajoute à la liste ; une clé inscrite deux fois n'y figure qu'une fois.
        using var withMore = Factory(Token, features: ["assisted-cmdb", "assisted-ticket-triage"]);
        var declared = JsonDocument.Parse(await (await Health(withMore, Token)).Content.ReadAsStringAsync()).RootElement.GetProperty("features");
        Assert.Equal(["assisted-ticket-triage", "assisted-cmdb"], declared.EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task OPT_10_sans_le_bon_jeton_rien_ne_repond()
    {
        using var factory = Factory(Token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Health(factory, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Health(factory, Token + "x")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Health(factory, Token[..^1])).StatusCode);
        // Route inconnue : 401 aussi, le jeton passe avant tout.
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/internal/events")).StatusCode);
    }

    [Fact]
    public async Task OPT_10_un_jeton_absent_ou_trop_court_n_ouvre_rien()
    {
        using (var none = Factory(null))
            Assert.Equal(HttpStatusCode.Unauthorized, (await Health(none, "")).StatusCode);
        const string court = "jeton-de-31-caracteres-01234567";
        Assert.Equal(31, court.Length);
        using var tooShort = Factory(court);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Health(tooShort, court)).StatusCode);
    }

    [Fact]
    public async Task OPT_10_le_jeton_d_exemple_est_refuse_hors_demonstration()
    {
        static IConfiguration Config(string token, bool allow) => new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Features:Token"] = token, ["Security:AllowSampleSecrets"] = allow ? "true" : null }).Build();
        static HostingEnvironment Env(string name) => new() { EnvironmentName = name };

        var refused = Assert.Throws<InvalidOperationException>(() =>
            ContainerHost.CheckToken(Config(ContainerHost.SampleToken, false), Env("Production"), NullLogger.Instance));
        Assert.Contains("Démarrage refusé", refused.Message);
        Assert.DoesNotContain(ContainerHost.SampleToken, refused.Message);   // le message nomme le réglage, pas sa valeur
        ContainerHost.CheckToken(Config(ContainerHost.SampleToken, true), Env("Production"), NullLogger.Instance);   // démonstration
        ContainerHost.CheckToken(Config(ContainerHost.SampleToken, false), Env("Development"), NullLogger.Instance);   // poste de développement
        ContainerHost.CheckToken(Config(Token, false), Env("Production"), NullLogger.Instance);

        // Au démarrage réel de l'hôte : refusé sans le mode démonstration, servi avec.
        using (var production = Factory(ContainerHost.SampleToken))
            Assert.Contains("Features:Token", Assert.ThrowsAny<Exception>(() => production.CreateClient()).ToString());
        using var demo = Factory(ContainerHost.SampleToken, allowSample: true);
        Assert.Equal(HttpStatusCode.OK, (await Health(demo, ContainerHost.SampleToken)).StatusCode);
    }

    [Fact]
    public void OPT_10_le_jeton_d_exemple_est_celui_que_la_demonstration_donne_au_conteneur()
    {
        // Les tests s'exécutent depuis bin/… : on remonte jusqu'à la racine du dépôt.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docker-compose.demo.yml"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var demo = File.ReadAllText(Path.Combine(dir.FullName, "docker-compose.demo.yml"));
        Assert.Contains($"Features__Token: \"{ContainerHost.SampleToken}\"", demo);
        Assert.Contains("Security__AllowSampleSecrets: \"true\"", demo[demo.IndexOf("\n  features:", StringComparison.Ordinal)..]);
    }
}
