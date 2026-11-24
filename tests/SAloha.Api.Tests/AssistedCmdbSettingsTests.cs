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

/// <summary>
/// Réglages de la CMDB assistée (CMA-12) : tâches proposées, CI au plus par analyse, paires au plus par requête, aide au
/// sas ; bornes, journal d'audit (OPT-07), lecture par les gestionnaires et par le conteneur.
/// </summary>
[Collection("api")]
public class AssistedCmdbSettingsTests(ApiFixture api) : IAsyncLifetime
{
    private const string Path = $"/api/admin/features/{FeatureCatalog.AssistedCmdb}/settings";

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
        await db.AssistedCmdbAnalyses.ExecuteDeleteAsync();
        await db.OptionalFeatures.ExecuteDeleteAsync();
    }

    private async Task EnableAsync()
    {
        await using var db = api.Database();
        db.OptionalFeatures.Add(new OptionalFeature { Key = FeatureCatalog.AssistedCmdb, Enabled = true, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    /// <summary>Un hôte neuf relit l'état des fonctionnalités (la garde le garde en mémoire) ; port interne du conteneur.</summary>
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

    private static async Task<string> Message(HttpResponseMessage res, HttpStatusCode expected)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.StatusCode == expected, $"HTTP {(int)res.StatusCode} au lieu de {(int)expected} : {body}");
        return JsonDocument.Parse(body).RootElement.GetProperty("message").GetString()!;
    }

    /// <summary>Lecture du conteneur sur le port interne : statut HTTP et corps.</summary>
    private static async Task<(int Status, string Body)> Internal(WebApplicationFactory<Program> host, string path)
    {
        var parts = path.Split('?');
        var context = await host.Server.SendAsync(c =>
        {
            c.Request.Method = "GET"; c.Request.Path = parts[0];
            if (parts.Length > 1) c.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString("?" + parts[1]);
            c.Connection.LocalPort = 8081; c.Request.Headers.Authorization = $"Bearer {FakeFeaturesContainer.Token}";
        });
        return (context.Response.StatusCode, await new StreamReader(context.Response.Body).ReadToEndAsync());
    }

    private static object Settings(string[]? tasks = null, int maxItems = 500, int pairs = 50, bool staging = true) => new
    {
        tasks = tasks ?? ["rename", "normalize", "reclassify", "merge", "relate", "criticality"], maxItems, pairsPerRequest = pairs, stagingHelp = staging,
    };

    /// <summary>Un périmètre neuf, son gestionnaire et un CI en service.</summary>
    private async Task<(int Perimeter, string Manager, int Ci)> SiteAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var admin = api.Client();
        var perimeter = (await Json(await admin.PostAsJsonAsync("/api/admin/perimeters", new { name = $"Site {tag}" }))).GetProperty("id").GetInt32();
        var manager = $"gest.{tag}";
        await using (var db = api.Database())
        {
            db.PerimeterAssignments.Add(new PerimeterAssignment
            {
                PerimeterId = perimeter, PrincipalType = "User", PrincipalId = manager, PrincipalDisplayName = "Gestionnaire du site", Role = "Manager",
            });
            await db.SaveChangesAsync();
        }
        var ci = (await Json(await admin.PostAsJsonAsync("/api/configuration-items", new
        {
            title = $"srvpaie-{tag}", ciType = "Serveur", environment = "Production", location = "Siège", status = "En service", perimeterId = perimeter,
            ownerType = "User", ownerId = "ci.owner", ownerDisplayName = "Owner",
        }))).GetProperty("id").GetInt32();
        return (perimeter, manager, ci);
    }

    [Fact]
    public async Task CMA_12_les_reglages_par_defaut_se_modifient_chaque_changement_est_trace_et_une_tache_retiree_ne_se_lance_plus()
    {
        var admin = api.Client();
        var defaults = await Json(await admin.GetAsync(Path));
        // CMA-11 : Normaliser, Fusionner et Relier, sous 70 % de précision mesurée, sont coupées par défaut.
        Assert.Equal(["rename", "reclassify", "criticality"], defaults.GetProperty("tasks").EnumerateArray().Select(t => t.GetString()));
        Assert.Equal((500, 50, true), (defaults.GetProperty("maxItems").GetInt32(), defaults.GetProperty("pairsPerRequest").GetInt32(),
            defaults.GetProperty("stagingHelp").GetBoolean()));

        await EnableAsync();
        long last;
        await using (var db = api.Database()) last = await db.AuditEntries.MaxAsync(a => (long?)a.Id) ?? 0;
        // Les tâches se rangent dans l'ordre de la liste, sans doublon.
        var saved = await Json(await admin.PutAsJsonAsync(Path, Settings(["reclassify", "rename", "rename"], maxItems: 20, pairs: 10)));
        Assert.Equal(["rename", "reclassify"], saved.GetProperty("tasks").EnumerateArray().Select(t => t.GetString()));
        await using (var db = api.Database())
        {
            var lines = await db.AuditEntries.AsNoTracking().Where(a => a.Id > last).OrderBy(a => a.Id).ToListAsync();
            Assert.Equal(
            [
                ("CMDB assistée — Tâches proposées", "Renommer, Reclasser, Criticité", "Renommer, Reclasser"),
                ("CMDB assistée — CI au plus par analyse", "500", "20"), ("CMDB assistée — Paires au plus par requête", "50", "10"),
            ], lines.Select(a => (a.Field, a.OldValue, a.NewValue)));
            Assert.All(lines, a => Assert.Equal(("parametre", "Fonctionnalités"), (a.EntityType, a.Reference)));
            last = lines[^1].Id;
            // La fonctionnalité reste activée : les réglages ne touchent qu'à leur colonne.
            Assert.True((await db.OptionalFeatures.AsNoTracking().SingleAsync(f => f.Key == FeatureCatalog.AssistedCmdb)).Enabled);
        }
        // Une demande qui ne change rien n'écrit rien (OPT-07).
        await Json(await admin.PutAsJsonAsync(Path, Settings(["rename", "reclassify"], maxItems: 20, pairs: 10)));
        await using (var db = api.Database()) Assert.False(await db.AuditEntries.AnyAsync(a => a.Id > last));
        Assert.Equal(HttpStatusCode.Forbidden, (await api.Client("Manager", "lova.rabe").PutAsJsonAsync(Path, Settings())).StatusCode);

        // Les gestionnaires lisent ce qui les concerne ; une tâche retirée ne se lance pas, la limite réglée vaut.
        var (perimeter, manager, ci) = await SiteAsync();
        using var factory = Factory();
        var gest = As(factory, manager, "User");
        var shown = await Json(await gest.GetAsync("/api/assisted-cmdb/settings"));
        Assert.Equal(["rename", "reclassify"], shown.GetProperty("tasks").EnumerateArray().Select(t => t.GetString()));
        Assert.Equal((20, true), (shown.GetProperty("maxItems").GetInt32(), shown.GetProperty("stagingHelp").GetBoolean()));
        Assert.Contains("non proposée par l'administrateur : « Fusionner »", await Message(await gest.PostAsJsonAsync("/api/assisted-cmdb/analyses",
            new { perimeterId = perimeter, tasks = new[] { "rename", "merge" } }), HttpStatusCode.BadRequest));
        Assert.Contains("20 CI au plus", await Message(await gest.PostAsJsonAsync("/api/assisted-cmdb/analyses",
            new { perimeterId = perimeter, tasks = new[] { "rename" }, configurationItemIds = Enumerable.Range(ci, 21).ToArray() }), HttpStatusCode.BadRequest));
        Assert.Equal(HttpStatusCode.Accepted, (await gest.PostAsJsonAsync("/api/assisted-cmdb/analyses",
            new { perimeterId = perimeter, tasks = new[] { "reclassify" } })).StatusCode);

        // Le conteneur lit les mêmes réglages sur le port interne.
        var (status, body) = await Internal(factory, $"/internal/features/{FeatureCatalog.AssistedCmdb}/settings");
        Assert.Equal(200, status);
        Assert.Equal(10, JsonDocument.Parse(body).RootElement.GetProperty("pairsPerRequest").GetInt32());
    }

    [Theory]
    [InlineData("supprimer", 500, 50, "Tâche inconnue")]
    [InlineData("rename", 19, 50, "CI au plus par analyse")]
    [InlineData("rename", 501, 50, "CI au plus par analyse")]
    [InlineData("rename", 500, 9, "Paires au plus par requête")]
    [InlineData("rename", 500, 51, "Paires au plus par requête")]
    public async Task CMA_12_un_reglage_hors_de_ses_bornes_est_refuse(string task, int maxItems, int pairs, string expected)
    {
        var res = await api.Client().PutAsJsonAsync(Path, Settings([task], maxItems, pairs));
        Assert.Contains(expected, await Message(res, HttpStatusCode.BadRequest));
        await using var db = api.Database();
        Assert.False(await db.OptionalFeatures.AnyAsync(f => f.Key == FeatureCatalog.AssistedCmdb));
    }

    [Fact]
    public async Task CMA_12_l_aide_au_sas_coupee_ne_remet_plus_d_actif_au_conteneur()
    {
        var (perimeter, _, _) = await SiteAsync();
        await EnableAsync();
        var admin = api.Client();
        var tag = Guid.NewGuid().ToString("N")[..8];
        var job = (await Json(await admin.PostAsJsonAsync("/api/discovery/jobs", new
        {
            name = $"Balayage {tag}", scanType = "IpSweep", targetRange = "10.62.0.0/28", frequencyHours = 0, perimeterId = perimeter, assistedCmdb = true,
        }))).GetProperty("id").GetInt32();
        await using (var db = api.Database())
        {
            db.DiscoveredAssets.Add(new DiscoveredAsset
            {
                PerimeterId = perimeter, Hostname = $"inconnu-{tag}", IpAddress = "10.62.0.9", DetectedType = "Serveur", Status = DiscoveredAsset.ToReconcile,
                JobId = job,
            });
            await db.SaveChangesAsync();
        }
        int Assets(string body) => JsonDocument.Parse(body).RootElement.GetProperty("assets").GetArrayLength();
        using var factory = Factory();
        Assert.Equal(1, Assets((await Internal(factory, $"/internal/discovery/staging?job={job}")).Body));

        await Json(await admin.PutAsJsonAsync(Path, Settings(staging: false)));
        Assert.False((await Json(await As(factory, "hery.rakoto", "Admin").GetAsync("/api/assisted-cmdb/settings"))).GetProperty("stagingHelp").GetBoolean());
        Assert.Equal(0, Assets((await Internal(factory, $"/internal/discovery/staging?job={job}")).Body));
    }
}
