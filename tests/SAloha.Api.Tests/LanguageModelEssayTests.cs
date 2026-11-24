using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Features;

namespace SAloha.Api.Tests;

/// <summary>Conteneur factice qui répond à l'essai du modèle de langage (OPT-26) par une trace réglable.</summary>
public sealed class FakeEssayContainer : IAsyncLifetime
{
    private WebApplication app = null!;
    public string Url { get; private set; } = "";
    public readonly ConcurrentQueue<(string Authorization, JsonElement Body)> Received = new();
    public volatile string Outcome = LanguageModelCall.Valid;
    public volatile string? Error;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0));
        app = builder.Build();
        app.MapPost("/language-model/test", async (HttpContext c) =>
        {
            Received.Enqueue((c.Request.Headers.Authorization.ToString(), await JsonSerializer.DeserializeAsync<JsonElement>(c.Request.Body)));
            return Results.Json(new
            {
                call = new
                {
                    feature = LanguageModelCall.EssayFeature, task = LanguageModelCall.EssayTask, model = "qwen2.5:7b", location = "Dans le SI",
                    promptVersion = "1", inputHash = new string('a', 64), durationMs = 840, outcome = Outcome, attempts = 1, rejected = 0, error = Error,
                },
            });
        });
        await app.StartAsync();
        Url = app.Urls.Single();
    }

    public async Task DisposeAsync() => await app.DisposeAsync();
}

/// <summary>Essai du modèle de langage (OPT-26), traces des appels et compteurs (OPT-27).</summary>
[Collection("api")]
public class LanguageModelEssayTests(ApiFixture api, FakeEssayContainer container) : IClassFixture<FakeEssayContainer>, IAsyncLifetime
{
    private const string Path = "/api/admin/settings/language-model";

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
        await db.LanguageModelCalls.ExecuteDeleteAsync();
        await db.LanguageModelSettings.ExecuteDeleteAsync();
        await db.OptionalFeatures.ExecuteDeleteAsync();
        container.Received.Clear();
        (container.Outcome, container.Error) = (LanguageModelCall.Valid, null);
    }

    private WebApplicationFactory<Program> Factory(string? featuresUrl) => new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
        .UseSetting("ConnectionStrings:Default", api.Database().Database.GetConnectionString())
        .UseSetting("Jwt:Key", ApiFixture.JwtKey).UseSetting("Jwt:Issuer", "S-Aloha")
        .UseSetting("Discovery:IntervalMinutes", "0").UseSetting("Discovery:VaultKey", "cle-du-coffre-de-test-uniquement-0123456789")
        .UseSetting("Features:Url", featuresUrl ?? "").UseSetting("Features:Token", FakeFeaturesContainer.Token).UseSetting("Features:InternalPort", "8081"));

    private static HttpClient Admin(WebApplicationFactory<Program> factory, string role = "Admin")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFixture.Token("hery.rakoto", role));
        return client;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode} : {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static object Settings(bool enabled) => new
    {
        enabled, location = "Dans le SI", url = "http://10.0.0.5:11434/v1", model = "qwen2.5:7b", key = "cle-de-l-essai",
        timeoutSeconds = 30, maxResponseKb = 64,
    };

    [Fact]
    public async Task OPT_26_l_essai_passe_par_le_conteneur_avec_les_reglages_enregistres_meme_desactives()
    {
        using var factory = Factory(container.Url);
        var admin = Admin(factory);
        // Rien d'enregistré : 400.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"{Path}/test", null)).StatusCode);

        // Réglages enregistrés mais désactivés, CMDB assistée coupée : l'essai passe quand même.
        await Json(await admin.PutAsJsonAsync(Path, Settings(enabled: false)));
        var ok = await Json(await admin.PostAsync($"{Path}/test", null));
        Assert.True(ok.GetProperty("ok").GetBoolean());
        Assert.Contains("joignable", ok.GetProperty("message").GetString());
        Assert.Equal(840, ok.GetProperty("durationMs").GetInt32());
        var (authorization, body) = Assert.Single(container.Received);
        Assert.Equal($"Bearer {FakeFeaturesContainer.Token}", authorization);
        Assert.Equal(("cle-de-l-essai", "http://10.0.0.5:11434/v1", 30), (body.GetProperty("key").GetString(), body.GetProperty("url").GetString(),
            body.GetProperty("timeoutSeconds").GetInt32()));
        Assert.DoesNotContain("cle-de-l-essai", ok.ToString());

        // Échec du modèle : le motif, sans secret ; l'essai est tracé sous son nom (OPT-27).
        (container.Outcome, container.Error) = (LanguageModelCall.Failed, "Le modèle répond HTTP 500.");
        var failed = await Json(await admin.PostAsync($"{Path}/test", null));
        Assert.False(failed.GetProperty("ok").GetBoolean());
        Assert.Equal("Échec : Le modèle répond HTTP 500.", failed.GetProperty("message").GetString());
        await using (var db = api.Database())
            Assert.Equal([LanguageModelCall.Valid, LanguageModelCall.Failed], await db.LanguageModelCalls.AsNoTracking()
                .Where(c => c.Feature == LanguageModelCall.EssayFeature && c.Task == LanguageModelCall.EssayTask).OrderBy(c => c.Id).Select(c => c.Outcome).ToListAsync());

        // Conteneur absent : le message le dit ; l'essai reste à l'Admin.
        using var without = Factory(null);
        var down = await Json(await Admin(without).PostAsync($"{Path}/test", null));
        Assert.False(down.GetProperty("ok").GetBoolean());
        Assert.Contains("Conteneur features injoignable", down.GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await Admin(factory, "Manager").PostAsync($"{Path}/test", null)).StatusCode);
    }

    [Fact]
    public async Task OPT_27_la_page_tire_ses_compteurs_des_traces_gardees_90_jours()
    {
        await using (var db = api.Database())
        {
            LanguageModelCall Trace(string outcome, int ms, int rejected = 0, int daysAgo = 0) => new()
            {
                At = DateTime.UtcNow.AddDays(-daysAgo), Feature = FeatureCatalog.AssistedCmdb, Task = "proprietaire", Model = "qwen2.5:7b",
                Location = "Dans le SI", PromptVersion = "1", InputHash = new string('0', 64), DurationMs = ms, Outcome = outcome,
                Attempts = rejected + 1, Rejected = rejected,
            };
            db.LanguageModelCalls.AddRange(Trace(LanguageModelCall.Valid, 1000), Trace(LanguageModelCall.Valid, 3000),
                Trace(LanguageModelCall.NonConforming, 5000, rejected: 2), Trace(LanguageModelCall.Valid, 9000, daysAgo: 40),
                Trace(LanguageModelCall.Failed, 100, daysAgo: 91));
            await db.SaveChangesAsync();
        }
        using var factory = Factory(container.Url);
        var usage = await Json(await Admin(factory).GetAsync($"{Path}/usage?days=30"));
        var rows = usage.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal([LanguageModelCall.Valid, LanguageModelCall.NonConforming], rows.Select(r => r.GetProperty("outcome").GetString()));
        var valid = rows[0];
        Assert.Equal((FeatureCatalog.AssistedCmdb, 2, 2000), (valid.GetProperty("feature").GetString(), valid.GetProperty("count").GetInt32(),
            valid.GetProperty("averageMs").GetInt32()));
        Assert.Equal(2, rows[1].GetProperty("rejected").GetInt32());
        // La période se borne à 90 jours ; la trace de 40 jours y entre.
        var wide = await Json(await Admin(factory).GetAsync($"{Path}/usage?days=365"));
        Assert.Equal(90, wide.GetProperty("days").GetInt32());
        Assert.Equal(3, wide.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("outcome").GetString() == LanguageModelCall.Valid)
            .GetProperty("count").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, (await Admin(factory, "Manager").GetAsync($"{Path}/usage")).StatusCode);

        // Purge quotidienne : plus de 90 jours, effacée ; les autres restent. Le service de purge de l'hôte a pu passer
        // le premier, à son démarrage : seul l'état final compte.
        await using (var db = api.Database())
        {
            await FeatureEventsRetention.PurgeCallsAsync(db, 90);
            var left = await db.LanguageModelCalls.AsNoTracking().Select(c => c.At).ToListAsync();
            Assert.Equal(4, left.Count);
            Assert.All(left, at => Assert.True(at > DateTime.UtcNow.AddDays(-90)));
        }
    }
}
