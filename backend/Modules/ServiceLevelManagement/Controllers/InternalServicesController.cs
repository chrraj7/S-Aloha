using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Data;
using SAloha.Api.Core.Features;

namespace SAloha.Api.Modules.ServiceLevelManagement;

/// <summary>
/// OPT-13 : le catalogue des services, dans la vue de l'API publique (ADR-0016), pour le conteneur
/// <c>features</c> : statut (un service voté doit être en service, TRI-03) et criticité (TRI-04). Le
/// catalogue se lit dans toute l'organisation (ADR-0014 D2) : aucun filtre de périmètre.
/// </summary>
[ApiController]
[Route("internal/services")]
[Authorize(Policy = ContainerTokenHandler.Policy)]
[InternalFeature(FeatureCatalog.AssistedTriage, FeatureCatalog.AssistedCmdb)]
public class InternalServicesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List() =>
        Ok((await db.Services.AsNoTracking().Include(s => s.Agreements).OrderBy(s => s.Id).ToListAsync()).Select(ItServiceView.From));
}
