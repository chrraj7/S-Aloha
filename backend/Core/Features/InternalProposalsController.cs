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
/// par l'API sur l'enregistrement, pas reprise du conteneur.
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
        if (target is not Record record) return NotFound(new { message = "Enregistrement introuvable." });
        var after = input.After?.Trim() ?? "";
        if (after.Length > 2000) return BadRequest(new { message = "Valeur proposée : 2 000 caractères au plus." });
        if (await field.Validate(db, after) is { } error) return BadRequest(new { message = error });
        var before = field.Read(target);
        if (string.Equals(before, after, StringComparison.Ordinal))
            return BadRequest(new { message = "La valeur proposée est déjà celle de l'enregistrement." });
        var reasons = input.Reasons?.Trim() ?? "";
        if (reasons.Length > 1000) return BadRequest(new { message = "Motifs : 1 000 caractères au plus." });

        var existing = await db.Proposals.AsNoTracking().FirstOrDefaultAsync(p => p.Status == Proposal.Open && p.Feature == feature.Key
            && p.TargetType == field.TargetType && p.TargetId == record.Id && p.Field == field.Name && p.After == after);
        if (existing is not null) return Ok(new { existing.Id, Created = false });

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
}
