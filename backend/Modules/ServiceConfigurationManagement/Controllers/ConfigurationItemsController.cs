using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Auth;
using SAloha.Api.Core.Data;
using SAloha.Api.Core.Links;
using SAloha.Api.Core.Records;
using SAloha.Api.Core.Notifications;

namespace SAloha.Api.Modules.ServiceConfigurationManagement;

[Route("api/configuration-items")]
public class ConfigurationItemsController(AppDbContext db) : RecordController<ConfigurationItem, ConfigurationItemDto, ConfigurationItemView>(db)
{
    protected override ConfigurationItemView View(ConfigurationItem e) => ConfigurationItemView.From(e);
    protected override string Code => "CI";
    protected override string LinkType => "ci";
    protected override string[] Statuses => ConfigurationItem.Statuses;
    protected override IReadOnlyDictionary<string, string[]> Transitions => ConfigurationItem.Transitions;
    protected override bool StatusChosenAtCreation => true;
    protected override string InitialStatus(ConfigurationItem e) => "En service";

    // Champs propres au CI, portefeuille applicatif et actifs matériels : règles communes (CiRules).
    protected override string? Apply(ConfigurationItem e, ConfigurationItemDto dto, bool creating) =>
        CiRules.Apply(e, dto, string.IsNullOrEmpty(dto.Status) ? (creating ? "En service" : e.Status) : dto.Status);

    // CFG-22 : CI passé Hors service → propriétaire, et responsables des services touchés (CFG-21).
    protected override void OnNotify(ConfigurationItem e, string? from, Notifier n)
    {
        if (from is null || from == "" || e.Status != "Hors service") return;
        n.ToOwner(e, LinkType, "CI passé hors service");
        foreach (var s in _servicesTouched) n.ToPrincipal(e, LinkType, s.OwnerType, s.OwnerId, $"CI passé hors service : service {s.Reference} touché");
    }

    private List<ServiceLevelManagement.ItService> _servicesTouched = [];

    // Les services touchés se calculent avant l'envoi (OnNotify est synchrone).
    protected override async Task BeforeSaveAsync(ConfigurationItem e, bool creating)
    {
        if (creating || e.Status != "Hors service" || Db.Entry(e).Property(x => x.Status).OriginalValue == "Hors service") return;
        var graph = await ImpactGraphAsync();
        _servicesTouched = await ServicesOfAsync(Traverse(e.Id, graph.Down).Select(x => x.Id).Append(e.Id));
    }

    /// <summary>CFG-16 : attributs définis pour chaque type de CI.</summary>
    [HttpGet("attribute-types")]
    public IActionResult AttributeTypes() => Ok(CiAttributes.ByType);

    public const int MaxImportLines = 5000;
    private static readonly string[] ImportColumns = ["nom", "type", "environnement", "emplacement", "propriétaire", "statut"];

    private static string Key(string header) =>
        new string(header.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());

    /// <summary>
    /// CFG-30 : import en masse (CSV, séparateur « ; » ou « , », en-tête obligatoire : nom, type,
    /// environnement, emplacement, propriétaire, statut ; les quatre dernières facultatives). Chaque
    /// ligne passe les règles d'une création (types, statut, propriétaire, unicité, y compris entre
    /// lignes du fichier). Une seule ligne refusée et rien n'est créé : le rapport liste les refus.
    /// <c>dryRun</c> : contrôle seul.
    /// </summary>
    [HttpPost("import")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Import(CiImportDto dto, [FromQuery] bool dryRun = false)
    {
        await ScopeAsync();
        var (perimeterId, refused, forbidden) = await Access.ChooseAsync(dto.PerimeterId);
        if (forbidden) return Forbid();
        if (refused is not null) return BadRequest(new { message = refused });
        ActOn(perimeterId!.Value);

        var rows = ComplianceAssessment.Csv.Parse(dto.Csv ?? "");
        if (rows.Count < 2) return BadRequest(new { message = "Fichier vide : une ligne d'en-tête puis une ligne par CI." });
        if (rows.Count - 1 > MaxImportLines) return BadRequest(new { message = $"{MaxImportLines} lignes au plus par import." });
        var header = rows[0].Select(Key).ToList();
        var unknown = header.Where(h => h != "" && !ImportColumns.Select(Key).Contains(h)).ToList();
        if (!header.Contains("nom") || !header.Contains("type") || unknown.Count > 0)
            return BadRequest(new { message = $"En-tête attendu : {string.Join(";", ImportColumns)} (nom et type obligatoires)"
                + (unknown.Count > 0 ? $" ; colonnes inconnues : {string.Join(", ", unknown)}." : ".") });
        string Cell(List<string> row, string column) { var i = header.IndexOf(Key(column)); return i >= 0 && i < row.Count ? row[i].Trim() : ""; }

        var errors = new List<object>();
        var accepted = new List<ConfigurationItemDto>();
        var seen = new HashSet<string>();
        for (var n = 1; n < rows.Count; n++)
        {
            var row = rows[n];
            var owner = Cell(row, "propriétaire");
            var status = Cell(row, "statut");
            var line = new ConfigurationItemDto(Cell(row, "nom"), null, status == "" ? "Planifié" : status,
                owner == "" ? null : "User", owner == "" ? null : owner, owner == "" ? null : owner,
                Cell(row, "type"), Cell(row, "environnement") is var env && env != "" ? env : "Production", Cell(row, "emplacement"), perimeterId);
            var probe = new ConfigurationItem { PerimeterId = perimeterId.Value };
            var error = Fill(probe, line, true) ?? await ValidateAsync(probe)
                ?? Allowed.Check("Statut", line.Status, Statuses)
                ?? await CheckStatusAsync(probe, line.Status!) ?? await ConflictAsync(probe);
            // CFG-17 : un nom par environnement, aussi entre les lignes du fichier.
            if (error is null && !seen.Add($"{probe.Title.ToLowerInvariant()}|{probe.Environment}"))
                error = $"« {probe.Title} » figure deux fois pour l'environnement {probe.Environment} dans le fichier.";
            if (error is not null) errors.Add(new { Line = n + 1, Name = line.Title, Message = error });
            else accepted.Add(line);
        }
        if (errors.Count > 0)
            return BadRequest(new { message = $"{errors.Count} ligne(s) refusée(s) : rien n'a été importé.", Created = 0, Errors = errors });
        if (dryRun) return Ok(new { message = $"{accepted.Count} CI prêts à importer.", Created = 0, Ready = accepted.Count, Errors = errors });

        await using var tx = await Db.Database.BeginTransactionAsync();
        var created = new List<string>();
        foreach (var line in accepted)
        {
            var e = await References.CreateAsync(Db, Code, () =>
            {
                var x = new ConfigurationItem { PerimeterId = perimeterId.Value };
                Fill(x, line, true);
                x.Status = line.Status!;
                x.CreatedBy = Me; x.CreatedByDisplayName = MyDisplay;
                return x;
            });
            created.Add(e.Reference);
        }
        await tx.CommitAsync();
        return Ok(new { message = $"{created.Count} CI importés.", Created = created.Count, References = created, Errors = errors });
    }

    /// <summary>CFG-15 : le propriétaire (ou un gestionnaire) atteste que la fiche est exacte.</summary>
    [HttpPost("{id:int}/verify")]
    public async Task<IActionResult> Verify(int id)
    {
        var e = await Db.ConfigurationItems.FindAsync(id);
        if (e is null || !await VisibleAsync(e)) return NotFound();
        ActOn(e.PerimeterId);
        if (!IsManager && !User.Designates(e.OwnerType, e.OwnerId)) return Forbid();
        e.LastVerifiedAt = DateTime.UtcNow; e.LastVerifiedBy = MyDisplay;
        await Db.SaveChangesAsync();
        return Ok(View(e));
    }

    // CFG-12 : retirer un CI est réservé aux gestionnaires.
    protected override bool RequiresManager(string status) => status == "Retiré";

    // CFG-11, CFG-43, CFG-44, CFG-48 : conditions communes (CiRules).
    // CFG-13 : un CI relié à un incident ou changement ouvert ne peut pas être retiré.
    protected override async Task<string?> CheckStatusAsync(ConfigurationItem e, string status)
    {
        if (CiRules.CheckStatus(e, status) is { } error) return error;

        if (status == "Retiré")
        {
            var openIncidents = await (
                from link in Db.ItemLinks
                where (link.FromType == "ci" && link.FromId == e.Id && link.ToType == "incident")
                   || (link.ToType == "ci" && link.ToId == e.Id && link.FromType == "incident")
                let incId = link.FromType == "incident" ? link.FromId : link.ToId
                join inc in Db.Incidents on incId equals inc.Id
                where !IncidentManagement.Incident.Done.Contains(inc.Status)
                select inc.Reference
            ).ToListAsync();

            var openChanges = await (
                from link in Db.ItemLinks
                where (link.FromType == "ci" && link.FromId == e.Id && link.ToType == "change")
                   || (link.ToType == "ci" && link.ToId == e.Id && link.FromType == "change")
                let chgId = link.FromType == "change" ? link.FromId : link.ToId
                join chg in Db.Changes on chgId equals chg.Id
                where !ChangeEnablement.Change.Done.Contains(chg.Status)
                select chg.Reference
            ).ToListAsync();

            var openRefs = openIncidents.Concat(openChanges).ToList();
            if (openRefs.Count > 0)
                return $"Ce CI est relié à des enregistrements encore ouverts : {string.Join(", ", openRefs)}.";
        }

        return null;
    }

    // CFG-17, CFG-43, CFG-44 : nom par environnement, code applicatif et numéro de série uniques.
    protected override Task<string?> ConflictAsync(ConfigurationItem e) => CiRules.ConflictAsync(Db, e);

    // Recherche élargie aux identifiants : code applicatif, numéro de série, adresse IP.
    protected override IQueryable<ConfigurationItem> Search(IQueryable<ConfigurationItem> q, string key) =>
        q.Where(x => x.Title.ToLower().Contains(key) || x.Reference.ToLower().Contains(key)
                     || (x.AppCode != null && x.AppCode.ToLower().Contains(key))
                     || (x.SerialNumber != null && x.SerialNumber.ToLower().Contains(key))
                     || (x.IpAddress != null && x.IpAddress.Contains(key)));

    protected override IQueryable<ConfigurationItem> Filter(IQueryable<ConfigurationItem> q)
    {
        var type = Request.Query["type"].ToString();
        return string.IsNullOrEmpty(type) ? q : q.Where(c => c.CiType == type);
    }

    /// <summary>
    /// CFG-39 : rapport d'inventaire — portefeuille applicatif (gouvernance, criticité, cycle) et
    /// actifs matériels (identification, garantie, fin de support CFG-47), sur mes périmètres.
    /// </summary>
    [HttpGet("inventory")]
    public async Task<IActionResult> Inventory([FromQuery] bool retired = false)
    {
        var q = await InScopeAsync(Db.ConfigurationItems.AsNoTracking());
        if (!retired) q = q.Where(c => c.Status != "Retiré");
        var today = DateTime.UtcNow.Date;
        var rules = await Db.Set<EndOfLifeRule>().AsNoTracking().ToListAsync();
        var apps = await q.Where(c => c.CiType == "Application").OrderBy(c => c.AppCode ?? c.Title).ToListAsync();
        var hardwareTypes = ConfigurationItem.HardwareTypes;
        var hardware = await q.Where(c => hardwareTypes.Contains(c.CiType)).OrderBy(c => c.CiType).ThenBy(c => c.Title).ToListAsync();
        bool Governed(ConfigurationItem a) => a.AppCode != null && a.BusinessUnit != null && a.BusinessOwnerId != null
                                              && a.TechnicalOwnerId != null && a.Criticality != null;
        var assets = hardware.Select(c => new { Ci = c, Support = Obsolescence.Of(c, rules, today), Warranty = Obsolescence.Warranty(c, today) }).ToList();
        return Ok(new
        {
            Summary = new
            {
                Applications = apps.Count,
                Governed = apps.Count(Governed),
                Hardware = hardware.Count,
                SupportPassed = assets.Count(a => a.Support.State == Obsolescence.Passed),
                SupportNear = assets.Count(a => a.Support.State == Obsolescence.Near),
                WarrantyExpired = assets.Count(a => a.Warranty == Obsolescence.WarrantyExpired),
                MissingSerial = hardware.Count(c => ConfigurationItem.PhysicalTypes.Contains(c.CiType) && c.SerialNumber == null),
            },
            Applications = apps.Select(a => new
            {
                a.Id, a.Reference, a.Title, a.Status, a.Environment, a.AppCode, a.BusinessUnit, a.BusinessOwnerDisplayName,
                a.TechnicalOwnerDisplayName, a.OwnerDisplayName, a.Criticality, a.AppLifecycle, a.RtoHours, a.RpoHours,
                a.GdprSensitive, a.Nis2Scope, Governed = Governed(a)
            }),
            Hardware = assets.Select(x => new
            {
                x.Ci.Id, x.Ci.Reference, x.Ci.Title, x.Ci.CiType, x.Ci.Status, x.Ci.Environment, x.Ci.Vendor, x.Ci.Model,
                x.Ci.SerialNumber, x.Ci.Location, x.Ci.IpAddress, x.Ci.OperatingSystem, x.Ci.PurchaseDate, x.Ci.WarrantyExpiry,
                x.Warranty, SupportState = x.Support.State, SupportDate = x.Support.Date, SupportSource = x.Support.Source,
                x.Ci.OwnerDisplayName
            })
        });
    }

    protected override IQueryable<ConfigurationItem> WithDetails(IQueryable<ConfigurationItem> q) =>
        q.Include(c => c.Outgoing).ThenInclude(r => r.Target)
         .Include(c => c.Incoming).ThenInclude(r => r.Source);

    /// <summary>
    /// Vue d'impact transitive : en aval, ce qui est touché si ce CI tombe ;
    /// en amont, ce dont il dépend. Parcours en largeur sur tout le graphe des
    /// relations : chaque CI n'apparaît qu'une fois, à sa plus courte distance,
    /// ce qui arrête aussi le parcours en cas de cycle. S'y ajoutent les incidents et
    /// les changements ouverts reliés à ce CI, que l'utilisateur voit (MCP-05, <c>ci_impact</c>).
    /// </summary>
    [HttpGet("{id:int}/impact")]
    public async Task<IActionResult> Impact(int id)
    {
        if (!await VisibleAsync(id)) return NotFound();
        var graph = await ImpactGraphAsync();
        var downstream = Traverse(id, graph.Down);
        var upstream = Traverse(id, graph.Up);
        var describe = await DescriberAsync(downstream.Concat(upstream).SelectMany(n => new[] { n.Id, n.Via }));
        // CFG-21 : les services touchés si ce CI tombe (les siens et ceux des CI en aval).
        var services = await ServicesTouchedAsync(downstream.Select(n => n.Id).Append(id));
        var (incidents, changes) = await OpenLinkedAsync(id);
        return Ok(new { Downstream = downstream.Select(describe), Upstream = upstream.Select(describe), Services = services,
            OpenIncidents = incidents, OpenChanges = changes });
    }

    /// <summary>MCP-05 : incidents et changements ouverts reliés à ce CI, dans les périmètres de l'utilisateur (PER-08), les plus anciens d'abord.</summary>
    private async Task<(List<object> Incidents, List<object> Changes)> OpenLinkedAsync(int id)
    {
        var links = await Db.ItemLinks.AsNoTracking()
            .Where(l => (l.FromType == "ci" && l.FromId == id && (l.ToType == "incident" || l.ToType == "change"))
                     || (l.ToType == "ci" && l.ToId == id && (l.FromType == "incident" || l.FromType == "change")))
            .ToListAsync();
        var others = links.Select(l => l.FromType == "ci" && l.FromId == id ? (Type: l.ToType, Id: l.ToId) : (Type: l.FromType, Id: l.FromId)).ToList();
        var incidentIds = others.Where(o => o.Type == "incident").Select(o => o.Id).Distinct().ToList();
        var changeIds = others.Where(o => o.Type == "change").Select(o => o.Id).Distinct().ToList();
        var scope = await ScopeAsync();
        var incidents = await Db.Incidents.AsNoTracking()
            .Where(i => incidentIds.Contains(i.Id) && !IncidentManagement.Incident.Done.Contains(i.Status))
            .OrderBy(i => i.CreatedAt).ThenBy(i => i.Id)
            .Select(i => new { i.Id, i.Reference, i.Title, i.Status, i.Priority, i.PerimeterId, i.CreatedAt }).ToListAsync();
        var changes = await Db.Changes.AsNoTracking()
            .Where(c => changeIds.Contains(c.Id) && !ChangeEnablement.Change.Done.Contains(c.Status))
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
            .Select(c => new { c.Id, c.Reference, c.Title, c.Status, c.PerimeterId, c.CreatedAt }).ToListAsync();
        return (incidents.Where(i => scope.Sees(i.PerimeterId)).Select(i => (object)new { i.Id, i.Reference, i.Title, i.Status, i.Priority, i.CreatedAt }).ToList(),
            changes.Where(c => scope.Sees(c.PerimeterId)).Select(c => (object)new { c.Id, c.Reference, c.Title, c.Status, c.CreatedAt }).ToList());
    }

    /// <summary>
    /// CFG-20 : impact d'un enregistrement relié à des CI (un changement) — ses CI visibles,
    /// tout ce qui en dépend en aval (à la plus courte distance), et les services touchés.
    /// </summary>
    [HttpGet("impact-of")]
    public async Task<IActionResult> ImpactOf([FromQuery] string type, [FromQuery] int id)
    {
        var kind = ItemLinks.ByType(type);
        var target = kind is null ? null : await kind.Query(Db).FirstOrDefaultAsync(t => t.Id == id);
        var scope = await ScopeAsync();
        if (target is null || !scope.Sees(target)) return NotFound();
        var linked = await Db.ItemLinks.AsNoTracking()
            .Where(l => (l.FromType == type && l.FromId == id && l.ToType == "ci") || (l.ToType == type && l.ToId == id && l.FromType == "ci"))
            .Select(l => l.FromType == "ci" ? l.FromId : l.ToId).Distinct().ToListAsync();
        var cis = await Db.ConfigurationItems.AsNoTracking().Where(c => linked.Contains(c.Id)).ToListAsync();
        cis = cis.Where(c => scope.Sees(c.PerimeterId)).ToList();
        var graph = await ImpactGraphAsync();
        var downstream = cis.SelectMany(c => Traverse(c.Id, graph.Down))
            .Where(n => !cis.Any(c => c.Id == n.Id))
            .GroupBy(n => n.Id).Select(g => g.OrderBy(n => n.Depth).First()).OrderBy(n => n.Depth).ToList();
        var describe = await DescriberAsync(downstream.SelectMany(n => new[] { n.Id, n.Via }));
        var services = await ServicesTouchedAsync(cis.Select(c => c.Id).Concat(downstream.Select(n => n.Id)));
        return Ok(new
        {
            Cis = cis.Select(c => new { c.Id, c.Reference, c.Title, c.CiType, c.Status }),
            Downstream = downstream.Select(describe), Services = services
        });
    }

    private record Graph(ILookup<int, (int Next, string Type)> Down, ILookup<int, (int Next, string Type)> Up);

    /// <summary>Arcs « la panne de From touche To », avec la relation d'origine, dans les deux sens.</summary>
    private async Task<Graph> ImpactGraphAsync()
    {
        var relations = await Db.CiRelationships.AsNoTracking().ToListAsync();
        var impacts = relations.Select(r => CiRelationship.FailureFlowsFromTarget(r.Type)
            ? (From: r.TargetId, To: r.SourceId, r.Type)
            : (From: r.SourceId, To: r.TargetId, r.Type)).ToList();
        return new Graph(impacts.ToLookup(a => a.From, a => (Next: a.To, a.Type)), impacts.ToLookup(a => a.To, a => (Next: a.From, a.Type)));
    }

    /// <summary>Description d'un nœud d'impact ; un CI d'un autre périmètre n'est pas décrit (PER-08).</summary>
    private async Task<Func<ImpactNode, object>> DescriberAsync(IEnumerable<int> idList)
    {
        var ids = idList.Distinct().ToList();
        var cis = await Db.ConfigurationItems.AsNoTracking().Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.Reference, c.Title, c.CiType, c.Status, c.PerimeterId }).ToDictionaryAsync(c => c.Id);
        var scope = await ScopeAsync();
        string Name(int ciId, string text) => scope.Sees(cis[ciId].PerimeterId) ? text : "CI d'un autre périmètre";
        return n => new
        {
            Id = scope.Sees(cis[n.Id].PerimeterId) ? n.Id : 0,
            Reference = Name(n.Id, cis[n.Id].Reference), Title = Name(n.Id, cis[n.Id].Title), cis[n.Id].CiType, cis[n.Id].Status,
            n.Depth, n.Relation,
            Via = new { Id = scope.Sees(cis[n.Via].PerimeterId) ? n.Via : 0, Reference = Name(n.Via, cis[n.Via].Reference), Title = Name(n.Via, cis[n.Via].Title) }
        };
    }

    /// <summary>CFG-21 : services supportés par ces CI (liens CI ↔ service), du catalogue.</summary>
    private async Task<List<ServiceLevelManagement.ItService>> ServicesOfAsync(IEnumerable<int> ciIdList)
    {
        var ciIds = ciIdList.Distinct().ToList();
        var serviceIds = await Db.ItemLinks.AsNoTracking()
            .Where(l => (l.FromType == "ci" && ciIds.Contains(l.FromId) && l.ToType == "service")
                        || (l.ToType == "ci" && ciIds.Contains(l.ToId) && l.FromType == "service"))
            .Select(l => l.FromType == "service" ? l.FromId : l.ToId).Distinct().ToListAsync();
        return await Db.Services.AsNoTracking().Where(s => serviceIds.Contains(s.Id)).OrderBy(s => s.Title).ToListAsync();
    }

    private async Task<List<object>> ServicesTouchedAsync(IEnumerable<int> ciIds) =>
        (await ServicesOfAsync(ciIds)).Select(s => (object)new { s.Id, s.Reference, s.Title, s.Status, s.OwnerDisplayName }).ToList();

    private record ImpactNode(int Id, int Depth, int Via, string Relation);

    private static List<ImpactNode> Traverse(int start, ILookup<int, (int Next, string Type)> edges)
    {
        var seen = new HashSet<int> { start };
        var result = new List<ImpactNode>();
        var queue = new Queue<(int Id, int Depth)>([(start, 0)]);
        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();
            foreach (var (next, type) in edges[current])
            {
                if (!seen.Add(next)) continue;
                result.Add(new ImpactNode(next, depth + 1, current, type));
                queue.Enqueue((next, depth + 1));
            }
        }
        return result;
    }

    /// <summary>Relie ce CI à un autre, désigné par sa référence.</summary>
    [HttpPost("{id:int}/relations")]
    public async Task<IActionResult> AddRelation(int id, CiRelationshipDto dto)
    {
        var error = Allowed.Check("Type de relation", dto.Type, CiRelationship.Types);
        if (error is not null) return BadRequest(new { message = error });
        var source = await Db.ConfigurationItems.FirstOrDefaultAsync(c => c.Id == id);
        if (source is null || !await VisibleAsync(source)) return NotFound();
        var reference = dto.TargetReference.Trim().ToUpper();
        var target = await Db.ConfigurationItems.FirstOrDefaultAsync(c => c.Reference == reference);
        // PER-08 : relier suppose de voir les deux CI.
        if (target is null || !await VisibleAsync(target)) return BadRequest(new { message = $"Aucun CI de référence « {dto.TargetReference} »." });
        if (target.Id == id) return BadRequest(new { message = "Un CI ne peut pas être relié à lui-même." });
        // CFG-14 : un CI retiré ne peut plus être relié.
        if (source.Status == "Retiré" || target.Status == "Retiré")
            return BadRequest(new { message = "Un CI retiré ne peut plus être relié." });
        if (await Db.CiRelationships.AnyAsync(r => r.SourceId == id && r.TargetId == target.Id && r.Type == dto.Type))
            return Conflict(new { message = "Cette relation existe déjà." });

        var rel = new CiRelationship { SourceId = id, TargetId = target.Id, Type = dto.Type };
        Db.CiRelationships.Add(rel);
        await Db.SaveChangesAsync();
        return Ok(new { rel.Id, rel.Type, Target = new { target.Id, target.Reference, target.Title } });
    }

    [HttpDelete("relations/{relationId:int}")]
    public async Task<IActionResult> RemoveRelation(int relationId)
    {
        var rel = await Db.CiRelationships.FindAsync(relationId);
        if (rel is null || !await VisibleAsync(rel.SourceId)) return NotFound();
        Db.CiRelationships.Remove(rel);
        await Db.SaveChangesAsync();
        return NoContent();
    }
}
