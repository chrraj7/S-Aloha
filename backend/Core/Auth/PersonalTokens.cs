using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SAloha.Api.Core.Data;
using SAloha.Api.Core.Features;

namespace SAloha.Api.Core.Auth;

/// <summary>
/// MCP-03 : jeton d'accès personnel, créé par l'utilisateur pour un client MCP. Seule son
/// empreinte SHA-256 est gardée ; le jeton lui-même n'est affiché qu'une fois, à la création.
/// </summary>
public class PersonalToken
{
    public int Id { get; set; }
    [MaxLength(200)] public string Username { get; set; } = "";
    [MaxLength(250)] public string DisplayName { get; set; } = "";
    [MaxLength(100)] public string Name { get; set; } = "";
    /// <summary>Empreinte SHA-256 du jeton, en hexadécimal minuscule (index unique).</summary>
    [MaxLength(64)] public string Hash { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    [MaxLength(200)] public string? RevokedBy { get; set; }
    /// <summary>MCP-09 : dernière utilisation, tenue à la minute près.</summary>
    public DateTime? LastUsedAt { get; set; }
    /// <summary>MCP-04 : rôle et groupes relus dans l'annuaire, et quand.</summary>
    [MaxLength(20)] public string? Role { get; set; }
    public string? GroupsJson { get; set; }
    public DateTime? CheckedAt { get; set; }

    public bool Active(DateTime now) => RevokedAt is null && ExpiresAt > now;
}

public record PersonalTokenDto(string? Name, int? Days);

public record PersonalTokenView(int Id, string Name, string Username, string DisplayName, DateTime CreatedAt, DateTime ExpiresAt, DateTime? LastUsedAt)
{
    public static PersonalTokenView From(PersonalToken t) => new(t.Id, t.Name, t.Username, t.DisplayName, t.CreatedAt, t.ExpiresAt, t.LastUsedAt);
}

/// <summary>Le jeton en clair, rendu une seule fois à sa création.</summary>
public record CreatedPersonalToken(PersonalTokenView Token, string Secret);

public static class PersonalTokens
{
    /// <summary>Préfixe des jetons personnels : il les distingue d'un jeton de session à la lecture de l'en-tête.</summary>
    public const string Prefix = "sat_";
    public const int MaxActive = 5, MinDays = 1, MaxDays = 90, DefaultDays = 30;
    /// <summary>MCP-04 : l'identité se relit dans l'annuaire au moins toutes les 5 minutes.</summary>
    public static readonly TimeSpan Recheck = TimeSpan.FromMinutes(5);
    /// <summary>MCP-09 : la dernière utilisation s'écrit une fois par minute au plus.</summary>
    public static readonly TimeSpan UseResolution = TimeSpan.FromMinutes(1);
    /// <summary>Claim de l'identifiant du jeton, pour la traçabilité (MCP-09).</summary>
    public const string TokenClaim = "personal_token";

    public static string NewSecret() => Prefix + Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string HashOf(string secret) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    /// <summary>L'en-tête présente-t-il un jeton personnel ? Sert à choisir le schéma d'authentification.</summary>
    public static bool Presented(HttpRequest request) =>
        request.Headers.Authorization.ToString().StartsWith("Bearer " + Prefix, StringComparison.Ordinal);

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>
/// MCP-03, MCP-04 : authentifie un jeton personnel. Il n'ouvre que les routes <c>GET</c> et seulement
/// quand le serveur MCP est activé ; l'identité est relue dans l'annuaire à la première requête puis
/// toutes les 5 minutes (compte retiré des groupes de rôle : 401 ; annuaire injoignable : 503). Le
/// rôle et les groupes sont ceux du moment, jamais ceux de la création. Le jeton n'est jamais journalisé.
/// </summary>
public class PersonalTokenHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    AppDbContext db, IUserDirectory directory, IFeatureGate features) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "PersonalToken";
    private const string DirectoryDown = "personal-token-directory-down";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!PersonalTokens.Presented(Request)) return AuthenticateResult.NoResult();
        if (!await features.IsEnabledAsync(FeatureCatalog.McpServer, Context.RequestAborted))
            return AuthenticateResult.Fail("Serveur MCP coupé : jetons personnels refusés.");
        if (!HttpMethods.IsGet(Request.Method) && !HttpMethods.IsHead(Request.Method))
            return AuthenticateResult.Fail("Un jeton personnel n'ouvre que la lecture.");

        var hash = PersonalTokens.HashOf(Request.Headers.Authorization.ToString()["Bearer ".Length..]);
        var now = DateTime.UtcNow;
        var token = await db.PersonalTokens.FirstOrDefaultAsync(t => t.Hash == hash, Context.RequestAborted);
        if (token is null || !token.Active(now)) return AuthenticateResult.Fail("Jeton personnel inconnu, expiré ou révoqué.");

        if (token.CheckedAt is null || now - token.CheckedAt > PersonalTokens.Recheck)
        {
            DirectoryAccount? account;
            try { account = await directory.FindAsync(token.Username); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogWarning("Annuaire injoignable pour le jeton personnel {TokenId} : {Error}", token.Id, ex.Message.Split('\n')[0]);
                Context.Items[DirectoryDown] = true;
                return AuthenticateResult.Fail("Annuaire injoignable.");
            }
            if (account is null) return AuthenticateResult.Fail("Compte absent des groupes de rôle ou désactivé.");
            (token.Role, token.GroupsJson, token.CheckedAt) = (account.Role, JsonSerializer.Serialize(account.Groups), now);
        }
        if (token.LastUsedAt is null || now - token.LastUsedAt > PersonalTokens.UseResolution) token.LastUsedAt = now;
        await db.SaveChangesAsync(Context.RequestAborted);

        var groups = JsonSerializer.Deserialize<List<string>>(token.GroupsJson ?? "[]") ?? [];
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, token.Username),
            new Claim("displayName", token.DisplayName),
            new Claim(ClaimTypes.Role, token.Role!),
            new Claim(PersonalTokens.TokenClaim, token.Id.ToString()),
        }.Concat(groups.Select(g => new Claim(Principals.GroupClaim, g))), SchemeName);
        // MCP-09, côté API : compte et identifiant du jeton, jamais le jeton ni le contenu.
        Logger.LogInformation("Jeton personnel {TokenId} de {Username} : {Method} {Path}", token.Id, token.Username, Request.Method, Request.Path.Value);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = Context.Items.ContainsKey(DirectoryDown) ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }
}
