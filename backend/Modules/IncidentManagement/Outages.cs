using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Data;
using SAloha.Api.Core.Features;
using SAloha.Api.Core.Notifications;
using SAloha.Api.Core.Perimeters;

namespace SAloha.Api.Modules.IncidentManagement;

/// <summary>
/// TRI-07 : panne probable signalée par le tri assisté. Le groupe se calcule dans le conteneur
/// <c>features</c> (TRI-05, TRI-06) ; l'API le garde, en vérifie les règles (un seul périmètre, incidents
/// ouverts), notifie les gestionnaires à sa formation, une seule fois, et sert le bandeau des fiches.
/// </summary>
public class ProbableOutage : IHasPerimeter
{
    public const int MinMembers = 2;
    /// <summary>Lien des incidents du groupe : même service, même CI relié, ou sens proche.</summary>
    public static readonly string[] Bases = ["service", "ci", "sens"];

    public int Id { get; set; }
    [MaxLength(40)] public string Feature { get; set; } = FeatureCatalog.AssistedTriage;
    public int PerimeterId { get; set; }
    [MaxLength(10)] public string Basis { get; set; } = "service";
    public int? ServiceId { get; set; }
    [MaxLength(500)] public string Reasons { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Le groupe s'est défait (moins de deux incidents ouverts) ; null tant qu'il dure.</summary>
    public DateTime? ClosedAt { get; set; }
    public List<ProbableOutageMember> Members { get; set; } = [];
}

public class ProbableOutageMember
{
    public int OutageId { get; set; }
    public int IncidentId { get; set; }
}

/// <summary>Groupe tel que le conteneur le voit : ses membres, ce qui les lie ; <c>Closed</c> : il s'est défait.</summary>
public record OutageInput(string? Feature, int[]? Members, string? Basis, int? ServiceId, string? Reasons, bool Closed = false);

/// <summary>
/// TRI-07 : seconde écriture permise au conteneur, après les propositions (OPT-14, décision du 2026-11-23).
/// Le groupe s'identifie par ses membres : un signalement qui partage un incident avec un groupe ouvert
/// le met à jour (deux groupes qui se rejoignent n'en font plus qu'un) ; sinon il en forme un, et seulement
/// alors les gestionnaires du périmètre sont notifiés. Les incidents qui ne sont plus ouverts sont écartés ;
/// sous deux incidents ouverts, ou sur <c>closed</c>, le groupe se ferme.
/// </summary>
[ApiController]
[Route("internal/outages")]
[Authorize(Policy = ContainerTokenHandler.Policy)]
[InternalFeature(FeatureCatalog.AssistedTriage)]
public class InternalOutagesController(AppDbContext db, Notifier notify) : ControllerBase
{
    public const int MaxMembers = 200;
    /// <summary>TRI-05 : ouverts au sens de la corrélation (Nouveau, En cours, En attente).</summary>
    public static readonly string[] Open = ["Nouveau", "En cours", "En attente"];

    [HttpPost]
    public async Task<IActionResult> Signal(OutageInput input)
    {
        if (input.Feature != FeatureCatalog.AssistedTriage) return BadRequest(new { message = "Seul le tri assisté signale une panne probable." });
        var ids = (input.Members ?? []).Distinct().ToList();
        if (ids.Count == 0 || ids.Count > MaxMembers) return BadRequest(new { message = $"De 1 à {MaxMembers} incidents." });
        if (!input.Closed && !ProbableOutage.Bases.Contains(input.Basis))
            return BadRequest(new { message = $"Lien inconnu : « {input.Basis} ». Valeurs : {string.Join(", ", ProbableOutage.Bases)}." });
        var reasons = input.Reasons?.Trim() ?? "";
        if (reasons.Length > 500) return BadRequest(new { message = "Motif : 500 caractères au plus." });

        var incidents = await db.Incidents.AsNoTracking().Where(i => ids.Contains(i.Id))
            .Select(i => new { i.Id, i.PerimeterId, i.Status, i.CreatedAt, i.Reference, i.Title }).ToListAsync();
        // TRI-11 : une panne probable ne regroupe jamais deux périmètres.
        if (incidents.Select(i => i.PerimeterId).Distinct().Count() > 1)
            return BadRequest(new { message = "Une panne probable ne regroupe jamais deux périmètres." });
        var open = incidents.Where(i => Open.Contains(i.Status)).OrderBy(i => i.CreatedAt).ThenBy(i => i.Id).ToList();
        var known = await db.ProbableOutages.Include(o => o.Members)
            .Where(o => o.ClosedAt == null && o.Members.Any(m => ids.Contains(m.IncidentId))).OrderBy(o => o.Id).ToListAsync();

        if (input.Closed || open.Count < ProbableOutage.MinMembers)
        {
            foreach (var o in known) o.ClosedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Ok(new { Id = known.FirstOrDefault()?.Id, Closed = true });
        }

        var outage = known.FirstOrDefault();
        var created = outage is null;
        if (outage is null)
        {
            outage = new ProbableOutage { PerimeterId = open[0].PerimeterId };
            db.ProbableOutages.Add(outage);
        }
        // Deux groupes qui se rejoignent : le plus ancien garde le groupe, les autres se ferment.
        foreach (var other in known.Skip(1)) other.ClosedAt = DateTime.UtcNow;
        outage.Basis = input.Basis!;
        outage.ServiceId = input.Basis == "service" ? input.ServiceId : null;
        outage.Reasons = reasons;
        outage.UpdatedAt = DateTime.UtcNow;
        outage.Members.RemoveAll(m => !open.Any(i => i.Id == m.IncidentId));
        foreach (var i in open.Where(i => outage.Members.All(m => m.IncidentId != i.Id)))
            outage.Members.Add(new ProbableOutageMember { IncidentId = i.Id });

        if (created)
        {
            // TRI-07 : à la formation du groupe, une seule notification, sur le plus ancien incident.
            var anchor = await db.Incidents.AsNoTracking().FirstAsync(i => i.Id == open[0].Id);
            notify.AsSystem = true;
            notify.ToManagers(anchor, "incident", $"Panne probable : {open.Count} incidents ouverts{(reasons == "" ? "" : $" ({reasons})")}");
        }
        await db.SaveChangesAsync();
        if (created) await notify.SaveAsync();
        return created ? StatusCode(StatusCodes.Status201Created, new { outage.Id, Created = true }) : Ok(new { outage.Id, Created = false });
    }
}
