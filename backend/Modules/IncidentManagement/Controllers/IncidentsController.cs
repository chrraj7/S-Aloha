using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Data;
using SAloha.Api.Core.Itil;
using SAloha.Api.Core.Links;
using SAloha.Api.Core.Records;
using SAloha.Api.Core.Notifications;

namespace SAloha.Api.Modules.IncidentManagement;

[Route("api/incidents")]
public class IncidentsController(AppDbContext db) : RecordController<Incident, IncidentDto, IncidentView>(db)
{
    protected override IncidentView View(Incident e) =>
        _unresolvedChildren is { Count: > 0 } left ? IncidentView.From(e) with { UnresolvedChildren = left } : IncidentView.From(e);
    protected override string Code => "INC";
    protected override string LinkType => "incident";
    protected override string[] Statuses => Incident.Statuses;
    protected override IReadOnlyDictionary<string, string[]> Transitions => Incident.Transitions;

    protected override string? Apply(Incident e, IncidentDto dto, bool creating)
    {
        var error = Allowed.Check("Impact", dto.Impact, Priority.Impacts)
                    ?? Allowed.Check("Urgence", dto.Urgency, Priority.Urgencies)
                    ?? Allowed.Check("Code de résolution", dto.ResolutionCode, Incident.ResolutionCodes, optional: true)
                    ?? Allowed.Check("Motif d'attente", dto.OnHoldReason, Incident.OnHoldReasons, optional: true)
                    ?? Allowed.Check("Canal", dto.Channel, Incident.Channels, optional: true);
        if (error is not null) return error;

        // INC-13 : seul un gestionnaire déclare ou retire le caractère majeur.
        if (creating && dto.IsMajor && !IsManager)
            return "Seul un gestionnaire peut déclarer un incident majeur.";
        if (!creating && dto.IsMajor != e.IsMajor && !IsManager)
            return "Seul un gestionnaire peut déclarer ou retirer le caractère majeur d'un incident.";

        e.Impact = dto.Impact!; e.Urgency = dto.Urgency!;
        _becameMajor = dto.IsMajor && (creating || !e.IsMajor);
        e.IsMajor = dto.IsMajor;
        // INC-13 : un incident majeur a toujours priorité P1.
        e.Priority = e.IsMajor ? "P1" : Priority.Compute(e.Impact, e.Urgency);

        // INC-13 : un incident majeur a une personne pour responsable (pas un groupe),
        // désignée par un gestionnaire ; les autres champs restent modifiables par tous.
        if (e.IsMajor && e.OwnerType == "Group")
            return "Un incident majeur doit avoir un gestionnaire pour responsable (pas un groupe).";
        if (e.IsMajor && !IsManager && e.OwnerId != OriginalOwner(e, creating))
            return "Seul un gestionnaire désigne le responsable d'un incident majeur.";

        // INC-40 : tout rôle marque un incident de sécurité ; non précisé, la marque reste.
        if (dto.IsSecurity is { } security)
        {
            if (!security && e.IsSecurity && e.QualifiedAt is not null)
                return "Incident qualifié au titre des déclarations réglementaires : il reste un incident de sécurité.";
            e.IsSecurity = security;
        }
        e.Category = dto.Category ?? ""; e.AffectedService = dto.AffectedService ?? ""; e.ServiceId = dto.ServiceId;
        e.Resolution = dto.Resolution;
        e.ResolutionCode = string.IsNullOrEmpty(dto.ResolutionCode) ? null : dto.ResolutionCode;
        e.ReopenReason = string.IsNullOrWhiteSpace(dto.ReopenReason) ? null : dto.ReopenReason.Trim();
        e.OnHoldReason = string.IsNullOrEmpty(dto.OnHoldReason) ? null : dto.OnHoldReason;
        // INC-27 : début constaté, jamais postérieur à la déclaration.
        e.OccurredAt = Dates.Utc(dto.OccurredAt);
        if (e.OccurredAt > (creating ? DateTime.UtcNow : e.CreatedAt).AddMinutes(5))
            return "Le début constaté ne peut pas être postérieur à la déclaration de l'incident.";
        // INC-23 : un changement de responsable exclut la résolution au premier contact.
        if (!creating && e.OwnerId != OriginalOwner(e, creating)) e.OwnerChanged = true;
        // INC-04, INC-05 : canal et utilisateur affecté ; INC-17 : escalade vers un autre groupe motivée.
        e.Channel = string.IsNullOrEmpty(dto.Channel) ? null : dto.Channel;
        var caller = string.IsNullOrWhiteSpace(dto.CallerId) ? null : dto.CallerId.Trim();
        (e.CallerId, e.CallerDisplayName) = (caller, caller is null ? null : dto.CallerDisplayName ?? caller);
        var reason = string.IsNullOrWhiteSpace(dto.EscalationReason) ? null : dto.EscalationReason.Trim();
        if (!creating && e.OwnerType == "Group" && e.OwnerId != OriginalOwner(e, creating))
        {
            if (reason is null || reason == Db.Entry(e).Property(x => x.EscalationReason).OriginalValue)
                return "Motiver l'escalade vers un autre groupe : le motif est tracé dans l'historique.";
        }
        e.EscalationReason = reason;
        return null;
    }

    // SLM-10, INC-20 : service du catalogue (vérifié ici, recopié avant l'écriture).
    // ADM-51 : catégorie tirée de la liste de la pratique ; SLM-10 : service du catalogue.
    protected override async Task<string?> ValidateAsync(Incident e)
    {
        var before = Db.Entry(e).State is Microsoft.EntityFrameworkCore.EntityState.Detached or Microsoft.EntityFrameworkCore.EntityState.Added ? null : Db.Entry(e).Property(x => x.Category).OriginalValue;
        var (error, value) = await Core.Itil.Category.CheckAsync(Db, "incident", e.Category, before);
        e.Category = value;
        return error ?? await ServiceCatalog.ResolveAsync(Db, e) ?? await MajorOwnerAsync(e);
    }

    /// <summary>
    /// INC-13 : le responsable d'un incident majeur est gestionnaire du périmètre de l'incident
    /// (rôle par périmètre, PER-03 ; Admin partout). Vérifié quand il est désigné ou que
    /// l'incident devient majeur. Annuaire injoignable : la désignation passe (tracée au
    /// journal) pour ne pas bloquer la gestion d'une crise.
    /// </summary>
    private async Task<string?> MajorOwnerAsync(Incident e)
    {
        if (!e.IsMajor || e.OwnerType != "User" || string.IsNullOrWhiteSpace(e.OwnerId)) return null;
        var creating = e.Id == 0;
        var entry = Db.Entry(e);
        if (!creating && entry.Property(x => x.IsMajor).OriginalValue && OriginalOwner(e, false) == e.OwnerId) return null;
        var directory = HttpContext.RequestServices.GetRequiredService<SAloha.Api.Core.Auth.IUserDirectory>();
        bool? manager;
        try { manager = await SAloha.Api.Core.Perimeters.PerimeterAccess.IsManagerAsync(Db, directory, e.OwnerId, e.PerimeterId); }
        catch (Exception ex)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<IncidentsController>>()
                .LogWarning(ex, "Annuaire injoignable : rôle du responsable {Owner} de l'incident majeur non vérifié", e.OwnerId.Replace("\n", ""));
            return null;
        }
        return manager == true ? null
            : $"Le responsable d'un incident majeur doit être gestionnaire du périmètre de l'incident : {e.OwnerDisplayName ?? e.OwnerId} ne l'est pas.";
    }

    /// <summary>Responsable avant la modification (aucun à la création).</summary>
    private string? OriginalOwner(Incident e, bool creating) =>
        creating ? null : Db.Entry(e).Property(x => x.OwnerId).OriginalValue;

    protected override async Task<string?> CheckStatusAsync(Incident e, string status)
    {
        // INC-07 : l'attente est motivée (motif fermé) ; vérifié à l'entrée En attente, pour qu'un
        // incident déjà en attente avant la règle reste modifiable.
        if (status == "En attente" && e.Status != "En attente" && e.OnHoldReason is null)
            return $"Choisir le motif de l'attente : {string.Join(", ", Incident.OnHoldReasons)}.";

        // INC-06 : la prise en charge suppose un responsable, utilisateur ou groupe.
        if (status == "En cours" && string.IsNullOrWhiteSpace(e.OwnerId))
            return "Désigner un responsable (utilisateur ou groupe) pour prendre l'incident en charge.";

        // INC-11 : réouverture d'un incident Résolu dans les 10 jours ouvrés, avec motif.
        if (e.Status == "Résolu" && status == "En cours")
        {
            if (e.ResolvedAt is not null && DateTime.UtcNow > Dates.AddBusinessDays(e.ResolvedAt.Value, 10))
                return "Délai de réouverture dépassé (10 jours ouvrés). Créer un nouvel incident et le relier à celui-ci.";
            if (string.IsNullOrWhiteSpace(e.ReopenReason))
                return "Motiver la réouverture de l'incident.";
        }

        if (!Incident.Done.Contains(status)) return null;
        // INC-43 : un incident cyber ne se clôt qu'avec des déclarations closes ou non requises.
        if (status == "Clos" && e.IsSecurity)
        {
            var open = await Db.RegulatoryNotifications.AsNoTracking()
                .Where(d => d.IncidentId == e.Id && !RegulatoryNotifications.RegulatoryNotification.Done.Contains(d.Status))
                .Select(d => d.Reference).ToListAsync();
            if (open.Count > 0)
                return $"Déclarations réglementaires en cours ({string.Join(", ", open)}) : les clore ou les déclarer non requises avant de clore l'incident.";
        }
        if (string.IsNullOrWhiteSpace(e.Resolution))
            return "Décrire la résolution avant de résoudre ou clore l'incident.";
        // INC-15 : un incident majeur se résout relié à un problème (la revue s'y conduit).
        if (status == "Résolu" && e.Status != "Résolu" && e.IsMajor && !await ItemLinks.IsLinkedToAsync(Db, "incident", e.Id, "problem"))
            return "Incident majeur : le relier à un problème (existant, ou « Ouvrir un problème lié ») avant de le résoudre : la revue de l'incident majeur s'y conduit.";
        // INC-08 : code de résolution ; un doublon est relié à l'incident conservé.
        if (e.ResolutionCode is null)
            return $"Choisir un code de résolution : {string.Join(", ", Incident.ResolutionCodes)}.";
        if (e.ResolutionCode == "Doublon" && !await ItemLinks.IsLinkedToAsync(Db, "incident", e.Id, "incident"))
            return "Un doublon doit être relié à l'incident conservé (onglet Liens).";
        return null;
    }

    /// <summary>INC-21 : échéance recalculée à la création et quand la priorité ou le service change.</summary>
    protected override async Task BeforeSaveAsync(Incident e, bool creating)
    {
        await ServiceCatalog.ResolveAsync(Db, e);   // SLM-10, INC-20
        if (creating) e.Category = (await Core.Itil.Category.CheckAsync(Db, "incident", e.Category, null)).Value;   // ADM-51
        var entry = Db.Entry(e);
        // INC-22 : l'horloge SLA s'arrête En attente ; à la reprise, l'attente (en heures de
        // service) prolonge les échéances. Le motif ne vaut que pendant l'attente (INC-07).
        var from = creating ? null : entry.Property(x => x.Status).OriginalValue;
        var resumed = false;
        if (e.Status == "En attente" && from != "En attente") e.OnHoldSince = DateTime.UtcNow;
        else if (from == "En attente" && e.Status != "En attente")
        {
            if (e.OnHoldSince is { } since)
            {
                var held = (await IncidentSla.CalendarAsync(Db, e.ServiceId)).Between(since, DateTime.UtcNow);
                e.HeldMinutes += (int)Math.Round(held.TotalMinutes);
            }
            e.OnHoldSince = null; resumed = true;
        }
        if (e.Status != "En attente") e.OnHoldReason = null;
        if (!creating && e.Status == "Résolu" && from is not null && !Incident.Done.Contains(from)) await ResolveChildrenAsync(e);
        if (creating || resumed || entry.Property(x => x.Priority).IsModified || entry.Property(x => x.AffectedService).IsModified
            || entry.Property(x => x.ServiceId).IsModified || e.ResolutionDueAt is null)
        {
            e.ResolutionDueAt = await IncidentSla.DueAsync(Db, e);
            e.ResponseDueAt = await IncidentSla.ResponseDueAsync(Db, e);
        }
    }

    /// <summary>
    /// INC-16 : résoudre le parent résout ses enfants ouverts avec la même résolution et le même
    /// code ; l'attente d'un enfant prend fin (son temps compte comme à une reprise, INC-22).
    /// Le journal d'audit trace chaque enfant ; déclarant et utilisateur affecté sont notifiés.
    /// Un enfant majeur sans problème relié reste ouvert (INC-15 sans exception, décision du
    /// 2026-10-26) ; la réponse le liste avec la raison.
    /// </summary>
    private async Task ResolveChildrenAsync(Incident parent)
    {
        var now = DateTime.UtcNow;
        foreach (var child in await Db.Incidents.Where(c => c.ParentId == parent.Id && !Incident.Done.Contains(c.Status)).ToListAsync())
        {
            if (child.IsMajor && !await ItemLinks.IsLinkedToAsync(Db, "incident", child.Id, "problem"))
            {
                _unresolvedChildren.Add(new(child.Reference, "Incident majeur sans problème relié (INC-15) : le relier à un problème, puis le résoudre."));
                continue;
            }
            if (child.Status == "En attente" && child.OnHoldSince is { } since)
                child.HeldMinutes += (int)Math.Round((await IncidentSla.CalendarAsync(Db, child.ServiceId)).Between(since, now).TotalMinutes);
            var from = child.Status;
            (child.Status, child.Resolution, child.ResolutionCode) = ("Résolu", parent.Resolution, parent.ResolutionCode);
            (child.OnHoldSince, child.OnHoldReason, child.UpdatedAt) = (null, null, now);
            OnStatusChanged(child, from);
            Notify.ToCreator(child, LinkType, "Incident résolu");
            Notify.ToUser(child, LinkType, child.CallerId, "Incident résolu");
        }
    }

    private bool _becameMajor;

    /// <summary>INC-16 : enfants que la résolution du parent a laissés ouverts.</summary>
    private readonly List<UnresolvedChildView> _unresolvedChildren = [];

    // INC-25 : incident majeur déclaré → gestionnaires ; résolution → déclarant.
    protected override void OnNotify(Incident e, string? from, Notifier n)
    {
        if (_becameMajor) n.ToManagers(e, LinkType, "Incident majeur déclaré");
        // INC-05 : l'utilisateur affecté reçoit les notifications publiques, comme le déclarant.
        if (from is not null && e.Status == "Résolu") { n.ToCreator(e, LinkType, "Incident résolu"); n.ToUser(e, LinkType, e.CallerId, "Incident résolu"); }
    }

    protected override void OnStatusChanged(Incident e, string from)
    {
        // INC-23 : fixé à la première résolution ; une réouverture l'annule.
        if (Incident.Done.Contains(e.Status) && !Incident.Done.Contains(from))
            e.FirstContactResolution = e.FirstContactResolution ?? (e.FirstResponseAt is null && !e.OwnerChanged && e.ReopenCount == 0);
        if (from == "Résolu" && e.Status == "En cours") e.FirstContactResolution = false;
        // INC-11 : réouverture d'un incident résolu incrémente ReopenCount ; le motif
        // est effacé à la résolution suivante (l'historique le garde), pour qu'une
        // nouvelle réouverture en exige un nouveau.
        if (from == "Résolu" && e.Status == "En cours")
            e.ReopenCount++;
        if (e.Status == "Résolu") e.ReopenReason = null;

        // Rouvert, un incident n'est plus résolu : ses dates sont effacées
        // pour ne pas fausser le MTTR (même règle que les problèmes, M4).
        e.ResolvedAt = Incident.Done.Contains(e.Status) ? e.ResolvedAt ?? DateTime.UtcNow : null;
        // Première prise en charge : posée une fois, jamais effacée (délai de prise en charge, INC-21).
        if (e.Status == "En cours") e.FirstResponseAt ??= DateTime.UtcNow;
        e.ClosedAt = e.Status == "Clos" ? DateTime.UtcNow : null;
    }

    /// <summary>INC-16 : parent et enfants d'un incident (enfants visibles seulement).</summary>
    [HttpGet("{id:int}/family")]
    public async Task<IActionResult> Family(int id)
    {
        var e = await Db.Incidents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (e is null || !await VisibleAsync(e)) return NotFound();
        IncidentRelative Of(Incident i) => new(i.Id, i.Reference, i.Title, i.Status, i.Priority);
        var parent = e.ParentId is int pid ? await Db.Incidents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == pid) : null;
        var children = new List<IncidentRelative>();
        foreach (var c in await Db.Incidents.AsNoTracking().Where(x => x.ParentId == id).OrderBy(x => x.CreatedAt).ToListAsync())
            if (await VisibleAsync(c)) children.Add(Of(c));
        return Ok(new { Parent = parent is not null && await VisibleAsync(parent) ? Of(parent) : null, Children = children });
    }

    /// <summary>
    /// INC-16 : rattacher un incident à un parent (même panne), par sa référence, ou l'en détacher.
    /// Un seul niveau : le parent n'a pas de parent, l'enfant n'a pas d'enfants ; même périmètre ;
    /// ni soi-même ni un incident clos ; un incident résolu ou clos ne change plus de parent.
    /// </summary>
    [HttpPut("{id:int}/parent")]
    public async Task<IActionResult> SetParent(int id, IncidentParentDto dto)
    {
        var e = await Db.Incidents.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null || !await VisibleAsync(e)) return NotFound();
        await ScopeAsync();
        ActOn(e.PerimeterId);
        if (Incident.Done.Contains(e.Status)) return BadRequest(new { message = "Incident résolu ou clos : son rattachement ne change plus." });
        var reference = dto.ParentReference?.Trim();
        if (string.IsNullOrEmpty(reference)) e.ParentId = null;
        else
        {
            var parent = await Db.Incidents.FirstOrDefaultAsync(x => x.Reference.ToLower() == reference.ToLower());
            var error = parent is null || !await VisibleAsync(parent) ? (string?)$"Incident {reference} introuvable."
                : parent.Id == e.Id ? "Un incident ne peut pas être son propre parent."
                : parent.PerimeterId != e.PerimeterId ? "Le parent doit être dans le même périmètre que l'incident."
                : parent.Status == "Clos" ? "Le parent est clos : rattacher à un incident ouvert."
                : parent.ParentId is not null ? $"{parent.Reference} est lui-même rattaché à un parent : rattacher à ce dernier."
                : await Db.Incidents.AnyAsync(x => x.ParentId == e.Id) ? "Cet incident a des enfants : il ne peut pas être rattaché à un parent."
                : null;
            if (error is not null) return BadRequest(new { message = error });
            e.ParentId = parent!.Id;
        }
        e.UpdatedAt = DateTime.UtcNow;
        await Db.SaveChangesAsync();
        return Ok(View(e));
    }

    /// <summary>
    /// INC-41, INC-42 : qualification réglementaire d'un incident cyber par un gestionnaire.
    /// Chaque régime retenu (NIS 2 si important, RGPD si violation de données) reçoit une
    /// déclaration À déclarer reliée à l'incident ; « non » sur un régime exige un motif.
    /// INC-44 : un incident important au sens NIS 2 est proposé comme incident majeur.
    /// </summary>
    [HttpPost("{id:int}/qualification")]
    public async Task<IActionResult> Qualify(int id, IncidentQualificationDto dto)
    {
        var e = await Db.Incidents.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null || !await VisibleAsync(e)) return NotFound();
        await ScopeAsync();
        ActOn(e.PerimeterId);
        if (!IsManager) return Forbid();
        if (!e.IsSecurity) return BadRequest(new { message = "Marquer d'abord l'incident comme incident de sécurité (cyber)." });
        if (!dto.Nis2Important && string.IsNullOrWhiteSpace(dto.Nis2Reason))
            return BadRequest(new { message = "Motiver pourquoi l'incident n'est pas important au sens de NIS 2." });
        if (!dto.PersonalDataBreach && string.IsNullOrWhiteSpace(dto.PersonalDataBreachReason))
            return BadRequest(new { message = "Motiver pourquoi l'incident n'est pas une violation de données à caractère personnel." });
        var awareAt = Dates.Utc(dto.AwareAt) ?? e.CreatedAt;
        if (awareAt > DateTime.UtcNow.AddMinutes(5)) return BadRequest(new { message = "La prise de connaissance ne peut pas être dans le futur." });

        (e.Nis2Important, e.Nis2Reason) = (dto.Nis2Important, dto.Nis2Reason?.Trim());
        (e.PersonalDataBreach, e.PersonalDataBreachReason) = (dto.PersonalDataBreach, dto.PersonalDataBreachReason?.Trim());
        (e.QualifiedAt, e.QualifiedBy) = (DateTime.UtcNow, MyDisplay);
        await Db.SaveChangesAsync();

        var regimes = new List<string>();
        if (dto.Nis2Important) regimes.Add(RegulatoryNotifications.RegulatoryNotification.Nis2);
        if (dto.PersonalDataBreach) regimes.Add(RegulatoryNotifications.RegulatoryNotification.Gdpr);
        var existing = await Db.RegulatoryNotifications.Where(d => d.IncidentId == id).Select(d => d.Regime).ToListAsync();
        var created = new List<string>();
        foreach (var regime in regimes.Except(existing))
        {
            var declaration = await References.CreateAsync(Db, "DRG", () =>
            {
                var d = new RegulatoryNotifications.RegulatoryNotification
                {
                    PerimeterId = e.PerimeterId, IncidentId = id, Regime = regime,
                    Authority = RegulatoryNotifications.RegulatoryNotification.AuthorityOf(regime), AwareAt = awareAt,
                    Title = $"{regime} — {e.Reference} {e.Title}", Status = RegulatoryNotifications.RegulatoryNotification.ToDeclare,
                    OwnerType = "User", OwnerId = Me, OwnerDisplayName = MyDisplay, CreatedBy = Me, CreatedByDisplayName = MyDisplay
                };
                d.ComputeDueDates();
                return d;
            });
            Db.ItemLinks.Add(new ItemLink { FromType = "notification", FromId = declaration.Id, ToType = "incident", ToId = id, CreatedBy = Me });
            Notify.ToManagers(declaration, "notification", "Déclaration réglementaire créée");   // DRG-13
            created.Add(declaration.Reference);
        }
        await Db.SaveChangesAsync();
        await Notify.SaveAsync();
        return Ok(new { Incident = View(e), Created = created, ProposeMajor = dto.Nis2Important && !e.IsMajor });
    }
}
