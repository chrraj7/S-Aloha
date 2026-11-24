using System.ComponentModel.DataAnnotations;
using System.Globalization;
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

/// <summary>
/// CMA-12 : réglages de la CMDB assistée, par l'Admin (OPT-02) : tâches proposées aux gestionnaires, CI au plus par
/// analyse (de 20 à 500 ; <c>AssistedCmdb:MaxItems</c> reste le plafond de l'exploitation), paires au plus par requête de
/// Fusionner (de 10 à 50, CMA-06), aide au sas (CMA-08). Une tâche retirée n'est plus lancée, ni traitée par le conteneur.
/// </summary>
public record AssistedCmdbSettings : IFeatureSettings
{
    public const int MinItems = 20, MaxItemsCeiling = 500, MinPairs = 10, MaxPairs = 50;

    public string[] Tasks { get; init; } = [.. AssistedCmdbTasks.Labels.Keys];
    public int MaxItems { get; init; } = MaxItemsCeiling;
    public int PairsPerRequest { get; init; } = MaxPairs;
    public bool StagingHelp { get; init; } = true;

    public string? Error()
    {
        if (Tasks is null) return "Tâches proposées : une liste est attendue.";
        if (Tasks.FirstOrDefault(t => !AssistedCmdbTasks.Labels.ContainsKey(t ?? "")) is { } unknown)
            return $"Tâche inconnue : « {unknown} ». Valeurs : {string.Join(", ", AssistedCmdbTasks.Labels.Keys)}.";
        if (MaxItems is < MinItems or > MaxItemsCeiling) return $"CI au plus par analyse : de {MinItems} à {MaxItemsCeiling}.";
        if (PairsPerRequest is < MinPairs or > MaxPairs) return $"Paires au plus par requête : de {MinPairs} à {MaxPairs}.";
        return null;
    }

    /// <summary>Les tâches sans doublon, dans l'ordre de la liste (CMA-02).</summary>
    public AssistedCmdbSettings Normalized() => this with { Tasks = [.. AssistedCmdbTasks.Labels.Keys.Where(Tasks.Contains)] };

    public IEnumerable<(string Label, string Value)> Traced()
    {
        var tasks = Normalized().Tasks;
        yield return ("Tâches proposées", tasks.Length == 0 ? "aucune" : string.Join(", ", tasks.Select(t => AssistedCmdbTasks.Labels[t])));
        yield return ("CI au plus par analyse", MaxItems.ToString(CultureInfo.InvariantCulture));
        yield return ("Paires au plus par requête", PairsPerRequest.ToString(CultureInfo.InvariantCulture));
        yield return ("Aide au sas", StagingHelp ? "oui" : "non");
    }

    public static AssistedCmdbSettings Of(OptionalFeature? row) => FeatureSettings.Read(row, () => new AssistedCmdbSettings());

    public static async Task<AssistedCmdbSettings> ReadAsync(AppDbContext db, CancellationToken ct = default) =>
        Of(await FeatureSettings.RowAsync(db, FeatureCatalog.AssistedCmdb, ct));
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
    /// <summary>CMA-01, CMA-12 : le réglage de l'Admin, sous le plafond de l'exploitation.</summary>
    private int MaxItems(AssistedCmdbSettings settings) => Math.Max(1, Math.Min(config.GetValue("AssistedCmdb:MaxItems", 500), settings.MaxItems));
    private int StaleMinutes => Math.Max(1, config.GetValue("AssistedCmdb:StaleMinutes", 30));

    private async Task<Dictionary<int, string>> PerimeterNamesAsync() =>
        await db.Perimeters.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name);

    /// <summary>CMA-12 : ce que l'Admin a réglé et dont l'interface a besoin : tâches proposées, CI au plus, aide au sas.</summary>
    [HttpGet("~/api/assisted-cmdb/settings")]
    public async Task<IActionResult> Settings()
    {
        var settings = await AssistedCmdbSettings.ReadAsync(db, HttpContext.RequestAborted);
        return Ok(new { settings.Normalized().Tasks, MaxItems = MaxItems(settings), settings.StagingHelp });
    }

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
        // CMA-12 : une tâche que l'Admin ne propose pas ne se lance pas.
        var settings = await AssistedCmdbSettings.ReadAsync(db);
        if (tasks.FirstOrDefault(t => !settings.Tasks.Contains(t)) is { } cut)
            return BadRequest(new { message = $"Tâche non proposée par l'administrateur : « {AssistedCmdbTasks.Labels[cut]} »." });
        var maxItems = MaxItems(settings);

        int count;
        string? itemIds = null;
        if (input.ConfigurationItemIds is { } chosen)
        {
            var ids = chosen.Distinct().ToList();
            if (ids.Count == 0) return BadRequest(new { message = "Choisir au moins un CI, ou tout le périmètre." });
            if (ids.Count > maxItems) return BadRequest(new { message = $"{maxItems} CI au plus par analyse." });
            var found = await db.ConfigurationItems.AsNoTracking().Where(c => ids.Contains(c.Id) && c.PerimeterId == perimeter)
                .Select(c => c.Id).ToListAsync();
            if (found.Count != ids.Count) return BadRequest(new { message = "CI introuvables ou hors du périmètre de l'analyse." });
            (count, itemIds) = (ids.Count, string.Join(',', ids.Order()));
        }
        else
        {
            count = await db.ConfigurationItems.CountAsync(c => c.PerimeterId == perimeter && c.Status != "Retiré");
            if (count == 0) return BadRequest(new { message = "Aucun CI à analyser dans ce périmètre." });
            if (count > maxItems)
                return BadRequest(new { message = $"Le périmètre compte {count} CI : en choisir {maxItems} au plus." });
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

/// <summary>Compte rendu du conteneur : nouvel état, motif d'un échec, propositions faites et écartées jusque-là.</summary>
public record AnalysisReport(string? Status, string? Reason, int? ProposalCount, int? RejectedCount);

/// <summary>
/// CMA-01 : le conteneur lit l'analyse annoncée par le fil (<c>analysis.requested</c>) et en rend compte : En cours au
/// démarrage puis après chaque lot (le dernier compte rendu la garde vivante), Terminée ou En échec à la fin. Une
/// analyse finie ne change plus (409) ; un retour en arrière non plus.
/// </summary>
[ApiController]
[Route("internal/assisted-cmdb/analyses")]
[Authorize(Policy = ContainerTokenHandler.Policy)]
[InternalFeature(FeatureCatalog.AssistedCmdb)]
public class InternalAssistedCmdbAnalysesController(AppDbContext db) : ControllerBase
{
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var a = await db.AssistedCmdbAnalyses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return NotFound();
        return Ok(new { a.Id, a.PerimeterId, Tasks = a.TaskList, ConfigurationItemIds = a.ItemList, a.Status, a.ItemCount });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Report(int id, AnalysisReport report)
    {
        var a = await db.AssistedCmdbAnalyses.FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return NotFound();
        var next = report.Status ?? a.Status;
        if (!AssistedCmdbAnalysis.Statuses.Contains(next))
            return BadRequest(new { message = $"Statut inconnu : « {next} ». Valeurs : {string.Join(", ", AssistedCmdbAnalysis.Statuses)}." });
        var allowed = a.Status switch
        {
            AssistedCmdbAnalysis.Queued => next is AssistedCmdbAnalysis.Running or AssistedCmdbAnalysis.Failed,
            AssistedCmdbAnalysis.Running => next is AssistedCmdbAnalysis.Running or AssistedCmdbAnalysis.Done or AssistedCmdbAnalysis.Failed,
            _ => false,
        };
        if (!allowed) return Conflict(new { message = $"Analyse {a.Status.ToLowerInvariant()} : passage à « {next} » refusé." });
        if (next == AssistedCmdbAnalysis.Failed && string.IsNullOrWhiteSpace(report.Reason))
            return BadRequest(new { message = "Une analyse en échec porte son motif (OPT-28)." });
        if ((report.Reason?.Length ?? 0) > 500 || report.ProposalCount < 0 || report.RejectedCount < 0)
            return BadRequest(new { message = "Motif de 500 caractères au plus ; comptes positifs." });

        var now = DateTime.UtcNow;
        if (a.Status == AssistedCmdbAnalysis.Queued && next != AssistedCmdbAnalysis.Queued) a.StartedAt ??= now;
        if (next is AssistedCmdbAnalysis.Done or AssistedCmdbAnalysis.Failed) a.FinishedAt = now;
        (a.Status, a.UpdatedAt) = (next, now);
        if (report.Reason is not null) a.Reason = report.Reason.Trim();
        if (report.ProposalCount is { } proposals) a.ProposalCount = proposals;
        if (report.RejectedCount is { } rejected) a.RejectedCount = rejected;
        await db.SaveChangesAsync();
        return Ok(new { a.Id, a.Status });
    }
}

/// <summary>
/// CMA-04 : convention de nommage d'un type de CI, tenue par les gestionnaires dans Paramétrage : une description et
/// des exemples, transmis au modèle. Sans convention pour un type, la tâche Renommer ne s'y applique pas.
/// </summary>
public class NamingConvention
{
    public const int MaxExamples = 5, MaxExampleLength = 100, MaxDescriptionLength = 500;

    public int Id { get; set; }
    [MaxLength(30)] public string CiType { get; set; } = "";
    [MaxLength(500)] public string Description { get; set; } = "";
    /// <summary>Exemples, un par ligne.</summary>
    [MaxLength(600)] public string Examples { get; set; } = "";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(200)] public string UpdatedBy { get; set; } = "";

    public List<string> ExampleList => Examples.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
}

/// <summary>Saisie d'une convention ; une description vide la retire.</summary>
public record NamingInput(string? Description, string[]? Examples);

public record NamingConventionView(string CiType, string Description, List<string> Examples, DateTime UpdatedAt, string UpdatedBy)
{
    public static NamingConventionView From(NamingConvention n) => new(n.CiType, n.Description, n.ExampleList, n.UpdatedAt, n.UpdatedBy);
}

/// <summary>
/// Paramétrage › Convention de nommage (CMA-04) : lecture pour tous, écriture par un gestionnaire d'au moins un
/// périmètre (PER-13, comme les autres référentiels sans périmètre). La CMDB assistée coupée, les routes n'existent pas.
/// </summary>
[ApiController]
[Route("api/assisted-cmdb/naming")]
[Authorize(Policy = "User")]
[RequiresFeature(FeatureCatalog.AssistedCmdb)]
public class AssistedCmdbNamingController(AppDbContext db) : ControllerBase
{
    internal static async Task<List<NamingConvention>> AllAsync(AppDbContext db) =>
        (await db.NamingConventions.AsNoTracking().ToListAsync()).OrderBy(n => Array.IndexOf(ConfigurationItem.Types, n.CiType)).ToList();

    [HttpGet]
    public async Task<IActionResult> List() => Ok((await AllAsync(db)).Select(NamingConventionView.From));

    [HttpPut("{ciType}")]
    [Authorize(Policy = "Manager")]
    public async Task<IActionResult> Put(string ciType, NamingInput input)
    {
        if (!ConfigurationItem.Types.Contains(ciType))
            return BadRequest(new { message = $"Type de CI inconnu : « {ciType} ». Valeurs : {string.Join(", ", ConfigurationItem.Types)}." });
        var description = input.Description?.Trim() ?? "";
        var examples = (input.Examples ?? []).Select(e => e?.Trim() ?? "").Where(e => e != "").Distinct().ToList();
        if (description.Length > NamingConvention.MaxDescriptionLength)
            return BadRequest(new { message = $"Description : {NamingConvention.MaxDescriptionLength} caractères au plus." });
        if (examples.Count > NamingConvention.MaxExamples || examples.Any(e => e.Length > NamingConvention.MaxExampleLength || e.Contains('\n')))
            return BadRequest(new { message = $"{NamingConvention.MaxExamples} exemples au plus, d'une ligne de {NamingConvention.MaxExampleLength} caractères au plus." });

        var n = await db.NamingConventions.FirstOrDefaultAsync(x => x.CiType == ciType);
        if (description == "")
        {
            // Sans description, plus de convention : Renommer ne s'applique plus à ce type.
            if (n is not null) db.NamingConventions.Remove(n);
            await db.SaveChangesAsync();
            return NoContent();
        }
        if (n is null) db.NamingConventions.Add(n = new NamingConvention { CiType = ciType });
        (n.Description, n.Examples, n.UpdatedAt, n.UpdatedBy) =
            (description, string.Join('\n', examples), DateTime.UtcNow, User.FindFirst("displayName")?.Value ?? User.Identity?.Name ?? "");
        await db.SaveChangesAsync();
        return Ok(NamingConventionView.From(n));
    }
}

/// <summary>CMA-04 : le conteneur lit les conventions avant une analyse qui renomme.</summary>
[ApiController]
[Route("internal/assisted-cmdb/naming")]
[Authorize(Policy = ContainerTokenHandler.Policy)]
[InternalFeature(FeatureCatalog.AssistedCmdb)]
public class InternalAssistedCmdbNamingController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List() =>
        Ok((await AssistedCmdbNamingController.AllAsync(db)).Select(n => new { n.CiType, n.Description, Examples = n.ExampleList }));
}

/// <summary>Un CI cité par une proposition : le second d'une fusion (CMA-05), la cible d'une relation (CMA-02).</summary>
public record CmdbCiRef(int Id, string Reference, string Title);

/// <summary>
/// Une proposition ouverte de la file : son CI, sa tâche, l'avant et l'après, son motif ; périmée si le CI a changé depuis.
/// Une fusion désigne en plus l'autre CI (<c>other</c>), que la proposition suggère de conserver, une relation son CI cible ;
/// elles sont périmées si l'un des deux a changé.
/// </summary>
public record CmdbProposalView(int Id, int CiId, string CiReference, string CiTitle, int PerimeterId, string Task, string Field, string? Before,
    string After, string Reasons, DateTime CreatedAt, bool Stale, CmdbCiRef? Other = null);

/// <summary>
/// Configuration › CMDB assistée, file des propositions (CMA-03) : les propositions ouvertes de la CMDB assistée sur les CI
/// des périmètres dont l'utilisateur est gestionnaire, filtrées par périmètre et par tâche, les plus anciennes d'abord
/// (200). La décision passe par les routes communes des propositions (OPT-15), toutes les règles des CI comprises.
/// </summary>
[ApiController]
[Route("api/assisted-cmdb/proposals")]
[Authorize(Policy = "User")]
[RequiresFeature(FeatureCatalog.AssistedCmdb)]
public class AssistedCmdbProposalsController(AppDbContext db, PerimeterAccess access) : ControllerBase
{
    /// <summary>Tâche d'une proposition, d'après son champ (CMA-02).</summary>
    public static readonly Dictionary<string, string> TaskOfField = new()
    {
        ["title"] = AssistedCmdbTasks.Rename, ["operatingSystem"] = AssistedCmdbTasks.Normalize, ["vendor"] = AssistedCmdbTasks.Normalize,
        ["model"] = AssistedCmdbTasks.Normalize, ["ciType"] = AssistedCmdbTasks.Reclassify, ["criticality"] = AssistedCmdbTasks.Criticality,
        [ProposalFields.Merge] = AssistedCmdbTasks.Merge, [ProposalFields.Relation] = AssistedCmdbTasks.Relate,
    };

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? perimeter, [FromQuery] string? task)
    {
        var scope = await access.GetAsync();
        if (perimeter is { } p && !scope.ManagerOn(p)) return Forbid();
        if (task is not null && !AssistedCmdbTasks.Labels.ContainsKey(task))
            return BadRequest(new { message = $"Tâche inconnue : « {task} ». Valeurs : {string.Join(", ", AssistedCmdbTasks.Labels.Keys)}." });
        var query = db.Proposals.AsNoTracking().Where(x => x.Feature == FeatureCatalog.AssistedCmdb && x.TargetType == "ci" && x.Status == Proposal.Open);
        if (perimeter is not null) query = query.Where(x => x.PerimeterId == perimeter);
        else if (!scope.Global)
        {
            var managed = scope.ManagedIds.ToList();
            query = query.Where(x => managed.Contains(x.PerimeterId));
        }
        if (task is not null)
        {
            var fields = TaskOfField.Where(f => f.Value == task).Select(f => f.Key).ToList();
            query = query.Where(x => fields.Contains(x.Field));
        }
        var proposals = await query.OrderBy(x => x.Id).Take(200).ToListAsync();
        int? OtherOf(Proposal x) => ProposalFields.Of("ci", x.Field)?.Other?.Invoke(x.After);
        var ids = proposals.Select(x => x.TargetId).Concat(proposals.Select(OtherOf).OfType<int>()).Distinct().ToList();
        var cis = await db.ConfigurationItems.AsNoTracking().Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.Reference, c.Title, c.UpdatedAt, c.PerimeterId }).ToDictionaryAsync(c => c.Id);
        // L'autre CI d'une fusion ou d'une relation n'est décrit que s'il est visible (PER-08) : sinon, refusée, elle attend son rejet.
        var other = (Proposal x) => OtherOf(x) is { } o && cis.TryGetValue(o, out var c) && scope.Sees(c.PerimeterId) ? c : null;
        return Ok(proposals.Where(x => cis.ContainsKey(x.TargetId)).Select(x => new CmdbProposalView(x.Id, x.TargetId, cis[x.TargetId].Reference,
            cis[x.TargetId].Title, x.PerimeterId, TaskOfField.GetValueOrDefault(x.Field, ""), x.Field, x.Before, x.After, x.Reasons, x.CreatedAt,
            cis[x.TargetId].UpdatedAt > x.CreatedAt || other(x)?.UpdatedAt > x.CreatedAt,
            other(x) is { } c ? new CmdbCiRef(c.Id, c.Reference, c.Title) : null)));
    }
}
