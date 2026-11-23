using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SAloha.Api.Core.Data;

namespace SAloha.Api.Core.Features;

/// <summary>
/// OPT-11 : les routes internes (<c>/internal/…</c>) n'existent que sur le port interne
/// (<c>Features:InternalPort</c>, 8081), publié nulle part et que nginx ne relaie pas ; ce port ne
/// sert rien d'autre. Le port est celui de la connexion, jamais l'en-tête Host, qu'un client choisit.
/// </summary>
public static class InternalPort
{
    public const string Prefix = "/internal";

    public static Func<HttpContext, Func<Task>, Task> Guard(IConfiguration config)
    {
        var port = config.GetValue("Features:InternalPort", 0);
        return async (http, next) =>
        {
            var isInternal = http.Request.Path.StartsWithSegments(Prefix);
            var onInternalPort = port > 0 && http.Connection.LocalPort == port;
            if (isInternal != onInternalPort && !(onInternalPort && http.Request.Path.StartsWithSegments("/health")))
            {
                http.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            await next();
        };
    }
}

/// <summary>
/// OPT-10 : le conteneur <c>features</c> s'authentifie par son jeton (<c>Features:Token</c>),
/// comparé en temps constant ; un jeton absent ou de moins de 32 caractères n'ouvre rien.
/// Le jeton n'est jamais journalisé.
/// </summary>
public class ContainerTokenHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    IConfiguration config) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "FeatureContainer", Role = "FeatureContainer", Policy = "FeatureContainer";
    public const int MinLength = 32;

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var expected = config["Features:Token"] ?? "";
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.Ordinal)) return Task.FromResult(AuthenticateResult.NoResult());
        if (expected.Length < MinLength) return Task.FromResult(AuthenticateResult.Fail("Jeton du conteneur non configuré."));
        var given = Encoding.UTF8.GetBytes(header["Bearer ".Length..]);
        if (!CryptographicOperations.FixedTimeEquals(given, Encoding.UTF8.GetBytes(expected)))
            return Task.FromResult(AuthenticateResult.Fail("Jeton du conteneur refusé."));
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, FeatureCatalog.FeaturesContainer), new Claim(ClaimTypes.Role, Role)], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

/// <summary>
/// OPT-04, OPT-13 : une lecture interne n'est servie au conteneur que si l'une des fonctionnalités qui
/// s'en servent est activée ; sinon 403. Posée sur un contrôleur ou une action de <c>/internal/…</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class InternalFeatureAttribute(params string[] keys) : Attribute, IAsyncActionFilter
{
    public string[] Keys { get; } = keys.Select(k => FeatureCatalog.ByKey(k)?.Key ?? throw new ArgumentException($"Fonctionnalité inconnue : {k}", nameof(keys))).ToArray();

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var enabled = await context.HttpContext.RequestServices.GetRequiredService<IFeatureGate>().EnabledAsync(context.HttpContext.RequestAborted);
        if (!Keys.Any(enabled.Contains))
        {
            context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
            return;
        }
        await next();
    }
}

/// <summary>
/// OPT-12 : fil d'événements lu par le conteneur <c>features</c>, avec un curseur. Seuls les types
/// lus par une fonctionnalité activée sont servis ; aucune fonctionnalité activée : 403 (OPT-04).
/// </summary>
[ApiController]
[Route("internal/events")]
[Authorize(Policy = ContainerTokenHandler.Policy)]
public class InternalEventsController(AppDbContext db, IFeatureGate gate) : ControllerBase
{
    public const int MaxLimit = 500;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] long after = 0, [FromQuery] int limit = MaxLimit)
    {
        var enabled = await gate.EnabledAsync(HttpContext.RequestAborted);
        var types = FeatureEventSources.AllTypes.Where(t => FeatureEventSources.ConsumedBy(t, enabled)).ToList();
        if (types.Count == 0) return Forbid();
        limit = Math.Clamp(limit, 1, MaxLimit);
        var events = await db.FeatureEvents.AsNoTracking().Where(e => e.Id > after && types.Contains(e.Type))
            .OrderBy(e => e.Id).Take(limit).Select(e => FeatureEventView.From(e)).ToListAsync();
        var oldest = await db.FeatureEvents.MinAsync(e => (long?)e.Id) ?? 0;
        return Ok(new
        {
            Events = events,
            Next = events.Count > 0 ? events[^1].Id : after,
            // Le conteneur a manqué des événements purgés (plus de 7 jours de retard) : il repart d'un état complet.
            Gap = after > 0 && oldest > after + 1,
        });
    }
}
