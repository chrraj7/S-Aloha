using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Data;
using SAloha.Api.Core.Notifications;
using SAloha.Api.Core.Perimeters;
using SAloha.Api.Modules.ServiceConfigurationManagement.Discovery;

namespace SAloha.Api.Modules.ServiceConfigurationManagement.Controllers;

/// <summary>
/// Sas de staging (CFG-40, CFG-41, CFG-42) : actifs découverts à rapprocher d'un CI, à créer en
/// CI ou à ignorer. Réservé aux gestionnaires, sur les périmètres qu'ils gèrent (PER-06).
/// </summary>
[ApiController]
[Route("api/discovery/staging")]
[Authorize(Policy = "User")]
public class StagingController(AppDbContext db, PerimeterAccess access, MatchingEngine matching) : ControllerBase
{
    private string Me => User.FindFirst("displayName")?.Value ?? User.Identity?.Name ?? "";

    /// <summary>Actifs des périmètres gérés ; null si l'utilisateur n'en gère aucun.</summary>
    private async Task<IQueryable<DiscoveredAsset>?> ManagedAsync()
    {
        var scope = await access.GetAsync();
        if (scope.Global) return db.Set<DiscoveredAsset>();
        var ids = scope.ManagedIds.ToList();
        return ids.Count == 0 ? null : db.Set<DiscoveredAsset>().Where(a => ids.Contains(a.PerimeterId));
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? source, [FromQuery] string? q,
        [FromQuery] bool unknown = false, [FromQuery] int? ci = null, [FromQuery] int? perimeter = null)
    {
        if (await ManagedAsync() is not { } query) return Forbid();
        // Un CI rapproché puis déplacé vers un périmètre invisible ne se décrit pas (PER-05).
        var scope = await access.GetAsync();
        var global = scope.Global;
        var visible = scope.Ids.ToList();
        query = query.AsNoTracking();
        if (perimeter is not null) query = query.Where(a => a.PerimeterId == perimeter);
        if (!string.IsNullOrEmpty(status)) query = query.Where(a => a.Status == status);
        if (!string.IsNullOrEmpty(source)) query = query.Where(a => a.Sources.Contains(source));
        if (ci is not null) query = query.Where(a => a.MatchedCiId == ci);
        if (unknown) query = query.Where(a => !a.InDirectory && a.MatchedCiId == null && DiscoveredAsset.Open.Contains(a.Status));
        if (!string.IsNullOrWhiteSpace(q))
        {
            var key = q.Trim().ToLower();
            query = query.Where(a => (a.IpAddress != null && a.IpAddress.Contains(key)) || (a.Hostname != null && a.Hostname.Contains(key))
                                     || (a.MacAddress != null && a.MacAddress.ToLower().Contains(key))
                                     || (a.SerialNumber != null && a.SerialNumber.ToLower().Contains(key)));
        }
        var rows = await query.OrderBy(a => a.Status == DiscoveredAsset.InConflict ? 0 : a.Status == DiscoveredAsset.ToReconcile ? 1 : 2)
            .ThenByDescending(a => a.LastSeenAt).Take(1000)
            .Select(a => new
            {
                a.Id, a.PerimeterId, a.IpAddress, a.MacAddress, a.Hostname, a.DetectedType, a.DetectedOs, a.Vendor, a.Model,
                a.SerialNumber, a.Sources, a.InDirectory, a.Status, a.ConfidenceScore, a.MatchRule, a.SuggestedAction, a.ConflictReason,
                a.FirstSeenAt, a.LastSeenAt,
                Unknown = !a.InDirectory && a.MatchedCiId == null && DiscoveredAsset.Open.Contains(a.Status),
                MatchedCi = a.MatchedCi == null || !(global || visible.Contains(a.MatchedCi.PerimeterId)) ? null : new { a.MatchedCi.Id, a.MatchedCi.Reference, a.MatchedCi.Title }
            }).ToListAsync();
        return Ok(rows);
    }

    /// <summary>Compteurs du sas (en-tête de la page, console des gestionnaires).</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> Summary()
    {
        if (await ManagedAsync() is not { } query) return Forbid();
        var all = await query.AsNoTracking().Select(a => new { a.Status, a.InDirectory, a.MatchedCiId }).ToListAsync();
        var perimeters = await query.Select(a => a.PerimeterId).Distinct().ToListAsync();
        return Ok(new
        {
            // CFG-55 : relations proposées en attente de décision.
            Relations = await db.RelationProposals.CountAsync(r => r.Status == RelationProposal.Proposed && perimeters.Contains(r.PerimeterId)),
            ToReconcile = all.Count(a => a.Status == DiscoveredAsset.ToReconcile),
            Conflicts = all.Count(a => a.Status == DiscoveredAsset.InConflict),
            Unknown = all.Count(a => !a.InDirectory && a.MatchedCiId == null && DiscoveredAsset.Open.Contains(a.Status)),
            Reconciled = all.Count(a => a.Status == DiscoveredAsset.Reconciled),
            Ignored = all.Count(a => a.Status == DiscoveredAsset.Ignored),
        });
    }

    /// <summary>Fiche d'un actif : rapprochement recalculé, et CI pré-rempli pour une création.</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        if (await ManagedAsync() is not { } query) return Forbid();
        var asset = await query.AsNoTracking().Include(a => a.MatchedCi).FirstOrDefaultAsync(a => a.Id == id);
        if (asset is null) return NotFound();
        var match = DiscoveredAsset.Open.Contains(asset.Status) ? await matching.FindAsync(asset) : null;
        var target = match?.Ci ?? asset.MatchedCi;
        if (target is not null && !(await access.GetAsync()).Sees(target.PerimeterId)) target = null;   // PER-05
        asset.MatchedCi = null;   // décrit par TargetCi
        var software = await db.DiscoveredSoftware.AsNoTracking().Where(s => s.AssetId == id).OrderBy(s => s.Name)
            .Select(s => new { s.Name, s.Version }).ToListAsync();
        return Ok(new
        {
            Asset = DiscoveredAssetView.From(asset),
            Software = software,
            Suggestion = match is null ? null : new { match.Score, match.Rule, match.Action, match.Conflict },
            TargetCi = target is null ? null : new
            {
                target.Id, target.Reference, target.Title, target.CiType, target.Status, target.Environment, target.IpAddress,
                target.MacAddress, target.OperatingSystem, target.Vendor, target.Model, target.SerialNumber, target.Location
            },
            Proposal = new
            {
                Title = asset.Hostname?.Split('.')[0] ?? asset.IpAddress ?? $"actif-{asset.Id}",
                CiType = ConfigurationItem.Types.Contains(asset.DetectedType) ? asset.DetectedType : "Autre",
                asset.IpAddress, asset.MacAddress, OperatingSystem = asset.DetectedOs, asset.Vendor, asset.Model, asset.SerialNumber,
                Location = asset.Location ?? ""
            }
        });
    }

    /// <summary>
    /// CFG-40 : rapproche l'actif d'un CI existant du même périmètre (<c>update</c>), ou crée le CI
    /// (<c>create</c>) selon les règles du registre — propriétaire, unicité, portefeuille, matériel.
    /// </summary>
    [HttpPost("{id:int}/reconcile")]
    public async Task<IActionResult> Reconcile(int id, ReconcileAssetDto dto)
    {
        if (await ManagedAsync() is not { } query) return Forbid();
        var asset = await query.FirstOrDefaultAsync(a => a.Id == id);
        if (asset is null) return NotFound();
        if (asset.Status == DiscoveredAsset.Reconciled) return BadRequest(new { message = "Cet actif est déjà rapproché." });

        ConfigurationItem ci;
        if (dto.Action == "update")
        {
            var found = await db.ConfigurationItems.FirstOrDefaultAsync(c => c.Id == dto.TargetCiId);
            if (found is null || found.PerimeterId != asset.PerimeterId)
                return BadRequest(new { message = "Choisir un CI du même périmètre que l'actif." });
            ci = found;
            if (ci.Status == "Retiré") return BadRequest(new { message = $"{ci.Reference} est retiré : il ne peut plus être rapproché (CFG-14)." });
            if (!ConfigurationItem.HardwareTypes.Contains(ci.CiType))
                return BadRequest(new { message = $"{ci.Reference} est de type {ci.CiType} : un actif découvert se rapproche d'un serveur ou d'un équipement." });
            if (await matching.ApplyAsync(ci, asset) is { } conflict) return Conflict(new { message = conflict });
        }
        else if (dto.Action == "create")
        {
            if (dto.Ci is not { } form) return BadRequest(new { message = "Décrire le CI à créer." });
            if (string.IsNullOrWhiteSpace(form.Title)) return BadRequest(new { message = "Le titre est obligatoire." });
            var status = string.IsNullOrEmpty(form.Status) ? "Planifié" : form.Status;
            var error = Core.Records.Allowed.Check("Statut", status, ["Planifié", "En service", "Hors service"]);
            if (error is not null) return BadRequest(new { message = error });
            ConfigurationItem Build()
            {
                var x = new ConfigurationItem
                {
                    PerimeterId = asset.PerimeterId, Title = form.Title.Trim(), Description = form.Description ?? "", Status = "",
                    CreatedBy = User.Identity?.Name ?? "", CreatedByDisplayName = Me
                };
                var hasOwner = !string.IsNullOrWhiteSpace(form.OwnerId);
                x.OwnerType = hasOwner ? form.OwnerType ?? "User" : null;
                x.OwnerId = hasOwner ? form.OwnerId : null;
                x.OwnerDisplayName = hasOwner ? form.OwnerDisplayName ?? form.OwnerId : null;
                error = CiRules.Apply(x, form, status) ?? Core.Records.Lengths.Check(x);
                x.Status = status;
                return x;
            }
            var probe = Build();
            error ??= CiRules.CheckStatus(probe, status);
            if (error is not null) return BadRequest(new { message = error });
            if (await CiRules.ConflictAsync(db, probe) is { } twin) return Conflict(new { message = twin });
            ci = await References.CreateAsync(db, "CI", Build);
            // SOC-21 : le propriétaire désigné est prévenu, comme à une création depuis le registre.
            var notify = HttpContext.RequestServices.GetRequiredService<Notifier>();
            if (ci.OwnerId is not null) notify.ToOwner(ci, "ci", ci.OwnerType == "Group" ? "Confié à votre groupe" : "Confié à vous");
            await notify.SaveAsync();
        }
        else return BadRequest(new { message = "Action invalide : « update » (CI existant) ou « create » (nouveau CI)." });

        MatchingEngine.MarkReconciled(asset, ci, Me);
        asset.SuggestedAction = dto.Action == "create" ? "CreateNew" : "UpdateExisting";
        await db.SaveChangesAsync();
        // CMA-08 : la décision du rapprochement décide aussi les propositions de la CMDB assistée sur l'actif.
        await AssistedStaging.DecideAsync(db, asset, dto.Action, ci, Me);
        return Ok(new { asset.Id, asset.Status, ConfigurationItem = new { ci.Id, ci.Reference, ci.Title, ci.CiType, ci.Status } });
    }

    [HttpPost("{id:int}/ignore")]
    public Task<IActionResult> Ignore(int id) => SetStatusAsync(id, DiscoveredAsset.Open, DiscoveredAsset.Ignored);

    /// <summary>Un actif ignoré par erreur revient à rapprocher.</summary>
    [HttpPost("{id:int}/restore")]
    public Task<IActionResult> Restore(int id) => SetStatusAsync(id, [DiscoveredAsset.Ignored], DiscoveredAsset.ToReconcile);

    private async Task<IActionResult> SetStatusAsync(int id, string[] from, string to)
    {
        if (await ManagedAsync() is not { } query) return Forbid();
        var asset = await query.FirstOrDefaultAsync(a => a.Id == id);
        if (asset is null) return NotFound();
        if (!from.Contains(asset.Status))
            return BadRequest(new { message = $"Un actif « {asset.Status} » ne peut pas passer à « {to} »." });
        asset.Status = to;
        if (to == DiscoveredAsset.ToReconcile) asset.MatchedCiId = null;
        await db.SaveChangesAsync();
        if (to == DiscoveredAsset.Ignored) await AssistedStaging.DecideAsync(db, asset, "ignore", null, Me);
        return Ok(new { asset.Id, asset.Status });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await ManagedAsync() is not { } query) return Forbid();
        var asset = await query.FirstOrDefaultAsync(a => a.Id == id);
        if (asset is null) return NotFound();
        db.Remove(asset);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
