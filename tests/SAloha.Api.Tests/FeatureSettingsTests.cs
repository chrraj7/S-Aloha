using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Features;

namespace SAloha.Api.Tests;

/// <summary>Réglages du tri assisté (TRI-13) : bornes, journal d'audit (OPT-07), lecture interne par le conteneur.</summary>
[Collection("api")]
public class FeatureSettingsTests(ApiFixture api) : IAsyncLifetime
{
    private const string Path = $"/api/admin/features/{FeatureCatalog.AssistedTriage}/settings";

    public async Task InitializeAsync()
    {
        // Le démarrage de l'API applique les migrations : lancé seul, ce test arrive avant elles.
        await api.Client().GetAsync("/health/live");
        await ResetAsync();
    }

    public Task DisposeAsync() => ResetAsync();

    private async Task ResetAsync(bool enabled = false)
    {
        await using var db = api.Database();
        await db.OptionalFeatures.ExecuteDeleteAsync();
        if (enabled) db.OptionalFeatures.Add(new OptionalFeature { Key = FeatureCatalog.AssistedTriage, Enabled = true, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode} : {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static object Settings(int neighbours = 10, int minVoters = 3, int minSharePercent = 50, int window = 60, int outage = 3,
        double similarity = 0.60, bool group = true) => new
    {
        neighbours, minVoters, minSharePercent, correlationWindowMinutes = window, outageThreshold = outage, correlationMinSimilarity = similarity,
        suggestCategory = true, suggestService = true, suggestGroup = group, suggestImpact = true, suggestOutage = true,
    };

    [Fact]
    public async Task TRI_13_les_reglages_par_defaut_se_modifient_et_chaque_changement_est_trace()
    {
        var admin = api.Client();
        var defaults = await Json(await admin.GetAsync(Path));
        Assert.Equal((10, 3, 50, 60, 3), (defaults.GetProperty("neighbours").GetInt32(), defaults.GetProperty("minVoters").GetInt32(),
            defaults.GetProperty("minSharePercent").GetInt32(), defaults.GetProperty("correlationWindowMinutes").GetInt32(),
            defaults.GetProperty("outageThreshold").GetInt32()));
        Assert.Equal(0.60, defaults.GetProperty("correlationMinSimilarity").GetDouble(), 3);
        Assert.True(defaults.GetProperty("suggestGroup").GetBoolean());

        long last;
        await using (var db = api.Database()) last = await db.AuditEntries.MaxAsync(a => (long?)a.Id) ?? 0;
        await Json(await admin.PutAsJsonAsync(Path, Settings(neighbours: 15, group: false)));
        await using (var db = api.Database())
        {
            var lines = await db.AuditEntries.AsNoTracking().Where(a => a.Id > last).OrderBy(a => a.Id).ToListAsync();
            Assert.Equal([("Tri assisté des tickets — Voisins", "10", "15"), ("Tri assisté des tickets — Suggérer le groupe", "oui", "non")],
                lines.Select(a => (a.Field, a.OldValue, a.NewValue)));
            Assert.All(lines, a => Assert.Equal(("parametre", "Fonctionnalités"), (a.EntityType, a.Reference)));
            last = lines[^1].Id;
        }
        var saved = await Json(await admin.GetAsync(Path));
        Assert.Equal((15, false), (saved.GetProperty("neighbours").GetInt32(), saved.GetProperty("suggestGroup").GetBoolean()));

        // Une demande qui ne change rien n'écrit rien (OPT-07).
        await Json(await admin.PutAsJsonAsync(Path, Settings(neighbours: 15, group: false)));
        await using (var db = api.Database()) Assert.False(await db.AuditEntries.AnyAsync(a => a.Id > last));

        // Admin seul ; une fonctionnalité sans réglages, ou inconnue : 404.
        Assert.Equal(HttpStatusCode.Forbidden, (await api.Client("Manager", "lova.rabe").GetAsync(Path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/admin/features/{FeatureCatalog.AssistedCmdb}/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync("/api/admin/features/inconnue/settings", Settings())).StatusCode);
    }

    [Theory]
    [InlineData(4, 3, 50, 60, 3, 0.60, "Voisins")]
    [InlineData(21, 3, 50, 60, 3, 0.60, "Voisins")]
    [InlineData(10, 1, 50, 60, 3, 0.60, "Votants minimaux")]
    [InlineData(10, 3, 49, 60, 3, 0.60, "Part minimale")]
    [InlineData(10, 3, 50, 241, 3, 0.60, "Fenêtre de corrélation")]
    [InlineData(10, 3, 50, 60, 1, 0.60, "Seuil de panne probable")]
    [InlineData(10, 3, 50, 60, 3, 0.96, "Seuil de similarité")]
    [InlineData(5, 6, 50, 60, 3, 0.60, "pas plus que de voisins")]
    public async Task TRI_13_un_reglage_hors_de_ses_bornes_est_refuse(int neighbours, int minVoters, int share, int window, int outage, double similarity,
        string expected)
    {
        var res = await api.Client().PutAsJsonAsync(Path, Settings(neighbours, minVoters, share, window, outage, similarity));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains(expected, await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TRI_13_le_conteneur_lit_les_reglages_du_tri_active_sur_le_port_interne()
    {
        await Json(await api.Client().PutAsJsonAsync(Path, Settings(window: 120)));
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Default", api.Database().Database.GetConnectionString())
            .UseSetting("Jwt:Key", ApiFixture.JwtKey)
            .UseSetting("Jwt:Issuer", "S-Aloha")
            .UseSetting("Discovery:IntervalMinutes", "0")
            .UseSetting("Discovery:VaultKey", "cle-du-coffre-de-test-uniquement-0123456789")
            .UseSetting("Features:Token", FakeFeaturesContainer.Token)
            .UseSetting("Features:InternalPort", "8081"));
        async Task<(int, string)> Read(string? bearer = FakeFeaturesContainer.Token)
        {
            var context = await factory.Server.SendAsync(c =>
            {
                c.Request.Method = "GET";
                c.Request.Path = $"/internal/features/{FeatureCatalog.AssistedTriage}/settings";
                c.Connection.LocalPort = 8081;
                if (bearer is not null) c.Request.Headers.Authorization = $"Bearer {bearer}";
            });
            return (context.Response.StatusCode, await new StreamReader(context.Response.Body).ReadToEndAsync());
        }
        Assert.Equal(401, (await Read(null)).Item1);
        Assert.Equal(403, (await Read()).Item1);   // tri coupé (OPT-04)

        // Le tri activé (la porte relit la base après 30 s ; une fabrique neuve, tout de suite).
        await using (var db = api.Database())
        {
            var row = await db.OptionalFeatures.SingleAsync(f => f.Key == FeatureCatalog.AssistedTriage);
            row.Enabled = true;
            await db.SaveChangesAsync();
        }
        (factory.Services.GetService(typeof(IFeatureGate)) as IFeatureGate)!.Invalidate();
        var (status, body) = await Read();
        Assert.Equal(200, status);
        Assert.Equal(120, JsonDocument.Parse(body).RootElement.GetProperty("correlationWindowMinutes").GetInt32());
    }
}
