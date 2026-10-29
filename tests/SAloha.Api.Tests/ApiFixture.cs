using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SAloha.Api.Core.Search;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;

namespace SAloha.Api.Tests;

/// <summary>
/// L'API réelle sur un PostgreSQL jetable : l'index unique et la concurrence
/// sont précisément ce que ces tests vérifient, une base en mémoire ne les
/// reproduirait pas. L'AD n'est pas sollicité : les jetons sont signés ici,
/// avec la clé de configuration, comme le ferait TokenService.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    internal const string JwtKey = "cle-de-test-uniquement-0123456789abcdef0123456789abcdef0123";
    // S3 : seule origine externe admise par le CORS des tests.
    internal const string AllowedOrigin = "https://frontend.example";

    // Image déjà présente localement : aucun téléchargement au lancement.
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:18-alpine").Build();

    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Default", _db.GetConnectionString())
            .UseSetting("Jwt:Key", JwtKey)
            .UseSetting("Jwt:Issuer", "S-Aloha")
            .UseSetting("Cors:AllowedOrigins:0", AllowedOrigin)
            // Découverte : pas de lancement planifié (seuls les lancements des tests), coffre configuré.
            .UseSetting("Discovery:IntervalMinutes", "0")
            .UseSetting("Discovery:VaultKey", "cle-du-coffre-de-test-uniquement-0123456789")
            // Embeddings déterministes à la place du service Ollama (ADR-0013).
            .ConfigureTestServices(s =>
            {
                s.RemoveAll<IEmbeddingClient>();
                s.AddSingleton<IEmbeddingClient, FakeEmbeddingClient>();
                // Rôle d'un autre compte (INC-13) : annuaire de test, sans LDAP.
                s.RemoveAll<SAloha.Api.Core.Auth.IUserDirectory>();
                s.AddSingleton<SAloha.Api.Core.Auth.IUserDirectory, FakeUserDirectory>();
                // Connexion (S5) : mot de passe de test, sinon le vrai service LDAP (injoignable).
                s.AddSingleton<FakeUserDirectory>();
                s.RemoveAll<SAloha.Api.Core.Auth.IAuthenticator>();
                s.AddSingleton<SAloha.Api.Core.Auth.IAuthenticator, FakeAuthenticator>();
                s.RemoveAll<SAloha.Api.Core.Auth.IDirectoryRoster>();
                s.AddSingleton<SAloha.Api.Core.Auth.IDirectoryRoster, FakeRoster>();
                // Collecteurs de découverte factices : ni réseau ni annuaire pendant les tests.
                s.RemoveAll<SAloha.Api.Modules.ServiceConfigurationManagement.Discovery.IDiscoveryCollector>();
                foreach (var type in SAloha.Api.Modules.ServiceConfigurationManagement.DiscoveryJob.ScanTypes)
                    s.AddScoped<SAloha.Api.Modules.ServiceConfigurationManagement.Discovery.IDiscoveryCollector>(_ => new FakeCollector(type));
            }));
    }

    /// <summary>Accès direct à la base, pour simuler ce que l'API ne fait pas (nouvelle version embarquée…).</summary>
    public SAloha.Api.Core.Data.AppDbContext Database() =>
        new(new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<SAloha.Api.Core.Data.AppDbContext>()
            .UseNpgsql(_db.GetConnectionString()).Options);

    public HttpClient Client(string role = "Admin", string username = "hery.rakoto", params string[] groups)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(username, role, groups));
        return client;
    }

    /// <summary>Client sans jeton, qui garde ses cookies (session du navigateur, S5).</summary>
    public HttpClient AnonymousClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    internal static string Token(string username, string role, params string[] groups)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey));
        var token = new JwtSecurityToken(
            issuer: "S-Aloha",
            audience: "S-Aloha",   // S8
            claims: [new Claim(ClaimTypes.Name, username), new Claim("displayName", username), new Claim(ClaimTypes.Role, role),
                     .. groups.Select(g => new Claim(SAloha.Api.Core.Auth.Principals.GroupClaim, g))],
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _db.DisposeAsync();
    }
}

[CollectionDefinition("api")]
public class ApiCollection : ICollectionFixture<ApiFixture>;
