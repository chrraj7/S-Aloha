using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Features;

namespace SAloha.Api.Tests;

/// <summary>Réglages du modèle de langage (OPT-20), emplacement (OPT-22), clé au coffre et remise au conteneur (OPT-23).</summary>
[Collection("api")]
public class LanguageModelSettingsTests(ApiFixture api) : IAsyncLifetime
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
        await db.LanguageModelSettings.ExecuteDeleteAsync();
        await db.OptionalFeatures.ExecuteDeleteAsync();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode} : {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static async Task<string> Refused(HttpResponseMessage res)
    {
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("message").GetString()!;
    }

    private static object Dto(string url, string location = "Dans le SI", string? key = null, bool enabled = true, bool confirm = false,
        int timeout = 120) => new
    {
        enabled, location, url, model = "qwen2.5:7b", key, timeoutSeconds = timeout, maxResponseKb = 64, confirmHosted = confirm,
    };

    [Fact]
    public async Task OPT_20_OPT_23_les_reglages_s_enregistrent_la_cle_au_coffre_jamais_renvoyee_et_traces()
    {
        var admin = api.Client();
        var empty = await Json(await admin.GetAsync(Path));
        Assert.Equal((false, "Dans le SI", 120, false), (empty.GetProperty("enabled").GetBoolean(), empty.GetProperty("location").GetString(),
            empty.GetProperty("timeoutSeconds").GetInt32(), empty.GetProperty("hasKey").GetBoolean()));

        long last;
        await using (var db = api.Database()) last = await db.AuditEntries.MaxAsync(a => (long?)a.Id) ?? 0;
        var saved = await Json(await admin.PutAsJsonAsync(Path, Dto("http://10.0.0.5:11434/v1", key: "cle-secrete-du-modele")));
        Assert.True(saved.GetProperty("hasKey").GetBoolean());
        Assert.DoesNotContain("cle-secrete-du-modele", saved.ToString());
        await using (var db = api.Database())
        {
            var row = await db.LanguageModelSettings.AsNoTracking().SingleAsync();
            Assert.NotEqual("cle-secrete-du-modele", row.EncryptedKey);   // chiffrée par le coffre
            var lines = await db.AuditEntries.AsNoTracking().Where(a => a.Id > last).ToListAsync();
            Assert.Contains(lines, a => a.Field == "key" && a.NewValue == "modifiée" && a.OldValue == null);
            Assert.Contains(lines, a => a.Field == "url" && a.NewValue == "http://10.0.0.5:11434/v1");
            Assert.DoesNotContain(lines, a => (a.NewValue ?? "").Contains("cle-secrete"));
        }

        // Bornes et champs requis.
        Assert.Contains("10 à 300", await Refused(await admin.PutAsJsonAsync(Path, Dto("http://10.0.0.5:11434/v1", timeout: 5))));
        Assert.Contains("terminée par /v1", await Refused(await admin.PutAsJsonAsync(Path, Dto("http://10.0.0.5:11434/api"))));
        Assert.Contains("requis", await Refused(await admin.PutAsJsonAsync(Path, Dto(""))));
        Assert.Equal(HttpStatusCode.Forbidden, (await api.Client("Manager", "lova.rabe").GetAsync(Path)).StatusCode);
    }

    [Fact]
    public async Task OPT_22_une_adresse_hors_du_SI_exige_l_emplacement_heberge_confirme()
    {
        var admin = api.Client();
        // Adresse publique déclarée « dans le SI » : refusée.
        Assert.Contains("se résout hors du SI", await Refused(await admin.PutAsJsonAsync(Path, Dto("https://8.8.8.8/v1"))));
        // Hébergé sans confirmation : refusé ; confirmé : accepté, et la page le dit en permanence.
        Assert.Contains("quittent le SI", await Refused(await admin.PutAsJsonAsync(Path, Dto("https://8.8.8.8/v1", "Hébergé hors du SI"))));
        var hosted = await Json(await admin.PutAsJsonAsync(Path, Dto("https://8.8.8.8/v1", "Hébergé hors du SI", confirm: true)));
        Assert.True(hosted.GetProperty("dataLeavesSi").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, hosted.GetProperty("hostedConfirmedAt").ValueKind);
        // Adresse de bouclage, privée ou locale au lien : dans le SI.
        foreach (var url in new[] { "http://127.0.0.1:11434/v1", "http://localhost:11434/v1", "http://192.168.1.20/v1", "http://[fd00::1]/v1" })
            Assert.Equal("Dans le SI", (await Json(await admin.PutAsJsonAsync(Path, Dto(url)))).GetProperty("location").GetString());
        // Chaque changement d'emplacement est tracé (OPT-07).
        await using var db = api.Database();
        Assert.Contains(await db.AuditEntries.AsNoTracking().Where(a => a.Reference == "Modèle de langage" && a.Field == "location").ToListAsync(),
            a => a.OldValue == "Hébergé hors du SI" && a.NewValue == "Dans le SI");
    }

    [Fact]
    public async Task OPT_23_le_conteneur_lit_les_reglages_cle_comprise_et_la_CMDB_assistee_en_depend()
    {
        await Json(await api.Client().PutAsJsonAsync(Path, Dto("http://10.0.0.5:11434/v1", key: "cle-du-conteneur")));
        await using (var db = api.Database())
        {
            db.OptionalFeatures.Add(new OptionalFeature { Key = FeatureCatalog.AssistedCmdb, Enabled = true, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
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
                c.Request.Path = "/internal/language-model";
                c.Connection.LocalPort = 8081;
                if (bearer is not null) c.Request.Headers.Authorization = $"Bearer {bearer}";
            });
            return (context.Response.StatusCode, await new StreamReader(context.Response.Body).ReadToEndAsync());
        }
        Assert.Equal(401, (await Read(null)).Item1);
        var (status, body) = await Read();
        Assert.Equal(200, status);
        var read = JsonDocument.Parse(body).RootElement;
        Assert.Equal(("cle-du-conteneur", "qwen2.5:7b", 120), (read.GetProperty("key").GetString(), read.GetProperty("model").GetString(),
            read.GetProperty("timeoutSeconds").GetInt32()));

        // OPT-03 : la dépendance de la CMDB assistée suit les réglages.
        var cmdb = (await Json(await api.Client().GetAsync("/api/admin/features"))).EnumerateArray()
            .Single(f => f.GetProperty("key").GetString() == FeatureCatalog.AssistedCmdb);
        var dependency = cmdb.GetProperty("dependencies").EnumerateArray().Single(d => d.GetProperty("key").GetString() == "language-model");
        Assert.True(dependency.GetProperty("ok").GetBoolean());
        Assert.Contains("qwen2.5:7b", dependency.GetProperty("message").GetString());

        // CMDB assistée coupée : la lecture interne est refusée (OPT-04).
        await using (var db = api.Database()) await db.OptionalFeatures.ExecuteDeleteAsync();
        using var off = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Default", api.Database().Database.GetConnectionString())
            .UseSetting("Jwt:Key", ApiFixture.JwtKey).UseSetting("Jwt:Issuer", "S-Aloha")
            .UseSetting("Discovery:IntervalMinutes", "0").UseSetting("Discovery:VaultKey", "cle-du-coffre-de-test-uniquement-0123456789")
            .UseSetting("Features:Token", FakeFeaturesContainer.Token).UseSetting("Features:InternalPort", "8081"));
        var refused = await off.Server.SendAsync(c =>
        {
            c.Request.Method = "GET"; c.Request.Path = "/internal/language-model"; c.Connection.LocalPort = 8081;
            c.Request.Headers.Authorization = $"Bearer {FakeFeaturesContainer.Token}";
        });
        Assert.Equal(403, refused.Response.StatusCode);
    }
}
