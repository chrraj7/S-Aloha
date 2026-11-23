using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Audit;
using SAloha.Api.Core.Data;

namespace SAloha.Api.Core.Features;

/// <summary>
/// Fonctionnalités optionnelles (OPT-01 à OPT-08, ADR-0023) : la liste des fonctionnalités activées,
/// que l'interface lit pour ne montrer que ce qui existe, et leur activation par l'Admin.
/// </summary>
[ApiController]
[Route("api")]
public class FeaturesController(AppDbContext db, FeatureStatus status, ContainerProbe probe, IFeatureGate gate) : ControllerBase
{
    public const string AuditType = "parametre", AuditReference = "Fonctionnalités";

    /// <summary>OPT-04 : les clés des fonctionnalités activées, et rien d'autre.</summary>
    [HttpGet("features")]
    [Authorize(Policy = "User")]
    public async Task<IEnumerable<string>> Enabled() => (await gate.EnabledAsync(HttpContext.RequestAborted)).Order();

    /// <summary>OPT-01, OPT-02 : chaque fonctionnalité, son conteneur, ses dépendances, le choix de l'Admin.</summary>
    [HttpGet("admin/features")]
    [Authorize(Policy = "Admin")]
    public async Task<List<FeatureView>> List() => await status.ViewsAsync(HttpContext.RequestAborted);

    /// <summary>OPT-03, OPT-07 : activer (conteneur et dépendances vérifiés) ou couper, tracé au journal d'audit.</summary>
    [HttpPut("admin/features/{key}")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Switch(string key, FeatureSwitchDto dto)
    {
        var feature = FeatureCatalog.ByKey(key);
        if (feature is null) return NotFound();
        var ct = HttpContext.RequestAborted;
        var warnings = new List<string>();
        if (dto.Enabled)
        {
            var container = await probe.ProbeAsync(feature.Container, ct);
            var dependencies = await status.DependenciesAsync(feature, ct);
            if (FeatureStatus.ActivationRefusal(feature, container, dependencies) is { } refusal) return BadRequest(new { message = refusal });
            warnings.AddRange(dependencies.Where(d => !d.Required && !d.Ok).Select(d => $"{d.Name} : {d.Message}"));
        }

        var row = await db.OptionalFeatures.FirstOrDefaultAsync(f => f.Key == key, ct);
        if ((row?.Enabled ?? false) != dto.Enabled)
        {
            if (row is null) db.OptionalFeatures.Add(row = new OptionalFeature { Key = key });
            var me = User.Identity?.Name ?? "";
            var display = User.FindFirst("displayName")?.Value ?? me;
            // OPT-07 : la référence d'audit tient en 20 caractères ; le champ nomme la fonctionnalité.
            db.AuditEntries.Add(new AuditEntry
            {
                EntityType = AuditType, EntityId = Array.IndexOf(FeatureCatalog.All, feature) + 1, Reference = AuditReference,
                Action = AuditEntry.Modification, Field = feature.Name,
                OldValue = row.Enabled ? "activée" : "coupée", NewValue = dto.Enabled ? "activée" : "coupée",
                Author = me, AuthorDisplayName = display, At = DateTime.UtcNow
            });
            (row.Enabled, row.UpdatedAt, row.UpdatedBy) = (dto.Enabled, DateTime.UtcNow, display);
            await db.SaveChangesAsync(ct);
            gate.Invalidate();
        }
        var view = (await status.ViewsAsync(ct)).Single(v => v.Key == key);
        return Ok(new { Feature = view, Warnings = warnings });
    }
}
