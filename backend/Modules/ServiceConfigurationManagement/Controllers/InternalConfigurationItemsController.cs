using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Data;
using SAloha.Api.Core.Features;

namespace SAloha.Api.Modules.ServiceConfigurationManagement;

/// <summary>
/// OPT-13 : des CI par leurs identifiants, dans la vue de l'API publique (ADR-0016), pour le conteneur
/// <c>features</c> : la criticité des CI reliés à un incident (TRI-04). Les identifiants viennent de
/// l'incident traité ou du formulaire de l'agent, qui les voit.
/// </summary>
[ApiController]
[Route("internal/configuration-items")]
[Authorize(Policy = ContainerTokenHandler.Policy)]
[InternalFeature(FeatureCatalog.AssistedTriage, FeatureCatalog.AssistedCmdb)]
public class InternalConfigurationItemsController(AppDbContext db) : ControllerBase
{
    public const int MaxIds = 50;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? ids)
    {
        var wanted = (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => int.TryParse(v, out var id) ? id : 0).Where(id => id > 0).Distinct().ToList();
        if (wanted.Count > MaxIds) return BadRequest(new { message = $"{MaxIds} identifiants au plus." });
        if (wanted.Count == 0) return Ok(Array.Empty<ConfigurationItemView>());
        var items = await db.ConfigurationItems.AsNoTracking().AsSplitQuery()
            .Include(c => c.Outgoing).ThenInclude(r => r.Target)
            .Include(c => c.Incoming).ThenInclude(r => r.Source)
            .Where(c => wanted.Contains(c.Id)).OrderBy(c => c.Id).ToListAsync();
        return Ok(items.Select(ConfigurationItemView.From));
    }
}
