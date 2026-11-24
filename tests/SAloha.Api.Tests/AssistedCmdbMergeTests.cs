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

/// <summary>CMDB assistée : fusion de deux CI (CMA-05) et paires rejetées (CMA-06, côté API).</summary>
[Collection("api")]
public class AssistedCmdbMergeTests(ApiFixture api) : IAsyncLifetime
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

    [Fact]
    public async Task CMA_05_la_fusion_reporte_liens_relations_et_actifs_du_sas_puis_retire_le_doublon_trace_sur_les_deux_CI()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (perimeter, manager) = await SiteAsync(tag);
        var admin = api.Client();
        var dup = await Json(await admin.PostAsJsonAsync("/api/configuration-items", Ci($"Serveur paie prod {tag}", perimeter)));
        var keep = await Json(await admin.PostAsJsonAsync("/api/configuration-items", Ci($"SRV-PAIE-01-{tag}", perimeter)));
        var host = await Json(await admin.PostAsJsonAsync("/api/configuration-items", Ci($"VM-HOTE-{tag}", perimeter)));
        var db1 = await Json(await admin.PostAsJsonAsync("/api/configuration-items", Ci($"BDD-PAIE-{tag}", perimeter)));
        async Task Relate(JsonElement from, JsonElement to, string type) =>
            await Json(await admin.PostAsJsonAsync($"/api/configuration-items/{Id(from)}/relations", new { targetReference = Ref(to), type }));
        await Relate(host, dup, "Héberge");          // reportée : VM-HOTE Héberge SRV-PAIE-01
        await Relate(dup, db1, "Dépend de");         // déjà portée par le CI conservé : ne se double pas
        await Relate(keep, db1, "Dépend de");
        await Relate(dup, keep, "Se connecte à");    // deviendrait une relation du CI vers lui-même : disparaît

        object Incident(string title) => new { title, description = "d", impact = "Faible", urgency = "Faible", category = "Test", affectedService = "Test", perimeterId = perimeter };
        var open = await Json(await admin.PostAsJsonAsync("/api/incidents", Incident($"Paie lente {tag}")));
        var shared = await Json(await admin.PostAsJsonAsync("/api/incidents", Incident($"Paie en erreur {tag}")));
        async Task Link(JsonElement incident, JsonElement ci) =>
            await Json(await admin.PostAsJsonAsync("/api/links", new { fromType = "incident", fromId = Id(incident), toReference = Ref(ci) }));
        await Link(open, dup);       // incident ouvert : sans report, CFG-13 interdirait de retirer le doublon
        await Link(shared, dup);     // déjà relié au CI conservé : pas de lien en double
        await Link(shared, keep);
        int asset;
        await using (var db = api.Database())
        {
            var a = new DiscoveredAsset { PerimeterId = perimeter, Hostname = $"srvpaie-{tag}", MatchedCiId = Id(dup), Status = DiscoveredAsset.ToReconcile };
            db.DiscoveredAssets.Add(a);
            await db.SaveChangesAsync();
            asset = a.Id;
        }

        using var factory = Factory();
        var (status, body) = await Propose(factory, Id(dup), "merge", Id(keep).ToString());
        Assert.Equal(201, status);
        var merge = JsonDocument.Parse(body).RootElement.GetProperty("id").GetInt32();
        Assert.Equal(201, (await Propose(factory, Id(dup), "title", $"SRV-PAIE-02-{tag}")).Status);   // ouverte sur le doublon : expirera
        var gest = As(factory, manager, "User");

        // La file décrit la fusion : le doublon visé, et l'autre CI, que la proposition suggère de conserver.
        var item = Assert.Single((await Json(await gest.GetAsync($"{Queue}?perimeter={perimeter}&task=merge"))).EnumerateArray());
        Assert.Equal((Id(dup), Id(keep), Ref(keep), false), (item.GetProperty("ciId").GetInt32(), item.GetProperty("other").GetProperty("id").GetInt32(),
            item.GetProperty("other").GetProperty("reference").GetString(), item.GetProperty("stale").GetBoolean()));

        // Le gestionnaire garde le CI suggéré : la proposition est acceptée.
        var done = await Json(await gest.PostAsJsonAsync($"{Queue}/{merge}/merge", new { keepId = Id(keep) }));
        Assert.Equal((1, 1, 1), (done.GetProperty("links").GetInt32(), done.GetProperty("relations").GetInt32(), done.GetProperty("assets").GetInt32()));

        var retired = await Json(await admin.GetAsync($"/api/configuration-items/{Id(dup)}"));
        Assert.Equal("Retiré", retired.GetProperty("status").GetString());
        var kept = await Json(await admin.GetAsync($"/api/configuration-items/{Id(keep)}"));
        Assert.Equal("En service", kept.GetProperty("status").GetString());
        await using (var db = api.Database())
        {
            var links = await db.ItemLinks.AsNoTracking().Where(l => l.FromType == "incident" && (l.FromId == Id(open) || l.FromId == Id(shared))).ToListAsync();
            Assert.Equal([(Id(open), Id(keep)), (Id(shared), Id(keep))], links.Select(l => (l.FromId, l.ToId)).Order());
            var relations = await db.CiRelationships.AsNoTracking().Where(r => new[] { Id(dup), Id(keep), Id(host), Id(db1) }.Contains(r.SourceId)).ToListAsync();
            Assert.Equal([(Id(keep), Id(db1), "Dépend de"), (Id(host), Id(keep), "Héberge")],
                relations.Select(r => (r.SourceId, r.TargetId, r.Type)).OrderBy(r => r.Item3));
            Assert.Equal(Id(keep), (await db.DiscoveredAssets.AsNoTracking().SingleAsync(a => a.Id == asset)).MatchedCiId);
            var proposals = await db.Proposals.AsNoTracking().Where(p => p.TargetId == Id(dup) && p.Feature == FeatureCatalog.AssistedCmdb)
                .OrderBy(p => p.Id).Select(p => new { p.Status, p.FinalValue }).ToListAsync();
            Assert.Equal([("Acceptée", Id(keep).ToString()), ("Expirée", null)], proposals.Select(p => (p.Status, p.FinalValue)));
        }

        // Le journal des deux CI trace la fusion ; la transition du doublon porte son motif ; l'origine est la CMDB assistée.
        var dupAudit = (await Json(await admin.GetAsync($"/api/audit?type=ci&id={Id(dup)}"))).EnumerateArray().ToList();
        var transition = Assert.Single(dupAudit, a => a.GetProperty("field").GetString() == "status");
        Assert.Equal(("Retiré", $"Fusionné dans {Ref(keep)}", "CMDB assistée"), (transition.GetProperty("newValue").GetString(),
            transition.GetProperty("reason").GetString(), transition.GetProperty("origin").GetString()));
        Assert.Contains(dupAudit, a => a.GetProperty("action").GetString() == "Fusion" && a.GetProperty("newValue").GetString()!.Contains(Ref(keep)));
        var keepAudit = (await Json(await admin.GetAsync($"/api/audit?type=ci&id={Id(keep)}"))).EnumerateArray().ToList();
        Assert.Contains(keepAudit, a => a.GetProperty("action").GetString() == "Fusion" && a.GetProperty("newValue").GetString()!.Contains(Ref(dup))
            && a.GetProperty("origin").GetString() == "CMDB assistée");
        Assert.Contains(keepAudit, a => a.GetProperty("action").GetString() == "Lien ajouté" && a.GetProperty("newValue").GetString() == Ref(open));
        Assert.Equal(0, (await Json(await gest.GetAsync($"{Queue}?perimeter={perimeter}"))).GetArrayLength());
    }

    [Fact]
    public async Task CMA_05_CMA_06_fusion_refusee_hors_paire_entre_environnements_ou_perimetres_et_paire_rejetee_jamais_reproposee()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (perimeter, manager) = await SiteAsync(tag);
        var (elsewhere, _) = await SiteAsync($"{tag}b");
        var admin = api.Client();
        var a = await Json(await admin.PostAsJsonAsync("/api/configuration-items", Ci($"srvpaie1-{tag}", perimeter)));
        var b = await Json(await admin.PostAsJsonAsync("/api/configuration-items", Ci($"SRV-PAIE-01-{tag}", perimeter)));
        var recette = await Json(await admin.PostAsJsonAsync("/api/configuration-items", Ci($"SRV-PAIE-01-{tag}", perimeter, "Recette")));
        var usine = await Json(await admin.PostAsJsonAsync("/api/configuration-items", Ci($"SRV-PAIE-USINE-{tag}", elsewhere)));

        using var factory = Factory();
        // À son dépôt (OPT-14) : ni entre deux environnements, ni entre deux périmètres, ni avec soi-même, ni vers un CI inconnu.
        Assert.Equal(400, (await Propose(factory, Id(a), "merge", Id(recette).ToString())).Status);
        Assert.Equal(400, (await Propose(factory, Id(a), "merge", Id(usine).ToString())).Status);
        Assert.Equal(400, (await Propose(factory, Id(a), "merge", Id(a).ToString())).Status);
        Assert.Equal(400, (await Propose(factory, Id(a), "merge", "CI-inconnu")).Status);
        var (status, body) = await Propose(factory, Id(a), "merge", Id(b).ToString());
        Assert.Equal(201, status);
        var merge = JsonDocument.Parse(body).RootElement.GetProperty("id").GetInt32();
        // La paire compte dans les deux sens : redéposée à l'envers, c'est la même proposition.
        var again = await Propose(factory, Id(b), "merge", Id(a).ToString());
        Assert.Equal((200, merge), (again.Status, JsonDocument.Parse(again.Body).RootElement.GetProperty("id").GetInt32()));

        var gest = As(factory, manager, "User");
        // Le CI conservé est l'un des deux ; seul le gestionnaire du périmètre fusionne ; la modification du CI ne décide pas une fusion.
        Assert.Equal(HttpStatusCode.BadRequest, (await gest.PostAsJsonAsync($"{Queue}/{merge}/merge", new { keepId = Id(recette) })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await As(factory, "gest.ailleurs", "User").PostAsJsonAsync($"{Queue}/{merge}/merge", new { keepId = Id(b) })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await As(factory, $"agent.{tag}", "User").PostAsJsonAsync($"{Queue}/{merge}/merge", new { keepId = Id(b) })).StatusCode);
        var put = new HttpRequestMessage(HttpMethod.Put, $"/api/configuration-items/{Id(a)}") { Content = JsonContent.Create(Ci($"srvpaie1-{tag}", perimeter)) };
        put.Headers.Add("X-S-Aloha-Proposal", merge.ToString());
        Assert.Equal(HttpStatusCode.BadRequest, (await gest.SendAsync(put)).StatusCode);

        // Un CI passé dans un autre environnement depuis : refusée (400) ; revenu, la proposition est périmée (409).
        await Json(await admin.PutAsJsonAsync($"/api/configuration-items/{Id(b)}", Ci($"SRV-PAIE-01-{tag}", perimeter, "Développement")));
        var refused = await gest.PostAsJsonAsync($"{Queue}/{merge}/merge", new { keepId = Id(b) });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("environnements différents", await refused.Content.ReadAsStringAsync());
        await Json(await admin.PutAsJsonAsync($"/api/configuration-items/{Id(b)}", Ci($"SRV-PAIE-01-{tag}", perimeter)));
        Assert.Equal(HttpStatusCode.Conflict, (await gest.PostAsJsonAsync($"{Queue}/{merge}/merge", new { keepId = Id(b) })).StatusCode);
        Assert.True((await Json(await gest.GetAsync($"{Queue}?perimeter={perimeter}&task=merge")))[0].GetProperty("stale").GetBoolean());

        // CMA-06 : rejetée, la paire n'est plus reproposée, dans aucun sens, et le conteneur la lit pour ne plus la soumettre.
        Assert.Equal(HttpStatusCode.NoContent, (await gest.PostAsync($"/api/configuration-items/{Id(a)}/proposals/{merge}/reject", null)).StatusCode);
        Assert.Equal(200, (await Propose(factory, Id(b), "merge", Id(a).ToString())).Status);
        Assert.Equal(0, (await Json(await gest.GetAsync($"{Queue}?perimeter={perimeter}"))).GetArrayLength());
        var pairs = JsonDocument.Parse(await Internal(factory, $"/internal/assisted-cmdb/declined-pairs?perimeter={perimeter}")).RootElement;
        Assert.Equal([new[] { Math.Min(Id(a), Id(b)), Math.Max(Id(a), Id(b)) }], pairs.EnumerateArray().Select(p => p.EnumerateArray().Select(x => x.GetInt32()).ToArray()));
        Assert.Equal("[]", await Internal(factory, $"/internal/assisted-cmdb/declined-pairs?perimeter={elsewhere}"));

        // La CMDB assistée coupée, la fusion n'existe pas (OPT-04).
        await using (var db = api.Database()) await db.OptionalFeatures.ExecuteDeleteAsync();
        using var off = Factory();
        Assert.Equal(HttpStatusCode.NotFound, (await As(off, manager, "User").PostAsJsonAsync($"{Queue}/{merge}/merge", new { keepId = Id(b) })).StatusCode);
    }
}
