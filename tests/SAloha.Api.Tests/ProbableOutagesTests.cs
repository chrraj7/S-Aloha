using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Features;
using SAloha.Api.Core.Notifications;

namespace SAloha.Api.Tests;

/// <summary>Pannes probables signalées par le tri assisté (TRI-07, TRI-11) et lecture des incidents ouverts (OPT-13, TRI-06).</summary>
[Collection("api")]
public class ProbableOutagesTests(ApiFixture api) : IAsyncLifetime
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

    private WebApplicationFactory<Program> Factory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Default", api.Database().Database.GetConnectionString())
            .UseSetting("Jwt:Key", ApiFixture.JwtKey)
            .UseSetting("Jwt:Issuer", "S-Aloha")
            .UseSetting("Discovery:IntervalMinutes", "0")
            .UseSetting("Discovery:VaultKey", "cle-du-coffre-de-test-uniquement-0123456789")
            .UseSetting("Features:Token", FakeFeaturesContainer.Token)
            .UseSetting("Features:InternalPort", "8081"));

    private static HttpClient As(WebApplicationFactory<Program> factory, string user, string role = "User")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFixture.Token(user, role));
        return client;
    }

    private static async Task<(int Status, JsonElement? Body)> Internal(WebApplicationFactory<Program> factory, string method, string path, object? body = null)
    {
        var uri = new Uri("http://api" + path);
        var json = body is null ? null : Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body, JsonSerializerOptions.Web));
        var context = await factory.Server.SendAsync(c =>
        {
            c.Request.Method = method;
            c.Request.Path = uri.AbsolutePath;
            c.Request.QueryString = new QueryString(uri.Query);
            c.Connection.LocalPort = 8081;
            if (json is not null)
            {
                c.Request.ContentType = "application/json";
                c.Request.ContentLength = json.Length;
                c.Request.Body = new MemoryStream(json);
            }
            c.Request.Headers.Authorization = $"Bearer {FakeFeaturesContainer.Token}";
        });
        var text = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return (context.Response.StatusCode, text.Length > 0 && text[0] is '{' or '[' ? JsonDocument.Parse(text).RootElement : null);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode} : {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static int Id(JsonElement e) => e.GetProperty("id").GetInt32();

    /// <summary>Siège et Usine ; un agent et un gestionnaire du Siège, un agent de l'Usine ; un service.</summary>
    private async Task<(int Siege, int Usine, int Service)> SetupAsync(HttpClient admin, string tag)
    {
        var siege = Id(await Json(await admin.PostAsJsonAsync("/api/admin/perimeters", new { name = $"Siège {tag}" })));
        var usine = Id(await Json(await admin.PostAsJsonAsync("/api/admin/perimeters", new { name = $"Usine {tag}" })));
        async Task Assign(int p, string id, string role) => await Json(await admin.PostAsJsonAsync($"/api/admin/perimeters/{p}/assignments",
            new { principalType = "User", principalId = id, principalDisplayName = id, role }));
        await Assign(siege, $"agent.{tag}", "User");
        await Assign(siege, $"gestion.{tag}", "Manager");
        await Assign(usine, $"usine.{tag}", "User");
        var service = Id(await Json(await admin.PostAsJsonAsync("/api/services", new
        {
            title = $"Messagerie {tag}", status = "En service", criticality = "Élevée", serviceHours = "24/7", perimeterId = siege,
            ownerType = "User", ownerId = $"svc.{tag}", ownerDisplayName = "Responsable",
        })));
        return (siege, usine, service);
    }

    private static async Task<JsonElement> IncidentAsync(HttpClient admin, string title, int perimeter, int? service = null) =>
        await Json(await admin.PostAsJsonAsync("/api/incidents", new
        {
            title, description = "Plus de courriels", impact = "Moyen", urgency = "Moyenne", perimeterId = perimeter, serviceId = service, isMajor = false,
        }));

    private static object Signal(int service, params int[] members) =>
        new { feature = FeatureCatalog.AssistedTriage, members, basis = "service", serviceId = service, reasons = $"{members.Length} incidents ouverts sur le même service" };

    private async Task<int> NotificationsAsync(int perimeter)
    {
        await using var db = api.Database();
        return await db.Notifications.AsNoTracking()
            .CountAsync(n => n.PerimeterId == perimeter && n.RecipientType == Notification.ToManagers && n.Event.StartsWith("Panne probable"));
    }

    [Fact]
    public async Task TRI_07_une_panne_signalee_notifie_les_gestionnaires_une_seule_fois_et_sert_le_bandeau()
    {
        using var factory = Factory();
        var admin = As(factory, "hery.rakoto", "Admin");
        var (siege, usine, service) = await SetupAsync(admin, "tri07");
        var a = await IncidentAsync(admin, "Outlook muet 1 (TRI-07)", siege, service);
        var b = await IncidentAsync(admin, "Outlook muet 2 (TRI-07)", siege, service);
        var c = await IncidentAsync(admin, "Outlook muet 3 (TRI-07)", siege, service);

        var (status, first) = await Internal(factory, "POST", "/internal/outages", Signal(service, Id(c), Id(b), Id(a)));
        Assert.Equal(201, status);
        Assert.Equal(1, await NotificationsAsync(siege));
        await using (var db = api.Database())
            Assert.Equal(Id(a), (await db.Notifications.AsNoTracking().SingleAsync(n => n.PerimeterId == siege && n.Event.StartsWith("Panne probable"))).TargetId);

        // Un quatrième incident rejoint le groupe : même panne, aucune notification de plus.
        var d = await IncidentAsync(admin, "Outlook muet 4 (TRI-07)", siege, service);
        var (again, same) = await Internal(factory, "POST", "/internal/outages", Signal(service, Id(a), Id(b), Id(c), Id(d)));
        Assert.Equal(200, again);
        Assert.Equal(Id(first!.Value), Id(same!.Value));
        Assert.Equal(1, await NotificationsAsync(siege));

        // Le bandeau : 4 incidents ouverts depuis le plus ancien, parent proposé ; seul un gestionnaire déclare majeur.
        var banner = (await Json(await As(factory, "agent.tri07").GetAsync($"/api/incidents/{Id(d)}/triage"))).GetProperty("outage");
        Assert.Equal(4, banner.GetProperty("count").GetInt32());
        // Depuis la création du plus ancien (la base garde la microseconde).
        Assert.True((a.GetProperty("createdAt").GetDateTime() - banner.GetProperty("since").GetDateTime()).Duration() < TimeSpan.FromMilliseconds(1));
        Assert.Equal(Id(a), Id(banner.GetProperty("parent")));
        Assert.Equal(service, Id(banner.GetProperty("service")));
        Assert.Equal(4, banner.GetProperty("members").GetArrayLength());
        Assert.False(banner.GetProperty("canDeclareMajor").GetBoolean());
        var managed = (await Json(await As(factory, "gestion.tri07").GetAsync($"/api/incidents/{Id(b)}/triage"))).GetProperty("outage");
        Assert.True(managed.GetProperty("canDeclareMajor").GetBoolean());

        // Hors du groupe : pas de bandeau ; hors de ses périmètres : 404 ; tri coupé : la route n'existe pas (OPT-04).
        var alone = await IncidentAsync(admin, "Imprimante (TRI-07)", siege);
        Assert.Equal(JsonValueKind.Null, (await Json(await As(factory, "agent.tri07").GetAsync($"/api/incidents/{Id(alone)}/triage"))).GetProperty("outage").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, (await As(factory, "usine.tri07").GetAsync($"/api/incidents/{Id(a)}/triage")).StatusCode);
        await SwitchAsync();
        using var off = Factory();
        Assert.Equal(HttpStatusCode.NotFound, (await As(off, "agent.tri07").GetAsync($"/api/incidents/{Id(a)}/triage")).StatusCode);
        Assert.Equal(403, (await Internal(off, "POST", "/internal/outages", Signal(service, Id(a), Id(b)))).Status);
        _ = usine;
    }

    [Fact]
    public async Task TRI_11_une_panne_ne_regroupe_jamais_deux_perimetres_et_se_ferme_sous_deux_incidents_ouverts()
    {
        using var factory = Factory();
        var admin = As(factory, "hery.rakoto", "Admin");
        var (siege, usine, service) = await SetupAsync(admin, "tri11");
        var a = await IncidentAsync(admin, "VPN 1 (TRI-11)", siege, service);
        var b = await IncidentAsync(admin, "VPN 2 (TRI-11)", siege, service);
        var c = await IncidentAsync(admin, "VPN 3 (TRI-11)", siege, service);
        var elsewhere = await IncidentAsync(admin, "VPN Usine (TRI-11)", usine, service);

        Assert.Equal(400, (await Internal(factory, "POST", "/internal/outages", Signal(service, Id(a), Id(b), Id(elsewhere)))).Status);
        Assert.Equal(400, (await Internal(factory, "POST", "/internal/outages",
            new { feature = FeatureCatalog.AssistedTriage, members = new[] { Id(a), Id(b) }, basis = "météo" })).Status);
        Assert.Equal(400, (await Internal(factory, "POST", "/internal/outages",
            new { feature = FeatureCatalog.AssistedCmdb, members = new[] { Id(a), Id(b) }, basis = "service" })).Status);
        Assert.Equal(0, await NotificationsAsync(usine));

        Assert.Equal(201, (await Internal(factory, "POST", "/internal/outages", Signal(service, Id(a), Id(b), Id(c)))).Status);
        // Deux incidents résolus : il n'en reste qu'un ouvert, le groupe se ferme, le bandeau disparaît.
        foreach (var x in new[] { b, c })
            await Json(await admin.PutAsJsonAsync($"/api/incidents/{Id(x)}", new
            {
                title = x.GetProperty("title").GetString(), description = "Plus de courriels", status = "Résolu", impact = "Moyen", urgency = "Moyenne",
                perimeterId = siege, serviceId = service, isMajor = false, resolution = "Redémarrage", resolutionCode = "Correctif appliqué",
            }));
        var (_, closed) = await Internal(factory, "POST", "/internal/outages", Signal(service, Id(a), Id(b), Id(c)));
        Assert.True(closed!.Value.GetProperty("closed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, (await Json(await As(factory, "agent.tri11").GetAsync($"/api/incidents/{Id(a)}/triage"))).GetProperty("outage").ValueKind);

        // Un groupe qui se défait dans le conteneur se ferme aussi sur son signalement « closed ».
        var d = await IncidentAsync(admin, "VPN 4 (TRI-11)", siege, service);
        Assert.Equal(201, (await Internal(factory, "POST", "/internal/outages", Signal(service, Id(a), Id(d)))).Status);
        var (_, ended) = await Internal(factory, "POST", "/internal/outages",
            new { feature = FeatureCatalog.AssistedTriage, members = new[] { Id(a), Id(d) }, closed = true });
        Assert.True(ended!.Value.GetProperty("closed").GetBoolean());
        await using var db = api.Database();
        Assert.False(await db.ProbableOutages.AnyAsync(o => o.PerimeterId == siege && o.ClosedAt == null));
    }

    [Fact]
    public async Task OPT_13_les_incidents_ouverts_se_lisent_page_apres_page_avec_leurs_CI()
    {
        using var factory = Factory();
        var admin = As(factory, "hery.rakoto", "Admin");
        var (siege, _, service) = await SetupAsync(admin, "opt13o");
        var first = await IncidentAsync(admin, "Ouvert 1 (OPT-13)", siege, service);
        var second = await IncidentAsync(admin, "Ouvert 2 (OPT-13)", siege, service);
        var resolved = await IncidentAsync(admin, "Résolu (OPT-13)", siege, service);
        await Json(await admin.PutAsJsonAsync($"/api/incidents/{Id(resolved)}", new
        {
            title = "Résolu (OPT-13)", description = "Plus de courriels", status = "Résolu", impact = "Moyen", urgency = "Moyenne",
            perimeterId = siege, serviceId = service, isMajor = false, resolution = "Redémarrage", resolutionCode = "Correctif appliqué",
        }));
        var ci = await Json(await admin.PostAsJsonAsync("/api/configuration-items", new
        {
            title = "Exchange (OPT-13 ouverts)", ciType = "Application", environment = "Production", status = "Planifié", perimeterId = siege,
            ownerType = "User", ownerId = "ci.opt13o", ownerDisplayName = "Responsable",
        }));
        await Json(await admin.PostAsJsonAsync("/api/links", new { fromType = "ci", fromId = Id(ci), toReference = second.GetProperty("reference").GetString() }));

        var (status, page) = await Internal(factory, "GET", $"/internal/incidents/open?after={Id(first) - 1}&limit=1");
        Assert.Equal(200, status);
        var item = Assert.Single(page!.Value.GetProperty("items").EnumerateArray());
        Assert.Equal(Id(first), Id(item.GetProperty("incident")));
        Assert.Equal(Id(first), page.Value.GetProperty("next").GetInt32());

        var (_, rest) = await Internal(factory, "GET", $"/internal/incidents/open?after={Id(first)}");
        var items = rest!.Value.GetProperty("items").EnumerateArray().ToList();
        var mine = items.Single(i => Id(i.GetProperty("incident")) == Id(second));
        Assert.Equal([Id(ci)], mine.GetProperty("configurationItemIds").EnumerateArray().Select(e => e.GetInt32()));
        Assert.DoesNotContain(items, i => Id(i.GetProperty("incident")) == Id(resolved));   // TRI-05 : ouverts seulement
        Assert.Equal(JsonValueKind.Null, rest.Value.GetProperty("next").ValueKind);
    }
}
