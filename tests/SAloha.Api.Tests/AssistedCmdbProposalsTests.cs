using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Features;
using SAloha.Api.Core.Perimeters;

namespace SAloha.Api.Tests;

/// <summary>CMDB assistée : file des propositions et décision par les règles des CI (CMA-03).</summary>
[Collection("api")]
public class AssistedCmdbProposalsTests(ApiFixture api) : IAsyncLifetime
{
    private const string Queue = "/api/assisted-cmdb/proposals";

    public async Task InitializeAsync()
    {
        // Le démarrage de l'API applique les migrations : lancé seul, ce test arrive avant elles.
        await api.Client().GetAsync("/health/live");
        await using var db = api.Database();
        await db.OptionalFeatures.ExecuteDeleteAsync();
        await db.Proposals.Where(p => p.Feature == FeatureCatalog.AssistedCmdb).ExecuteDeleteAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = api.Database();
        await db.OptionalFeatures.ExecuteDeleteAsync();
    }

    private WebApplicationFactory<Program> Factory() => new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
        .UseSetting("ConnectionStrings:Default", api.Database().Database.GetConnectionString())
        .UseSetting("Jwt:Key", ApiFixture.JwtKey).UseSetting("Jwt:Issuer", "S-Aloha")
        .UseSetting("Discovery:IntervalMinutes", "0").UseSetting("Discovery:VaultKey", "cle-du-coffre-de-test-uniquement-0123456789")
        .UseSetting("Features:Token", FakeFeaturesContainer.Token).UseSetting("Features:InternalPort", "8081"));

    private static HttpClient As(WebApplicationFactory<Program> factory, string username, string role)
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

    private static async Task Propose(WebApplicationFactory<Program> host, int ci, string field, string after)
    {
        var context = await host.Server.SendAsync(c =>
        {
            c.Request.Method = "POST"; c.Request.Path = "/internal/proposals"; c.Connection.LocalPort = 8081;
            c.Request.Headers.Authorization = $"Bearer {FakeFeaturesContainer.Token}";
            c.Request.ContentType = "application/json";
            c.Request.Body = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new
            {
                feature = FeatureCatalog.AssistedCmdb, targetType = "ci", targetId = ci, field, after, reasons = "Proposé par la CMDB assistée.",
                cited = Array.Empty<object>(), confidence = (double?)null,
            }));
        });
        Assert.True(context.Response.StatusCode is 200 or 201, $"Proposition refusée : HTTP {context.Response.StatusCode}");
    }

    private static object Ci(string title, int perimeter, string os = "Win2019 std") => new
    {
        title, ciType = "Serveur", environment = "Production", location = "Siège", status = "En service", perimeterId = perimeter, operatingSystem = os,
        ownerType = "User", ownerId = "ci.owner", ownerDisplayName = "Owner",
    };

    [Fact]
    public async Task CMA_03_la_file_montre_les_propositions_du_gestionnaire_et_un_renommage_contraire_a_l_unicite_est_refuse_et_reste_ouvert()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var admin = api.Client();
        var perimeter = (await Json(await admin.PostAsJsonAsync("/api/admin/perimeters", new { name = $"Site {tag}" }))).GetProperty("id").GetInt32();
        var manager = $"gest.{tag}";
        await using (var db = api.Database())
        {
            db.PerimeterAssignments.Add(new PerimeterAssignment
            {
                PerimeterId = perimeter, PrincipalType = "User", PrincipalId = manager, PrincipalDisplayName = "Gestionnaire", Role = "Manager",
            });
            db.OptionalFeatures.Add(new OptionalFeature { Key = FeatureCatalog.AssistedCmdb, Enabled = true, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var existing = $"SRV-PAIE-{tag}";
        await Json(await admin.PostAsJsonAsync("/api/configuration-items", Ci(existing, perimeter)));
        var target = (await Json(await admin.PostAsJsonAsync("/api/configuration-items", Ci($"srvpaie-{tag}", perimeter)))).GetProperty("id").GetInt32();

        using var host = Factory();
        await Propose(host, target, "title", existing);                                      // Renommer
        await Propose(host, target, "operatingSystem", "Windows Server 2019 Standard");      // Normaliser
        var gest = As(host, manager, "User");

        // La file du gestionnaire : par tâche, avant et après.
        var all = await Json(await gest.GetAsync($"{Queue}?perimeter={perimeter}"));
        Assert.Equal([("rename", "title"), ("normalize", "operatingSystem")], all.EnumerateArray().Select(p => (p.GetProperty("task").GetString(), p.GetProperty("field").GetString())));
        Assert.Equal(("Win2019 std", false), (all[1].GetProperty("before").GetString(), all[1].GetProperty("stale").GetBoolean()));
        Assert.Equal(1, (await Json(await gest.GetAsync($"{Queue}?task=rename"))).GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, (await gest.GetAsync($"{Queue}?task=supprimer")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await As(host, "gest.ailleurs", "User").GetAsync($"{Queue}?perimeter={perimeter}")).StatusCode);
        Assert.Equal(0, (await Json(await As(host, "gest.ailleurs", "User").GetAsync(Queue))).GetArrayLength());

        // CMA-03 : accepter passe par les règles des CI ; un nom déjà pris dans l'environnement est refusé (409, CFG-17), la proposition reste.
        var rename = all[0].GetProperty("id").GetInt32();
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/configuration-items/{target}") { Content = JsonContent.Create(Ci(existing, perimeter)) };
        request.Headers.Add("X-S-Aloha-Proposal", rename.ToString());
        Assert.Equal(HttpStatusCode.Conflict, (await gest.SendAsync(request)).StatusCode);
        Assert.Contains((await Json(await gest.GetAsync(Queue))).EnumerateArray(), p => p.GetProperty("id").GetInt32() == rename);

        // Accepter la normalisation : la proposition sort de la file ; la renommer restante, rejetée, en sort aussi.
        var normalize = all[1].GetProperty("id").GetInt32();
        request = new HttpRequestMessage(HttpMethod.Put, $"/api/configuration-items/{target}")
            { Content = JsonContent.Create(Ci($"srvpaie-{tag}", perimeter, "Windows Server 2019 Standard")) };
        request.Headers.Add("X-S-Aloha-Proposal", normalize.ToString());
        await Json(await gest.SendAsync(request));
        Assert.Equal(HttpStatusCode.NoContent, (await gest.PostAsync($"/api/configuration-items/{target}/proposals/{rename}/reject", null)).StatusCode);
        Assert.Equal(0, (await Json(await gest.GetAsync($"{Queue}?perimeter={perimeter}"))).GetArrayLength());
        await using (var db = api.Database())
            Assert.Equal(("Acceptée", "Rejetée"), (await db.Proposals.Where(p => p.Id == normalize).Select(p => p.Status).SingleAsync(),
                await db.Proposals.Where(p => p.Id == rename).Select(p => p.Status).SingleAsync()));

        // La CMDB assistée coupée, la file n'existe pas (OPT-04).
        await using (var db = api.Database()) await db.OptionalFeatures.ExecuteDeleteAsync();
        using var off = Factory();
        Assert.Equal(HttpStatusCode.NotFound, (await As(off, manager, "User").GetAsync(Queue)).StatusCode);
    }
}
