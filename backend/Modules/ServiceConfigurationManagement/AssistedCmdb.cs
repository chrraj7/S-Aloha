using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Data;
using SAloha.Api.Core.Features;
using SAloha.Api.Core.Perimeters;

namespace SAloha.Api.Modules.ServiceConfigurationManagement;

/// <summary>
/// CMA-01 : une analyse de la CMDB assistée, lancée par un gestionnaire sur tout un périmètre ou sur une sélection de CI,
/// avec ses tâches (CMA-02). Une seule à la fois par périmètre (index unique filtré sur les analyses actives) ; En file,
/// puis En cours, Terminée ou En échec, motif gardé (OPT-28). Le conteneur <c>features</c> la reçoit par le fil
/// d'événements et en rend compte par le port interne.
/// </summary>
public class AssistedCmdbAnalysis : IHasPerimeter
{
    public const string Queued = "En file", Running = "En cours", Done = "Terminée", Failed = "En échec";
    public static readonly string[] Statuses = [Queued, Running, Done, Failed];
    public static readonly string[] ActiveStatuses = [Queued, Running];

    public int Id { get; set; }
    public int PerimeterId { get; set; }
    /// <summary>Tâches choisies (clés de <see cref="AssistedCmdbTasks"/>), séparées par des virgules.</summary>
    [MaxLength(100)] public string Tasks { get; set; } = "";
    /// <summary>CI choisis, séparés par des virgules ; null : tout le périmètre (hors CI retirés).</summary>
    public string? ItemIds { get; set; }
    [MaxLength(20)] public string Status { get; set; } = Queued;
    [MaxLength(500)] public string? Reason { get; set; }
    /// <summary>CI analysés : la sélection, ou les CI du périmètre au lancement.</summary>
    public int ItemCount { get; set; }
    public int ProposalCount { get; set; }
    /// <summary>CMA-09 : propositions écartées (non conformes).</summary>
    public int RejectedCount { get; set; }
    [MaxLength(200)] public string RequestedBy { get; set; } = "";
    [MaxLength(200)] public string RequestedByDisplayName { get; set; } = "";
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    /// <summary>Dernier compte rendu du conteneur ; sans nouvelles depuis <c>AssistedCmdb:StaleMinutes</c>, l'analyse est interrompue.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<string> TaskList => Tasks.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
    public List<int>? ItemList => ItemIds?.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
}

/// <summary>CMA-02 : les tâches d'une analyse, clé et libellé.</summary>
public static class AssistedCmdbTasks
{
    public const string Rename = "rename", Normalize = "normalize", Reclassify = "reclassify", Merge = "merge", Relate = "relate",
        Criticality = "criticality";

    public static readonly Dictionary<string, string> Labels = new()
    {
        [Rename] = "Renommer", [Normalize] = "Normaliser", [Reclassify] = "Reclasser", [Merge] = "Fusionner", [Relate] = "Relier",
        [Criticality] = "Criticité",
    };
}

/// <summary>Lancement : périmètre, tâches, et CI choisis (absents : tout le périmètre).</summary>
public record AnalysisInput(int? PerimeterId, string[]? Tasks, int[]? ConfigurationItemIds);

public record AssistedCmdbAnalysisView(int Id, int PerimeterId, string PerimeterName, List<string> Tasks, bool WholePerimeter, int ItemCount,
    string Status, string? Reason, int ProposalCount, int RejectedCount, string RequestedBy, DateTime RequestedAt, DateTime? StartedAt,
    DateTime? FinishedAt)
{
    public static AssistedCmdbAnalysisView From(AssistedCmdbAnalysis a, string perimeter) => new(a.Id, a.PerimeterId, perimeter, a.TaskList,
        a.ItemIds is null, a.ItemCount, a.Status, a.Reason, a.ProposalCount, a.RejectedCount, a.RequestedByDisplayName, a.RequestedAt, a.StartedAt,
        a.FinishedAt);
}

/// <summary>
/// Configuration › CMDB assistée : lancer et suivre les analyses (CMA-01), gestionnaires du périmètre. La CMDB assistée
/// coupée, les routes n'existent pas (OPT-04).
/// </summary>
[ApiController]
[Route("api/assisted-cmdb/analyses")]
[Authorize(Policy = "User")]
[RequiresFeature(FeatureCatalog.AssistedCmdb)]
public class AssistedCmdbAnalysesController(AppDbContext db, PerimeterAccess access, IConfiguration config) : ControllerBase
{
    private int MaxItems => Math.Max(1, config.GetValue("AssistedCmdb:MaxItems", 500));
    private int StaleMinutes => Math.Max(1, config.GetValue("AssistedCmdb:StaleMinutes", 30));

    private async Task<Dictionary<int, string>> PerimeterNamesAsync() =>
        await db.Perimeters.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name);

    [HttpPost]
    public async Task<IActionResult> Launch(AnalysisInput input)
    {
        var (perimeter, error, forbidden) = await access.ChooseAsync(input.PerimeterId);
        if (forbidden) return Forbid();
        if (error is not null) return BadRequest(new { message = error });
        var scope = await access.GetAsync();
        if (!scope.ManagerOn(perimeter!.Value)) return Forbid();

        var tasks = (input.Tasks ?? []).Select(t => t?.Trim() ?? "").Distinct().ToList();
        if (tasks.Count == 0) return BadRequest(new { message = "Choisir au moins une tâche." });
        if (tasks.FirstOrDefault(t => !AssistedCmdbTasks.Labels.ContainsKey(t)) is { } unknown)
            return BadRequest(new { message = $"Tâche inconnue : « {unknown} ». Valeurs : {string.Join(", ", AssistedCmdbTasks.Labels.Keys)}." });

        int count;
        string? itemIds = null;
        if (input.ConfigurationItemIds is { } chosen)
        {
            var ids = chosen.Distinct().ToList();
            if (ids.Count == 0) return BadRequest(new { message = "Choisir au moins un CI, ou tout le périmètre." });
            if (ids.Count > MaxItems) return BadRequest(new { message = $"{MaxItems} CI au plus par analyse." });
            var found = await db.ConfigurationItems.AsNoTracking().Where(c => ids.Contains(c.Id) && c.PerimeterId == perimeter)
                .Select(c => c.Id).ToListAsync();
            if (found.Count != ids.Count) return BadRequest(new { message = "CI introuvables ou hors du périmètre de l'analyse." });
            (count, itemIds) = (ids.Count, string.Join(',', ids.Order()));
        }
        else
        {
            count = await db.ConfigurationItems.CountAsync(c => c.PerimeterId == perimeter && c.Status != "Retiré");
            if (count == 0) return BadRequest(new { message = "Aucun CI à analyser dans ce périmètre." });
            if (count > MaxItems)
                return BadRequest(new { message = $"Le périmètre compte {count} CI : en choisir {MaxItems} au plus." });
        }

        // Sans compte rendu du conteneur depuis trop longtemps, une analyse active est interrompue et ne bloque plus.
        var stale = DateTime.UtcNow.AddMinutes(-StaleMinutes);
        foreach (var old in await db.AssistedCmdbAnalyses.Where(a => a.PerimeterId == perimeter
                     && AssistedCmdbAnalysis.ActiveStatuses.Contains(a.Status) && a.UpdatedAt < stale).ToListAsync())
            (old.Status, old.Reason, old.FinishedAt, old.UpdatedAt) =
                (AssistedCmdbAnalysis.Failed, $"Interrompue : aucun compte rendu du conteneur depuis {StaleMinutes} minutes.", DateTime.UtcNow, DateTime.UtcNow);
        await db.SaveChangesAsync();
        if (await db.AssistedCmdbAnalyses.AsNoTracking().FirstOrDefaultAsync(a => a.PerimeterId == perimeter
                && AssistedCmdbAnalysis.ActiveStatuses.Contains(a.Status)) is { } running)
            return Conflict(new { message = $"Une analyse est déjà {running.Status.ToLowerInvariant()} sur ce périmètre (n° {running.Id})." });

        var analysis = new AssistedCmdbAnalysis
        {
            PerimeterId = perimeter.Value, Tasks = string.Join(',', tasks), ItemIds = itemIds, ItemCount = count,
            RequestedBy = User.Identity?.Name ?? "", RequestedByDisplayName = User.FindFirst("displayName")?.Value ?? User.Identity?.Name ?? "",
        };
        db.AssistedCmdbAnalyses.Add(analysis);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Deux lancements simultanés : l'index unique des analyses actives n'en garde qu'un.
            return Conflict(new { message = "Une analyse vient d'être lancée sur ce périmètre." });
        }
        var names = await PerimeterNamesAsync();
        return Accepted(AssistedCmdbAnalysisView.From(analysis, names.GetValueOrDefault(analysis.PerimeterId, "")));
    }

    /// <summary>Analyses des périmètres dont l'utilisateur est gestionnaire, les plus récentes d'abord (50).</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? perimeter)
    {
        var scope = await access.GetAsync();
        if (perimeter is { } p && !scope.ManagerOn(p)) return Forbid();
        var query = db.AssistedCmdbAnalyses.AsNoTracking();
        if (perimeter is not null) query = query.Where(a => a.PerimeterId == perimeter);
        else if (!scope.Global)
        {
            var managed = scope.ManagedIds.ToList();
            query = query.Where(a => managed.Contains(a.PerimeterId));
        }
        var items = await query.OrderByDescending(a => a.Id).Take(50).ToListAsync();
        var names = await PerimeterNamesAsync();
        return Ok(items.Select(a => AssistedCmdbAnalysisView.From(a, names.GetValueOrDefault(a.PerimeterId, ""))));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var a = await db.AssistedCmdbAnalyses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (a is null || !(await access.GetAsync()).ManagerOn(a.PerimeterId)) return NotFound();
        var names = await PerimeterNamesAsync();
        return Ok(AssistedCmdbAnalysisView.From(a, names.GetValueOrDefault(a.PerimeterId, "")));
    }
}
