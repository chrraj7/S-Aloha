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

    private static object Call(string feature = FeatureCatalog.AssistedCmdb, string outcome = LanguageModelCall.Valid, int duration = 1200, int rejected = 0,
        string? hash = null) => new
    {
        feature, task = "proprietaire", model = "qwen2.5:7b", location = "Dans le SI", promptVersion = "1", inputHash = hash ?? new string('0', 64),
        durationMs = duration, outcome, attempts = rejected + 1, rejected, error = (string?)null,
    };

    [Fact]
    public async Task OPT_27_le_conteneur_trace_chaque_appel_et_la_page_en_tire_ses_compteurs_gardes_90_jours()
    {
        static async Task<int> Send(WebApplicationFactory<Program> host, object call)
        {
            var context = await host.Server.SendAsync(c =>
            {
                c.Request.Method = "POST";
                c.Request.Path = "/internal/language-model/calls";
                c.Connection.LocalPort = 8081;
                c.Request.Headers.Authorization = $"Bearer {FakeFeaturesContainer.Token}";
                c.Request.ContentType = "application/json";
                c.Request.Body = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(call));
            });
            return context.Response.StatusCode;
        }

        // CMDB assistée coupée : aucune trace reçue (OPT-04).
        using (var off = Factory(container.Url)) Assert.Equal(403, await Send(off, Call()));
        await using (var db = api.Database())
        {
            db.OptionalFeatures.Add(new OptionalFeature { Key = FeatureCatalog.AssistedCmdb, Enabled = true, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        // La garde garde l'état des fonctionnalités en mémoire : un hôte neuf le relit.
        using var factory = Factory(container.Url);
        Task<int> Trace(object call) => Send(factory, call);
        Assert.Equal(201, await Trace(Call(duration: 1000)));
        Assert.Equal(201, await Trace(Call(duration: 3000)));
        Assert.Equal(201, await Trace(Call(outcome: LanguageModelCall.NonConforming, rejected: 2)));
        // Hors bornes : issue, fonctionnalité ou empreinte inconnues.
        Assert.Equal(400, await Trace(Call(outcome: "réussi")));
        Assert.Equal(400, await Trace(Call(feature: FeatureCatalog.McpServer)));
        Assert.Equal(400, await Trace(Call(hash: "pas-une-empreinte")));

        // Ni l'invite ni la réponse : la table n'a pas de colonne pour elles.
        Assert.DoesNotContain(typeof(LanguageModelCall).GetProperties(), p => p.Name is "Prompt" or "Input" or "Response" or "Output");

        var usage = await Json(await Admin(factory).GetAsync($"{Path}/usage?days=30"));
        var rows = usage.GetProperty("rows").EnumerateArray().ToList();
        var valid = rows.Single(r => r.GetProperty("outcome").GetString() == LanguageModelCall.Valid);
        Assert.Equal((FeatureCatalog.AssistedCmdb, 2, 2000), (valid.GetProperty("feature").GetString(), valid.GetProperty("count").GetInt32(),
            valid.GetProperty("averageMs").GetInt32()));
        Assert.Equal(2, rows.Single(r => r.GetProperty("outcome").GetString() == LanguageModelCall.NonConforming).GetProperty("rejected").GetInt32());

        // 90 jours : une trace plus ancienne est purgée, les autres restent.
        await using (var db = api.Database())
        {
            var old = await db.LanguageModelCalls.OrderBy(c => c.Id).FirstAsync();
            old.At = DateTime.UtcNow.AddDays(-91);
            await db.SaveChangesAsync();
            Assert.Equal(1, await FeatureEventsRetention.PurgeCallsAsync(db, 90));
            Assert.Equal(2, await db.LanguageModelCalls.CountAsync());
        }
    }
}
