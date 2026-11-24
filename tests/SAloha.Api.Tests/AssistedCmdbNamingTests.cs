using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Features;
using SAloha.Api.Core.Perimeters;

namespace SAloha.Api.Tests;

/// <summary>CMDB assistée : convention de nommage par type de CI (CMA-04), tenue par les gestionnaires, lue par le conteneur.</summary>
[Collection("api")]
public class AssistedCmdbNamingTests(ApiFixture api) : IAsyncLifetime
{
    private const string Path = "/api/assisted-cmdb/naming";

    public async Task InitializeAsync()
    {
        // Le démarrage de l'API applique les migrations : lancé seul, ce test arrive avant elles.
        await api.Client().GetAsync("/health/live");
        await ResetAsync();
    }

    public Task DisposeAsync() => ResetAsync();

    private async Task ResetAsync()
    {
        await using var db = api.Database();
        await db.NamingConventions.ExecuteDeleteAsync();
        await db.OptionalFeatures.ExecuteDeleteAsync();
        await db.PerimeterAssignments.Where(a => a.PrincipalId == "gest.nommage").ExecuteDeleteAsync();
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

    private static async Task<string> Refused(HttpResponseMessage res)
    {
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("message").GetString()!;
    }

    [Fact]
    public async Task CMA_04_un_gestionnaire_tient_la_convention_par_type_lue_par_tous_et_par_le_conteneur()
    {
        // CMDB assistée coupée : la route n'existe pas (OPT-04).
        using (var off = Factory()) Assert.Equal(HttpStatusCode.NotFound, (await As(off, "hery.rakoto", "Admin").GetAsync(Path)).StatusCode);
        await using (var db = api.Database())
        {
            db.OptionalFeatures.Add(new OptionalFeature { Key = FeatureCatalog.AssistedCmdb, Enabled = true, UpdatedAt = DateTime.UtcNow });
            // Un gestionnaire d'au moins un périmètre tient les référentiels sans périmètre (PER-13).
            db.PerimeterAssignments.Add(new PerimeterAssignment
            {
                PerimeterId = Perimeter.DefaultId, PrincipalType = "User", PrincipalId = "gest.nommage", PrincipalDisplayName = "Gestionnaire", Role = "Manager",
            });
            await db.SaveChangesAsync();
        }
        using var factory = Factory();
        var manager = As(factory, "gest.nommage", "User");
        var saved = await Json(await manager.PutAsJsonAsync($"{Path}/Serveur",
            new { description = "SRV-<RÔLE>-<NN>, en majuscules", examples = new[] { "SRV-PAIE-01", " SRV-MAIL-02 ", "SRV-PAIE-01", "" } }));
        Assert.Equal(("Serveur", "SRV-<RÔLE>-<NN>, en majuscules"), (saved.GetProperty("ciType").GetString(), saved.GetProperty("description").GetString()));
        Assert.Equal(["SRV-PAIE-01", "SRV-MAIL-02"], saved.GetProperty("examples").EnumerateArray().Select(e => e.GetString()));
        await Json(await manager.PutAsJsonAsync($"{Path}/{Uri.EscapeDataString("Base de données")}", new { description = "BDD-<APPLI>-<ENV>", examples = new[] { "BDD-PAIE-PROD" } }));

        // Lecture pour tous, dans l'ordre des types ; écriture refusée à un simple utilisateur.
        var agent = As(factory, "agent.simple", "User");
        Assert.Equal(["Serveur", "Base de données"], (await Json(await agent.GetAsync(Path))).EnumerateArray().Select(n => n.GetProperty("ciType").GetString()));
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.PutAsJsonAsync($"{Path}/Serveur", new { description = "x" })).StatusCode);

        // Bornes : type inconnu, description trop longue, trop d'exemples.
        Assert.Contains("Type de CI inconnu", await Refused(await manager.PutAsJsonAsync($"{Path}/Imprimante", new { description = "IMP-<NN>" })));
        Assert.Contains("500 caractères", await Refused(await manager.PutAsJsonAsync($"{Path}/Serveur", new { description = new string('x', 501) })));
        Assert.Contains("5 exemples", await Refused(await manager.PutAsJsonAsync($"{Path}/Serveur",
            new { description = "SRV", examples = Enumerable.Range(1, 6).Select(i => $"SRV-{i}").ToArray() })));

        // Le conteneur lit les conventions sur le port interne (format attendu par la tâche Renommer).
        var context = await factory.Server.SendAsync(c =>
        {
            c.Request.Method = "GET"; c.Request.Path = "/internal/assisted-cmdb/naming"; c.Connection.LocalPort = 8081;
            c.Request.Headers.Authorization = $"Bearer {FakeFeaturesContainer.Token}";
        });
        Assert.Equal(200, context.Response.StatusCode);
        var read = JsonDocument.Parse(await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync()).RootElement;
        Assert.Equal(("Serveur", "SRV-<RÔLE>-<NN>, en majuscules", "SRV-MAIL-02"), (read[0].GetProperty("ciType").GetString(),
            read[0].GetProperty("description").GetString(), read[0].GetProperty("examples")[1].GetString()));

        // Une description vide retire la convention : Renommer ne s'applique plus à ce type.
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync($"{Path}/Serveur", new { description = "" })).StatusCode);
        Assert.Equal(["Base de données"], (await Json(await agent.GetAsync(Path))).EnumerateArray().Select(n => n.GetProperty("ciType").GetString()));
    }
}
