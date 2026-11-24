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

/// <summary>CMDB assistée : lancer et suivre une analyse (CMA-01), gestionnaires du périmètre, une à la fois.</summary>
[Collection("api")]
public class AssistedCmdbAnalysesTests(ApiFixture api) : IAsyncLifetime
{
    private const string Path = "/api/assisted-cmdb/analyses";

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

    /// <summary>Un hôte neuf relit l'état des fonctionnalités (la garde le garde en mémoire).</summary>
    private WebApplicationFactory<Program> Factory(int maxItems = 500) => new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
        .UseSetting("ConnectionStrings:Default", api.Database().Database.GetConnectionString())
        .UseSetting("Jwt:Key", ApiFixture.JwtKey).UseSetting("Jwt:Issuer", "S-Aloha")
        .UseSetting("Discovery:IntervalMinutes", "0").UseSetting("Discovery:VaultKey", "cle-du-coffre-de-test-uniquement-0123456789")
        .UseSetting("AssistedCmdb:MaxItems", maxItems.ToString()));

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

    /// <summary>Un périmètre neuf, son gestionnaire et deux CI (un en service, un retiré).</summary>
    private async Task<(int Perimeter, string Manager, int[] Cis)> SiteAsync()
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
        var cis = new List<int>();
        foreach (var (title, status) in new[] { ($"srvpaie-{tag}", "En service"), ($"ancien-{tag}", "Retiré") })
            cis.Add((await Json(await admin.PostAsJsonAsync("/api/configuration-items", new
            {
                title, ciType = "Serveur", environment = "Production", location = "Siège", status, perimeterId = perimeter,
                ownerType = "User", ownerId = "ci.owner", ownerDisplayName = "Owner",
            }))).GetProperty("id").GetInt32());
        return (perimeter, manager, cis.ToArray());
    }

    [Fact]
    public async Task CMA_01_un_gestionnaire_lance_une_analyse_du_perimetre_mise_en_file_une_seule_a_la_fois()
    {
        var (perimeter, manager, cis) = await SiteAsync();
        // CMDB assistée coupée : la route n'existe pas (OPT-04).
        using (var off = Factory())
            Assert.Equal(HttpStatusCode.NotFound, (await As(off, manager, "User").PostAsJsonAsync(Path, new { perimeterId = perimeter, tasks = new[] { "rename" } })).StatusCode);

        await EnableAsync();
        using var factory = Factory();
        var gest = As(factory, manager, "User");   // gestionnaire par son affectation au périmètre (PER-03)
        var res = await gest.PostAsJsonAsync(Path, new { perimeterId = perimeter, tasks = new[] { "rename", "normalize", "rename" } });
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        var a = await Json(res);
        Assert.Equal(("En file", true, 1, manager), (a.GetProperty("status").GetString(), a.GetProperty("wholePerimeter").GetBoolean(),
            a.GetProperty("itemCount").GetInt32(), a.GetProperty("requestedBy").GetString()));   // le CI retiré n'est pas compté
        Assert.Equal(["rename", "normalize"], a.GetProperty("tasks").EnumerateArray().Select(t => t.GetString()));

        // Une seule à la fois par périmètre.
        Assert.Contains("déjà en file", await Message(await gest.PostAsJsonAsync(Path, new { perimeterId = perimeter, tasks = new[] { "reclassify" } }),
            HttpStatusCode.Conflict));

        // Suivi : la liste du périmètre et la fiche ; un autre gestionnaire ne la voit pas.
        var id = a.GetProperty("id").GetInt32();
        Assert.Equal(id, (await Json(await gest.GetAsync($"{Path}?perimeter={perimeter}")))[0].GetProperty("id").GetInt32());
        Assert.Equal("En file", (await Json(await gest.GetAsync($"{Path}/{id}"))).GetProperty("status").GetString());
        var other = As(factory, "gest.ailleurs", "User");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"{Path}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync($"{Path}?perimeter={perimeter}")).StatusCode);

        // Sans compte rendu du conteneur depuis 30 minutes, l'analyse est interrompue et ne bloque plus.
        await using (var db = api.Database())
            await db.AssistedCmdbAnalyses.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.UpdatedAt, DateTime.UtcNow.AddMinutes(-31)));
        var next = await gest.PostAsJsonAsync(Path, new { perimeterId = perimeter, tasks = new[] { "reclassify" }, configurationItemIds = new[] { cis[0] } });
        Assert.Equal(HttpStatusCode.Accepted, next.StatusCode);
        var stale = await Json(await gest.GetAsync($"{Path}/{id}"));
        Assert.Equal("En échec", stale.GetProperty("status").GetString());
        Assert.Contains("aucun compte rendu du conteneur", stale.GetProperty("reason").GetString());
        Assert.False((await Json(next)).GetProperty("wholePerimeter").GetBoolean());
    }

    [Fact]
    public async Task CMA_01_refus_hors_gestionnaire_tache_inconnue_selection_hors_perimetre_ou_trop_grande()
    {
        var (perimeter, manager, cis) = await SiteAsync();
        var (otherPerimeter, _, otherCis) = await SiteAsync();
        await EnableAsync();
        using var factory = Factory(maxItems: 1);
        var gest = As(factory, manager, "User");

        // Un utilisateur du périmètre qui n'en est pas gestionnaire : 403 ; un périmètre qu'il ne voit pas : 403.
        await using (var db = api.Database())
        {
            db.PerimeterAssignments.Add(new PerimeterAssignment
            {
                PerimeterId = perimeter, PrincipalType = "User", PrincipalId = "agent.simple", PrincipalDisplayName = "Agent", Role = "User",
            });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden,
            (await As(factory, "agent.simple", "User").PostAsJsonAsync(Path, new { perimeterId = perimeter, tasks = new[] { "rename" } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await gest.PostAsJsonAsync(Path, new { perimeterId = otherPerimeter, tasks = new[] { "rename" } })).StatusCode);

        Assert.Contains("au moins une tâche", await Message(await gest.PostAsJsonAsync(Path, new { perimeterId = perimeter, tasks = Array.Empty<string>() }),
            HttpStatusCode.BadRequest));
        Assert.Contains("Tâche inconnue", await Message(await gest.PostAsJsonAsync(Path, new { perimeterId = perimeter, tasks = new[] { "supprimer" } }),
            HttpStatusCode.BadRequest));
        // Un CI d'un autre périmètre : refusé ; plus de CI que la limite (1 ici) : refusé.
        Assert.Contains("hors du périmètre", await Message(await gest.PostAsJsonAsync(Path,
            new { perimeterId = perimeter, tasks = new[] { "rename" }, configurationItemIds = new[] { otherCis[0] } }), HttpStatusCode.BadRequest));
        Assert.Contains("1 CI au plus", await Message(await gest.PostAsJsonAsync(Path,
            new { perimeterId = perimeter, tasks = new[] { "rename" }, configurationItemIds = cis }), HttpStatusCode.BadRequest));
        await using (var db = api.Database()) Assert.False(await db.AssistedCmdbAnalyses.AnyAsync(a => a.PerimeterId == perimeter));
    }
}
