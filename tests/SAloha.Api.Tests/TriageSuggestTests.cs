using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Features;

namespace SAloha.Api.Tests;

/// <summary>Conteneur factice qui répond à <c>POST /triage/suggest</c> avec la réponse que le test lui donne.</summary>
public sealed class FakeTriageContainer : IAsyncLifetime
{
    private WebApplication app = null!;
    public string Url { get; private set; } = "";
    public volatile object Answer = new { suggestions = Array.Empty<object>(), related = Array.Empty<int>() };
    public readonly ConcurrentQueue<JsonElement> Drafts = new();
    public readonly ConcurrentQueue<string> Authorizations = new();

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0));
        app = builder.Build();
        app.MapPost("/triage/suggest", async (HttpContext c) =>
        {
            Authorizations.Enqueue(c.Request.Headers.Authorization.ToString());
            Drafts.Enqueue(await JsonSerializer.DeserializeAsync<JsonElement>(c.Request.Body));
            return Results.Json(Answer);
        });
        await app.StartAsync();
        Url = app.Urls.Single();
    }

    public async Task DisposeAsync() => await app.DisposeAsync();
}

/// <summary>Suggestions à la saisie relayées au conteneur (TRI-02, OPT-16) ; périmètres de l'agent et sources visibles (TRI-11).</summary>
[Collection("api")]
public class TriageSuggestTests(ApiFixture api, FakeTriageContainer container) : IClassFixture<FakeTriageContainer>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        // Le démarrage de l'API applique les migrations : lancé seul, ce test arrive avant elles.
        await api.Client().GetAsync("/health/live");
        await SwitchAsync(FeatureCatalog.AssistedTriage);
    }

    public Task DisposeAsync() => SwitchAsync();

    private async Task SwitchAsync(params string[] enabled)
    {
        await using var db = api.Database();
        await db.OptionalFeatures.ExecuteDeleteAsync();
        foreach (var key in enabled) db.OptionalFeatures.Add(new OptionalFeature { Key = key, Enabled = true, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    private WebApplicationFactory<Program> Factory(string? url) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Default", api.Database().Database.GetConnectionString())
            .UseSetting("Jwt:Key", ApiFixture.JwtKey)
            .UseSetting("Jwt:Issuer", "S-Aloha")
            .UseSetting("Discovery:IntervalMinutes", "0")
            .UseSetting("Discovery:VaultKey", "cle-du-coffre-de-test-uniquement-0123456789")
            .UseSetting("Features:Url", url ?? "")
            .UseSetting("Features:Token", FakeFeaturesContainer.Token));

    private static HttpClient As(WebApplicationFactory<Program> factory, string user, string role = "User")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFixture.Token(user, role));
        return client;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode} : {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static int Id(JsonElement e) => e.GetProperty("id").GetInt32();

    private static readonly object Draft = new { title = "Outlook ne reçoit plus les courriels", description = "Depuis ce matin" };

    [Fact]
    public async Task TRI_11_a_la_saisie_le_conteneur_cherche_dans_les_perimetres_de_l_agent_et_la_reponse_ne_cite_que_ce_qu_il_voit()
    {
        using var factory = Factory(container.Url);
        var admin = As(factory, "hery.rakoto", "Admin");
        var siege = Id(await Json(await admin.PostAsJsonAsync("/api/admin/perimeters", new { name = "Siège TRI-11 saisie" })));
        var usine = Id(await Json(await admin.PostAsJsonAsync("/api/admin/perimeters", new { name = "Usine TRI-11 saisie" })));
        await Json(await admin.PostAsJsonAsync($"/api/admin/perimeters/{siege}/assignments",
            new { principalType = "User", principalId = "agent.tri11s", principalDisplayName = "Agent", role = "User" }));
        var service = Id(await Json(await admin.PostAsJsonAsync("/api/services", new
        {
            title = "Messagerie TRI-11 saisie", status = "En service", criticality = "Élevée", serviceHours = "24/7", perimeterId = siege,
            ownerType = "User", ownerId = "svc.tri11s", ownerDisplayName = "Responsable",
        })));
        object Incident(string title, int p) => new { title, description = "d", impact = "Moyen", urgency = "Moyenne", perimeterId = p, isMajor = false };
        var mine = await Json(await admin.PostAsJsonAsync("/api/incidents", Incident("Voisin du Siège (TRI-11)", siege)));
        var hidden = await Json(await admin.PostAsJsonAsync("/api/incidents", Incident("Voisin de l'Usine (TRI-11)", usine)));
        container.Answer = new
        {
            suggestions = new object[]
            {
                new { field = "category", value = "Messagerie", reasons = "2 des 2 incidents résolus les plus proches portent la catégorie Messagerie",
                      cited = new[] { new { type = "incident", id = Id(mine) }, new { type = "incident", id = Id(hidden) } } },
                new { field = "serviceId", value = service.ToString(), reasons = "3 des 3 incidents résolus les plus proches portent le service", cited = Array.Empty<object>() },
            },
            related = new[] { Id(mine), Id(hidden) },
        };

        var agent = As(factory, "agent.tri11s");
        var before = container.Drafts.Count;
        var answer = await Json(await agent.PostAsJsonAsync("/api/triage/suggest", Draft));
        var sent = container.Drafts.Skip(before).Single();
        Assert.Equal([siege], sent.GetProperty("perimeters").EnumerateArray().Select(e => e.GetInt32()));   // ses seuls périmètres
        Assert.Equal("Outlook ne reçoit plus les courriels", sent.GetProperty("title").GetString());
        Assert.Equal($"Bearer {FakeFeaturesContainer.Token}", container.Authorizations.Last());

        var suggestions = answer.GetProperty("suggestions").EnumerateArray().ToList();
        var category = suggestions.Single(s => s.GetProperty("field").GetString() == "category");
        Assert.Equal([mine.GetProperty("reference").GetString()], category.GetProperty("cited").EnumerateArray().Select(c => c.GetProperty("reference").GetString()));
        Assert.Equal("Messagerie TRI-11 saisie", suggestions.Single(s => s.GetProperty("field").GetString() == "serviceId").GetProperty("label").GetString());
        Assert.Equal([Id(mine)], answer.GetProperty("related").EnumerateArray().Select(Id));

        // L'Admin voit tout : le conteneur cherche dans tous les périmètres.
        before = container.Drafts.Count;
        await Json(await admin.PostAsJsonAsync("/api/triage/suggest", Draft));
        var all = container.Drafts.Skip(before).Single().GetProperty("perimeters").EnumerateArray().Select(e => e.GetInt32()).ToList();
        Assert.Contains(siege, all);
        Assert.Contains(usine, all);
    }

    [Fact]
    public async Task TRI_02_un_titre_trop_court_un_tri_coupe_ou_un_conteneur_arrete_ne_bloquent_jamais_la_saisie()
    {
        using var factory = Factory(container.Url);
        var agent = As(factory, "hery.rakoto", "Admin");
        var before = container.Drafts.Count;
        var tooShort = await Json(await agent.PostAsJsonAsync("/api/triage/suggest", new { title = "Out" }));
        Assert.Equal(0, tooShort.GetProperty("suggestions").GetArrayLength());
        Assert.Equal(before, container.Drafts.Count);   // le conteneur n'est pas appelé

        // Conteneur arrêté (rien n'écoute à son adresse) : réponse vide, jamais une erreur à l'écran (OPT-16).
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        using (var stopped = Factory($"http://127.0.0.1:{port}"))
        {
            var res = await As(stopped, "hery.rakoto", "Admin").PostAsJsonAsync("/api/triage/suggest", Draft);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.Equal(0, (await Json(res)).GetProperty("related").GetArrayLength());
        }

        // Tri coupé : la route n'existe pas (OPT-04).
        await SwitchAsync();
        using var off = Factory(container.Url);
        Assert.Equal(HttpStatusCode.NotFound, (await As(off, "hery.rakoto", "Admin").PostAsJsonAsync("/api/triage/suggest", Draft)).StatusCode);
    }
}
