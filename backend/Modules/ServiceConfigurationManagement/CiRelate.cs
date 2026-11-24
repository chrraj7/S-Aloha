using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Audit;
using SAloha.Api.Core.Data;
using SAloha.Api.Core.Features;
using SAloha.Api.Core.Perimeters;

namespace SAloha.Api.Modules.ServiceConfigurationManagement;

/// <summary>
/// CMA-02, CMA-09 : relation proposée par la CMDB assistée entre deux CI existants, « &lt;cible&gt;|&lt;type&gt; » sur le CI
/// source. Ni vers soi, ni hors liste, ni déjà existante (CFG-03), ni avec un CI retiré (CFG-14), ni entre deux périmètres.
/// </summary>
public static class CiRelate
{
    public record Value(int Target, string Type);

    public static Value? Parse(string? value) =>
        value?.Split('|', 2) is [var id, var type] && int.TryParse(id, out var target) ? new Value(target, type) : null;

    public static string Format(int target, string type) => $"{target}|{type}";

    /// <summary>Pourquoi cette relation ne se propose ou ne se crée pas : message, ou null.</summary>
    public static async Task<string?> RefusalAsync(AppDbContext db, ConfigurationItem source, ConfigurationItem? target, string type)
    {
        if (!CiRelationship.Types.Contains(type))
            return $"Type de relation inconnu : « {type} ». Valeurs : {string.Join(", ", CiRelationship.Types)}.";
        if (target is null) return "Relation : le CI cible est introuvable.";
        if (target.Id == source.Id) return "Un CI ne peut pas être relié à lui-même.";
        if (target.PerimeterId != source.PerimeterId) return "Relation refusée : les deux CI sont dans deux périmètres différents.";
        if (source.Status == "Retiré" || target.Status == "Retiré") return "Un CI retiré ne peut plus être relié.";
        if (await db.CiRelationships.AnyAsync(r => r.SourceId == source.Id && r.TargetId == target.Id && r.Type == type))
            return "Cette relation existe déjà.";
        return null;
    }

    /// <summary>OPT-14 : à son dépôt, la relation proposée doit pouvoir se créer.</summary>
    public static async Task<string?> ValidateAsync(AppDbContext db, object source, string value) =>
        Parse(value) is { } v
            ? await RefusalAsync(db, (ConfigurationItem)source, await db.ConfigurationItems.AsNoTracking().FirstOrDefaultAsync(c => c.Id == v.Target), v.Type)
            : $"Relation : valeur attendue « <identifiant du CI cible>|<type> », reçue « {value} ».";
}

/// <summary>Décision d'une relation : son type, au besoin corrigé ; absent, celui proposé.</summary>
public record RelateInput(string? Type);

/// <summary>
/// CMA-02, CMA-03 : accepter une relation proposée, en gardant ou en corrigeant son type. Par un utilisateur qui voit les deux
/// CI (PER-08), comme pour relier à la main ; la relation se crée avec les règles de la CMDB (CFG-03, CFG-14) et son journal
/// porte l'origine « CMDB assistée » (OPT-08). Le type proposé accepte la proposition, un autre la corrige. Périmée si l'un des
/// deux CI a changé depuis (409) ; le rejet passe par la route commune (OPT-15). La CMDB assistée coupée, la route n'existe pas.
/// </summary>
[ApiController]
[Route("api/assisted-cmdb/proposals")]
[Authorize(Policy = "User")]
[RequiresFeature(FeatureCatalog.AssistedCmdb)]
public class AssistedCmdbRelateController(AppDbContext db, PerimeterAccess access) : ControllerBase
{
    [HttpPost("{id:int}/relate")]
    public async Task<IActionResult> Relate(int id, RelateInput input)
    {
        var p = await db.Proposals.FirstOrDefaultAsync(x => x.Id == id && x.Feature == FeatureCatalog.AssistedCmdb && x.TargetType == "ci"
            && x.Field == ProposalFields.Relation && x.Status == Proposal.Open);
        var proposed = CiRelate.Parse(p?.After);
        var pair = p is null || proposed is null ? []
            : await db.ConfigurationItems.Where(c => c.Id == p.TargetId || c.Id == proposed.Target).ToListAsync();
        var source = pair.FirstOrDefault(c => c.Id == p?.TargetId);
        var target = pair.FirstOrDefault(c => c.Id == proposed?.Target);
        var scope = await access.GetAsync();
        if (p is null || proposed is null || source is null || !scope.Sees(source.PerimeterId))
            return NotFound(new { message = "Proposition introuvable ou déjà décidée." });
        // PER-08 : relier suppose de voir les deux CI.
        if (target is not null && !scope.Sees(target.PerimeterId)) return BadRequest(new { message = "Relation : le CI cible est introuvable." });
        var type = string.IsNullOrWhiteSpace(input.Type) ? proposed.Type : input.Type.Trim();
        if (await CiRelate.RefusalAsync(db, source, target, type) is { } refusal)
            return refusal == "Cette relation existe déjà." ? Conflict(new { message = refusal }) : BadRequest(new { message = refusal });
        if (source.UpdatedAt > p.CreatedAt || target!.UpdatedAt > p.CreatedAt)
            return Conflict(new { message = $"Proposition périmée : {source.Reference} ou {target!.Reference} a été modifié depuis ; elle sera recalculée." });

        var relation = new CiRelationship { SourceId = source.Id, TargetId = target.Id, Type = type };
        db.CiRelationships.Add(relation);
        using (AuditOrigin.Use(AuditEntry.AssistedCmdb))
            await db.SaveChangesAsync();
        var final = CiRelate.Format(target.Id, type);
        await ProposalDecisions.DecideAsync(db, p, final == p.After ? Proposal.Accepted : Proposal.Corrected, final,
            User.FindFirst("displayName")?.Value ?? User.Identity?.Name ?? "");
        return Ok(new { relation.Id, relation.Type, Target = new { target.Id, target.Reference, target.Title } });
    }
}
