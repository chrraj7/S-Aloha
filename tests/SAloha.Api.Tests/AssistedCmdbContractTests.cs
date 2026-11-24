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

/// <summary>
/// CMDB assistée, contrat avec le conteneur (CMA-01, OPT-12, OPT-13) : l'analyse lancée part au fil, le conteneur la lit,
/// lit les CI du périmètre et en rend compte.
/// </summary>
[Collection("api")]
public class AssistedCmdbContractTests(ApiFixture api) : IAsyncLifetime
{
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

    private async Task EnableAsync(params string[] keys)
    {
        await using var db = api.Database();
        foreach (var key in keys) db.OptionalFeatures.Add(new OptionalFeature
        {
            Key = key, Enabled = true, UpdatedAt = DateTime.UtcNow,
            // Toutes les tâches proposées (CMA-12 : par défaut, celles mesurées sous 70 % sont coupées, CMA-11).
            SettingsJson = key == FeatureCatalog.AssistedCmdb ? """{"tasks":["rename","normalize","reclassify","merge","relate","criticality"]}""" : null,
        });
        await db.SaveChangesAsync();
    }

    private WebApplicationFactory<Program> Factory() => new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
        .UseSetting("ConnectionStrings:Default", api.Database().Database.GetConnectionString())
        .UseSetting("Jwt:Key", ApiFixture.JwtKey).UseSetting("Jwt:Issuer", "S-Aloha")
        .UseSetting("Discovery:IntervalMinutes", "0").UseSetting("Discovery:VaultKey", "cle-du-coffre-de-test-uniquement-0123456789")
        .UseSetting("Features:Token", FakeFeaturesContainer.Token).UseSetting("Features:InternalPort", "8081"));

    /// <summary>Appel du conteneur sur le port interne, avec son jeton.</summary>
    private static async Task<(int Status, JsonElement Body)> Internal(WebApplicationFactory<Program> host, string method, string pathAndQuery,
        object? body = null)
    {
        var context = await host.Server.SendAsync(c =>
        {
            c.Request.Method = method;
            var q = pathAndQuery.IndexOf('?');
            c.Request.Path = q < 0 ? pathAndQuery : pathAndQuery[..q];
            if (q >= 0) c.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString(pathAndQuery[q..]);
            c.Connection.LocalPort = 8081;
            c.Request.Headers.Authorization = $"Bearer {FakeFeaturesContainer.Token}";
            if (body is not null)
            {
                c.Request.ContentType = "application/json";
                c.Request.Body = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(body));
            }
        });
        var text = await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync();
        return (context.Response.StatusCode, text.Length > 0 && text[0] is '{' or '[' ? JsonDocument.Parse(text).RootElement : default);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode} : {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private async Task<(int Perimeter, int[] Cis)> SiteAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var admin = api.Client();
        var perimeter = (await Json(await admin.PostAsJsonAsync("/api/admin/perimeters", new { name = $"Site {tag}" }))).GetProperty("id").GetInt32();
        var cis = new List<int>();
        foreach (var (title, status) in new[] { ($"srvpaie-{tag}", "En service"), ($"appli-paie-{tag}", "En service"), ($"ancien-{tag}", "Retiré") })
            cis.Add((await Json(await admin.PostAsJsonAsync("/api/configuration-items", new
            {
                title, ciType = "Serveur", environment = "Production", location = "Siège", status, perimeterId = perimeter,
                ownerType = "User", ownerId = "ci.owner", ownerDisplayName = "Owner",
            }))).GetProperty("id").GetInt32());
        return (perimeter, cis.ToArray());
    }

    [Fact]
    public async Task CMA_01_l_analyse_part_au_fil_le_conteneur_la_lit_lit_les_CI_du_perimetre_et_en_rend_compte()
    {
        var (perimeter, cis) = await SiteAsync();
        var (otherPerimeter, otherCis) = await SiteAsync();
        await EnableAsync(FeatureCatalog.AssistedCmdb);
        using var host = Factory();
        var admin = host.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFixture.Token("hery.rakoto", "Admin"));
        long last;
        await using (var db = api.Database()) last = await db.FeatureEvents.MaxAsync(e => (long?)e.Id) ?? 0;
        var id = (await Json(await admin.PostAsJsonAsync("/api/assisted-cmdb/analyses", new { perimeterId = perimeter, tasks = new[] { "rename", "reclassify" } })))
            .GetProperty("id").GetInt32();

        // OPT-12 : l'événement « analysis.requested » est au fil, lu par la CMDB assistée.
        var (status, page) = await Internal(host, "GET", $"/internal/events?after={last}&limit=500");
        Assert.Equal(200, status);
        var e = Assert.Single(page.GetProperty("events").EnumerateArray(), x => x.GetProperty("type").GetString() == "analysis.requested");
        Assert.Equal(("cmdb-analysis", id, perimeter), (e.GetProperty("entityType").GetString(), e.GetProperty("entityId").GetInt32(), e.GetProperty("perimeterId").GetInt32()));

        // Le conteneur lit l'analyse, puis les CI du périmètre (hors retirés), page après page.
        var (_, analysis) = await Internal(host, "GET", $"/internal/assisted-cmdb/analyses/{id}");
        Assert.Equal((perimeter, "En file", JsonValueKind.Null), (analysis.GetProperty("perimeterId").GetInt32(), analysis.GetProperty("status").GetString(),
            analysis.GetProperty("configurationItemIds").ValueKind));
        Assert.Equal(["rename", "reclassify"], analysis.GetProperty("tasks").EnumerateArray().Select(t => t.GetString()));
        var (_, items) = await Internal(host, "GET", $"/internal/configuration-items?perimeter={perimeter}");
        Assert.Equal(cis[..2], items.EnumerateArray().Select(i => i.GetProperty("id").GetInt32()));
        Assert.DoesNotContain(items.EnumerateArray(), i => otherCis.Contains(i.GetProperty("id").GetInt32()));
        var (_, rest) = await Internal(host, "GET", $"/internal/configuration-items?perimeter={perimeter}&after={cis[0]}");
        Assert.Equal([cis[1]], rest.EnumerateArray().Select(i => i.GetProperty("id").GetInt32()));

        // Comptes rendus : En cours, puis un lot (propositions et écartées), puis Terminée ; ensuite, plus rien ne change.
        Assert.Equal(200, (await Internal(host, "PUT", $"/internal/assisted-cmdb/analyses/{id}", new { status = "En cours" })).Status);
        Assert.Equal(200, (await Internal(host, "PUT", $"/internal/assisted-cmdb/analyses/{id}", new { status = "En cours", proposalCount = 3, rejectedCount = 1 })).Status);
        Assert.Equal(400, (await Internal(host, "PUT", $"/internal/assisted-cmdb/analyses/{id}", new { status = "Abandonnée" })).Status);
        Assert.Equal(400, (await Internal(host, "PUT", $"/internal/assisted-cmdb/analyses/{id}", new { status = "En échec" })).Status);   // sans motif
        Assert.Equal(200, (await Internal(host, "PUT", $"/internal/assisted-cmdb/analyses/{id}", new { status = "Terminée", proposalCount = 4 })).Status);
        Assert.Equal(409, (await Internal(host, "PUT", $"/internal/assisted-cmdb/analyses/{id}", new { status = "En cours" })).Status);
        var done = await Json(await admin.GetAsync($"/api/assisted-cmdb/analyses/{id}"));
        Assert.Equal(("Terminée", 4, 1), (done.GetProperty("status").GetString(), done.GetProperty("proposalCount").GetInt32(), done.GetProperty("rejectedCount").GetInt32()));
        Assert.NotEqual(JsonValueKind.Null, done.GetProperty("startedAt").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, done.GetProperty("finishedAt").ValueKind);

        // Une analyse qui échoue porte son motif (OPT-28), visible du gestionnaire.
        var failing = (await Json(await admin.PostAsJsonAsync("/api/assisted-cmdb/analyses", new { perimeterId = otherPerimeter, tasks = new[] { "normalize" } })))
            .GetProperty("id").GetInt32();
        Assert.Equal(200, (await Internal(host, "PUT", $"/internal/assisted-cmdb/analyses/{failing}", new { status = "En échec", reason = "Pas de réponse du modèle en 120 s." })).Status);
        Assert.Equal("Pas de réponse du modèle en 120 s.", (await Json(await admin.GetAsync($"/api/assisted-cmdb/analyses/{failing}"))).GetProperty("reason").GetString());
    }

    [Fact]
    public async Task OPT_04_le_tri_seul_ne_parcourt_pas_un_perimetre_et_rien_ne_part_au_fil_CMDB_coupee()
    {
        var (perimeter, cis) = await SiteAsync();
        await EnableAsync(FeatureCatalog.AssistedTriage);
        using var host = Factory();
        // Le tri lit des CI désignés, jamais tout un périmètre.
        Assert.Equal(200, (await Internal(host, "GET", $"/internal/configuration-items?ids={cis[0]}")).Status);
        Assert.Equal(403, (await Internal(host, "GET", $"/internal/configuration-items?perimeter={perimeter}")).Status);
        Assert.Equal(403, (await Internal(host, "GET", "/internal/assisted-cmdb/analyses/1")).Status);
    }
}
