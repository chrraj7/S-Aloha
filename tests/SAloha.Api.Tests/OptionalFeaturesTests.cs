using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SAloha.Api.Core.Audit;
using SAloha.Api.Core.Data;
using SAloha.Api.Core.Features;

namespace SAloha.Api.Tests;

/// <summary>Conteneur factice des fonctionnalités : contrôle de santé, jeton, panne à la demande.</summary>
public sealed class FakeFeaturesContainer : IAsyncLifetime
{
    public const string Token = "jeton-du-conteneur-de-test";
    private WebApplication _app = null!;
    public string Url { get; private set; } = "";
    public volatile bool Up = true;
    public string[] Features = [FeatureCatalog.AssistedTriage, FeatureCatalog.AssistedCmdb];

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0));
        _app = builder.Build();
        _app.MapGet("/health", (HttpContext c) =>
            !Up ? Results.StatusCode(503)
            : c.Request.Headers.Authorization.ToString() != $"Bearer {Token}" ? Results.StatusCode(401)
            : Results.Json(new { version = "essai", features = Features }));
        await _app.StartAsync();
        Url = _app.Urls.Single();
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();
}

/// <summary>Fonctionnalités optionnelles : activation, coupure, conteneurs, origine du journal (OPT-01 à OPT-08, ADR-0023).</summary>
[Collection("api")]
public class OptionalFeaturesTests(ApiFixture api, FakeFeaturesContainer container) : IClassFixture<FakeFeaturesContainer>, IAsyncLifetime
{
    public Task InitializeAsync() => ResetAsync();
    public Task DisposeAsync() => ResetAsync();

    /// <summary>Chaque test part de fonctionnalités toutes coupées, conteneur en marche.</summary>
    private async Task ResetAsync()
    {
        // Le démarrage de l'API applique les migrations : lancé seul, ce test arrive avant elles.
        await api.Client().GetAsync("/health/live");
        container.Up = true;
        container.Features = [FeatureCatalog.AssistedTriage, FeatureCatalog.AssistedCmdb];
        await using var db = api.Database();
        await db.OptionalFeatures.ExecuteDeleteAsync();
    }

    private WebApplicationFactory<Program> Factory(string? url, string? token = FakeFeaturesContainer.Token) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Default", api.Database().Database.GetConnectionString())
            .UseSetting("Jwt:Key", ApiFixture.JwtKey)
            .UseSetting("Jwt:Issuer", "S-Aloha")
            .UseSetting("Discovery:IntervalMinutes", "0")
            .UseSetting("Discovery:VaultKey", "cle-du-coffre-de-test-uniquement-0123456789")
            .UseSetting("Features:Url", url ?? "")
            .UseSetting("Features:Token", token ?? ""));

    private static HttpClient Client(WebApplicationFactory<Program> factory, string role = "Admin", string username = "hery.rakoto")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFixture.Token(username, role));
        return client;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode} : {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static JsonElement Feature(JsonElement list, string key) => list.EnumerateArray().Single(f => f.GetProperty("key").GetString() == key);

    private static Task<HttpResponseMessage> Switch(HttpClient client, string key, bool enabled) =>
        client.PutAsJsonAsync($"/api/admin/features/{key}", new { enabled });

    private static async Task<string> Message(HttpResponseMessage res)
    {
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("message").GetString()!;
    }

    [Fact]
    public async Task OPT_01_sans_conteneur_configure_toutes_sont_indisponibles()
    {
        var list = await Json(await api.Client().GetAsync("/api/admin/features"));
        Assert.Equal(3, list.GetArrayLength());
        foreach (var f in list.EnumerateArray())
        {
            Assert.Equal("Indisponible", f.GetProperty("state").GetString());
            Assert.False(f.GetProperty("containerState").GetProperty("configured").GetBoolean());
        }
        Assert.Equal("mcp", Feature(list, FeatureCatalog.McpServer).GetProperty("container").GetString());
    }

    [Fact]
    public async Task OPT_01_le_conteneur_qui_declare_une_fonctionnalite_la_rend_disponible()
    {
        container.Features = [FeatureCatalog.AssistedTriage];
        using var factory = Factory(container.Url);
        var list = await Json(await Client(factory).GetAsync("/api/admin/features"));
        var triage = Feature(list, FeatureCatalog.AssistedTriage);
        Assert.Equal("Coupée", triage.GetProperty("state").GetString());
        Assert.True(triage.GetProperty("available").GetBoolean());
        Assert.Equal("essai", triage.GetProperty("containerState").GetProperty("version").GetString());
        // Non déclarée par le conteneur, ou dans un autre conteneur (non configuré) : indisponible.
        Assert.Equal("Indisponible", Feature(list, FeatureCatalog.AssistedCmdb).GetProperty("state").GetString());
        Assert.Equal("Indisponible", Feature(list, FeatureCatalog.McpServer).GetProperty("state").GetString());
    }

    [Fact]
    public async Task OPT_02_page_reservee_a_l_admin()
    {
        var user = api.Client("User", "fara.rasoa");
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/admin/features")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Switch(user, FeatureCatalog.AssistedTriage, true)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Switch(api.Client(), "inconnue", true)).StatusCode);
    }

    [Fact]
    public async Task OPT_03_activation_refusee_sans_conteneur_ou_avec_un_jeton_refuse()
    {
        Assert.Contains("non configuré", await Message(await Switch(api.Client(), FeatureCatalog.AssistedTriage, true)));

        using (var wrongToken = Factory(container.Url, "mauvais-jeton"))
            Assert.Contains("Jeton refusé", await Message(await Switch(Client(wrongToken), FeatureCatalog.AssistedTriage, true)));

        container.Up = false;
        using (var down = Factory(container.Url))
            Assert.Contains("503", await Message(await Switch(Client(down), FeatureCatalog.AssistedTriage, true)));

        // Port fermé : injoignable.
        using (var closed = Factory("http://127.0.0.1:9"))
            Assert.Contains("injoignable", await Message(await Switch(Client(closed), FeatureCatalog.AssistedTriage, true)));

        await using var db = api.Database();
        Assert.False(await db.OptionalFeatures.AnyAsync(f => f.Enabled));
    }

    [Fact]
    public async Task OPT_03_dependance_obligatoire_absente_refuse_dependance_facultative_avertit()
    {
        using var factory = Factory(container.Url);
        var admin = Client(factory);
        // CMDB assistée : modèle de langage non réglé.
        Assert.Contains("Modèle de langage", await Message(await Switch(admin, FeatureCatalog.AssistedCmdb, true)));
        // Tri assisté : sans service d'embeddings (non configuré ici), activé avec un avertissement.
        var result = await Json(await Switch(admin, FeatureCatalog.AssistedTriage, true));
        Assert.Equal("Activée", result.GetProperty("feature").GetProperty("state").GetString());
        Assert.Contains(result.GetProperty("warnings").EnumerateArray(), w => w.GetString()!.Contains("Service d'embeddings"));
    }

    [Fact]
    public async Task OPT_04_get_features_ne_rend_que_les_cles_activees_et_suit_la_coupure()
    {
        using var factory = Factory(container.Url);
        var admin = Client(factory);
        var user = Client(factory, "User", "fara.rasoa");
        Assert.Empty((await Json(await user.GetAsync("/api/features"))).EnumerateArray());

        await Json(await Switch(admin, FeatureCatalog.AssistedTriage, true));
        var keys = (await Json(await user.GetAsync("/api/features"))).EnumerateArray().Select(k => k.GetString()).ToList();
        Assert.Equal([FeatureCatalog.AssistedTriage], keys);

        // Même instance : la coupure est prise en compte aussitôt (cache invalidé).
        await Json(await Switch(admin, FeatureCatalog.AssistedTriage, false));
        Assert.Empty((await Json(await user.GetAsync("/api/features"))).EnumerateArray());
    }

    private sealed class StubGate(params string[] enabled) : IFeatureGate
    {
        public Task<IReadOnlySet<string>> EnabledAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlySet<string>>(enabled.ToHashSet());
        public async Task<bool> IsEnabledAsync(string key, CancellationToken ct = default) => (await EnabledAsync(ct)).Contains(key);
        public void Invalidate() { }
    }

    private static async Task<(IActionResult? Result, bool Ran)> Filter(IFeatureGate gate)
    {
        var http = new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton(gate).BuildServiceProvider() };
        var context = new ActionExecutingContext(new ActionContext(http, new RouteData(), new ActionDescriptor()),
            [], new Dictionary<string, object?>(), controller: new object());
        var ran = false;
        await new RequiresFeatureAttribute(FeatureCatalog.AssistedTriage).OnActionExecutionAsync(context, () =>
        {
            ran = true;
            return Task.FromResult(new ActionExecutedContext(context, [], new object()));
        });
        return (context.Result, ran);
    }

    [Fact]
    public async Task OPT_04_une_route_d_une_fonctionnalite_coupee_repond_404()
    {
        var (off, ranOff) = await Filter(new StubGate());
        Assert.IsType<NotFoundResult>(off);
        Assert.False(ranOff);
        var (on, ranOn) = await Filter(new StubGate(FeatureCatalog.AssistedTriage));
        Assert.Null(on);
        Assert.True(ranOn);
        Assert.Throws<ArgumentException>(() => new RequiresFeatureAttribute("inconnue"));
    }

    [Fact]
    public async Task OPT_05_conteneur_perdu_la_fonctionnalite_reste_activee_et_la_sante_le_signale()
    {
        using var factory = Factory(container.Url);
        var admin = Client(factory);
        await Json(await Switch(admin, FeatureCatalog.AssistedTriage, true));

        container.Up = false;
        var triage = Feature(await Json(await admin.GetAsync("/api/admin/features")), FeatureCatalog.AssistedTriage);
        Assert.Equal("Activée", triage.GetProperty("state").GetString());
        Assert.False(triage.GetProperty("available").GetBoolean());

        var components = (await Json(await admin.GetAsync("/api/admin/health"))).GetProperty("components").EnumerateArray().ToList();
        var line = components.Single(c => c.GetProperty("name").GetString() == "Fonctionnalité : Tri assisté des tickets");
        Assert.Equal("Dégradé", line.GetProperty("state").GetString());
        var host = components.Single(c => c.GetProperty("name").GetString() == "Conteneur « features »");
        Assert.Equal("Dégradé", host.GetProperty("state").GetString());
        Assert.Contains("Tri assisté des tickets", host.GetProperty("message").GetString());

        // Le conteneur revient : la fonctionnalité est de nouveau saine, sans réactivation.
        container.Up = true;
        components = (await Json(await admin.GetAsync("/api/admin/health"))).GetProperty("components").EnumerateArray().ToList();
        Assert.Equal("OK", components.Single(c => c.GetProperty("name").GetString() == "Fonctionnalité : Tri assisté des tickets")
            .GetProperty("state").GetString());
    }

    [Fact]
    public async Task OPT_07_activation_et_coupure_tracees_au_journal_d_audit()
    {
        using var factory = Factory(container.Url);
        var admin = Client(factory);
        // Repère par identifiant, pas par date : un test précédent a pu tracer une activation la même seconde.
        long last;
        await using (var before = api.Database())
            last = await before.AuditEntries.MaxAsync(a => (long?)a.Id) ?? 0;
        await Json(await Switch(admin, FeatureCatalog.AssistedTriage, true));
        await Json(await Switch(admin, FeatureCatalog.AssistedTriage, true));   // inchangé : rien de plus au journal
        await Json(await Switch(admin, FeatureCatalog.AssistedTriage, false));

        await using var db = api.Database();
        var lines = await db.AuditEntries.AsNoTracking()
            .Where(a => a.EntityType == FeaturesController.AuditType && a.Reference == FeaturesController.AuditReference && a.Id > last)
            .OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.Equal("Tri assisté des tickets", l.Field));
        Assert.Equal(("coupée", "activée"), (lines[0].OldValue, lines[0].NewValue));
        Assert.Equal(("activée", "coupée"), (lines[1].OldValue, lines[1].NewValue));
        Assert.All(lines, l => Assert.Equal(AuditEntry.Saisie, l.Origin));
        var row = await db.OptionalFeatures.AsNoTracking().SingleAsync(f => f.Key == FeatureCatalog.AssistedTriage);
        Assert.Equal("hery.rakoto", row.UpdatedBy);
    }

    [Fact]
    public async Task OPT_08_origine_saisie_par_defaut_portee_par_un_bloc_et_filtrable()
    {
        var admin = api.Client();
        var created = await Json(await admin.PostAsJsonAsync("/api/incidents",
            new { title = "Origine du journal (OPT-08)", impact = "Faible", urgency = "Faible" }));
        var id = created.GetProperty("id").GetInt32();
        var reference = created.GetProperty("reference").GetString()!;
        var history = await Json(await admin.GetAsync($"/api/audit?type=incident&id={id}"));
        Assert.All(history.EnumerateArray(), e => Assert.Equal("Saisie", e.GetProperty("origin").GetString()));

        // Une modification faite dans un bloc AuditOrigin.Use porte l'origine du bloc, et elle seule.
        using var factory = Factory(null);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var incident = await db.Incidents.SingleAsync(i => i.Id == id);
            using (AuditOrigin.Use(AuditEntry.AssistedTriage))
            {
                incident.Category = "Messagerie";
                await db.SaveChangesAsync();
            }
            incident.Urgency = "Moyenne";
            await db.SaveChangesAsync();
        }
        var lines = (await Json(await admin.GetAsync($"/api/audit?type=incident&id={id}"))).EnumerateArray().ToList();
        Assert.Equal("Tri assisté des tickets", lines.Single(e => e.GetProperty("field").GetString() == "category").GetProperty("origin").GetString());
        Assert.Equal("Saisie", lines.Single(e => e.GetProperty("field").GetString() == "urgency").GetProperty("origin").GetString());

        var filtered = await Json(await admin.GetAsync(
            $"/api/admin/audit?reference={reference}&origin={Uri.EscapeDataString(AuditEntry.AssistedTriage)}"));
        Assert.Equal(1, filtered.GetProperty("total").GetInt32());
        var csv = await (await admin.GetAsync($"/api/admin/audit/export?reference={reference}")).Content.ReadAsStringAsync();
        Assert.Contains(";Motif;Origine", csv);
        Assert.Contains(";Tri assisté des tickets", csv);
        Assert.Throws<ArgumentException>(() => AuditOrigin.Use("Inventée"));
    }
}
