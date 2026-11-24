using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Features;
using SAloha.Api.Core.Perimeters;
using SAloha.Api.Modules.ServiceConfigurationManagement;

namespace SAloha.Api.Tests;

/// <summary>CMDB assistée : relations proposées entre deux CI (CMA-02, CMA-09) et criticité d'une application.</summary>
[Collection("api")]
public class AssistedCmdbRelateTests(ApiFixture api) : IAsyncLifetime
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
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement;
    }

    private static int Id(JsonElement e) => e.GetProperty("id").GetInt32();
    private static string Ref(JsonElement e) => e.GetProperty("reference").GetString()!;

    /// <summary>Dépôt par le conteneur, sur le port interne (OPT-14) : statut HTTP et corps.</summary>
    private static async Task<(int Status, string Body)> Propose(WebApplicationFactory<Program> host, int ci, string field, string after,
        string reasons = "Même rôle, même système : probable doublon.")
    {
        var context = await host.Server.SendAsync(c =>
        {
            c.Request.Method = "POST"; c.Request.Path = "/internal/proposals"; c.Connection.LocalPort = 8081;
            c.Request.Headers.Authorization = $"Bearer {FakeFeaturesContainer.Token}";
            c.Request.ContentType = "application/json";
            c.Request.Body = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new
            {
                feature = FeatureCatalog.AssistedCmdb, targetType = "ci", targetId = ci, field, after, reasons,
                cited = Array.Empty<object>(), confidence = (double?)null,
            }));
        });
        return (context.Response.StatusCode, await new StreamReader(context.Response.Body).ReadToEndAsync());
    }

    private static async Task<string> Internal(WebApplicationFactory<Program> host, string path)
    {
        var context = await host.Server.SendAsync(c =>
        {
            c.Request.Method = "GET"; c.Request.Path = path.Split('?')[0]; c.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString("?" + path.Split('?')[1]);
            c.Connection.LocalPort = 8081; c.Request.Headers.Authorization = $"Bearer {FakeFeaturesContainer.Token}";
        });
        Assert.Equal(200, context.Response.StatusCode);
        return await new StreamReader(context.Response.Body).ReadToEndAsync();
    }

    private static object Ci(string title, int perimeter, string environment = "Production") => new
    {
        title, ciType = "Serveur", environment, location = "Siège", status = "En service", perimeterId = perimeter,
        ownerType = "User", ownerId = "ci.owner", ownerDisplayName = "Owner",
    };

    /// <summary>Un périmètre avec son gestionnaire et un agent (<c>agent.&lt;tag&gt;</c>), la CMDB assistée activée.</summary>
    private async Task<(int Perimeter, string Manager)> SiteAsync(string tag)
    {
        var perimeter = Id(await Json(await api.Client().PostAsJsonAsync("/api/admin/perimeters", new { name = $"Site {tag}" })));
        var manager = $"gest.{tag}";
        await using var db = api.Database();
        db.PerimeterAssignments.Add(new PerimeterAssignment
        {
            PerimeterId = perimeter, PrincipalType = "User", PrincipalId = manager, PrincipalDisplayName = "Gestionnaire", Role = "Manager",
        });
        db.PerimeterAssignments.Add(new PerimeterAssignment
        {
            PerimeterId = perimeter, PrincipalType = "User", PrincipalId = $"agent.{tag}", PrincipalDisplayName = "Agent", Role = "User",
        });
        if (!await db.OptionalFeatures.AnyAsync(f => f.Key == FeatureCatalog.AssistedCmdb))
            db.OptionalFeatures.Add(new OptionalFeature { Key = FeatureCatalog.AssistedCmdb, Enabled = true, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return (perimeter, manager);
    }

    private static object App(string title, int perimeter) => new
    {
        title, ciType = "Application", environment = "Production", location = "Siège", status = "Planifié", perimeterId = perimeter,
        ownerType = "User", ownerId = "ci.owner", ownerDisplayName = "Owner",
    };

    [Fact]
    public async Task CMA_02_CMA_09_une_relation_proposee_se_depose_valide_s_accepte_type_corrige_et_ne_revient_pas_rejetee()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (perimeter, manager) = await SiteAsync(tag);
        var (elsewhere, _) = await SiteAsync($"{tag}b");
        var admin = api.Client();
        var app = await Json(await admin.PostAsJsonAsync("/api/configuration-items", Ci($"APP-PAIE-{tag}", perimeter)));
        var db1 = await Json(await admin.PostAsJsonAsync("/api/configuration-items", Ci($"BDD-PAIE-{tag}", perimeter)));
        var vm = await Json(await admin.PostAsJsonAsync("/api/configuration-items", Ci($"VM-PAIE-{tag}", perimeter)));
        var usine = await Json(await admin.PostAsJsonAsync("/api/configuration-items", Ci($"BDD-USINE-{tag}", elsewhere)));
        await Json(await admin.PostAsJsonAsync($"/api/configuration-items/{Id(vm)}/relations", new { targetReference = Ref(app), type = "Héberge" }));

        using var factory = Factory();
        // OPT-14 : « <cible>|<type> » ; ni vers soi, ni hors liste, ni entre périmètres, ni déjà existante, ni mal formée.
        Assert.Equal(400, (await Propose(factory, Id(app), "relation", $"{Id(app)}|Dépend de")).Status);
        Assert.Equal(400, (await Propose(factory, Id(app), "relation", $"{Id(db1)}|Utilise")).Status);
        Assert.Equal(400, (await Propose(factory, Id(app), "relation", $"{Id(usine)}|Dépend de")).Status);
        Assert.Equal(400, (await Propose(factory, Id(vm), "relation", $"{Id(app)}|Héberge")).Status);
        Assert.Equal(400, (await Propose(factory, Id(app), "relation", Ref(db1))).Status);
        var (status, body) = await Propose(factory, Id(app), "relation", $"{Id(db1)}|Se connecte à", "La description cite la base de paie.");
        Assert.Equal(201, status);
        var relate = JsonDocument.Parse(body).RootElement.GetProperty("id").GetInt32();
        // Le sens compte : la même relation est la même proposition, l'inverse en est une autre ; plusieurs restent ouvertes.
        Assert.Equal((200, relate), ((await Propose(factory, Id(app), "relation", $"{Id(db1)}|Se connecte à")) is var again ? again.Status : 0,
            JsonDocument.Parse(again.Body).RootElement.GetProperty("id").GetInt32()));
        var (reverseStatus, reverseBody) = await Propose(factory, Id(db1), "relation", $"{Id(app)}|Fait partie de");
        Assert.Equal(201, reverseStatus);
        var reverse = JsonDocument.Parse(reverseBody).RootElement.GetProperty("id").GetInt32();

        var gest = As(factory, manager, "User");
        var queue = (await Json(await gest.GetAsync($"{Queue}?perimeter={perimeter}&task=relate"))).EnumerateArray().ToList();
        Assert.Equal(2, queue.Count);
        Assert.Equal((Id(db1), Ref(db1)), (queue[0].GetProperty("other").GetProperty("id").GetInt32(), queue[0].GetProperty("other").GetProperty("reference").GetString()));
        // La modification du CI ne décide pas une relation (OPT-15).
        var put = new HttpRequestMessage(HttpMethod.Put, $"/api/configuration-items/{Id(app)}") { Content = JsonContent.Create(Ci($"APP-PAIE-{tag}", perimeter)) };
        put.Headers.Add("X-S-Aloha-Proposal", relate.ToString());
        Assert.Equal(HttpStatusCode.BadRequest, (await gest.SendAsync(put)).StatusCode);

        // Accepter en corrigeant le type : la relation se crée, la proposition est corrigée, le journal porte l'origine.
        Assert.Equal(HttpStatusCode.BadRequest, (await gest.PostAsJsonAsync($"{Queue}/{relate}/relate", new { type = "Utilise" })).StatusCode);
        var created = await Json(await gest.PostAsJsonAsync($"{Queue}/{relate}/relate", new { type = "Dépend de" }));
        Assert.Equal(("Dépend de", Id(db1)), (created.GetProperty("type").GetString(), created.GetProperty("target").GetProperty("id").GetInt32()));
        await using (var db = api.Database())
        {
            Assert.True(await db.CiRelationships.AnyAsync(r => r.SourceId == Id(app) && r.TargetId == Id(db1) && r.Type == "Dépend de"));
            var decided = await db.Proposals.AsNoTracking().SingleAsync(p => p.Id == relate);
            Assert.Equal(("Corrigée", $"{Id(db1)}|Dépend de"), (decided.Status, decided.FinalValue));
        }
        var audit = (await Json(await admin.GetAsync($"/api/audit?type=ci&id={Id(app)}"))).EnumerateArray();
        Assert.Contains(audit, a => a.GetProperty("action").GetString() == "Lien ajouté" && a.GetProperty("newValue").GetString() == Ref(db1)
            && a.GetProperty("origin").GetString() == "CMDB assistée");

        // Une relation créée depuis à la main : l'accepter est refusé (409, CFG-03), elle reste à rejeter.
        await Json(await admin.PostAsJsonAsync($"/api/configuration-items/{Id(db1)}/relations", new { targetReference = Ref(app), type = "Fait partie de" }));
        Assert.Equal(HttpStatusCode.Conflict, (await gest.PostAsJsonAsync($"{Queue}/{reverse}/relate", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await gest.PostAsync($"/api/configuration-items/{Id(db1)}/proposals/{reverse}/reject", null)).StatusCode);

        // Rejetée, elle ne revient pas, même redéposée par le conteneur.
        await Json(await admin.DeleteAsync($"/api/configuration-items/relations/{(await RelationId(Id(db1), Id(app)))}"));
        Assert.Equal(200, (await Propose(factory, Id(db1), "relation", $"{Id(app)}|Fait partie de")).Status);
        Assert.Equal(0, (await Json(await gest.GetAsync($"{Queue}?perimeter={perimeter}"))).GetArrayLength());

        // La CMDB assistée coupée, la route n'existe pas (OPT-04).
        await using (var db = api.Database()) await db.OptionalFeatures.ExecuteDeleteAsync();
        using var off = Factory();
        Assert.Equal(HttpStatusCode.NotFound, (await As(off, manager, "User").PostAsJsonAsync($"{Queue}/{relate}/relate", new { })).StatusCode);
    }

    private async Task<int> RelationId(int source, int target)
    {
        await using var db = api.Database();
        return await db.CiRelationships.Where(r => r.SourceId == source && r.TargetId == target).Select(r => r.Id).SingleAsync();
    }

    [Fact]
    public async Task CMA_02_la_criticite_proposee_d_une_application_s_accepte_par_la_modification_du_CI()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (perimeter, manager) = await SiteAsync(tag);
        var app = await Json(await api.Client().PostAsJsonAsync("/api/configuration-items", App($"Paie {tag}", perimeter)));
        using var factory = Factory();
        Assert.Equal(400, (await Propose(factory, Id(app), "criticality", "Capitale")).Status);
        var (status, body) = await Propose(factory, Id(app), "criticality", "Vitale", "La paie arrête l'activité si elle tombe.");
        Assert.Equal(201, status);
        var id = JsonDocument.Parse(body).RootElement.GetProperty("id").GetInt32();
        var gest = As(factory, manager, "User");
        Assert.Equal("criticality", Assert.Single((await Json(await gest.GetAsync($"{Queue}?task=criticality&perimeter={perimeter}"))).EnumerateArray())
            .GetProperty("task").GetString());
        var put = new HttpRequestMessage(HttpMethod.Put, $"/api/configuration-items/{Id(app)}")
        {
            Content = JsonContent.Create(new
            {
                title = $"Paie {tag}", ciType = "Application", environment = "Production", location = "Siège", status = "Planifié", perimeterId = perimeter,
                ownerType = "User", ownerId = "ci.owner", ownerDisplayName = "Owner", criticality = "Vitale",
            }),
        };
        put.Headers.Add("X-S-Aloha-Proposal", id.ToString());
        Assert.Equal("Vitale", (await Json(await gest.SendAsync(put))).GetProperty("criticality").GetString());
        await using var db = api.Database();
        Assert.Equal("Acceptée", await db.Proposals.Where(p => p.Id == id).Select(p => p.Status).SingleAsync());
    }
}
