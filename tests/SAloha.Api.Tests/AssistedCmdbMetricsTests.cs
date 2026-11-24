using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Features;
using SAloha.Api.Modules.ServiceConfigurationManagement;

namespace SAloha.Api.Tests;

/// <summary>Mesure de la CMDB assistée (CMA-10) : par tâche, sur 30 et 90 jours ; réponses non conformes ; durée des analyses.</summary>
[Collection("api")]
public class AssistedCmdbMetricsTests(ApiFixture api) : IAsyncLifetime
{
    private const string Path = "/api/admin/assisted-cmdb/metrics";

    public async Task InitializeAsync()
    {
        // Le démarrage de l'API applique les migrations : lancé seul, ce test arrive avant elles.
        await api.Client().GetAsync("/health/live");
        await ResetAsync();
    }

    public Task DisposeAsync() => ResetAsync();

    /// <summary>La mesure porte sur toutes les propositions, traces et analyses de la CMDB assistée : chaque test part de zéro.</summary>
    private async Task ResetAsync()
    {
        await using var db = api.Database();
        await db.OptionalFeatures.ExecuteDeleteAsync();
        await db.Proposals.Where(p => p.Feature == FeatureCatalog.AssistedCmdb).ExecuteDeleteAsync();
        await db.LanguageModelCalls.ExecuteDeleteAsync();
        await db.AssistedCmdbAnalyses.ExecuteDeleteAsync();
    }

    private WebApplicationFactory<Program> Factory() => new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
        .UseSetting("ConnectionStrings:Default", api.Database().Database.GetConnectionString())
        .UseSetting("Jwt:Key", ApiFixture.JwtKey).UseSetting("Jwt:Issuer", "S-Aloha")
        .UseSetting("Discovery:IntervalMinutes", "0").UseSetting("Discovery:VaultKey", "cle-du-coffre-de-test-uniquement-0123456789"));

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

    [Fact]
    public async Task CMA_10_la_mesure_donne_par_tache_les_parts_decidees_les_reponses_non_conformes_et_la_duree_des_analyses()
    {
        var now = DateTime.UtcNow;
        var perimeter = (await Json(await api.Client().PostAsJsonAsync("/api/admin/perimeters", new { name = $"Site {Guid.NewGuid():N}"[..13] })))
            .GetProperty("id").GetInt32();
        await using (var db = api.Database())
        {
            Proposal P(string target, string field, string status, int daysAgo = 1) => new()
            {
                Feature = FeatureCatalog.AssistedCmdb, TargetType = target, TargetId = 1, PerimeterId = perimeter, Field = field, After = "x",
                Status = status, CreatedAt = now.AddDays(-daysAgo),
            };
            db.Proposals.AddRange(
                P("ci", "title", Proposal.Accepted), P("ci", "title", Proposal.Accepted), P("ci", "title", Proposal.Corrected),
                P("ci", "title", Proposal.Rejected), P("ci", "title", Proposal.Open), P("ci", "title", Proposal.Expired, daysAgo: 60),
                P("ci", "merge", Proposal.Rejected),
                // L'aide au sas : propositions sur un actif ; « ciType » d'un actif ne compte pas pour Reclasser.
                P("asset", "matchedCiId", Proposal.Accepted), P("asset", "ciType", Proposal.Corrected),
                // Une autre fonctionnalité ne compte pas.
                new Proposal { Feature = FeatureCatalog.AssistedTriage, TargetType = "incident", TargetId = 1, PerimeterId = perimeter, Field = "category",
                    After = "x", Status = Proposal.Accepted, CreatedAt = now });
            LanguageModelCall C(string task, string outcome, int rejected, int ms, int daysAgo = 1, string feature = FeatureCatalog.AssistedCmdb) => new()
            {
                At = now.AddDays(-daysAgo), Feature = feature, Task = task, Outcome = outcome, Rejected = rejected, DurationMs = ms, Attempts = 1,
                InputHash = new string('a', 64),
            };
            db.LanguageModelCalls.AddRange(
                C("rename", LanguageModelCall.Valid, 0, 1000), C("rename", LanguageModelCall.Valid, 1, 3000),
                C("rename", LanguageModelCall.NonConforming, 2, 2000), C("rename", LanguageModelCall.TimedOut, 0, 6000),
                C("rename", LanguageModelCall.Valid, 0, 1000, daysAgo: 60), C("staging", LanguageModelCall.Valid, 0, 500),
                C("rename", LanguageModelCall.NonConforming, 2, 100, feature: LanguageModelCall.EssayFeature));
            AssistedCmdbAnalysis A(string status, int requestedMinutesAgo, int? seconds) => new()
            {
                PerimeterId = perimeter, Tasks = "rename", Status = status, RequestedAt = now.AddMinutes(-requestedMinutesAgo),
                StartedAt = now.AddMinutes(-requestedMinutesAgo + 1), FinishedAt = seconds is { } s ? now.AddMinutes(-requestedMinutesAgo + 1).AddSeconds(s) : null,
            };
            db.AssistedCmdbAnalyses.AddRange(A(AssistedCmdbAnalysis.Done, 11, 120), A(AssistedCmdbAnalysis.Done, 6, 240),
                A(AssistedCmdbAnalysis.Failed, 20, 30), A(AssistedCmdbAnalysis.Done, 60 * 24 * 60, 600));
            db.OptionalFeatures.Add(new OptionalFeature { Key = FeatureCatalog.AssistedCmdb, Enabled = true, UpdatedAt = now });
            await db.SaveChangesAsync();
        }

        using var factory = Factory();
        var windows = (await Json(await As(factory, "hery.rakoto", "Admin").GetAsync(Path))).GetProperty("windows").EnumerateArray().ToList();
        Assert.Equal([30, 90], windows.Select(w => w.GetProperty("days").GetInt32()));
        JsonElement Task(int window, string task) => windows[window].GetProperty("tasks").EnumerateArray().Single(t => t.GetProperty("task").GetString() == task);
        Assert.Equal(["rename", "normalize", "reclassify", "merge", "relate", "criticality", "staging"],
            windows[0].GetProperty("tasks").EnumerateArray().Select(t => t.GetProperty("task").GetString()));
        int I(JsonElement e, string name) => e.GetProperty(name).GetInt32();

        // Renommer, 30 jours : 4 décidées (2 acceptées, 1 corrigée, 1 rejetée), 1 en attente ; 90 jours : l'expirée en plus.
        var rename = Task(0, "rename");
        Assert.Equal((4, 50, 25, 25, 0, 1), (I(rename, "decided"), I(rename, "acceptedShare"), I(rename, "correctedShare"), I(rename, "rejectedShare"),
            I(rename, "expiredShare"), I(rename, "pending")));
        var rename90 = Task(1, "rename");
        Assert.Equal((5, 40, 20), (I(rename90, "decided"), I(rename90, "acceptedShare"), I(rename90, "expiredShare")));
        // OPT-25, OPT-27 : 5 réponses (2 conformes, 3 écartées), un délai ; l'essai de la page ne compte pas.
        Assert.Equal((4, 5, 3, 60, 1, 3000), (I(rename, "calls"), I(rename, "responses"), I(rename, "nonConforming"), I(rename, "nonConformingShare"),
            I(rename, "failedCalls"), I(rename, "averageCallMs")));
        Assert.Equal((5, 6, 50), (I(rename90, "calls"), I(rename90, "responses"), I(rename90, "nonConformingShare")));

        Assert.Equal((1, 100), (I(Task(0, "merge"), "decided"), I(Task(0, "merge"), "rejectedShare")));
        Assert.Equal(0, I(Task(0, "reclassify"), "decided"));
        var staging = Task(0, "staging");
        Assert.Equal((2, 50, 50, 1, 0), (I(staging, "decided"), I(staging, "acceptedShare"), I(staging, "correctedShare"), I(staging, "calls"),
            I(staging, "nonConformingShare")));

        // Durée des analyses terminées : 30 jours, 120 s et 240 s ; 90 jours, celle de 600 s en plus. Une analyse en échec se compte à part.
        var analyses = windows[0].GetProperty("analyses");
        Assert.Equal((2, 1, 180, 240), (I(analyses, "done"), I(analyses, "failed"), I(analyses, "averageSeconds"), I(analyses, "longestSeconds")));
        var analyses90 = windows[1].GetProperty("analyses");
        Assert.Equal((3, 320, 600), (I(analyses90, "done"), I(analyses90, "averageSeconds"), I(analyses90, "longestSeconds")));

        // Admin seul ; la CMDB assistée coupée, la route n'existe pas (OPT-04).
        Assert.Equal(HttpStatusCode.Forbidden, (await As(factory, "lova.rabe", "Manager").GetAsync(Path)).StatusCode);
        await using (var db = api.Database()) await db.OptionalFeatures.ExecuteDeleteAsync();
        using var off = Factory();
        Assert.Equal(HttpStatusCode.NotFound, (await As(off, "hery.rakoto", "Admin").GetAsync(Path)).StatusCode);
    }
}
