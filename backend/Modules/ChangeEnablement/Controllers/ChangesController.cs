using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Data;
using SAloha.Api.Core.Links;
using SAloha.Api.Core.Records;
using SAloha.Api.Core.Notifications;

namespace SAloha.Api.Modules.ChangeEnablement;

[Route("api/changes")]
public class ChangesController(AppDbContext db) : RecordController<Change, ChangeDto, ChangeView>(db)
{
    protected override ChangeView View(Change e) => ChangeView.From(e);
    private const string StandardModel = "Modèle standard (pré-autorisé)";

    protected override string Code => "CHG";
    protected override string LinkType => "change";
    protected override string[] Statuses => Change.Statuses;
    protected override IReadOnlyDictionary<string, string[]> Transitions => Change.Transitions;

    protected override string? Apply(Change e, ChangeDto dto, bool creating)
    {
        var error = Allowed.Check("Type de changement", dto.ChangeType, Change.Types)
                    ?? Allowed.Check("Risque", dto.Risk, Change.Risks)
                    ?? Allowed.Check("Résultat", dto.Outcome, Change.Outcomes, optional: true);
        if (error is not null) return error;
        if (dto.PlannedStart is not null && dto.PlannedEnd is not null && dto.PlannedEnd < dto.PlannedStart)
            return "La fin planifiée précède le début planifié.";
        e.ChangeType = dto.ChangeType!; e.Risk = dto.Risk!;
        e.PlannedStart = Dates.Utc(dto.PlannedStart); e.PlannedEnd = Dates.Utc(dto.PlannedEnd);
        e.ImplementationPlan = dto.ImplementationPlan ?? ""; e.BackoutPlan = dto.BackoutPlan ?? "";
        e.Outcome = string.IsNullOrEmpty(dto.Outcome) ? null : dto.Outcome;
        e.RejectionReason = string.IsNullOrWhiteSpace(dto.RejectionReason) ? null : dto.RejectionReason.Trim();
        // CHG-14, CHG-15 : dates réelles et revue post-implémentation.
        if (dto.ActualStart is not null && dto.ActualEnd is not null && dto.ActualEnd < dto.ActualStart)
            return "La fin réelle précède le début réel.";
        e.ActualStart = Dates.Utc(dto.ActualStart); e.ActualEnd = Dates.Utc(dto.ActualEnd);
        e.PirNotes = string.IsNullOrWhiteSpace(dto.PirNotes) ? null : dto.PirNotes.Trim();
        e.ConflictComment = string.IsNullOrWhiteSpace(dto.ConflictComment) ? null : dto.ConflictComment.Trim();
        if (creating) e.StandardModelId = dto.StandardModelId;   // CHG-26 : choisi à la création
        return null;
    }

    /// <summary>
    /// CHG-26 : un changement Standard se crée depuis un modèle actif, dont il reprend les plans et
    /// le risque (son autorisation vient du modèle). Vérifié à la création, ou quand le type devient Standard.
    /// </summary>
    protected override async Task<string?> ValidateAsync(Change e)
    {
        var becameStandard = e.ChangeType == "Standard"
            && (e.Id == 0 || Db.Entry(e).Property(x => x.ChangeType).OriginalValue != "Standard");
        if (!becameStandard) return null;
        var model = e.StandardModelId is int id ? await Db.StandardChangeModels.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id) : null;
        if (model is null) return "Un changement Standard se crée depuis un modèle standard du catalogue ; sinon, créer un changement Normal.";
        if (!model.Active) return $"Le modèle standard « {model.Name} » n'est plus proposé.";
        if (model.SuspendedAt is not null)
            return $"Le modèle standard « {model.Name} » est suspendu depuis l'échec de {model.SuspendedByChange} : créer un changement Normal en attendant la revue d'un gestionnaire.";
        FromModel(e, model);
        return null;
    }

    private static void FromModel(Change e, StandardChangeModel model)
    {
        e.ImplementationPlan = model.ImplementationPlan; e.BackoutPlan = model.BackoutPlan; e.Risk = model.Risk;
    }

    protected override async Task BeforeSaveAsync(Change e, bool creating)
    {
        // CHG-26 : l'enregistrement réel reprend aussi les plans du modèle.
        if (creating && e.ChangeType == "Standard" && e.StandardModelId is int id
            && await Db.StandardChangeModels.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id) is { } model)
            FromModel(e, model);
        // CHG-27 : l'échec d'un changement standard suspend son modèle jusqu'à la revue d'un gestionnaire.
        if (!creating && e.Outcome == "Échoué" && Db.Entry(e).Property(x => x.Outcome).OriginalValue != "Échoué"
            && e.ChangeType == "Standard" && e.StandardModelId is int modelId
            && await Db.StandardChangeModels.FirstOrDefaultAsync(m => m.Id == modelId) is { SuspendedAt: null } failed)
        {
            (failed.SuspendedAt, failed.SuspendedByChangeId, failed.SuspendedByChange) = (DateTime.UtcNow, e.Id, e.Reference);
            Notify.ToManagers(e, LinkType, $"Modèle standard suspendu : {failed.Name}");
        }
        // CHG-20 : une nouvelle évaluation repart sans décision du CAB.
        if (!creating && e.Status == "Évalué" && Db.Entry(e).Property(x => x.Status).OriginalValue != "Évalué")
            Db.ChangeApprovals.RemoveRange(await Db.ChangeApprovals.Where(a => a.ChangeId == e.Id).ToListAsync());
    }

    /// <summary>
    /// L'autorisation porte sur un changement précis : en modifier le type, le
    /// risque, les plans ou le créneau la retire (retour à Évalué, en attente
    /// de l'autorité de changement). Un standard reste pré-autorisé quand on
    /// ne fait que le replanifier : son autorisation vient du modèle, pas du créneau.
    /// </summary>
    protected override string? StatusAfterEdit(Change e, ChangeDto dto) => Modified(e, dto) ? "Évalué" : null;

    private static bool Modified(Change e, ChangeDto dto)
    {
        if (e.Status is not ("Autorisé" or "Planifié")) return false;
        var rescheduled = Dates.Utc(dto.PlannedStart) != e.PlannedStart || Dates.Utc(dto.PlannedEnd) != e.PlannedEnd;
        return dto.ChangeType != e.ChangeType || dto.Risk != e.Risk
               || (dto.ImplementationPlan ?? "") != e.ImplementationPlan
               || (dto.BackoutPlan ?? "") != e.BackoutPlan
               || (rescheduled && e.ChangeType != "Standard");
    }

    /// <summary>
    /// CHG-07 : la modification d'un gestionnaire vaut autorisation, à son nom — sauf s'il est le
    /// demandeur (CHG-21, hors Admin), si le changement relève du CAB (CHG-20, Normal à risque
    /// Élevé) ou si le nouveau créneau crée un conflit sans commentaire de l'autorité (CHG-22) :
    /// retour à Évalué, comme pour un User (décisions du 2026-10-29, #209 et #210).
    /// </summary>
    protected override async Task<string?> StatusAfterManagerEditAsync(Change e, ChangeDto dto)
    {
        if (!Modified(e, dto)) return null;
        if (!User.IsInRole("Admin") && string.Equals(e.CreatedBy, Me, StringComparison.OrdinalIgnoreCase)) return "Évalué";
        if (dto.ChangeType == "Normal" && dto.Risk == "Élevé") return "Évalué";
        if (await NewConflictAsync(e, dto)) return "Évalué";
        (e.AuthorizedAt, e.AuthorizedBy) = (DateTime.UtcNow, MyDisplay);
        return null;
    }

    /// <summary>CHG-22 : le créneau demandé ajoute un conflit, et le commentaire de l'autorité n'a pas changé.</summary>
    private async Task<bool> NewConflictAsync(Change e, ChangeDto dto)
    {
        var (start, end) = (Dates.Utc(dto.PlannedStart), Dates.Utc(dto.PlannedEnd));
        if (start is null || (start == e.PlannedStart && end == e.PlannedEnd)) return false;
        var comment = string.IsNullOrWhiteSpace(dto.ConflictComment) ? null : dto.ConflictComment.Trim();
        if (comment is not null && comment != e.ConflictComment) return false;
        var before = (await ConflictsAsync()).GetValueOrDefault(e.Id, []).Select(c => c.ChangeId).ToHashSet();
        return (await ConflictsAsync((e.Id, start.Value, end))).GetValueOrDefault(e.Id, []).Any(c => !before.Contains(c.ChangeId));
    }

    // Un changement standard suit un modèle déjà évalué : il est pré-autorisé.
    protected override string InitialStatus(Change e) => e.ChangeType == "Standard" ? "Autorisé" : "Demandé";

    protected override bool RequiresManager(string status) => status is "Autorisé" or "Rejeté";

    // Un changement rejeté est terminé : il n'est ni planifié, ni mis en œuvre, ni clos.
    protected override async Task<string?> CheckStatusAsync(Change e, string status)
    {
        // Règles vérifiées à l'entrée du statut : un changement déjà dans ce statut avant elles reste modifiable.
        var entering = e.Status != status;
        // CHG-14 : dates réelles.
        if (entering && status == "Mis en œuvre" && e.ActualStart is null)
            return "Renseigner le début réel de la mise en œuvre.";
        if (entering && status == "Clos" && e.ActualEnd is null)
            return "Renseigner la fin réelle de la mise en œuvre avant de clore le changement.";
        // CHG-15 : revue post-implémentation d'un changement urgent ou échoué.
        if (entering && status == "Clos" && (e.ChangeType == "Urgent" || e.Outcome == "Échoué") && e.PirNotes is null)
            return "Rédiger la revue post-implémentation (ce qui s'est passé, causes, enseignements) : elle est exigée pour un changement urgent ou échoué.";
        // CHG-16 : un changement normal ou urgent s'évalue sur les CI qu'il touche (analyse d'impact, CFG-20).
        // À l'évaluation seulement (Demandé → Évalué) : le retour automatique à Évalué d'un
        // changement autorisé puis modifié (StatusAfterEdit) n'est pas bloqué.
        if (entering && status == "Évalué" && e.Status == "Demandé" && e.ChangeType != "Standard"
            && (e.Id == 0 || !await ItemLinks.IsLinkedToAsync(Db, LinkType, e.Id, "ci")))
            return "Relier au moins un CI au changement (onglet Liens) pour l'évaluer : l'analyse d'impact en part.";
        // CHG-20 : Normal à risque Élevé → le CAB autorise (deux gestionnaires, POST /approvals).
        if (entering && status == "Autorisé" && e.Id != 0 && e.RequiresCab)
            return "Changement normal à risque élevé : il est autorisé par le CAB, deux gestionnaires distincts approuvent (Approbations du CAB).";
        // CHG-25 : pas de créneau dans une période de gel, sauf changement Urgent.
        if (status == "Planifié" && e.ChangeType != "Urgent" && e.PlannedStart is { } start && e.PlannedEnd is { } end)
        {
            var moved = entering || e.Id == 0
                || Db.Entry(e).Property(x => x.PlannedStart).OriginalValue != start || Db.Entry(e).Property(x => x.PlannedEnd).OriginalValue != end;
            var freeze = moved ? await Db.ChangeFreezes.AsNoTracking().Where(f => f.Start < end && f.End > start).OrderBy(f => f.Start).FirstOrDefaultAsync() : null;
            if (freeze is not null)
                return $"Créneau dans une période de gel ({freeze.Reason}, du {freeze.Start:dd/MM/yyyy HH:mm} au {freeze.End:dd/MM/yyyy HH:mm} UTC) : seul un changement urgent s'y planifie.";
        }
        // CHG-22 : autoriser un changement en conflit de calendrier exige un commentaire de l'autorité.
        if (entering && status == "Autorisé" && e.Id != 0 && e.ConflictComment is null
            && (await ConflictsAsync()).GetValueOrDefault(e.Id) is { Count: > 0 } conflicts)
            return $"Conflit de calendrier avec {string.Join(", ", conflicts.Select(c => $"{c.ChangeReference} (CI {c.CiReference})").Distinct())} : "
                   + "commenter l'autorisation (pourquoi le conflit est acceptable ou comment il sera géré).";
        // CHG-21 : séparation des tâches — le demandeur n'autorise pas son propre changement (sauf Admin).
        if (entering && status == "Autorisé" && e.Id != 0 && !User.IsInRole("Admin")
            && string.Equals(e.CreatedBy, Me, StringComparison.OrdinalIgnoreCase))
            return "Le demandeur d'un changement ne peut pas l'autoriser lui-même : un autre gestionnaire décide.";
        return CheckBase(e, status);
    }

    private static string? CheckBase(Change e, string status)
    {
        if (status is "Planifié" or "Mis en œuvre" or "Clos" && e.AuthorizedAt is null)
            return "Le changement doit d'abord être autorisé par un gestionnaire.";
        // CHG-12 : un changement normal ou urgent s'évalue sur ses plans.
        if (status == "Évalué" && e.ChangeType != "Standard"
            && (string.IsNullOrWhiteSpace(e.ImplementationPlan) || string.IsNullOrWhiteSpace(e.BackoutPlan)))
            return "Renseigner le plan de mise en œuvre et le plan de retour arrière pour évaluer le changement.";
        // CHG-13 : un créneau complet pour le calendrier.
        if (status == "Planifié" && (e.PlannedStart is null || e.PlannedEnd is null))
            return "Renseigner le début et la fin planifiés pour inscrire le changement au calendrier.";
        // CHG-17 : un rejet se motive, le motif est communiqué au demandeur.
        if (status == "Rejeté" && e.RejectionReason is null)
            return "Motiver le rejet : le motif est communiqué au demandeur.";
        if (status == "Clos" && e.Outcome is null)
            return "Renseigner le résultat (Réussi ou Échoué) avant de clore le changement.";
        return null;
    }

    // CHG-24 : changement évalué → autorité (gestionnaires) ; autorisation ou rejet → demandeur et responsable.
    protected override void OnNotify(Change e, string? from, Notifier n)
    {
        if (from is null) return;
        if (e.Status == "Évalué") n.ToManagers(e, LinkType, "Changement à autoriser");
        var ev = e.Status switch { "Autorisé" => "Changement autorisé", "Rejeté" => "Changement rejeté", _ => null };
        if (ev is null) return;
        n.ToCreator(e, LinkType, ev);
        n.ToOwner(e, LinkType, ev);
    }

    protected override void OnStatusChanged(Change e, string from)
    {
        if (e.Status == "Autorisé" && e.AuthorizedAt is null)
        {
            e.AuthorizedAt = DateTime.UtcNow;
            e.AuthorizedBy = from == "" ? StandardModel : MyDisplay;
        }
        else if (e.Status is "Demandé" or "Évalué" or "Rejeté") { e.AuthorizedAt = null; e.AuthorizedBy = null; }
        e.ClosedAt = e.Status == "Clos" ? DateTime.UtcNow : null;
        // CHG-31 : point de départ de la relance d'un changement mis en œuvre, non clos.
        if (e.Status == "Mis en œuvre" && from != "Mis en œuvre") e.ImplementedAt = DateTime.UtcNow;
    }

    // Le registre n'affiche pas les plans : ils restent dans la fiche.
    protected override async Task<IEnumerable<object>> ListItems(IQueryable<Change> q) =>
        (await q.Select(c => new
        {
            c.Id, c.Reference, c.Title, c.Status, c.ChangeType, c.Risk, c.PlannedStart, c.PlannedEnd,
            c.ActualEnd, c.OwnerId, c.OwnerDisplayName, c.CreatedAt
        }).ToListAsync())
        // CHG-14 : dépassement de la fin planifiée, aussi au registre (calculé après lecture).
        .Select(c => new
        {
            c.Id, c.Reference, c.Title, c.Status, c.ChangeType, c.Risk, c.PlannedStart, c.PlannedEnd,
            c.ActualEnd, c.OwnerId, c.OwnerDisplayName, c.CreatedAt,
            Overrun = new Change { PlannedEnd = c.PlannedEnd, ActualEnd = c.ActualEnd, Status = c.Status }.Overrun
        });

    /// <summary>CHG-20 : décisions du CAB sur un changement (les plus récentes d'abord).</summary>
    [HttpGet("{id:int}/approvals")]
    public async Task<IActionResult> Approvals(int id) =>
        await VisibleAsync(id)
            ? Ok(await Db.ChangeApprovals.AsNoTracking().Where(a => a.ChangeId == id).OrderByDescending(a => a.At).Select(a => ChangeApprovalView.From(a)).ToListAsync())
            : NotFound();

    /// <summary>
    /// CHG-20 : un gestionnaire du périmètre, autre que le demandeur (CHG-21), approuve ou rejette
    /// un changement Normal à risque Élevé Évalué. Deux approbations distinctes l'autorisent ; un
    /// seul rejet, motivé, le rejette. Une décision par gestionnaire.
    /// </summary>
    [HttpPost("{id:int}/approvals")]
    public async Task<IActionResult> Decide(int id, ApprovalDto dto)
    {
        var e = await Db.Changes.FirstOrDefaultAsync(c => c.Id == id);
        if (e is null || !await VisibleAsync(e)) return NotFound();
        ActOn(e.PerimeterId);
        if (!IsManager) return Forbid();
        if (!e.RequiresCab) return BadRequest(new { message = "Seul un changement normal à risque élevé passe par le CAB." });
        if (e.Status != "Évalué") return BadRequest(new { message = "Le CAB décide sur un changement Évalué." });
        if (string.Equals(e.CreatedBy, Me, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Le demandeur ne siège pas au CAB pour son propre changement." });
        var comment = dto.Comment?.Trim() ?? "";
        if (!dto.Approve && comment == "") return BadRequest(new { message = "Motiver le rejet : le motif est communiqué au demandeur." });
        // CHG-22 : approuver un changement en conflit de calendrier se commente.
        if (dto.Approve && comment == "" && (await ConflictsAsync()).GetValueOrDefault(id) is { Count: > 0 })
            return BadRequest(new { message = "Changement en conflit de calendrier : commenter l'approbation (pourquoi le conflit est acceptable)." });
        if (await Db.ChangeApprovals.AnyAsync(a => a.ChangeId == id && a.Approver == Me))
            return Conflict(new { message = "Vous avez déjà donné votre décision sur ce changement." });
        var decision = new ChangeApproval { ChangeId = id, Approver = Me, ApproverDisplayName = MyDisplay, Approved = dto.Approve, Comment = comment };
        var error = Lengths.Check(decision);
        if (error is not null) return BadRequest(new { message = error });
        Db.ChangeApprovals.Add(decision);

        string? ev = null;
        if (!dto.Approve)
        {
            e.Status = "Rejeté"; e.RejectionReason = $"Rejeté par le CAB ({MyDisplay}) : {comment}";
            ev = "Changement rejeté";
        }
        else
        {
            var approvers = await Db.ChangeApprovals.Where(a => a.ChangeId == id && a.Approved)
                .Select(a => a.ApproverDisplayName).ToListAsync();
            approvers.Add(MyDisplay);
            if (approvers.Distinct().Count() >= 2)
            {
                e.Status = "Autorisé"; e.AuthorizedAt = DateTime.UtcNow;
                var by = $"CAB : {string.Join(", ", approvers.Distinct())}";
                e.AuthorizedBy = by.Length > 200 ? by[..200] : by;
                ev = "Changement autorisé";
            }
        }
        e.UpdatedAt = DateTime.UtcNow;
        await Db.SaveChangesAsync();
        if (ev is not null)
        {
            var n = Notify;
            n.ToCreator(e, LinkType, ev); n.ToOwner(e, LinkType, ev);
            await n.SaveAsync();
        }
        return Ok(new { e.Status, e.AuthorizedBy, Decision = ChangeApprovalView.From(decision) });
    }

    /// <summary>Calendrier des changements : changements planifiés non rejetés, à partir d'une date.</summary>
    [HttpGet("schedule")]
    public async Task<IActionResult> Schedule([FromQuery] DateTime? from)
    {
        var start = Dates.Utc(from) ?? DateTime.UtcNow.Date.AddDays(-7);
        var items = await (await InScopeAsync(Db.Changes.AsNoTracking()))
            .Where(c => c.PlannedStart != null && c.Status != "Rejeté"
                        && (c.PlannedEnd ?? c.PlannedStart) >= start)
            .OrderBy(c => c.PlannedStart)
            .Select(c => new { c.Id, c.Reference, c.Title, c.Status, c.ChangeType, c.Risk,
                               c.PlannedStart, c.PlannedEnd, c.OwnerDisplayName })
            .ToListAsync();
        var conflicts = await ConflictsAsync();
        var visible = await VisibleChangeIdsAsync();
        return Ok(items.Select(c => new
        {
            c.Id, c.Reference, c.Title, c.Status, c.ChangeType, c.Risk, c.PlannedStart, c.PlannedEnd,
            c.OwnerDisplayName, Conflicts = Visible(conflicts.GetValueOrDefault(c.Id, []), visible)
        }));
    }

    /// <summary>Conflits de calendrier d'un changement (vide s'il n'en a pas).</summary>
    [HttpGet("{id:int}/conflicts")]
    public async Task<IActionResult> Conflicts(int id) =>
        await VisibleAsync(id)
            ? Ok(Visible((await ConflictsAsync()).GetValueOrDefault(id, []), await VisibleChangeIdsAsync()))
            : NotFound();

    /// <summary>PER-08 : un conflit avec un changement d'un autre périmètre est signalé sans le décrire.</summary>
    private async Task<HashSet<int>?> VisibleChangeIdsAsync() =>
        (await ScopeAsync()).Global ? null
            : (await (await InScopeAsync(Db.Changes.AsNoTracking())).Select(c => c.Id).ToListAsync()).ToHashSet();

    private static List<Conflict> Visible(List<Conflict> list, HashSet<int>? visible) =>
        visible is null ? list : list.Select(c => visible.Contains(c.ChangeId) ? c
            : c with { ChangeId = 0, ChangeReference = "Autre périmètre", ChangeTitle = "Changement d'un autre périmètre" }).ToList();

    /// <summary>Conflit : un autre changement planifié sur le même CI, sur un créneau qui chevauche.</summary>
    public record Conflict(int CiId, string CiReference, string CiTitle, int ChangeId, string ChangeReference, string ChangeTitle);

    /// <summary>
    /// Deux changements non rejetés sont en conflit s'ils sont liés à un même
    /// CI (liens inter-processus) et que leurs créneaux se chevauchent. Sans
    /// fin planifiée, un changement occupe une heure.
    /// </summary>
    private async Task<Dictionary<int, List<Conflict>>> ConflictsAsync((int Id, DateTime Start, DateTime? End)? draft = null)
    {
        var planned = (await Db.Changes.AsNoTracking()
                .Where(c => c.PlannedStart != null && c.Status != "Rejeté")
                .Select(c => new { c.Id, c.Reference, c.Title, c.PlannedStart, c.PlannedEnd })
                .ToListAsync())
            .ToDictionary(c => c.Id, c => (c.Reference, c.Title, Start: c.PlannedStart!.Value,
                                           End: c.PlannedEnd ?? c.PlannedStart!.Value.AddHours(1)));
        // Créneau pas encore enregistré (modification en cours, CHG-22) à la place de celui en base.
        if (draft is { } d && planned.TryGetValue(d.Id, out var current))
            planned[d.Id] = (current.Reference, current.Title, d.Start, d.End ?? d.Start.AddHours(1));
        // Les liens se lisent dans les deux sens : changement → CI ou CI → changement.
        var touches = (await Db.ItemLinks.AsNoTracking()
                .Where(l => (l.FromType == "change" && l.ToType == "ci") || (l.FromType == "ci" && l.ToType == "change"))
                .ToListAsync())
            .Select(l => l.FromType == "change" ? (Change: l.FromId, Ci: l.ToId) : (Change: l.ToId, Ci: l.FromId))
            .Where(t => planned.ContainsKey(t.Change)).Distinct().ToList();

        var ciIds = touches.Select(t => t.Ci).Distinct().ToList();
        var cis = await ItemLinks.ByType("ci")!.Query(Db).Where(c => ciIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id);
        var result = new Dictionary<int, List<Conflict>>();
        foreach (var onCi in touches.Where(t => cis.ContainsKey(t.Ci)).GroupBy(t => t.Ci))
        {
            var ci = cis[onCi.Key];
            var changes = onCi.Select(t => t.Change).ToList();
            foreach (var a in changes)
            foreach (var b in changes.Where(b => b != a))
            {
                var (pa, pb) = (planned[a], planned[b]);
                if (pa.Start < pb.End && pb.Start < pa.End)
                {
                    if (!result.TryGetValue(a, out var list)) result[a] = list = [];
                    list.Add(new Conflict(ci.Id, ci.Reference, ci.Title, b, pb.Reference, pb.Title));
                }
            }
        }
        return result;
    }
}
