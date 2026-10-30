using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Itil;
using Microsoft.AspNetCore.Mvc;
using SAloha.Api.Core.Data;
using SAloha.Api.Core.Records;
using SAloha.Api.Core.Notifications;
using SAloha.Api.Core.Links;

namespace SAloha.Api.Modules.ServiceRequestManagement;

[Route("api/requests")]
public class ServiceRequestsController(AppDbContext db) : RecordController<ServiceRequest, ServiceRequestDto, ServiceRequestView>(db)
{
    protected override ServiceRequestView View(ServiceRequest e) => ServiceRequestView.From(e);
    protected override string Code => "REQ";
    protected override string LinkType => "request";
    protected override string[] Statuses => ServiceRequest.Statuses;
    protected override IReadOnlyDictionary<string, string[]> Transitions => ServiceRequest.Transitions;

    protected override string? Apply(ServiceRequest e, ServiceRequestDto dto, bool creating)
    {
        // REQ-20 : le modèle se choisit à la création ; il donne l'objet demandé et le service.
        if (creating) e.CatalogItemId = dto.CatalogItemId;
        if (string.IsNullOrWhiteSpace(dto.RequestedItem) && e.CatalogItemId is null) return "Préciser l'objet demandé, ou choisir un modèle du catalogue.";
        e.RequestedItem = dto.RequestedItem?.Trim() ?? "";
        // REQ-07 : le bénéficiaire vaut le demandeur par défaut ; une demande pour autrui garde les deux.
        if (!string.IsNullOrWhiteSpace(dto.RequestedFor))
            (e.RequestedFor, e.RequestedForDisplayName) = (dto.RequestedFor.Trim(), dto.RequestedForDisplayName ?? dto.RequestedFor.Trim());
        else if (creating) (e.RequestedFor, e.RequestedForDisplayName) = (Me, MyDisplay);
        e.ContestReason = string.IsNullOrWhiteSpace(dto.ContestReason) ? null : dto.ContestReason.Trim();
        // REQ-22 : l'échéance d'une demande issue d'un modèle à délai cible est calculée ; seul un
        // gestionnaire la modifie ensuite. Sans délai cible, l'échéance souhaitée reste libre.
        var due = Dates.Utc(dto.DueDate);
        // Le formulaire ne montre que le jour et le renvoie à minuit UTC : le même jour n'est pas
        // une modification, et l'heure calculée est gardée (REQ-41 compare à l'heure près).
        var sameDay = due is { TimeOfDay.Ticks: 0 } && due?.Date == e.DueDate?.Date;
        // Le délai cible du modèle n'est connu qu'en lecture de la base : refus dans ValidateAsync.
        _dueChangedByUser = !creating && !IsManager && due != e.DueDate && !sameDay;
        if (creating || e.CatalogItemId is null || (due != e.DueDate && !sameDay)) e.DueDate = due;
        e.RejectionReason = string.IsNullOrWhiteSpace(dto.RejectionReason) ? null : dto.RejectionReason.Trim();
        e.ServiceId = dto.ServiceId; e.AffectedService = dto.AffectedService ?? "";
        return null;
    }

    // SLM-10 : service du catalogue (vérifié ici, recopié sur l'enregistrement juste avant l'écriture).
    protected override async Task<string?> ValidateAsync(ServiceRequest e)
    {
        var item = await ItemAsync(e);
        if (e.CatalogItemId is not null && item is null) return "Choisir un modèle du catalogue de demandes.";
        if (_dueChangedByUser && item?.TargetHours is not null)
            return "Seul un gestionnaire modifie l'échéance d'une demande issue d'un modèle à délai cible.";
        if (e.Id == 0 && item is { Active: false }) return $"Le modèle « {item.Name} » n'est plus proposé.";
        _preApproved = e.Id == 0 && item is { RequiresApproval: false };
        FromItem(e, item);
        return await ServiceCatalog.ResolveAsync(Db, e);
    }

    private bool _preApproved;
    private bool _dueChangedByUser;

    private async Task<RequestCatalogItem?> ItemAsync(ServiceRequest e) =>
        e.CatalogItemId is int id ? await Db.RequestCatalogItems.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id) : null;

    /// <summary>REQ-20 : objet demandé et service repris du modèle quand la demande ne les précise pas.</summary>
    private static void FromItem(ServiceRequest e, RequestCatalogItem? item)
    {
        if (item is null) return;
        if (string.IsNullOrWhiteSpace(e.RequestedItem)) e.RequestedItem = item.Name;
        if (e.ServiceId is null && string.IsNullOrWhiteSpace(e.AffectedService)) e.ServiceId = item.ServiceId;
    }

    // REQ-21 : un modèle sans approbation crée la demande directement Approuvée.
    protected override string InitialStatus(ServiceRequest e) => _preApproved ? "Approuvée" : base.InitialStatus(e);

    /// <summary>
    /// À l'approbation (ou à la création pré-approuvée) : échéance = maintenant + délai cible du
    /// modèle, en heures de service de son service (REQ-22, SLM-14) ; le groupe d'exécution du
    /// modèle devient responsable, s'il n'y en a pas (REQ-23).
    /// </summary>
    protected override async Task BeforeSaveAsync(ServiceRequest e, bool creating)
    {
        var item = await ItemAsync(e);
        FromItem(e, item);
        await ServiceCatalog.ResolveAsync(Db, e);
        var approvedNow = e.Status == "Approuvée" && (creating || Db.Entry(e).Property(x => x.Status).OriginalValue != "Approuvée");
        if (item is null || !approvedNow) return;
        if (item.TargetHours is int hours)
        {
            var calendar = item.ServiceId is int sid
                ? (await Db.Services.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sid))?.Calendar() ?? ServiceCalendar.AlwaysOpen
                : ServiceCalendar.AlwaysOpen;
            e.DueDate = calendar.Add(DateTime.UtcNow, TimeSpan.FromHours(hours));
        }
        if (string.IsNullOrWhiteSpace(e.OwnerId) && item.FulfillmentGroup is { } group)
        {
            e.OwnerType = "Group"; e.OwnerId = group; e.OwnerDisplayName = group;
        }
        _tasksFrom = item.Id;
        // Approbation d'une demande existante : tâches écrites avec elle ; à la création, après (OnCreatedAsync).
        if (!creating) await CreateTasksAsync(e, save: false);
    }

    /// <summary>REQ-24 : modèle dont les tâches prédéfinies sont à recopier après l'écriture.</summary>
    private int? _tasksFrom;

    /// <summary>
    /// REQ-24 : à l'approbation, les tâches prédéfinies du modèle sont créées sur la demande, une
    /// fois, chacune avec son responsable (à défaut, celui de la demande).
    /// </summary>
    private async Task CreateTasksAsync(ServiceRequest e, bool save = true)
    {
        if (_tasksFrom is not int itemId || await Db.RequestTasks.AnyAsync(t => t.RequestId == e.Id && t.FromCatalog)) return;
        var templates = await Db.RequestCatalogTasks.AsNoTracking().Where(t => t.CatalogItemId == itemId).OrderBy(t => t.Order).ToListAsync();
        if (templates.Count == 0) return;
        Db.RequestTasks.AddRange(templates.Select(t => new RequestTask
        {
            RequestId = e.Id, Title = t.Title, Order = t.Order, FromCatalog = true, CreatedBy = Me,
            OwnerType = t.OwnerId is null ? e.OwnerType : t.OwnerType, OwnerId = t.OwnerId ?? e.OwnerId,
            OwnerDisplayName = t.OwnerId is null ? e.OwnerDisplayName : t.OwnerDisplayName
        }));
        if (save) await Db.SaveChangesAsync();
    }

    protected override Task OnCreatedAsync(ServiceRequest e) => CreateTasksAsync(e);

    protected override bool RequiresManager(string status) => status is "Approuvée" or "Rejetée";

    // Une demande rejetée est terminée : elle n'est ni traitée ni close.
    /// <summary>REQ-25 : satisfaite, toutes ses tâches sont terminées ou annulées.</summary>
    protected override async Task<string?> CheckStatusAsync(ServiceRequest e, string status)
    {
        if (status == "Satisfaite" && e.Status != "Satisfaite" && e.Id != 0)
        {
            var open = await Db.RequestTasks.Where(t => t.RequestId == e.Id && !RequestTask.Finished.Contains(t.Status)).Select(t => t.Title).ToListAsync();
            if (open.Count > 0)
                return $"Tâches encore ouvertes ({string.Join(", ", open.Select(t => $"« {t} »"))}) : les terminer ou les annuler avant de satisfaire la demande.";
        }
        return CheckStatus(e, status);
    }

    protected override string? CheckStatus(ServiceRequest e, string status)
    {
        if (e.ApprovedAt is null && status is "En cours" or "Satisfaite" or "Close")
            return "La demande doit d'abord être approuvée par un gestionnaire.";
        // REQ-06 : un rejet se motive, le motif est communiqué au demandeur.
        if (status == "Rejetée" && e.RejectionReason is null)
            return "Motiver le rejet : le motif est communiqué au demandeur.";
        // REQ-09 : annulation par le demandeur ou un gestionnaire.
        if (status == "Annulée" && !IsManager && !string.Equals(e.CreatedBy, Me, StringComparison.OrdinalIgnoreCase))
            return "Seul le demandeur ou un gestionnaire peut annuler la demande.";
        // REQ-11 : la clôture est la confirmation du demandeur (ou d'un gestionnaire) ; sinon automatique.
        if (status == "Close" && e.Status != "Close" && !IsManager && !string.Equals(e.CreatedBy, Me, StringComparison.OrdinalIgnoreCase))
            return "Seul le demandeur ou un gestionnaire confirme la clôture ; sans contestation, elle est automatique 5 jours ouvrés après la satisfaction.";
        // REQ-10 : contestation d'une demande satisfaite — demandeur, bénéficiaire ou gestionnaire,
        // motivée, dans les 5 jours ouvrés.
        if (e.Status == "Satisfaite" && status == "En cours")
        {
            if (!IsManager && !string.Equals(e.CreatedBy, Me, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(e.RequestedFor, Me, StringComparison.OrdinalIgnoreCase))
                return "Seul le demandeur, le bénéficiaire ou un gestionnaire peut contester une demande satisfaite.";
            if (e.ContestReason is null) return "Motiver la contestation : ce qui n'est pas conforme à la demande.";
            if (e.FulfilledAt is { } at && DateTime.UtcNow > Dates.AddBusinessDays(at, ServiceRequest.ContestBusinessDays))
                return $"Délai de contestation dépassé ({ServiceRequest.ContestBusinessDays} jours ouvrés) : soumettre une nouvelle demande.";
        }
        return null;
    }

    // REQ-31 : soumission → approbateurs ; approbation, rejet, satisfaction → demandeur et bénéficiaire.
    protected override void OnNotify(ServiceRequest e, string? from, Notifier n)
    {
        // REQ-21 : une demande pré-approuvée n'attend aucun approbateur.
        if (from == "") { if (e.Status == "Soumise") n.ToManagers(e, LinkType, "Demande à approuver"); return; }
        if (from is null) return;
        // REQ-10 : une contestation → responsable.
        if (from == "Satisfaite" && e.Status == "En cours") { n.ToOwner(e, LinkType, "Demande contestée"); return; }
        var ev = e.Status switch { "Approuvée" => "Demande approuvée", "Rejetée" => "Demande rejetée", "Satisfaite" => "Demande satisfaite", _ => null };
        if (ev is null) return;
        n.ToCreator(e, LinkType, ev);
        n.ToUser(e, LinkType, e.RequestedFor, ev);
    }

    protected override void OnStatusChanged(ServiceRequest e, string from)
    {
        if (e.Status == "Approuvée") { e.ApprovedAt = DateTime.UtcNow; e.ApprovedBy = from == "" && _preApproved ? "Modèle pré-approuvé" : MyDisplay; }
        else if (e.Status is "Soumise" or "Rejetée") { e.ApprovedAt = null; e.ApprovedBy = null; }
        e.FulfilledAt = e.Status is "Satisfaite" or "Close" && e.ApprovedAt is not null
            ? e.FulfilledAt ?? DateTime.UtcNow : null;
        e.ClosedAt = e.Status == "Close" ? DateTime.UtcNow : null;
        e.CancelledAt = e.Status == "Annulée" ? DateTime.UtcNow : null;
        // REQ-10 : le motif d'une contestation vaut jusqu'à la satisfaction suivante (l'historique le garde).
        if (e.Status == "Satisfaite") e.ContestReason = null;
    }

    /// <summary>
    /// REQ-12 : une demande qui révèle un besoin non standard se requalifie — un changement (ou
    /// un incident) est créé, relié à la demande, puis la demande est annulée avec ce motif.
    /// Mêmes droits et statuts que l'annulation (REQ-09) : demandeur ou gestionnaire, avant En cours.
    /// </summary>
    [HttpPost("{id:int}/requalify")]
    public async Task<IActionResult> Requalify(int id, RequalifyDto dto)
    {
        var e = await Db.ServiceRequests.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null || !await VisibleAsync(e)) return NotFound();
        await ScopeAsync();
        ActOn(e.PerimeterId);
        if (!IsManager && !string.Equals(e.CreatedBy, Me, StringComparison.OrdinalIgnoreCase)) return Forbid();
        if (dto.Target is not ("change" or "incident")) return BadRequest(new { message = "Requalifier en changement (« change ») ou en incident (« incident »)." });
        if (e.Status is not ("Soumise" or "Approuvée"))
            return BadRequest(new { message = $"Demande {e.Status.ToLowerInvariant()} : seule une demande Soumise ou Approuvée se requalifie." });
        var reason = dto.Reason?.Trim();
        if (string.IsNullOrEmpty(reason)) return BadRequest(new { message = "Motiver la requalification : ce qui rend le besoin non standard." });

        Core.Records.Record target = dto.Target == "change"
            ? await References.CreateAsync(Db, "CHG", () => new ChangeEnablement.Change
            {
                PerimeterId = e.PerimeterId, Title = e.Title, Description = $"{e.Description}\n\nRequalifié depuis {e.Reference} : {reason}".Trim(),
                Status = "Demandé", ChangeType = "Normal", Risk = "Moyen", CreatedBy = Me, CreatedByDisplayName = MyDisplay
            })
            : await References.CreateAsync(Db, "INC", () => new IncidentManagement.Incident
            {
                PerimeterId = e.PerimeterId, Title = e.Title, Description = $"{e.Description}\n\nRequalifié depuis {e.Reference} : {reason}".Trim(),
                Status = "Nouveau", Impact = "Moyen", Urgency = "Moyenne", Priority = Priority.Compute("Moyen", "Moyenne"),
                ServiceId = e.ServiceId, AffectedService = e.AffectedService, CreatedBy = Me, CreatedByDisplayName = MyDisplay
            });
        Db.ItemLinks.Add(new ItemLink { FromType = LinkType, FromId = e.Id, ToType = dto.Target, ToId = target.Id, CreatedBy = Me });
        var from = e.Status;
        e.Status = "Annulée";
        e.CancelReason = $"Requalifiée en {(dto.Target == "change" ? "changement" : "incident")} {target.Reference} : {reason}";
        if (e.CancelReason.Length > 2000) e.CancelReason = e.CancelReason[..2000];
        OnStatusChanged(e, from);
        e.UpdatedAt = DateTime.UtcNow;
        await Db.SaveChangesAsync();
        var n = Notify;
        n.ToCreator(e, LinkType, "Demande requalifiée");
        if (dto.Target == "change") n.ToManagers(target, "change", "Changement demandé");
        await n.SaveAsync();
        return Ok(new { Request = View(e), Target = new { Type = dto.Target, target.Id, target.Reference } });
    }

    private async Task<(ServiceRequest? Request, IActionResult? Refusal)> OpenAsync(int id)
    {
        var e = await Db.ServiceRequests.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null || !await VisibleAsync(e)) return (null, NotFound());
        await ScopeAsync();
        ActOn(e.PerimeterId);
        return (e, null);
    }

    /// <summary>REQ-24 : tâches d'exécution de la demande, dans l'ordre.</summary>
    [HttpGet("{id:int}/tasks")]
    public async Task<IActionResult> Tasks(int id)
    {
        var (e, refusal) = await OpenAsync(id);
        if (e is null) return refusal!;
        return Ok(await Db.RequestTasks.AsNoTracking().Where(t => t.RequestId == id).OrderBy(t => t.Order).ThenBy(t => t.Id)
            .Select(t => RequestTaskView.From(t)).ToListAsync());
    }

    private static string? CheckTask(RequestTaskDto dto) =>
        string.IsNullOrWhiteSpace(dto.Title) ? "Nommer la tâche."
        : Allowed.Check("Statut de la tâche", dto.Status, RequestTask.Statuses, optional: true);

    /// <summary>REQ-24 : on ajoute une tâche à une demande en cours de traitement.</summary>
    [HttpPost("{id:int}/tasks")]
    public async Task<IActionResult> AddTask(int id, RequestTaskDto dto)
    {
        var (e, refusal) = await OpenAsync(id);
        if (e is null) return refusal!;
        if (e.Status is not ("Approuvée" or "En cours")) return BadRequest(new { message = "Les tâches s'ajoutent à une demande approuvée ou en cours." });
        if (CheckTask(dto) is { } error) return BadRequest(new { message = error });
        var order = (await Db.RequestTasks.Where(t => t.RequestId == id).MaxAsync(t => (int?)t.Order) ?? 0) + 1;
        var task = new RequestTask
        {
            RequestId = id, Title = dto.Title.Trim(), Order = order, CreatedBy = Me, Status = dto.Status ?? RequestTask.ToDo,
            OwnerType = string.IsNullOrWhiteSpace(dto.OwnerId) ? null : dto.OwnerType ?? "User", OwnerId = string.IsNullOrWhiteSpace(dto.OwnerId) ? null : dto.OwnerId.Trim(),
            OwnerDisplayName = string.IsNullOrWhiteSpace(dto.OwnerId) ? null : dto.OwnerDisplayName ?? dto.OwnerId.Trim()
        };
        if (RequestTask.Finished.Contains(task.Status)) task.CompletedAt = DateTime.UtcNow;
        if (Lengths.Check(task) is { } length) return BadRequest(new { message = length });
        Db.RequestTasks.Add(task);
        await Db.SaveChangesAsync();
        return Ok(RequestTaskView.From(task));
    }

    /// <summary>REQ-24 : avancement d'une tâche (statut, responsable, titre), tant que la demande n'est pas satisfaite.</summary>
    [HttpPut("{id:int}/tasks/{taskId:int}")]
    public async Task<IActionResult> UpdateTask(int id, int taskId, RequestTaskDto dto)
    {
        var (e, refusal) = await OpenAsync(id);
        if (e is null) return refusal!;
        var task = await Db.RequestTasks.FirstOrDefaultAsync(t => t.Id == taskId && t.RequestId == id);
        if (task is null) return NotFound();
        if (ServiceRequest.Done.Contains(e.Status)) return BadRequest(new { message = $"Demande {e.Status.ToLowerInvariant()} : ses tâches ne changent plus." });
        if (CheckTask(dto) is { } error) return BadRequest(new { message = error });
        task.Title = dto.Title.Trim();
        (task.OwnerType, task.OwnerId, task.OwnerDisplayName) = string.IsNullOrWhiteSpace(dto.OwnerId) ? (null, null, null)
            : (dto.OwnerType ?? "User", dto.OwnerId.Trim(), dto.OwnerDisplayName ?? dto.OwnerId.Trim());
        var status = dto.Status ?? task.Status;
        task.CompletedAt = RequestTask.Finished.Contains(status) ? task.CompletedAt ?? DateTime.UtcNow : null;
        task.Status = status;
        if (Lengths.Check(task) is { } length) return BadRequest(new { message = length });
        await Db.SaveChangesAsync();
        return Ok(RequestTaskView.From(task));
    }

    /// <summary>Supprimer une tâche ajoutée par erreur : gestionnaire (les tâches du modèle s'annulent).</summary>
    [HttpDelete("{id:int}/tasks/{taskId:int}")]
    public async Task<IActionResult> DeleteTask(int id, int taskId)
    {
        var (e, refusal) = await OpenAsync(id);
        if (e is null) return refusal!;
        if (!IsManager) return Forbid();
        var task = await Db.RequestTasks.FirstOrDefaultAsync(t => t.Id == taskId && t.RequestId == id);
        if (task is null) return NotFound();
        if (task.FromCatalog) return BadRequest(new { message = "Une tâche du modèle ne se supprime pas : l'annuler." });
        Db.RequestTasks.Remove(task);
        await Db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>REQ-32 : le demandeur (ou le bénéficiaire) note la demande close, de 1 à 5, avec un commentaire facultatif.</summary>
    [HttpPost("{id:int}/satisfaction")]
    public async Task<IActionResult> Satisfaction(int id, SatisfactionDto dto)
    {
        var (e, refusal) = await OpenAsync(id);
        if (e is null) return refusal!;
        if (!string.Equals(e.CreatedBy, Me, StringComparison.OrdinalIgnoreCase) && !string.Equals(e.RequestedFor, Me, StringComparison.OrdinalIgnoreCase))
            return StatusCode(403, new { message = "L'enquête de satisfaction est réservée au demandeur et au bénéficiaire." });
        if (e.Status != "Close") return BadRequest(new { message = "L'enquête de satisfaction se remplit une fois la demande close." });
        if (dto.Score is < 1 or > 5) return BadRequest(new { message = "Noter la demande de 1 à 5." });
        (e.SatisfactionScore, e.SatisfactionComment, e.SatisfactionAt) = (dto.Score, string.IsNullOrWhiteSpace(dto.Comment) ? null : dto.Comment.Trim(), DateTime.UtcNow);
        e.UpdatedAt = DateTime.UtcNow;
        await Db.SaveChangesAsync();
        return Ok(View(e));
    }
}

/// <summary>
/// REQ-11 : une demande satisfaite sans contestation depuis 5 jours ouvrés est close
/// automatiquement ; le demandeur est prévenu (et invité à l'enquête, REQ-32).
/// </summary>
public static class RequestAutoClose
{
    public static async Task<int> RunAsync(AppDbContext db, Notifier notify, DateTime now)
    {
        var due = (await db.ServiceRequests.Where(r => r.Status == "Satisfaite" && r.FulfilledAt != null).ToListAsync())
            .Where(r => now > Dates.AddBusinessDays(r.FulfilledAt!.Value, ServiceRequest.ContestBusinessDays)).ToList();
        foreach (var r in due)
        {
            (r.Status, r.ClosedAt, r.AutoClosed, r.UpdatedAt) = ("Close", now, true, now);
            notify.ToCreator(r, "request", "Demande close automatiquement");
        }
        if (due.Count > 0) await db.SaveChangesAsync();
        return due.Count;
    }
}
