using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Audit;
using SAloha.Api.Core.Data;
using SAloha.Api.Core.Features;
using SAloha.Api.Core.Links;
using SAloha.Api.Core.Perimeters;

namespace SAloha.Api.Modules.ServiceConfigurationManagement;

/// <summary>
/// CMA-05 : fusion de deux CI d'un même périmètre et d'un même environnement. Les liens, les relations et les actifs du
/// sas rapprochés du doublon sont reportés sur le CI conservé, sans lien ni relation vers lui-même ni en double ; le
/// doublon passe Retiré, motif « Fusionné dans &lt;référence&gt; » ; une ligne « Fusion » s'écrit au journal des deux.
/// </summary>
public static class CiMerge
{
    private const string Retired = "Retiré";

    /// <summary>Pourquoi deux CI ne se fusionnent pas : message (400), ou null.</summary>
    public static string? Refusal(ConfigurationItem a, ConfigurationItem? b) => b switch
    {
        null => "Fusion : l'autre CI est introuvable.",
        _ when b.Id == a.Id => "Un CI ne se fusionne pas avec lui-même.",
        _ when a.PerimeterId != b.PerimeterId => "Fusion refusée : les deux CI sont dans deux périmètres différents.",
        _ when a.Environment != b.Environment => $"Fusion refusée : les deux CI sont dans deux environnements différents ({a.Environment}, {b.Environment}).",
        _ when a.Status == Retired || b.Status == Retired => "Fusion refusée : un CI retiré ne se fusionne plus.",
        _ => null,
    };

    /// <summary>OPT-14 : une proposition de fusion désigne, par son identifiant, un autre CI fusionnable avec le CI visé.</summary>
    public static async Task<string?> ValidateAsync(AppDbContext db, object target, string value) =>
        int.TryParse(value, out var id)
            ? Refusal((ConfigurationItem)target, await db.ConfigurationItems.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id))
            : $"Fusion : identifiant de CI invalide « {value} ».";

    public record Outcome(int Links, int Relations, int Assets, int Expired);

    /// <summary>
    /// Reporte sur <paramref name="keep"/> ce qui désigne <paramref name="duplicate"/>, puis retire le doublon. Un lien ou
    /// une relation qui deviendrait un lien du CI vers lui-même, ou qui existe déjà sur le CI conservé, disparaît avec
    /// le doublon. Les propositions encore ouvertes sur le doublon expirent : il n'est plus à décider. Le journal trace
    /// chaque lien et chaque relation retirés et ajoutés, la transition du doublon et la fusion sur les deux CI.
    /// </summary>
    /// <param name="decided">La proposition décidée par cette fusion, qui n'expire pas.</param>
    public static async Task<Outcome> RunAsync(AppDbContext db, ConfigurationItem keep, ConfigurationItem duplicate, int decided, string by,
        string byDisplay)
    {
        // Liens vers les autres pratiques (incidents, changements, services…), dans leur sens d'origine.
        var links = await db.ItemLinks.Where(l => (l.FromType == "ci" && l.FromId == duplicate.Id) || (l.ToType == "ci" && l.ToId == duplicate.Id))
            .ToListAsync();
        var linked = (await db.ItemLinks.AsNoTracking()
                .Where(l => (l.FromType == "ci" && l.FromId == keep.Id) || (l.ToType == "ci" && l.ToId == keep.Id)).ToListAsync())
            .Select(l => l.FromType == "ci" && l.FromId == keep.Id ? (l.ToType, l.ToId) : (l.FromType, l.FromId)).ToHashSet();
        var movedLinks = 0;
        foreach (var l in links)
        {
            var fromDuplicate = l.FromType == "ci" && l.FromId == duplicate.Id;
            var end = fromDuplicate ? (l.ToType, l.ToId) : (l.FromType, l.FromId);
            db.ItemLinks.Remove(l);
            if (end == ("ci", keep.Id) || !linked.Add(end)) continue;
            db.ItemLinks.Add(new ItemLink
            {
                FromType = l.FromType, FromId = fromDuplicate ? keep.Id : l.FromId, ToType = l.ToType, ToId = fromDuplicate ? l.ToId : keep.Id,
                CreatedBy = by,
            });
            movedLinks++;
        }

        // Relations entre CI (CFG-03 : ni vers soi, ni en double même source, cible et type).
        var relations = await db.CiRelationships.Where(r => r.SourceId == duplicate.Id || r.TargetId == duplicate.Id).ToListAsync();
        var related = (await db.CiRelationships.AsNoTracking().Where(r => r.SourceId == keep.Id || r.TargetId == keep.Id).ToListAsync())
            .Select(r => (r.SourceId, r.TargetId, r.Type)).ToHashSet();
        var movedRelations = 0;
        foreach (var r in relations)
        {
            var (source, target) = (r.SourceId == duplicate.Id ? keep.Id : r.SourceId, r.TargetId == duplicate.Id ? keep.Id : r.TargetId);
            db.CiRelationships.Remove(r);
            if (source == target || !related.Add((source, target, r.Type))) continue;
            db.CiRelationships.Add(new CiRelationship { SourceId = source, TargetId = target, Type = r.Type });
            movedRelations++;
        }

        // Actifs du sas rapprochés du doublon, ou qui le proposaient (CFG-41).
        var assets = await db.DiscoveredAssets.Where(a => a.MatchedCiId == duplicate.Id).ToListAsync();
        foreach (var a in assets) a.MatchedCiId = keep.Id;

        var now = DateTime.UtcNow;
        duplicate.Status = Retired;
        // CFG-48 : une application retirée a le cycle « Retirée ».
        if (duplicate.AppLifecycle is not null) duplicate.AppLifecycle = CiRules.LifecycleByStatus[Retired][0];
        (duplicate.UpdatedAt, keep.UpdatedAt) = (now, now);
        await db.SaveChangesAsync();

        var key = duplicate.Id.ToString();
        var open = await db.Proposals.Where(p => p.Id != decided && p.Status == Proposal.Open && p.TargetType == "ci"
            && (p.TargetId == duplicate.Id || (p.Field == ProposalFields.Merge && p.After == key))).ToListAsync();
        foreach (var p in open)
        {
            (p.Status, p.DecidedAt, p.DecidedBy) = (Proposal.Expired, now, "Système");
            await ProposalDecisions.CountAsync(db, p, Proposal.Expired);
        }
        var summary = $"{duplicate.Reference} fusionné dans {keep.Reference} : {movedLinks} lien(s), {movedRelations} relation(s), "
                      + $"{assets.Count} actif(s) du sas reportés.";
        foreach (var ci in new[] { keep, duplicate })
            db.AuditEntries.Add(new AuditEntry
            {
                EntityType = "ci", EntityId = ci.Id, Reference = ci.Reference, Action = AuditEntry.Merge, Field = "fusion",
                OldValue = duplicate.Reference, NewValue = keep.Reference, Reason = summary, Author = by, AuthorDisplayName = byDisplay, At = now,
                Origin = AuditEntry.AssistedCmdb,
            });
        await db.SaveChangesAsync();
        return new Outcome(movedLinks, movedRelations, assets.Count, open.Count);
    }
}

/// <summary>Décision d'une fusion : le CI conservé, l'un des deux de la proposition.</summary>
public record MergeInput(int? KeepId);

/// <summary>
/// CMA-05 : accepter une proposition de fusion, en choisissant le CI conservé (la proposition suggère le second, celui
/// qu'elle désigne). Réservé au gestionnaire du périmètre : la fusion retire un CI (CFG-12). Refusée entre deux
/// périmètres ou deux environnements, ou si l'un des CI est retiré (400) ; périmée si l'un d'eux a changé depuis (409).
/// Conserver le CI suggéré accepte la proposition ; conserver l'autre la corrige. Le rejet passe par la route commune
/// des propositions (OPT-15). La CMDB assistée coupée, la route n'existe pas (OPT-04).
/// </summary>
[ApiController]
[Route("api/assisted-cmdb/proposals")]
[Authorize(Policy = "User")]
[RequiresFeature(FeatureCatalog.AssistedCmdb)]
public class AssistedCmdbMergeController(AppDbContext db, PerimeterAccess access) : ControllerBase
{
    [HttpPost("{id:int}/merge")]
    public async Task<IActionResult> Merge(int id, MergeInput input)
    {
        var p = await db.Proposals.FirstOrDefaultAsync(x => x.Id == id && x.Feature == FeatureCatalog.AssistedCmdb && x.TargetType == "ci"
            && x.Field == ProposalFields.Merge && x.Status == Proposal.Open);
        var otherId = int.TryParse(p?.After, out var o) ? o : 0;
        var pair = p is null ? [] : await db.ConfigurationItems.Where(c => c.Id == p.TargetId || c.Id == otherId).ToListAsync();
        var target = pair.FirstOrDefault(c => c.Id == p?.TargetId);
        var scope = await access.GetAsync();
        if (p is null || target is null || !scope.Sees(target.PerimeterId)) return NotFound(new { message = "Proposition introuvable ou déjà décidée." });
        if (!scope.ManagerOn(target.PerimeterId)) return Forbid();
        if (input.KeepId != target.Id && input.KeepId != otherId)
            return BadRequest(new { message = "Choisir le CI conservé parmi les deux de la proposition." });
        var other = pair.FirstOrDefault(c => c.Id == otherId);
        if (CiMerge.Refusal(target, other) is { } refusal) return BadRequest(new { message = refusal });
        if (target.UpdatedAt > p.CreatedAt || other!.UpdatedAt > p.CreatedAt)
            return Conflict(new { message = $"Proposition périmée : {target.Reference} ou {other!.Reference} a été modifié depuis ; elle sera recalculée." });

        var (keep, duplicate) = input.KeepId == target.Id ? (target, other) : (other, target);
        var me = User.Identity?.Name ?? "";
        var display = User.FindFirst("displayName")?.Value ?? me;
        // Le motif accompagne la transition du doublon au journal (CMA-05) ; l'origine, toutes ses lignes (OPT-08).
        HttpContext.Items[AuditInterceptor.ReasonKey] = $"Fusionné dans {keep.Reference}";
        await using var tx = await db.Database.BeginTransactionAsync();
        CiMerge.Outcome outcome;
        using (AuditOrigin.Use(AuditEntry.AssistedCmdb))
            outcome = await CiMerge.RunAsync(db, keep, duplicate, p.Id, me, display);
        await ProposalDecisions.DecideAsync(db, p, keep.Id == otherId ? Proposal.Accepted : Proposal.Corrected, keep.Id.ToString(), display);
        await tx.CommitAsync();
        return Ok(new
        {
            Kept = new { keep.Id, keep.Reference, keep.Title }, Retired = new { duplicate.Id, duplicate.Reference, duplicate.Title },
            outcome.Links, outcome.Relations, outcome.Assets,
        });
    }
}

/// <summary>
/// CMA-06 : paires d'un périmètre dont la fusion a été rejetée, que le conteneur ne soumet plus au modèle : <c>[[a, b]]</c>,
/// <c>a &lt; b</c>, tant que la décision est conservée (13 mois, OPT-17).
/// </summary>
[ApiController]
[Route("internal/assisted-cmdb/declined-pairs")]
[Authorize(Policy = ContainerTokenHandler.Policy)]
[InternalFeature(FeatureCatalog.AssistedCmdb)]
public class InternalAssistedCmdbPairsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int perimeter)
    {
        var rows = await db.Proposals.AsNoTracking().Where(p => p.Feature == FeatureCatalog.AssistedCmdb && p.TargetType == "ci"
            && p.Field == ProposalFields.Merge && p.PerimeterId == perimeter && p.Status == Proposal.Rejected).Select(p => new { p.TargetId, p.After })
            .ToListAsync();
        return Ok(rows.Select(r => int.TryParse(r.After, out var other) ? new[] { Math.Min(r.TargetId, other), Math.Max(r.TargetId, other) } : null)
            .OfType<int[]>().DistinctBy(x => (x[0], x[1])).OrderBy(x => x[0]).ThenBy(x => x[1]));
    }
}
