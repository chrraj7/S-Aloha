using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SAloha.Api.Core.Features;

namespace SAloha.Api.Tests;

/// <summary>Conteneur factice qui répond à <c>POST /triage/suggest</c> comme le test le choisit.</summary>
public sealed class FakeSuggestContainer : IAsyncLifetime
{
    public enum Mode { Ok, Slow, Error, Garbage }
    public volatile Mode Behaviour = Mode.Ok;
    public int Calls;
    public volatile string? LastAuthorization, LastBody;
    private WebApplication _app = null!;
    public string Url { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0));
        _app = builder.Build();
        _app.MapPost("/triage/suggest", async (HttpContext c) =>
        {
            Interlocked.Increment(ref Calls);
            LastAuthorization = c.Request.Headers.Authorization.ToString();
            LastBody = await new StreamReader(c.Request.Body).ReadToEndAsync();
            switch (Behaviour)
            {
                case Mode.Slow:
                    try { await Task.Delay(TimeSpan.FromSeconds(5), c.RequestAborted); } catch (OperationCanceledException) { }
                    return Results.Json(new { category = "Trop tard", votes = 1 });
                case Mode.Error:
                    return Results.StatusCode(500);
                case Mode.Garbage:
                    return Results.Text("<html>pas du JSON</html>", "application/json");
                default:
                    return Results.Json(new { category = "Messagerie", votes = 7 });
            }
        });
        await _app.StartAsync();
        Url = _app.Urls.Single();
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();
}

/// <summary>Appels de l'API vers le conteneur features (OPT-16) : 2 s au plus, réponse vide et journalisée en cas d'échec.</summary>
[Collection("api")]
public class FeaturesClientTests(ApiFixture api, FakeSuggestContainer container) : IClassFixture<FakeSuggestContainer>, IAsyncLifetime
{
    private sealed record Suggestion(string Category, int Votes);

    /// <summary>Journal capturé : ce que l'API écrit pendant l'appel.</summary>
    private sealed class CapturedLogs : ILoggerProvider
    {
        public readonly ConcurrentQueue<string> Lines = new();
        public ILogger CreateLogger(string category) => new Logger(this, category);
        public void Dispose() { }

        private sealed class Logger(CapturedLogs owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> formatter) =>
                owner.Lines.Enqueue($"{level} {category} {formatter(state, ex)} {ex}");
        }
    }

    private readonly CapturedLogs logs = new();

    public async Task InitializeAsync()
    {
        // Le démarrage de l'API applique les migrations : lancé seul, ce test arrive avant elles.
        await api.Client().GetAsync("/health/live");
        container.Behaviour = FakeSuggestContainer.Mode.Ok;
        await using var db = api.Database();
        await db.OptionalFeatures.ExecuteDeleteAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = api.Database();
        await db.OptionalFeatures.ExecuteDeleteAsync();
    }

    private async Task EnableTriageAsync()
    {
        await using var db = api.Database();
        db.OptionalFeatures.Add(new OptionalFeature { Key = FeatureCatalog.AssistedTriage, Enabled = true, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    private WebApplicationFactory<Program> Factory(string? url) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Default", api.Database().Database.GetConnectionString())
            .UseSetting("Jwt:Key", ApiFixture.JwtKey)
            .UseSetting("Jwt:Issuer", "S-Aloha")
            .UseSetting("Discovery:IntervalMinutes", "0")
            .UseSetting("Discovery:VaultKey", "cle-du-coffre-de-test-uniquement-0123456789")
            .UseSetting("Features:Url", url ?? "")
            .UseSetting("Features:Token", FakeFeaturesContainer.Token)
            .ConfigureTestServices(s => s.AddSingleton<ILoggerProvider>(logs)));

    private static async Task<Suggestion?> Suggest(WebApplicationFactory<Program> factory, string feature = FeatureCatalog.AssistedTriage)
    {
        using var scope = factory.Services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<FeaturesClient>();
        return await client.PostAsync<Suggestion>(feature, "/triage/suggest", new { title = "Outlook muet depuis ce matin" });
    }

    /// <summary>Lignes d'avertissement du client, qui ne portent jamais le jeton ni le texte saisi.</summary>
    private List<string> Warnings()
    {
        var lines = logs.Lines.Where(l => l.StartsWith("Warning") && l.Contains(nameof(FeaturesClient))).ToList();
        Assert.All(lines, l => Assert.DoesNotContain(FakeFeaturesContainer.Token, l));
        Assert.All(lines, l => Assert.DoesNotContain("Outlook", l));
        return lines;
    }

    [Fact]
    public async Task OPT_16_l_api_appelle_le_conteneur_avec_son_jeton_et_rend_sa_reponse()
    {
        await EnableTriageAsync();
        using var factory = Factory(container.Url);
        var suggestion = await Suggest(factory);
        Assert.Equal(new Suggestion("Messagerie", 7), suggestion);
        Assert.Equal($"Bearer {FakeFeaturesContainer.Token}", container.LastAuthorization);
        Assert.Contains("\"title\":\"Outlook muet depuis ce matin\"", container.LastBody);
        Assert.Empty(Warnings());
    }

    [Fact]
    public async Task OPT_16_un_delai_depasse_rend_une_reponse_vide_en_2_secondes()
    {
        await EnableTriageAsync();
        container.Behaviour = FakeSuggestContainer.Mode.Slow;
        using var factory = Factory(container.Url);
        _ = factory.Services;   // démarrage de l'API hors du temps mesuré
        var watch = Stopwatch.StartNew();
        Assert.Null(await Suggest(factory));
        watch.Stop();
        Assert.InRange(watch.Elapsed, TimeSpan.FromSeconds(1.9), TimeSpan.FromSeconds(3.5));
        Assert.Contains("délai de 2 s dépassé", Assert.Single(Warnings()));
    }

    [Fact]
    public async Task OPT_16_un_echec_ou_un_conteneur_absent_rendent_une_reponse_vide_journalisee()
    {
        await EnableTriageAsync();
        using (var factory = Factory(container.Url))
        {
            container.Behaviour = FakeSuggestContainer.Mode.Error;
            Assert.Null(await Suggest(factory));
            container.Behaviour = FakeSuggestContainer.Mode.Garbage;
            Assert.Null(await Suggest(factory));
        }
        // Conteneur arrêté : rien n'écoute à son adresse.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        using (var stopped = Factory($"http://127.0.0.1:{port}"))
            Assert.Null(await Suggest(stopped));
        // Adresse non configurée.
        using (var unset = Factory(null))
            Assert.Null(await Suggest(unset));

        var warnings = Warnings();
        Assert.Equal(4, warnings.Count);
        Assert.Contains(warnings, w => w.Contains("réponse 500"));
        Assert.Contains(warnings, w => w.Contains("réponse illisible"));
        Assert.Contains(warnings, w => w.Contains("injoignable"));
        Assert.Contains(warnings, w => w.Contains("Features:Url"));
    }

    [Fact]
    public async Task OPT_16_une_fonctionnalite_coupee_n_appelle_pas_le_conteneur()
    {
        using var factory = Factory(container.Url);
        var before = container.Calls;
        Assert.Null(await Suggest(factory));
        Assert.Equal(before, container.Calls);
        Assert.Empty(Warnings());
        // Le serveur MCP n'est pas dans le conteneur features : ce client ne lui parle pas.
        await Assert.ThrowsAsync<ArgumentException>(() => Suggest(factory, FeatureCatalog.McpServer));
    }
}
