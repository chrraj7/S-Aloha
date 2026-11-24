using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Data;
using SAloha.Api.Core.Features;
using SAloha.Api.Core.Perimeters;

namespace SAloha.Api.Modules.ServiceConfigurationManagement;

/// <summary>
/// CMA-08 : aide au sas. Pour un actif À rapprocher que CFG-41 n'a rapproché d'aucun CI, ou En conflit, la CMDB assistée
/// propose un CI candidat (<c>matchedCiId</c>) ou un type pour la création (<c>ciType</c>), avec son motif ; la proposition
/// s'affiche sur la fiche de l'actif, et la décision reste celle du rapprochement (CFG-40) : rapprocher, créer ou ignorer
/// décide les propositions ouvertes de l'actif (acceptée si la décision les suit, corrigée sinon, rejetée si l'actif est ignoré).
/// </summary>
public static class AssistedStaging
{
    public const string Candidate = "matchedCiId", Type = "ciType";

    /// <summary>OPT-14 : un CI candidat existe, du périmètre de l'actif, se rapproche d'un actif (serveur ou équipement), et n'est pas retiré.</summary>
    public static async Task<string?> ValidateCandidateAsync(AppDbContext db, object target, string value)
    {
        var asset = (DiscoveredAsset)target;
        var ci = int.TryParse(value, out var id) ? await db.ConfigurationItems.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id) : null;
        if (ci is null || ci.PerimeterId != asset.PerimeterId) return $"CI candidat inconnu ou hors du périmètre de l'actif : « {value} ».";
        if (ci.Status == "Retiré") return $"{ci.Reference} est retiré : il ne peut plus être rapproché (CFG-14).";
        if (!ConfigurationItem.HardwareTypes.Contains(ci.CiType)) return $"{ci.Reference} est de type {ci.CiType} : un actif se rapproche d'un serveur ou d'un équipement.";
        return null;
    }

    /// <summary>
    /// CFG-40 : la décision du gestionnaire sur l'actif décide ses propositions ouvertes. Rapproché d'un CI existant : le CI
    /// candidat proposé est accepté s'il est celui-là, corrigé sinon ; un type proposé est corrigé (pas de création). Créé :
    /// le type proposé est accepté s'il est celui du CI créé, corrigé sinon ; un CI candidat est corrigé. Ignoré : rejetées.
    /// </summary>
    public static async Task DecideAsync(AppDbContext db, DiscoveredAsset asset, string action, ConfigurationItem? ci, string by)
    {
        var open = await db.Proposals.Where(p => p.Feature == FeatureCatalog.AssistedCmdb && p.TargetType == "asset" && p.TargetId == asset.Id
            && p.Status == Proposal.Open).ToListAsync();
        foreach (var p in open)
        {
            var final = p.Field == Candidate ? (action == "update" ? ci?.Id.ToString() : null) : (action == "create" ? ci?.CiType : null);
            var status = action == "ignore" ? Proposal.Rejected : final == p.After ? Proposal.Accepted : Proposal.Corrected;
            await ProposalDecisions.DecideAsync(db, p, status, action == "ignore" ? null : final, by);
        }
    }
}

public record StagingProposalView(int Id, string Field, string? Before, string After, string Reasons, DateTime CreatedAt, bool Stale,
    CmdbCiRef? Candidate);

/// <summary>
/// CMA-08 : propositions ouvertes de la CMDB assistée sur un actif du sas, pour la fiche de l'actif (gestionnaires du
/// périmètre, comme le sas) ; un CI candidat y est décrit. Rejeter sans décider de l'actif : route propre. La CMDB assistée
/// coupée, les routes n'existent pas (OPT-04).
/// </summary>
[ApiController]
[Route("api/discovery/staging/{id:int}/proposals")]
[Authorize(Policy = "User")]
[RequiresFeature(FeatureCatalog.AssistedCmdb)]
public class AssistedStagingController(AppDbContext db, PerimeterAccess access) : ControllerBase
{
    private async Task<DiscoveredAsset?> ManagedAsync(int id)
    {
        var asset = await db.DiscoveredAssets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
        return asset is not null && (await access.GetAsync()).ManagerOn(asset.PerimeterId) ? asset : null;
    }

    [HttpGet]
    public async Task<IActionResult> List(int id)
    {
        if (await ManagedAsync(id) is not { } asset) return NotFound();
        var proposals = await db.Proposals.AsNoTracking().Where(p => p.Feature == FeatureCatalog.AssistedCmdb && p.TargetType == "asset"
            && p.TargetId == id && p.Status == Proposal.Open).OrderBy(p => p.Id).ToListAsync();
        var ids = proposals.Where(p => p.Field == AssistedStaging.Candidate).Select(p => int.TryParse(p.After, out var c) ? c : 0).ToList();
        var cis = await db.ConfigurationItems.AsNoTracking().Where(c => ids.Contains(c.Id) && c.PerimeterId == asset.PerimeterId)
            .ToDictionaryAsync(c => c.Id, c => new CmdbCiRef(c.Id, c.Reference, c.Title));
        return Ok(proposals.Select(p => new StagingProposalView(p.Id, p.Field, p.Before, p.After, p.Reasons, p.CreatedAt, asset.LastSeenAt > p.CreatedAt,
            p.Field == AssistedStaging.Candidate && int.TryParse(p.After, out var c) ? cis.GetValueOrDefault(c) : null)));
    }

    [HttpPost("{proposalId:int}/reject")]
    public async Task<IActionResult> Reject(int id, int proposalId)
    {
        if (await ManagedAsync(id) is null) return NotFound();
        var p = await db.Proposals.FirstOrDefaultAsync(x => x.Id == proposalId && x.Feature == FeatureCatalog.AssistedCmdb && x.TargetType == "asset"
            && x.TargetId == id && x.Status == Proposal.Open);
        if (p is null) return NotFound(new { message = "Proposition introuvable ou déjà décidée." });
        await ProposalDecisions.DecideAsync(db, p, Proposal.Rejected, null, User.FindFirst("displayName")?.Value ?? User.Identity?.Name ?? "");
        return NoContent();
    }
}

/// <summary>
/// CMA-08, OPT-13 : à la fin d'une tâche de découverte (<c>discovery.finished</c>), le conteneur lit la tâche et les actifs
/// qu'elle laisse au sas sans rapprochement : À rapprocher sans CI, ou En conflit. Sans l'option « Analyser avec la CMDB
/// assistée », la liste est vide.
/// </summary>
[ApiController]
[Route("internal/discovery/staging")]
[Authorize(Policy = ContainerTokenHandler.Policy)]
[InternalFeature(FeatureCatalog.AssistedCmdb)]
public class InternalStagingController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int job)
    {
        var j = await db.Set<DiscoveryJob>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == job);
        if (j is null) return NotFound();
        var assets = !j.AssistedCmdb ? [] : await db.DiscoveredAssets.AsNoTracking()
            .Where(a => a.JobId == job && a.PerimeterId == j.PerimeterId
                && ((a.Status == DiscoveredAsset.ToReconcile && a.MatchedCiId == null) || a.Status == DiscoveredAsset.InConflict))
            .OrderBy(a => a.Id).Take(500).ToListAsync();
        return Ok(new
        {
            Job = new { j.Id, j.PerimeterId, j.AssistedCmdb, j.LastStatus },
            Assets = assets.Select(a => new
            {
                a.Id, a.PerimeterId, a.Status, a.Hostname, a.IpAddress, a.MacAddress, a.SerialNumber, a.Vendor, a.Model, a.DetectedOs, a.DetectedType,
                a.OpenPorts, a.Sources, a.InDirectory, a.MatchedCiId,
            }),
        });
    }
}
