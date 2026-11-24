using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Data;
using SAloha.Api.Core.Features;
using SAloha.Api.Core.Links;
using SAloha.Api.Core.Perimeters;

namespace SAloha.Api.Modules.IncidentManagement;

/// <summary>Incident en cours de saisie, tel que le formulaire le connaît (TRI-02).</summary>
public record TriageDraft(string? Title, string? Description, int? ServiceId, int[]? ConfigurationItemIds, string? Category, string? Impact,
    string? OwnerType, string? OwnerId);

/// <summary>Réponse du conteneur : suggestions et incidents ouverts proches, avant le filtre de visibilité.</summary>
public record ContainerSuggestion(string Field, string Value, string Reasons, List<CitedRef>? Cited, double? Confidence);
public record ContainerDraftSuggestions(List<ContainerSuggestion>? Suggestions, List<int>? Related);

/// <summary>
/// TRI-02 : suggestions à la saisie d'un incident, relayées au conteneur <c>features</c> (OPT-16 : 2 secondes au
/// plus ; échec, délai ou conteneur absent : réponse vide). TRI-11 : le conteneur cherche dans les périmètres
/// visibles de l'agent, et la réponse ne cite que ce qu'il voit. Le tri coupé, la route n'existe pas (OPT-04).
/// </summary>
[ApiController]
[Route("api/triage")]
[Authorize(Policy = "User")]
[RequiresFeature(FeatureCatalog.AssistedTriage)]
public class TriageController(AppDbContext db, PerimeterAccess access, FeaturesClient features) : ControllerBase
{
    [HttpPost("suggest")]
    public async Task<IActionResult> Suggest(TriageDraft draft)
    {
        var empty = new { Suggestions = Array.Empty<object>(), Related = Array.Empty<object>() };
        if ((draft.Title?.Trim().Length ?? 0) < 4) return Ok(empty);
        var scope = await access.GetAsync();
        var perimeters = scope.Global ? await db.Perimeters.AsNoTracking().Select(p => p.Id).ToListAsync() : scope.Ids.ToList();
        if (perimeters.Count == 0) return Ok(empty);
        var answer = await features.PostAsync<ContainerDraftSuggestions>(FeatureCatalog.AssistedTriage, "/triage/suggest",
            new { draft.Title, draft.Description, draft.ServiceId, draft.ConfigurationItemIds, draft.Category, draft.Impact, draft.OwnerType, draft.OwnerId,
                  Perimeters = perimeters }, HttpContext.RequestAborted);
        if (answer is null) return Ok(empty);

        // Sources citées et incidents proches : seulement ce que l'agent voit (TRI-11, PER-05).
        var wanted = (answer.Suggestions ?? []).SelectMany(s => s.Cited ?? []).Concat((answer.Related ?? []).Select(id => new CitedRef("incident", id)))
            .Distinct().ToList();
        var seen = new Dictionary<(string, int), LinkTarget>();
        foreach (var group in wanted.GroupBy(c => c.Type))
        {
            var kind = ItemLinks.ByType(group.Key);
            if (kind is null) continue;
            var ids = group.Select(c => c.Id).ToList();
            foreach (var t in await kind.Query(db).Where(t => ids.Contains(t.Id)).ToListAsync())
                if (scope.Sees(t)) seen[(group.Key, t.Id)] = t;
        }
        var services = await db.Services.AsNoTracking().Select(s => new { s.Id, s.Title }).ToDictionaryAsync(s => s.Id, s => s.Title);
        return Ok(new
        {
            Suggestions = (answer.Suggestions ?? []).Select(s => new
            {
                s.Field, s.Value,
                // Libellé de la valeur : le nom du service plutôt que son identifiant.
                Label = s.Field == "serviceId" && int.TryParse(s.Value, out var sid) ? services.GetValueOrDefault(sid, s.Value) : s.Value,
                s.Reasons,
                Cited = (s.Cited ?? []).Where(c => seen.ContainsKey((c.Type, c.Id)))
                    .Select(c => new CitedView(c.Type, c.Id, seen[(c.Type, c.Id)].Reference, seen[(c.Type, c.Id)].Title)).ToList(),
            }).ToList(),
            Related = (answer.Related ?? []).Where(id => seen.ContainsKey(("incident", id)))
                .Select(id => new { Id = id, seen[("incident", id)].Reference, seen[("incident", id)].Title, seen[("incident", id)].Status }).ToList(),
        });
    }

    /// <summary>Champs mesurés, dans l'ordre de la page, et leur libellé.</summary>
    private static readonly (string Field, string Label)[] Measured =
        [("category", "Catégorie"), ("serviceId", "Service"), ("owner", "Groupe responsable"), ("impact", "Impact")];

    /// <summary>
    /// TRI-10 : pour chaque champ, sur 30 et 90 jours, la part des suggestions acceptées, corrigées (autre valeur
    /// retenue), rejetées et laissées (expirées, ou sans décision quand l'incident a quitté « Nouveau ») ; celles d'un
    /// incident encore Nouveau attendent leur décision et ne comptent pas encore. Les suggestions appliquées à la
    /// saisie comptent acceptées (TRI-09).
    /// </summary>
    [HttpGet("~/api/admin/triage/metrics")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Metrics()
    {
        var now = DateTime.UtcNow;
        var rows = await db.Proposals.AsNoTracking()
            .Where(p => p.Feature == FeatureCatalog.AssistedTriage && p.TargetType == "incident" && p.CreatedAt >= now.AddDays(-90))
            .Select(p => new { p.Field, p.Status, p.CreatedAt, p.TargetId }).ToListAsync();
        var open = rows.Where(r => r.Status == Proposal.Open).Select(r => r.TargetId).Distinct().ToList();
        var stillNew = (await db.Incidents.AsNoTracking().Where(i => open.Contains(i.Id) && i.Status == "Nouveau").Select(i => i.Id).ToListAsync()).ToHashSet();
        static int Share(int n, int total) => total == 0 ? 0 : (int)Math.Round(n * 100.0 / total);
        object Window(int days)
        {
            var from = now.AddDays(-days);
            return new
            {
                Days = days,
                Fields = Measured.Select(m =>
                {
                    var mine = rows.Where(r => r.Field == m.Field && r.CreatedAt >= from).ToList();
                    int accepted = mine.Count(r => r.Status == Proposal.Accepted), corrected = mine.Count(r => r.Status == Proposal.Corrected),
                        rejected = mine.Count(r => r.Status == Proposal.Rejected),
                        left = mine.Count(r => r.Status == Proposal.Expired || (r.Status == Proposal.Open && !stillNew.Contains(r.TargetId))),
                        pending = mine.Count(r => r.Status == Proposal.Open && stillNew.Contains(r.TargetId));
                    var decided = accepted + corrected + rejected + left;
                    return new
                    {
                        m.Field, m.Label, Decided = decided, Accepted = accepted, Corrected = corrected, Rejected = rejected, Left = left, Pending = pending,
                        AcceptedShare = Share(accepted, decided), CorrectedShare = Share(corrected, decided),
                        RejectedShare = Share(rejected, decided), LeftShare = Share(left, decided),
                    };
                }).ToList(),
            };
        }
        return Ok(new { Windows = new[] { Window(30), Window(90) } });
    }
}
