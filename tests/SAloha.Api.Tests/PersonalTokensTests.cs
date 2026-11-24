using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SAloha.Api.Core.Auth;
using SAloha.Api.Core.Features;

namespace SAloha.Api.Tests;

/// <summary>Jetons d'accès personnels du serveur MCP, côté API (MCP-03, MCP-04, MCP-09).</summary>
[Collection("api")]
public class PersonalTokensTests(ApiFixture api)
{
    /// <summary>Garde des fonctionnalités pilotée par le test, sans le délai de 30 s de la vraie.</summary>
    private sealed class SwitchGate : IFeatureGate
    {
        public bool On = true;
        public Task<IReadOnlySet<string>> EnabledAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlySet<string>>(On ? new HashSet<string> { FeatureCatalog.McpServer } : new HashSet<string>());
        public Task<bool> IsEnabledAsync(string key, CancellationToken ct = default) => Task.FromResult(On && key == FeatureCatalog.McpServer);
        public void Invalidate() { }
    }

    /// <summary>Annuaire piloté par le test : rôle du moment, compte retiré, annuaire injoignable.</summary>
    private sealed class SwitchDirectory : IUserDirectory
    {
        public DirectoryAccount? Account = new("User", []);
        public bool Down;
        public Task<DirectoryAccount?> FindAsync(string username) =>
            Down ? throw new DirectoryUnavailableException("Annuaire injoignable (test)") : Task.FromResult(Account);
    }

    private readonly SwitchGate gate = new();
    private readonly SwitchDirectory directory = new();

    private WebApplicationFactory<Program> Factory()
    {
        _ = api.Client();   // l'hôte partagé a appliqué les migrations
        return new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Default", api.Database().Database.GetConnectionString())
            .UseSetting("Jwt:Key", ApiFixture.JwtKey).UseSetting("Jwt:Issuer", "S-Aloha")
            .UseSetting("Discovery:IntervalMinutes", "0").UseSetting("Discovery:VaultKey", "cle-du-coffre-de-test-uniquement-0123456789")
            .ConfigureTestServices(s =>
            {
                s.RemoveAll<IFeatureGate>(); s.AddSingleton<IFeatureGate>(gate);
                s.RemoveAll<IUserDirectory>(); s.AddSingleton<IUserDirectory>(directory);
            }));
    }

    private static HttpClient As(WebApplicationFactory<Program> f, string bearer)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return c;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode} : {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private async Task Age(int id)
    {
        await using var db = api.Database();
        var t = await db.PersonalTokens.SingleAsync(x => x.Id == id);
        t.CheckedAt = DateTime.UtcNow.AddMinutes(-6);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task MCP_03_un_jeton_personnel_se_cree_une_fois_ne_lit_que_et_se_revoque()
    {
        await using var f = Factory();
        var user = As(f, ApiFixture.Token("tech.mcp03", "User"));
        Assert.Equal(HttpStatusCode.BadRequest, (await user.PostAsJsonAsync("/api/me/tokens", new { name = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.PostAsJsonAsync("/api/me/tokens", new { name = "Éditeur", days = 91 })).StatusCode);
        var created = await Json(await user.PostAsJsonAsync("/api/me/tokens", new { name = "Éditeur" }));
        var secret = created.GetProperty("secret").GetString()!;
        var id = created.GetProperty("token").GetProperty("id").GetInt32();
        Assert.StartsWith(PersonalTokens.Prefix, secret);
        var expires = created.GetProperty("token").GetProperty("expiresAt").GetDateTime();
        Assert.InRange(expires, DateTime.UtcNow.AddDays(29.9), DateTime.UtcNow.AddDays(30.1));   // 30 jours par défaut

        // Affiché une seule fois ; seule l'empreinte est gardée.
        Assert.DoesNotContain(secret, await (await user.GetAsync("/api/me/tokens")).Content.ReadAsStringAsync());
        await using (var db = api.Database())
            Assert.Equal(PersonalTokens.HashOf(secret), (await db.PersonalTokens.SingleAsync(x => x.Id == id)).Hash);

        // Lecture seule : GET passe au nom du titulaire, PUT est refusé.
        var token = As(f, secret);
        Assert.Single((await Json(await token.GetAsync("/api/me/tokens"))).EnumerateArray());
        Assert.Equal(HttpStatusCode.Unauthorized, (await token.PutAsJsonAsync("/api/incidents/1", new { title = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await token.PostAsJsonAsync("/api/me/tokens", new { name = "Jeton par jeton" })).StatusCode);
        await using (var db = api.Database())
            Assert.NotNull((await db.PersonalTokens.SingleAsync(x => x.Id == id)).LastUsedAt);   // MCP-09

        // 5 jetons actifs au plus.
        for (var i = 2; i <= PersonalTokens.MaxActive; i++) await Json(await user.PostAsJsonAsync("/api/me/tokens", new { name = $"Client {i}", days = 1 }));
        Assert.Equal(HttpStatusCode.BadRequest, (await user.PostAsJsonAsync("/api/me/tokens", new { name = "Sixième" })).StatusCode);

        // Révocation : pas celle d'un autre compte (introuvable), celle du titulaire et celle de l'Admin.
        Assert.Equal(HttpStatusCode.NotFound, (await As(f, ApiFixture.Token("autre.mcp03", "User")).DeleteAsync($"/api/me/tokens/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await user.DeleteAsync($"/api/me/tokens/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await token.GetAsync("/api/me/tokens")).StatusCode);
        var admin = As(f, ApiFixture.Token("admin.mcp03", "Admin"));
        var other = (await Json(await admin.GetAsync("/api/admin/tokens"))).EnumerateArray()
            .First(t => t.GetProperty("username").GetString() == "tech.mcp03").GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/tokens/{other}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/admin/tokens")).StatusCode);

        // Fonctionnalité coupée : les routes n'existent pas, les jetons sont refusés.
        var spare = (await Json(await user.GetAsync("/api/me/tokens"))).EnumerateArray().Count();
        gate.On = false;
        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync("/api/me/tokens")).StatusCode);
        Assert.Equal(PersonalTokens.MaxActive - 2, spare);
    }

    [Fact]
    public async Task MCP_04_l_identite_d_un_jeton_est_relue_dans_l_annuaire()
    {
        await using var f = Factory();
        var user = As(f, ApiFixture.Token("tech.mcp04", "User"));
        var secret = (await Json(await user.PostAsJsonAsync("/api/me/tokens", new { name = "Assistant" }))).GetProperty("secret").GetString()!;
        var id = (await Json(await user.GetAsync("/api/me/tokens"))).EnumerateArray().Single().GetProperty("id").GetInt32();
        var token = As(f, secret);
        Assert.Equal(HttpStatusCode.Forbidden, (await token.GetAsync("/api/admin/tokens")).StatusCode);   // User dans l'annuaire

        // Le rôle est celui du moment, relu au-delà de 5 minutes, jamais celui de la création.
        directory.Account = new DirectoryAccount("Admin", []);
        Assert.Equal(HttpStatusCode.Forbidden, (await token.GetAsync("/api/admin/tokens")).StatusCode);   // pas encore relu
        await Age(id);
        Assert.Equal(HttpStatusCode.OK, (await token.GetAsync("/api/admin/tokens")).StatusCode);

        // Annuaire injoignable : 503 ; compte retiré des groupes de rôle : 401.
        await Age(id);
        directory.Down = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await token.GetAsync("/api/me/tokens")).StatusCode);
        directory.Down = false;
        directory.Account = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await token.GetAsync("/api/me/tokens")).StatusCode);

        // Serveur MCP coupé : le jeton est refusé.
        directory.Account = new DirectoryAccount("User", []);
        gate.On = false;
        Assert.Equal(HttpStatusCode.Unauthorized, (await token.GetAsync("/api/me/tokens")).StatusCode);
    }
}
