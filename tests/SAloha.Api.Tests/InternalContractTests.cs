using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SAloha.Api.Core.Data;
using SAloha.Api.Core.Features;

namespace SAloha.Api.Tests;

/// <summary>Contrat entre l'API et le conteneur des fonctionnalités : port interne, jeton, fil d'événements (OPT-10 à OPT-12).</summary>
[Collection("api")]
public class InternalContractTests(ApiFixture api, FakeFeaturesContainer container) : IClassFixture<FakeFeaturesContainer>, IAsyncLifetime
{
    private const int InternalPort = 8081;

    public async Task InitializeAsync()
    {
        // Le démarrage de l'API applique les migrations : lancé seul, ce test arrive avant elles.
        await api.Client().GetAsync("/health/live");
        await ResetAsync();
    }

    public Task DisposeAsync() => ResetAsync();

    private async Task ResetAsync()
    {
        container.Up = true;
        await using var db = api.Database();
        await db.OptionalFeatures.ExecuteDeleteAsync();
    }

    private WebApplicationFactory<Program> Factory(string token = FakeFeaturesContainer.Token) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Default", api.Database().Database.GetConnectionString())
            .UseSetting("Jwt:Key", ApiFixture.JwtKey)
            .UseSetting("Jwt:Issuer", "S-Aloha")
            .UseSetting("Discovery:IntervalMinutes", "0")
            .UseSetting("Discovery:VaultKey", "cle-du-coffre-de-test-uniquement-0123456789")
            .UseSetting("Features:Url", container.Url)
            .UseSetting("Features:Token", token)
            .UseSetting("Features:InternalPort", InternalPort.ToString()));

    private static HttpClient Admin(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFixture.Token("hery.rakoto", "Admin"));
        return client;
    }

    /// <summary>Requête reçue sur le port interne (le serveur de test n'ouvre pas de socket : le port est posé sur la connexion).</summary>
    private static async Task<(int Status, JsonElement? Body)> Internal(WebApplicationFactory<Program> factory, string path, string? bearer)
    {
        var uri = new Uri("http://api" + path);
        var context = await factory.Server.SendAsync(c =>
        {
            c.Request.Method = "GET";
            c.Request.Path = uri.AbsolutePath;
            c.Request.QueryString = new QueryString(uri.Query);
            c.Connection.LocalPort = InternalPort;
            if (bearer is not null) c.Request.Headers.Authorization = $"Bearer {bearer}";
        });
        var text = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return (context.Response.StatusCode, text.StartsWith('{') ? JsonDocument.Parse(text).RootElement : null);
    }

    private static async Task Enable(HttpClient admin, string key)
    {
        var res = await admin.PutAsJsonAsync($"/api/admin/features/{key}", new { enabled = true });
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
    }

    private static List<JsonElement> Events(JsonElement body) => body.GetProperty("events").EnumerateArray().ToList();

    [Fact]
    public async Task OPT_11_les_routes_internes_n_existent_que_sur_le_port_interne()
    {
        using var factory = Factory();
        var admin = Admin(factory);
        // Port public : la route interne n'existe pas, même avec le bon jeton.
        var outside = new HttpRequestMessage(HttpMethod.Get, "/internal/events");
        outside.Headers.Authorization = new AuthenticationHeaderValue("Bearer", FakeFeaturesContainer.Token);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().SendAsync(outside)).StatusCode);
        // Port interne : rien d'autre que les routes internes et les sondes.
        Assert.Equal(404, (await Internal(factory, "/api/incidents", FakeFeaturesContainer.Token)).Status);
        Assert.Equal(200, (await Internal(factory, "/health/live", null)).Status);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/features")).StatusCode);
    }

    [Fact]
    public async Task OPT_10_le_jeton_du_conteneur_est_exige_et_n_ouvre_que_les_fonctionnalites_activees()
    {
        using var factory = Factory();
        Assert.Equal(401, (await Internal(factory, "/internal/events", null)).Status);
        Assert.Equal(401, (await Internal(factory, "/internal/events", "jeton-faux-mais-assez-long-0123456789")).Status);
        // Un jeton d'utilisateur (JWT) n'est pas un jeton de conteneur.
        Assert.Equal(401, (await Internal(factory, "/internal/events", ApiFixture.Token("hery.rakoto", "Admin"))).Status);
        // Bon jeton, aucune fonctionnalité activée : rien à lire (OPT-04).
        Assert.Equal(403, (await Internal(factory, "/internal/events", FakeFeaturesContainer.Token)).Status);
        await Enable(Admin(factory), FeatureCatalog.AssistedTriage);
        Assert.Equal(200, (await Internal(factory, "/internal/events", FakeFeaturesContainer.Token)).Status);

        // Jeton configuré trop court (moins de 32 caractères) : refusé, même présenté tel quel.
        using var weak = Factory("court");
        Assert.Equal(401, (await Internal(weak, "/internal/events", "court")).Status);
    }

    [Fact]
    public async Task OPT_12_le_fil_porte_les_evenements_des_fonctionnalites_activees_avec_un_curseur()
    {
        using var factory = Factory();
        var admin = Admin(factory);
        await Enable(admin, FeatureCatalog.AssistedTriage);
        var (_, start) = await Internal(factory, "/internal/events?after=0", FakeFeaturesContainer.Token);
        var cursor = start!.Value.GetProperty("next").GetInt64();
        while (Events((await Internal(factory, $"/internal/events?after={cursor}", FakeFeaturesContainer.Token)).Body!.Value) is { Count: > 0 } more)
            cursor = more[^1].GetProperty("id").GetInt64();

        var created = await admin.PostAsJsonAsync("/api/incidents", new { title = "Fil des fonctionnalités", impact = "Faible", urgency = "Faible" });
        var incident = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement;
        var id = incident.GetProperty("id").GetInt32();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Incidents.SingleAsync(i => i.Id == id);
            row.Urgency = "Moyenne";
            await db.SaveChangesAsync();
            (row.Status, row.Resolution, row.ResolutionCode, row.ResolvedAt) = ("Résolu", "Redémarrage du service", "Résolu", DateTime.UtcNow);
            await db.SaveChangesAsync();
        }
        // CMDB assistée coupée : un CI créé n'inscrit rien.
        Assert.True((await admin.PostAsJsonAsync("/api/configuration-items", new { title = "fil-srv", ciType = "Serveur", environment = "Production",
            ownerType = "User", ownerId = "hery.rakoto", ownerDisplayName = "Hery Rakoto" })).IsSuccessStatusCode);

        var (status, body) = await Internal(factory, $"/internal/events?after={cursor}", FakeFeaturesContainer.Token);
        Assert.Equal(200, status);
        var events = Events(body!.Value);
        Assert.Equal(["incident.created", "incident.updated", "incident.resolved"], events.Select(e => e.GetProperty("type").GetString()).ToList());
        Assert.All(events, e => Assert.Equal(id, e.GetProperty("entityId").GetInt32()));
        Assert.All(events, e => Assert.Equal(incident.GetProperty("perimeterId").GetInt32(), e.GetProperty("perimeterId").GetInt32()));
        Assert.Equal(events[^1].GetProperty("id").GetInt64(), body.Value.GetProperty("next").GetInt64());
        Assert.False(body.Value.GetProperty("gap").GetBoolean());
        // Le curseur avancé : plus rien.
        var (_, after) = await Internal(factory, $"/internal/events?after={body.Value.GetProperty("next").GetInt64()}", FakeFeaturesContainer.Token);
        Assert.Empty(Events(after!.Value));

        await using var check = api.Database();
        Assert.False(await check.FeatureEvents.AnyAsync(e => e.Id > cursor && e.Type.StartsWith("ci.")));
    }

    [Fact]
    public async Task OPT_12_sans_fonctionnalite_qui_le_lit_rien_n_est_inscrit()
    {
        using var factory = Factory();
        await using var db = api.Database();
        var before = await db.FeatureEvents.CountAsync();
        Assert.True((await Admin(factory).PostAsJsonAsync("/api/incidents",
            new { title = "Aucune fonctionnalité activée", impact = "Faible", urgency = "Faible" })).IsSuccessStatusCode);
        Assert.Equal(before, await db.FeatureEvents.CountAsync());
    }

    [Fact]
    public async Task OPT_12_le_fil_garde_7_jours_et_signale_un_retard_qui_a_perdu_des_evenements()
    {
        using var factory = Factory();
        await Enable(Admin(factory), FeatureCatalog.AssistedTriage);
        await using var db = api.Database();
        // Fil vidé, puis six événements anciens et deux récents, dans l'ordre du temps (les identifiants croissent avec lui).
        await db.FeatureEvents.ExecuteDeleteAsync();
        var rows = Enumerable.Range(0, 8).Select(i => new FeatureEvent
        {
            Type = FeatureEventSources.IncidentUpdated, EntityType = "incident", EntityId = 1, At = DateTime.UtcNow.AddDays(i < 6 ? -8 : 0)
        }).ToList();
        foreach (var row in rows) { db.FeatureEvents.Add(row); await db.SaveChangesAsync(); }
        Assert.Equal(6, await FeatureEventsRetention.PurgeAsync(db, 7));
        Assert.False(await db.FeatureEvents.AnyAsync(e => e.At < DateTime.UtcNow.AddDays(-7)));

        // Un curseur antérieur aux événements purgés : le conteneur doit repartir d'un état complet.
        var (_, late) = await Internal(factory, $"/internal/events?after={rows[0].Id}", FakeFeaturesContainer.Token);
        Assert.True(late!.Value.GetProperty("gap").GetBoolean());
        var (_, fresh) = await Internal(factory, $"/internal/events?after={rows[5].Id}", FakeFeaturesContainer.Token);
        Assert.False(fresh!.Value.GetProperty("gap").GetBoolean());
        Assert.Equal([rows[6].Id, rows[7].Id], Events(fresh.Value).Select(e => e.GetProperty("id").GetInt64()).ToList());
    }
}
