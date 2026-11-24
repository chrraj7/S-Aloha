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

/// <summary>CMDB assistée : aide au sas (CMA-08), propositions sur les actifs décidées au rapprochement (CFG-40).</summary>
[Collection("api")]
public class AssistedCmdbStagingTests(ApiFixture api) : IAsyncLifetime
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
    public async Task CMA_08_les_actifs_laisses_au_sas_recoivent_un_CI_candidat_ou_un_type_decides_au_rapprochement()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (perimeter, manager) = await SiteAsync(tag);
        var (elsewhere, _) = await SiteAsync($"{tag}b");
        var admin = api.Client();
        var server = await Json(await admin.PostAsJsonAsync("/api/configuration-items", Ci($"srvpaie-{tag}", perimeter)));
        var app = await Json(await admin.PostAsJsonAsync("/api/configuration-items", new
        {
            title = $"Paie {tag}", ciType = "Application", environment = "Production", location = "Siège", status = "Planifié", perimeterId = perimeter,
        }));
        var usine = await Json(await admin.PostAsJsonAsync("/api/configuration-items", Ci($"srv-usine-{tag}", elsewhere)));

        // La tâche porte l'option « Analyser avec la CMDB assistée ».
        var job = await Json(await admin.PostAsJsonAsync("/api/discovery/jobs", new
        {
            name = $"Balayage {tag}", scanType = "IpSweep", targetRange = "10.61.0.0/28", frequencyHours = 0, perimeterId = perimeter, assistedCmdb = true,
        }));
        Assert.True(job.GetProperty("assistedCmdb").GetBoolean());
        int a1, a2, other;
        await using (var db = api.Database())
        {
            DiscoveredAsset A(string host, string status = DiscoveredAsset.ToReconcile, int? matched = null) => new()
            {
                PerimeterId = perimeter, Hostname = host, IpAddress = $"10.61.0.{Random.Shared.Next(2, 250)}", DetectedType = "Serveur", Status = status,
                MatchedCiId = matched, JobId = Id(job),
            };
            var list = new[] { A($"srvpaie-{tag}.siege.local"), A($"imprimante-{tag}"), A($"vu-{tag}", DiscoveredAsset.Reconciled, Id(server)),
                A($"apparie-{tag}", DiscoveredAsset.ToReconcile, Id(server)) };
            db.DiscoveredAssets.AddRange(list);
            await db.SaveChangesAsync();
            (a1, a2, other) = (list[0].Id, list[1].Id, list[3].Id);
        }

        using var factory = Factory();
        // OPT-13 : seuls les actifs laissés sans rapprochement par CFG-41 sont remis au conteneur.
        var read = JsonDocument.Parse(await Internal(factory, $"/internal/discovery/staging?job={Id(job)}")).RootElement;
        Assert.Equal([a1, a2], read.GetProperty("assets").EnumerateArray().Select(a => a.GetProperty("id").GetInt32()));

        // OPT-14 : un CI candidat du périmètre, serveur ou équipement, non retiré ; un type de la liste.
        Assert.Equal(400, (await PropositionActif(factory, a1, "matchedCiId", Id(app).ToString())).Status);
        Assert.Equal(400, (await PropositionActif(factory, a1, "matchedCiId", Id(usine).ToString())).Status);
        Assert.Equal(400, (await PropositionActif(factory, a2, "ciType", "Imprimante")).Status);
        Assert.Equal(201, (await PropositionActif(factory, a1, "matchedCiId", Id(server).ToString(), "Même nom d'hôte que le CI.")).Status);
        Assert.Equal(201, (await PropositionActif(factory, a1, "ciType", "Réseau")).Status);
        Assert.Equal(201, (await PropositionActif(factory, a2, "ciType", "Poste de travail", "Ports d'impression ouverts.")).Status);

        // La fiche de l'actif, pour le gestionnaire du périmètre seulement.
        var gest = As(factory, manager, "User");
        var shown = (await Json(await gest.GetAsync($"/api/discovery/staging/{a1}/proposals"))).EnumerateArray().ToList();
        Assert.Equal((Ref(server), "Même nom d'hôte que le CI."), (shown[0].GetProperty("candidate").GetProperty("reference").GetString(),
            shown[0].GetProperty("reasons").GetString()));
        Assert.Equal(HttpStatusCode.NotFound, (await As(factory, "gest.ailleurs", "User").GetAsync($"/api/discovery/staging/{a1}/proposals")).StatusCode);

        // CFG-40 décide : rapprocher du candidat l'accepte, et corrige le type proposé.
        await Json(await gest.PostAsJsonAsync($"/api/discovery/staging/{a1}/reconcile", new { action = "update", targetCiId = Id(server) }));
        // Ignorer l'actif rejette ses propositions.
        await Json(await gest.PostAsync($"/api/discovery/staging/{a2}/ignore", null));
        await using (var db = api.Database())
        {
            var decided = await db.Proposals.AsNoTracking().Where(p => p.TargetType == "asset" && (p.TargetId == a1 || p.TargetId == a2))
                .OrderBy(p => p.Id).ToListAsync();
            Assert.Equal([("matchedCiId", "Acceptée", Id(server).ToString()), ("ciType", "Corrigée", null), ("ciType", "Rejetée", null)],
                decided.Select(p => (p.Field, p.Status, p.FinalValue)));
        }

        // Une tâche sans l'option : rien n'est remis au conteneur.
        await Json(await admin.PutAsJsonAsync($"/api/discovery/jobs/{Id(job)}", new
        {
            name = $"Balayage {tag}", scanType = "IpSweep", targetRange = "10.61.0.0/28", frequencyHours = 0, perimeterId = perimeter, assistedCmdb = false,
        }));
        Assert.Equal(0, JsonDocument.Parse(await Internal(factory, $"/internal/discovery/staging?job={Id(job)}")).RootElement.GetProperty("assets").GetArrayLength());

        // La CMDB assistée coupée, la fiche n'a plus de propositions (OPT-04).
        await using (var db = api.Database()) await db.OptionalFeatures.ExecuteDeleteAsync();
        using var off = Factory();
        Assert.Equal(HttpStatusCode.NotFound, (await As(off, manager, "User").GetAsync($"/api/discovery/staging/{other}/proposals")).StatusCode);
    }

    /// <summary>Dépôt d'une proposition sur un actif du sas, par le conteneur (OPT-14).</summary>
    private static async Task<(int Status, string Body)> PropositionActif(WebApplicationFactory<Program> host, int asset, string field, string after,
        string reasons = "Aide au sas.")
    {
        var context = await host.Server.SendAsync(c =>
        {
            c.Request.Method = "POST"; c.Request.Path = "/internal/proposals"; c.Connection.LocalPort = 8081;
            c.Request.Headers.Authorization = $"Bearer {FakeFeaturesContainer.Token}";
            c.Request.ContentType = "application/json";
            c.Request.Body = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new
            {
                feature = FeatureCatalog.AssistedCmdb, targetType = "asset", targetId = asset, field, after, reasons,
                cited = Array.Empty<object>(), confidence = (double?)null,
            }));
        });
        return (context.Response.StatusCode, await new StreamReader(context.Response.Body).ReadToEndAsync());
    }
}
