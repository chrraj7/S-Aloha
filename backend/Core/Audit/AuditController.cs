using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Data;
using SAloha.Api.Core.Links;
using SAloha.Api.Core.Perimeters;

namespace SAloha.Api.Core.Audit;

/// <summary>
/// Historique d'un enregistrement (SOC-20), du plus récent au plus ancien. Il suit la
/// visibilité de la fiche (PER-05) : hors de ses périmètres, 404. Les types qui ne sont pas
/// des enregistrements (périmètres, paramètres) relèvent de l'Admin.
/// </summary>
[ApiController]
[Route("api/audit")]
[Authorize(Policy = "User")]
public class AuditController(AppDbContext db, PerimeterAccess access) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string type, [FromQuery] int id)
    {
        var scope = await access.GetAsync();
        if (!scope.Global)
        {
            var kind = ItemLinks.ByType(type);
            var target = kind is null ? null : await kind.Query(db).FirstOrDefaultAsync(t => t.Id == id);
            if (target is null || !scope.Sees(target)) return NotFound();
        }
        return Ok(await db.AuditEntries.AsNoTracking()
            .Where(a => a.EntityType == type && a.EntityId == id)
            .OrderByDescending(a => a.At).ThenByDescending(a => a.Id)
            .Select(a => AuditEntryView.From(a)).ToListAsync());
    }
}

/// <summary>Conservation du journal : 3 ans par défaut (Audit:RetentionYears), purge quotidienne.</summary>
public class AuditRetention(IServiceScopeFactory scopes, IConfiguration config, ILogger<AuditRetention> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var years = config.GetValue("Audit:RetentionYears", 3);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var limit = DateTime.UtcNow.AddYears(-years);
                var purged = await db.AuditEntries.Where(a => a.At < limit).ExecuteDeleteAsync(ct);
                if (purged > 0) logger.LogInformation("Journal d'audit : {Count} lignes de plus de {Years} ans purgées.", purged, years);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Journal d'audit : purge en échec.");
            }
            await Task.Delay(TimeSpan.FromDays(1), ct);
        }
    }
}
