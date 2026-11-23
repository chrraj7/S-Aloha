using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Data;
using SAloha.Api.Core.Features;
using SAloha.Api.Core.Search;

namespace SAloha.Api.Modules.IncidentManagement;

/// <summary>Brouillon ou incident dont le conteneur cherche les voisins, et périmètres où chercher (OPT-13).</summary>
public record InternalSimilarInput(string? Title, string? Description, int[]? Perimeters, int? ExcludeId, int Limit = 10);

/// <summary>Voisin résolu ou clos : son classement et la vue publique de l'incident (ADR-0016).</summary>
public record InternalNeighbour(double Score, bool Lexical, bool Semantic, IncidentView Incident);

/// <summary>
/// OPT-13 : lectures du tri assisté des tickets sur le port interne, dans les vues de l'API publique.
/// Un incident traité en tâche de fond se lit en entier, avec ses CI reliés ; ses voisins se cherchent
/// dans les périmètres que le conteneur donne : ceux de l'agent à la saisie, celui de l'incident en
/// tâche de fond (TRI-11).
/// </summary>
[ApiController]
[Route("internal")]
[Authorize(Policy = ContainerTokenHandler.Policy)]
[InternalFeature(FeatureCatalog.AssistedTriage)]
public class InternalIncidentsController(AppDbContext db, HybridSearch search) : ControllerBase
{
    public const int MaxLimit = 20, MaxText = 2000;

    /// <summary>L'incident et les identifiants des CI qui lui sont reliés, dans un sens ou dans l'autre (TRI-04).</summary>
    [HttpGet("incidents/{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var e = await db.Incidents.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id);
        if (e is null) return NotFound();
        var ciIds = await db.ItemLinks.AsNoTracking()
            .Where(l => (l.FromType == "incident" && l.FromId == id && l.ToType == "ci") || (l.ToType == "incident" && l.ToId == id && l.FromType == "ci"))
            .Select(l => l.FromType == "ci" ? l.FromId : l.ToId).Distinct().OrderBy(x => x).ToListAsync();
        return Ok(new { Incident = IncidentView.From(e), ConfigurationItemIds = ciIds });
    }

    /// <summary>
    /// TRI-03 : les incidents résolus ou clos les plus proches d'un texte. Même recherche que les cas
    /// similaires (RAG-06 : le sens sur le titre et la description, les mots sur le titre ; plancher de
    /// RAG-10). L'index ne contient que du contenu validé (RAG-01) : aucun incident ouvert n'en sort.
    /// </summary>
    [HttpPost("search/similar")]
    public async Task<IActionResult> Similar(InternalSimilarInput input)
    {
        if (input.Perimeters is null) return BadRequest(new { message = "Périmètres requis : ceux de l'agent, ou celui de l'incident traité." });
        var title = input.Title?.Trim() ?? "";
        if (title.Length == 0) return BadRequest(new { message = "Titre requis." });
        var description = input.Description?.Trim() ?? "";
        var query = title + "\n" + (description.Length > MaxText ? description[..MaxText] : description);
        var perimeters = input.Perimeters.Distinct().ToArray();
        var hits = await search.SearchAsync(query, ["incident"], perimeters,
            input.ExcludeId is { } exclude ? ("incident", exclude) : null, Math.Clamp(input.Limit, 1, MaxLimit), lexicalQuery: title);
        var ids = hits.Select(h => h.Id).ToList();
        var incidents = await db.Incidents.AsNoTracking()
            .Where(i => ids.Contains(i.Id) && Incident.Done.Contains(i.Status) && perimeters.Contains(i.PerimeterId))
            .ToDictionaryAsync(i => i.Id);
        return Ok(new
        {
            // Faux : voisins trouvés par les mots seuls (service d'embeddings absent ou en panne, RAG-02).
            Semantic = search.SemanticAvailable,
            Neighbours = hits.Where(h => incidents.ContainsKey(h.Id))
                .Select(h => new InternalNeighbour(h.Score, h.Lexical, h.Semantic, IncidentView.From(incidents[h.Id]))).ToList(),
        });
    }
}
