using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Data;
using SAloha.Api.Core.Records;

namespace SAloha.Api.Core.Features;

/// <summary>
/// OPT-14 : le conteneur dépose une proposition. L'API vérifie la fonctionnalité (activée), la cible
/// (elle existe), le champ (proposable par cette fonctionnalité) et la valeur (liste fermée du champ) ;
/// une proposition identique à une proposition ouverte n'est pas recréée. La valeur « avant » est lue
/// par l'API sur l'enregistrement, pas reprise du conteneur. Une proposition ouverte devenue périmée est
/// recalculée en place, comme celle dont la valeur change ; une valeur rejetée ou corrigée n'est pas reproposée.
/// Une proposition sur une paire (fusion) suit ses propres règles (<see cref="PairAsync"/>).
/// </summary>
[ApiController]
[Route("internal/proposals")]
[Authorize(Policy = ContainerTokenHandler.Policy)]
public class InternalProposalsController(AppDbContext db, IFeatureGate gate) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(ProposalInput input)
    {
        var feature = FeatureCatalog.ByKey(input.Feature);
        if (feature is null || !await gate.IsEnabledAsync(feature.Key, HttpContext.RequestAborted)) return Forbid();
        var field = ProposalFields.Of(input.TargetType ?? "", input.Field ?? "");
        if (field is null || field.Feature != feature.Key)
            return BadRequest(new { message = $"Champ non proposable par « {feature.Name} » : {input.TargetType}.{input.Field}." });
        var target = await ProposalFields.LoadAsync(db, field.TargetType, input.TargetId);
        if (ProposalFields.Stamp(target) is not { } record) return NotFound(new { message = "Enregistrement introuvable." });
        var after = input.After?.Trim() ?? "";
        if (after.Length > 2000) return BadRequest(new { message = "Valeur proposée : 2 000 caractères au plus." });
        if (await field.Validate(db, target, after) is { } error) return BadRequest(new { message = error });
        if (field.Pair) return await PairAsync(feature.Key, field, record, after, input);
        var before = field.Read(target);
        if (string.Equals(before, after, StringComparison.Ordinal))
            return BadRequest(new { message = "La valeur proposée est déjà celle de l'enregistrement." });
        var reasons = input.Reasons?.Trim() ?? "";
        if (reasons.Length > 1000) return BadRequest(new { message = "Motifs : 1 000 caractères au plus." });

        var same = db.Proposals.Where(p => p.Feature == feature.Key && p.TargetType == field.TargetType && p.TargetId == record.Id
            && p.Field == field.Name && p.After == after);
        // OPT-14 : une valeur que l'agent a écartée (rejetée, ou corrigée par une autre valeur) ne revient pas
        // quand le conteneur recalcule après une modification de l'enregistrement.
        if (await same.AsNoTracking().FirstOrDefaultAsync(p => p.Status == Proposal.Rejected || p.Status == Proposal.Corrected) is { } declined)
            return Ok(new { declined.Id, Created = false });
        // OPT-14 : une seule proposition ouverte par fonctionnalité, enregistrement et champ (une ligne par champ dans
        // l'encadré) : une valeur nouvelle remplace la proposition ouverte du champ, sur place.
        var existing = await db.Proposals.Where(p => p.Status == Proposal.Open && p.Feature == feature.Key && p.TargetType == field.TargetType
            && p.TargetId == record.Id && p.Field == field.Name).OrderBy(p => p.Id).FirstOrDefaultAsync();
        if (existing is not null)
        {
            // OPT-15 : périmée (enregistrement modifié depuis), elle est recalculée à l'identique : valeur avant,
            // motifs, sources et confiance repris, et datée de ce calcul ; elle redevient acceptable. Une valeur ou un
            // motif qui changent (panne probable qui grossit) la rafraîchissent de même.
            if (record.UpdatedAt > existing.CreatedAt || existing.After != after || existing.Reasons != reasons)
            {
                existing.After = after;
                existing.Before = before;
                existing.Reasons = reasons;
                existing.CitedJson = JsonSerializer.Serialize((input.Cited ?? []).Take(20).ToList(), JsonSerializerOptions.Web);
                existing.Confidence = input.Confidence is { } recalculated ? Math.Clamp(recalculated, 0, 1) : null;
                existing.CreatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
            return Ok(new { existing.Id, Created = false });
        }

        var proposal = new Proposal
        {
            Feature = feature.Key, TargetType = field.TargetType, TargetId = record.Id, PerimeterId = record.PerimeterId,
            Field = field.Name, Before = before, After = after, Reasons = reasons,
            CitedJson = JsonSerializer.Serialize((input.Cited ?? []).Take(20).ToList(), JsonSerializerOptions.Web),
            Confidence = input.Confidence is { } c ? Math.Clamp(c, 0, 1) : null,
        };
        db.Proposals.Add(proposal);
        await db.SaveChangesAsync();
        return StatusCode(StatusCodes.Status201Created, new { proposal.Id, Created = true });
    }

    /// <summary>
    /// Proposition sur une paire (fusion, CMA-05 et CMA-06 ; relation, CMA-02) : une fusion compte dans les deux sens, une
    /// relation dans le sien. Rejetée ou corrigée, elle ne revient pas ; ouverte, elle se rafraîchit sur place (motif, date) ;
    /// sinon elle se crée, l'autre enregistrement cité. Plusieurs paires restent ouvertes sur un même enregistrement : un CI
    /// peut avoir plusieurs doublons candidats, plusieurs relations à créer.
    /// </summary>
    private async Task<IActionResult> PairAsync(string feature, ProposalFields.Field field, ProposalFields.Stamped record, string after, ProposalInput input)
    {
        var other = field.Other!(after)!.Value;
        var reasons = input.Reasons?.Trim() ?? "";
        if (reasons.Length > 1000) return BadRequest(new { message = "Motifs : 1 000 caractères au plus." });
        var mirror = field.Mirror?.Invoke(record.Id);
        var pair = await db.Proposals.Where(p => p.Feature == feature && p.TargetType == field.TargetType && p.Field == field.Name
            && ((p.TargetId == record.Id && p.After == after) || (mirror != null && p.TargetId == other && p.After == mirror))).OrderBy(p => p.Id).ToListAsync();
        if (pair.FirstOrDefault(p => p.Status is Proposal.Rejected or Proposal.Corrected) is { } declined)
            return Ok(new { declined.Id, Created = false });
        if (pair.FirstOrDefault(p => p.Status == Proposal.Open) is { } open)
        {
            // Périmée (l'un des deux modifié depuis), elle redevient acceptable : le conteneur vient de la juger à nouveau.
            var changed = ProposalFields.Stamp(await ProposalFields.LoadAsync(db, field.TargetType, other))?.UpdatedAt;
            if (open.Reasons != reasons || record.UpdatedAt > open.CreatedAt || changed > open.CreatedAt)
            {
                (open.Reasons, open.CreatedAt) = (reasons, DateTime.UtcNow);
                await db.SaveChangesAsync();
            }
            return Ok(new { open.Id, Created = false });
        }
        var proposal = new Proposal
        {
            Feature = feature, TargetType = field.TargetType, TargetId = record.Id, PerimeterId = record.PerimeterId, Field = field.Name,
            Before = null, After = after, Reasons = reasons,
            CitedJson = JsonSerializer.Serialize(new List<CitedRef> { new(field.TargetType, other) }, JsonSerializerOptions.Web),
            Confidence = input.Confidence is { } c ? Math.Clamp(c, 0, 1) : null,
        };
        db.Proposals.Add(proposal);
        await db.SaveChangesAsync();
        return StatusCode(StatusCodes.Status201Created, new { proposal.Id, Created = true });
    }
}
