using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Data;
using SAloha.Api.Core.Features;

namespace SAloha.Api.Core.Auth;

/// <summary>
/// MCP-03 : jetons d'accès personnels, créés et révoqués par leur titulaire (Préférences › Jetons
/// d'accès), listés et révoqués par l'Admin. Serveur MCP coupé : ces routes n'existent pas (OPT-04).
/// </summary>
[ApiController]
[RequiresFeature(FeatureCatalog.McpServer)]
public class PersonalTokensController(AppDbContext db, ILogger<PersonalTokensController> logger) : ControllerBase
{
    private string Me => User.Identity?.Name ?? "";
    private string MyDisplay => User.FindFirst("displayName")?.Value ?? Me;

    private IQueryable<PersonalToken> Active() { var now = DateTime.UtcNow; return db.PersonalTokens.Where(t => t.RevokedAt == null && t.ExpiresAt > now); }

    [HttpGet("api/me/tokens")]
    [Authorize(Policy = "User")]
    public async Task<List<PersonalTokenView>> Mine() =>
        (await Active().Where(t => t.Username == Me).OrderByDescending(t => t.CreatedAt).ToListAsync()).Select(PersonalTokenView.From).ToList();

    /// <summary>
    /// MCP-01, MCP-09 : le jeton personnel présenté, pour le conteneur <c>mcp</c>, qui n'a ni secret ni route interne (MCP-02).
    /// Serveur MCP coupé : 404, comme toute route de la fonctionnalité, le conteneur répond alors 404 à <c>/mcp</c> ; jeton absent,
    /// inconnu, expiré, révoqué ou compte retiré : 401 ; annuaire injoignable : 503. Ouverte sans authentification pour que la
    /// fonctionnalité coupée se distingue d'un jeton refusé ; elle ne rend rien d'autre que le jeton présenté.
    /// </summary>
    [HttpGet("api/me/tokens/current")]
    [AllowAnonymous]
    public async Task<ActionResult<PersonalTokenView>> Current()
    {
        if (User.Identity?.AuthenticationType != PersonalTokenHandler.SchemeName
            || !int.TryParse(User.FindFirst(PersonalTokens.TokenClaim)?.Value, out var id))
        {
            if (HttpContext.Items.ContainsKey(PersonalTokenHandler.DirectoryDown)) return StatusCode(StatusCodes.Status503ServiceUnavailable);
            Response.Headers.WWWAuthenticate = "Bearer";
            return Unauthorized();
        }
        var token = await db.PersonalTokens.AsNoTracking().FirstAsync(t => t.Id == id);
        return PersonalTokenView.From(token);
    }

    [HttpPost("api/me/tokens")]
    [Authorize(Policy = "User")]
    public async Task<IActionResult> Create(PersonalTokenDto dto)
    {
        var name = dto.Name?.Trim() ?? "";
        if (name.Length is 0 or > 100) return BadRequest(new { message = "Nommer le jeton (100 caractères au plus) : le client qui s'en servira." });
        var days = dto.Days ?? PersonalTokens.DefaultDays;
        if (days is < PersonalTokens.MinDays or > PersonalTokens.MaxDays)
            return BadRequest(new { message = $"Durée de {PersonalTokens.MinDays} à {PersonalTokens.MaxDays} jours." });
        if (await Active().CountAsync(t => t.Username == Me) >= PersonalTokens.MaxActive)
            return BadRequest(new { message = $"{PersonalTokens.MaxActive} jetons actifs au plus : en révoquer un d'abord." });
        var secret = PersonalTokens.NewSecret();
        var token = new PersonalToken
        {
            Username = Me, DisplayName = MyDisplay, Name = name, Hash = PersonalTokens.HashOf(secret),
            ExpiresAt = DateTime.UtcNow.AddDays(days)
        };
        db.PersonalTokens.Add(token);
        await db.SaveChangesAsync();
        logger.LogInformation("Jeton personnel {TokenId} créé par {Username}, {Days} jours", token.Id, Me, days);
        return Ok(new CreatedPersonalToken(PersonalTokenView.From(token), secret));
    }

    [HttpDelete("api/me/tokens/{id:int}")]
    [Authorize(Policy = "User")]
    public Task<IActionResult> RevokeMine(int id) => RevokeAsync(id, mine: true);

    [HttpGet("api/admin/tokens")]
    [Authorize(Policy = "Admin")]
    public async Task<List<PersonalTokenView>> All() =>
        (await Active().OrderBy(t => t.Username).ThenByDescending(t => t.CreatedAt).ToListAsync()).Select(PersonalTokenView.From).ToList();

    [HttpDelete("api/admin/tokens/{id:int}")]
    [Authorize(Policy = "Admin")]
    public Task<IActionResult> Revoke(int id) => RevokeAsync(id, mine: false);

    private async Task<IActionResult> RevokeAsync(int id, bool mine)
    {
        var token = await db.PersonalTokens.FirstOrDefaultAsync(t => t.Id == id && t.RevokedAt == null);
        // Le jeton d'un autre compte est « introuvable » pour son titulaire présumé.
        if (token is null || (mine && !string.Equals(token.Username, Me, StringComparison.OrdinalIgnoreCase))) return NotFound();
        (token.RevokedAt, token.RevokedBy) = (DateTime.UtcNow, Me);
        await db.SaveChangesAsync();
        logger.LogInformation("Jeton personnel {TokenId} de {Owner} révoqué par {Username}", token.Id, token.Username, Me);
        return NoContent();
    }
}
