using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Audit;
using SAloha.Api.Core.Auth;
using SAloha.Api.Core.Data;
using SAloha.Api.Core.Links;
using SAloha.Api.Core.Notifications;
using SAloha.Api.Core.Perimeters;
using System.Linq.Expressions;

namespace SAloha.Api.Core.Records;

/// <summary>
/// API CRUD commune des processus : liste filtrée, détail, création avec
/// référence, modification avec contrôle des statuts, suppression (Admin).
/// Chaque module ne décrit que ce qui lui est propre (champs, statuts,
/// statuts réservés aux gestionnaires, effets d'un changement de statut).
/// </summary>
[ApiController]
[Authorize(Policy = "User")]
public abstract class RecordController<T, TDto, TView>(AppDbContext db) : ControllerBase
    where T : Record, new()
    where TDto : IRecordDto
{
    /// <summary>A2 (ADR-0016) : la réponse de l'API pour cet enregistrement — jamais l'entité EF elle-même.</summary>
    protected abstract TView View(T e);

    protected AppDbContext Db => db;
    protected string Me => User.Identity?.Name ?? "";
    protected string MyDisplay => User.FindFirst("displayName")?.Value ?? Me;
    /// <summary>
    /// Gestionnaire sur le périmètre de l'enregistrement traité (PER-06) : Manager
    /// de ce périmètre, ou Admin. Hors d'une action sur un enregistrement, le rôle AD.
    /// </summary>
    protected bool IsManager => _scope is not null && _perimeter is not null
        ? _scope.ManagerOn(_perimeter.Value)
        : User.IsInRole("Manager") || User.IsInRole("Admin");

    private PerimeterScope? _scope;
    private int? _perimeter;
    /// <summary>Portée de l'utilisateur (PER-03), chargée une fois par requête.</summary>
    protected async Task<PerimeterScope> ScopeAsync() => _scope ??= await Access.GetAsync();
    protected PerimeterAccess Access => HttpContext.RequestServices.GetRequiredService<PerimeterAccess>();
    /// <summary>
    /// Référentiel partagé (ADR-0014 D2) : enregistrements visibles de tous, quel que soit
    /// leur périmètre (catalogue des services, articles publiés) ; null : aucun.
    /// </summary>
    protected virtual Expression<Func<T, bool>>? SharedWithAll => null;
    /// <summary>Visible de l'utilisateur : dans ses périmètres, ou partagé (PER-05).</summary>
    protected async Task<bool> VisibleAsync(T e) =>
        (await ScopeAsync()).Sees(e.PerimeterId) || (SharedWithAll?.Compile()(e) ?? false);
    /// <summary>Pour les routes propres d'un module (impact, questionnaire…) : l'enregistrement est-il visible ?</summary>
    protected async Task<bool> VisibleAsync(int id)
    {
        var e = await db.Set<T>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return e is not null && await VisibleAsync(e);
    }
    /// <summary>Enregistrements visibles de l'utilisateur (PER-05).</summary>
    protected async Task<IQueryable<T>> InScopeAsync(IQueryable<T> q)
    {
        var scope = await ScopeAsync();
        if (scope.Global) return q;
        var ids = scope.Ids.ToList();
        // Une seule instance : chaque lecture de SharedWithAll construit une nouvelle expression.
        var shared = SharedWithAll;
        if (shared is null) return q.Where(x => ids.Contains(x.PerimeterId));
        var p = shared.Parameters[0];
        var inScope = Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [typeof(int)],
            Expression.Constant(ids), Expression.Property(p, nameof(Record.PerimeterId)));
        return q.Where(Expression.Lambda<Func<T, bool>>(Expression.OrElse(inScope, shared.Body), p));
    }
    /// <summary>Les règles de gestionnaire valent désormais sur ce périmètre (PER-06).</summary>
    protected void ActOn(int perimeterId) => _perimeter = perimeterId;

    /// <summary>Préfixe de référence (INC, CHG…).</summary>
    protected abstract string Code { get; }
    /// <summary>Type de l'enregistrement dans les liens inter-processus.</summary>
    protected abstract string LinkType { get; }
    /// <summary>Statuts admis ; le premier est le statut de création par défaut.</summary>
    protected abstract string[] Statuses { get; }
    /// <summary>Référentiel : création et modification réservées aux gestionnaires.</summary>
    protected virtual bool ManagerOnly => false;
    /// <summary>Droit de créer : par défaut, tous les rôles, ou les gestionnaires si <see cref="ManagerOnly"/>.</summary>
    protected virtual bool CanCreate() => !ManagerOnly || IsManager;
    /// <summary>
    /// Droit de modifier cet enregistrement, évalué sur les valeurs enregistrées
    /// (avant la recopie du DTO) : un projet se modifie par son chef de projet,
    /// qui n'est pas forcément gestionnaire.
    /// </summary>
    protected virtual bool CanEdit(T e) => !ManagerOnly || IsManager;

    /// <summary>Recopie les champs propres au processus : message d'erreur (400) ou null.</summary>
    protected abstract string? Apply(T e, TDto dto, bool creating);
    /// <summary>Contrôles nécessitant la base (existence d'un parent…).</summary>
    protected virtual Task<string?> ValidateAsync(T e) => Task.FromResult<string?>(null);
    /// <summary>Statut de création : le premier statut, sauf règle propre au processus.</summary>
    protected virtual string InitialStatus(T e) => Statuses[0];
    /// <summary>
    /// Doublon interdit (ex. deux CI de même nom dans un environnement) : message
    /// d'erreur, renvoyé en 409, ou null. Vérifié à la création et à chaque modification.
    /// </summary>
    protected virtual Task<string?> ConflictAsync(T e) => Task.FromResult<string?>(null);
    /// <summary>Inventaires (CI, services) : le statut de création est choisi à la saisie.</summary>
    protected virtual bool StatusChosenAtCreation => false;
    /// <summary>
    /// Transitions permises (SOC-05), tableau § 4 de chaque pratique : pour chaque
    /// statut, les statuts accessibles. Null : transitions libres (SOC-04). Les
    /// retours décidés par l'API (StatusAfterEdit) ne passent pas par ce graphe.
    /// </summary>
    protected virtual IReadOnlyDictionary<string, string[]>? Transitions => null;

    private static readonly string[] ClosedNames = ["Clos", "Close", "Rejeté", "Rejetée", "Annulée", "Réalisée", "Abandonnée", "Retiré"];

    /// <summary>
    /// SOC-15 : statuts finaux de la pratique — sans transition sortante, ou de clôture (Clos,
    /// Rejeté, Annulée…). Un enregistrement terminé ne se modifie que par un gestionnaire ; un
    /// User le rouvre par une transition permise.
    /// </summary>
    protected virtual IReadOnlyCollection<string> FinalStatuses => Transitions is null ? []
        : Statuses.Where(s => ClosedNames.Contains(s) || Transitions.TryGetValue(s, out var next) && next.Length == 0).ToList();
    /// <summary>Statuts que seul un gestionnaire peut poser (autoriser, approuver, publier…).</summary>
    protected virtual bool RequiresManager(string status) => false;
    /// <summary>
    /// Variante selon le statut d'origine (réactiver un article archivé est une décision
    /// de gestionnaire, le remettre en brouillon après une retouche ne l'est pas).
    /// </summary>
    protected virtual bool RequiresManager(string from, string to) => RequiresManager(to);
    /// <summary>
    /// Conditions du statut <paramref name="status"/> (résolution renseignée,
    /// autorisation obtenue…) : message d'erreur (400) ou null. Vérifiées à
    /// l'entrée dans le statut ET à chaque modification ultérieure : vider la
    /// résolution d'un incident résolu est refusé comme le serait sa résolution
    /// sans description. <c>e.Status</c> est encore le statut actuel.
    /// </summary>
    protected virtual string? CheckStatus(T e, string status) => null;
    /// <summary>
    /// Variante de <see cref="CheckStatus"/> pour les conditions qui
    /// interrogent la base (toutes les exigences d'une évaluation répondues…).
    /// </summary>
    protected virtual Task<string?> CheckStatusAsync(T e, string status) => Task.FromResult(CheckStatus(e, status));
    /// <summary>
    /// Statut auquel une modification faite par un non-gestionnaire ramène
    /// l'enregistrement, ou null : un article publié retouché repasse en
    /// brouillon, un changement autorisé modifié redemande une autorisation.
    /// Appelé avant la recopie du DTO : <c>e</c> porte encore les valeurs
    /// enregistrées. La modification d'un gestionnaire vaut validation, sauf ce que
    /// <see cref="StatusAfterManagerEditAsync"/> décide.
    /// </summary>
    protected virtual string? StatusAfterEdit(T e, TDto dto) => null;
    /// <summary>
    /// Variante pour un gestionnaire : les cas où sa modification ne vaut pas validation
    /// (changement renvoyé au CAB, demandeur qui réécrit son propre changement…). Appelée,
    /// comme <see cref="StatusAfterEdit"/>, avant la recopie du DTO.
    /// </summary>
    protected virtual Task<string?> StatusAfterManagerEditAsync(T e, TDto dto) => Task.FromResult<string?>(null);
    /// <summary>Effets d'un changement de statut (horodatages) ; <c>from</c> vide à la création.</summary>
    protected virtual void OnStatusChanged(T e, string from) { }
    /// <summary>
    /// Juste avant l'enregistrement (création et modification) : valeurs calculées
    /// qui interrogent la base (échéance SLA d'un incident, INC-21).
    /// </summary>
    protected virtual Task BeforeSaveAsync(T e, bool creating) => Task.CompletedTask;
    /// <summary>Notifications de l'enregistrement (SOC-21), envoyées après l'enregistrement.</summary>
    protected Notifier Notify => HttpContext.RequestServices.GetRequiredService<Notifier>();
    /// <summary>
    /// Événements propres à la pratique (§ 6 de son document) : <paramref name="from"/> est le
    /// statut quitté (null s'il n'a pas changé, vide à la création). L'affectation au
    /// responsable est notifiée par le socle.
    /// </summary>
    protected virtual void OnNotify(T e, string? from, Notifier n) { }

    /// <summary>SOC-21 : affectation au nouveau responsable, puis événements de la pratique.</summary>
    private async Task NotifyAsync(T e, string? from, (string? Type, string? Id) ownerBefore)
    {
        var n = Notify;
        if (!string.IsNullOrWhiteSpace(e.OwnerId)
            && (!string.Equals(e.OwnerId, ownerBefore.Id, StringComparison.OrdinalIgnoreCase) || e.OwnerType != ownerBefore.Type))
            n.ToOwner(e, LinkType, e.OwnerType == "Group" ? "Confié à votre groupe" : "Confié à vous");
        OnNotify(e, from, n);
        await n.SaveAsync();
    }

    /// <summary>Après la création (référence attribuée, enregistré) : éléments dépendants à créer.</summary>
    protected virtual Task OnCreatedAsync(T e) => Task.CompletedTask;
    protected virtual IQueryable<T> Filter(IQueryable<T> q) => q;
    protected virtual IQueryable<T> Search(IQueryable<T> q, string key) =>
        q.Where(x => x.Title.ToLower().Contains(key) || x.Reference.ToLower().Contains(key));
    protected virtual IQueryable<T> WithDetails(IQueryable<T> q) => q;
    /// <summary>Lignes du registre : un module allège ici ses champs volumineux (contenu, plans).</summary>
    protected virtual async Task<IEnumerable<object>> ListItems(IQueryable<T> q) => (await q.ToListAsync()).Select(e => (object)View(e)!);

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? q, [FromQuery] string? owner,
        [FromQuery] int? perimeter)
    {
        var query = Filter(await InScopeAsync(db.Set<T>().AsNoTracking()));
        if (perimeter is not null) query = query.Where(x => x.PerimeterId == perimeter);
        if (!string.IsNullOrEmpty(status)) query = query.Where(x => x.Status == status);
        // Insensible à la casse, comme l'annuaire (M2, M5).
        if (!string.IsNullOrWhiteSpace(q)) query = Search(query, q.Trim().ToLower());
        if (!string.IsNullOrEmpty(owner))
        {
            if (owner == "me")
            {
                // SOC-10 : « les miens » comprend ce qui est confié à l'un de mes groupes AD.
                var me = User.MeKey();
                var groups = User.GroupKeys();
                query = query.Where(x => x.OwnerId != null && (x.OwnerType == "Group"
                    ? groups.Contains(x.OwnerId.ToLower()) : x.OwnerId.ToLower() == me));
            }
            else
            {
                var key = owner.ToLower();
                query = query.Where(x => x.OwnerId != null && x.OwnerId.ToLower() == key);
            }
        }
        return Ok(await ListItems(query.OrderByDescending(x => x.CreatedAt)));
    }

    /// <summary>Graphe des transitions permises, pour proposer les seuls statuts accessibles.</summary>
    [HttpGet("transitions")]
    public IActionResult GetTransitions() => Ok(Transitions ?? StatusGraph.Free(Statuses));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TView>> Get(int id)
    {
        var e = await WithDetails(db.Set<T>().AsNoTracking()).FirstOrDefaultAsync(x => x.Id == id);
        // Hors de ses périmètres, l'enregistrement n'existe pas pour l'utilisateur (PER-05).
        return e is null || !await VisibleAsync(e) ? NotFound() : Ok(View(e));
    }

    [HttpPost]
    public async Task<ActionResult<TView>> Create(TDto dto)
    {
        // PER-04 : périmètre obligatoire, proposé quand l'utilisateur n'en a qu'un ; on ne
        // crée que dans un périmètre où l'on est affecté.
        await ScopeAsync();
        var (perimeterId, refused, forbidden) = await Access.ChooseAsync(dto.PerimeterId);
        if (forbidden) return Forbid();
        if (refused is not null) return BadRequest(new { message = refused });
        ActOn(perimeterId!.Value);
        if (!CanCreate()) return Forbid();
        var probe = new T { PerimeterId = perimeterId.Value };
        var error = Fill(probe, dto, true) ?? await ValidateAsync(probe)
            ?? (StatusChosenAtCreation ? Allowed.Check("Statut", dto.Status, Statuses, optional: true) : null);
        if (error is not null) return BadRequest(new { message = error });
        // Les conditions du statut de création s'appliquent aussi (CI En service : propriétaire, CFG-11).
        var initial = StatusChosenAtCreation && !string.IsNullOrEmpty(dto.Status) ? dto.Status : InitialStatus(probe);
        error = await CheckStatusAsync(probe, initial);
        if (error is not null) return BadRequest(new { message = error });
        error = await ConflictAsync(probe);
        if (error is not null) return Conflict(new { message = error });

        var e = await References.CreateAsync(db, Code, () =>
        {
            var x = new T { PerimeterId = perimeterId.Value };
            Fill(x, dto, true);
            x.Status = initial;
            x.CreatedBy = Me; x.CreatedByDisplayName = MyDisplay;
            OnStatusChanged(x, "");
            return x;
        });
        await BeforeSaveAsync(e, true);
        await db.SaveChangesAsync();
        await OnCreatedAsync(e);
        await NotifyAsync(e, "", (null, null));
        return CreatedAtAction(nameof(Get), new { id = e.Id }, View(e));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<TView>> Update(int id, TDto dto)
    {
        var e = await db.Set<T>().FindAsync(id);
        if (e is null || !await VisibleAsync(e)) return NotFound();
        var scope = await ScopeAsync();
        // Partagé mais hors de ses périmètres (article publié d'une autre entité) : lecture seule.
        if (!scope.Sees(e.PerimeterId)) return Forbid();
        ActOn(e.PerimeterId);
        if (!CanEdit(e)) return Forbid();
        // SOC-15 : un enregistrement terminé se modifie par un gestionnaire ; un User le rouvre.
        if (!IsManager && FinalStatuses.Contains(e.Status) && (dto.Status is null || dto.Status == e.Status))
            return StatusCode(403, new { message = $"{e.Reference} est {e.Status.ToLowerInvariant()} : seul un gestionnaire le modifie ; le rouvrir pour le reprendre, si la pratique le permet." });
        // PER-07 : changer de périmètre exige d'être gestionnaire des deux (ou Admin).
        var moveTo = dto.PerimeterId is { } wanted && wanted != e.PerimeterId ? wanted : (int?)null;
        if (moveTo is not null)
        {
            var (moveError, moveForbidden) = await Access.CheckMoveAsync(e.PerimeterId, moveTo.Value);
            if (moveForbidden) return Forbid();
            if (moveError is not null) return BadRequest(new { message = moveError });
        }
        var demoted = IsManager ? await StatusAfterManagerEditAsync(e, dto) : StatusAfterEdit(e, dto);
        var ownerBefore = (e.OwnerType, e.OwnerId);
        string? left = null;
        var error = Fill(e, dto, false) ?? await ValidateAsync(e);
        if (error is not null) return BadRequest(new { message = error });
        error = await ConflictAsync(e);
        if (error is not null) return Conflict(new { message = error });

        var target = demoted ?? dto.Status ?? e.Status;
        var changing = target != e.Status;
        if (changing && demoted is null)
        {
            error = Allowed.Check("Statut", target, Statuses);
            if (error is not null) return BadRequest(new { message = error });
            var (refused, forbidden) = ForcedTransition.Apply(HttpContext,
                Transitions is null ? null : StatusGraph.Check(Transitions, e.Status, target));
            if (forbidden) return Forbid();
            if (refused is not null) return BadRequest(new { message = refused });
            if (RequiresManager(e.Status, target) && !IsManager) return Forbid();
        }
        error = await CheckStatusAsync(e, target);
        if (error is not null) return BadRequest(new { message = error });
        if (changing)
        {
            var from = e.Status;
            e.Status = target;
            OnStatusChanged(e, from);
            left = from;
        }
        if (moveTo is not null) e.PerimeterId = moveTo.Value;
        e.UpdatedAt = DateTime.UtcNow;
        await BeforeSaveAsync(e, false);
        await db.SaveChangesAsync();
        // SOC-23 : la lecture filtre sur le périmètre copié dans la notification ; il suit le déplacement.
        if (moveTo is not null)
            await db.Notifications.Where(n => n.TargetType == LinkType && n.TargetId == id)
                .ExecuteUpdateAsync(u => u.SetProperty(n => n.PerimeterId, moveTo.Value));
        await NotifyAsync(e, left, ownerBefore);
        return Ok(View(e));
    }

    /// <summary>
    /// SOC-14 : ce que la suppression emporterait — liens, commentaires, notifications et
    /// dépendants supprimés en cascade (analyses, relations, accords…) — pour la confirmation.
    /// </summary>
    [HttpGet("{id:int}/deletion")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> DeletionPreview(int id)
    {
        var e = await db.Set<T>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        var links = await db.ItemLinks.CountAsync(l => (l.FromType == LinkType && l.FromId == id) || (l.ToType == LinkType && l.ToId == id));
        var comments = await db.Comments.CountAsync(c => c.TargetType == LinkType && c.TargetId == id);
        var attachments = await db.Attachments.CountAsync(a => a.TargetType == LinkType && a.TargetId == id);
        var notifications = await db.Notifications.CountAsync(n => n.TargetType == LinkType && n.TargetId == id);
        var impacts = (await Deletion.CascadeAsync<T>(db, id))
            .Prepend(new Deletion.Impact("notifications", notifications))
            .Prepend(new Deletion.Impact("pièces jointes", attachments))
            .Prepend(new Deletion.Impact("commentaires", comments))
            .Prepend(new Deletion.Impact("liens vers d'autres enregistrements", links))
            .Where(i => i.Count > 0).ToList();
        return Ok(new { e.Id, e.Reference, e.Title, Impacts = impacts });
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var e = await db.Set<T>().FindAsync(id);
        if (e is null) return NotFound();
        db.Set<T>().Remove(e);
        ItemLinks.RemoveFor(db, LinkType, id);
        // SOC-24 : ses commentaires partent avec lui.
        db.Comments.RemoveRange(db.Comments.Where(c => c.TargetType == LinkType && c.TargetId == id));
        // SOC-25 : ses pièces jointes aussi, base puis stockage.
        var attachments = await db.Attachments.Where(a => a.TargetType == LinkType && a.TargetId == id).ToListAsync();
        db.Attachments.RemoveRange(attachments);
        // SOC-21 : ses notifications aussi (elles mèneraient à une fiche disparue).
        var notifications = db.Notifications.Where(n => n.TargetType == LinkType && n.TargetId == id);
        db.NotificationReads.RemoveRange(db.NotificationReads.Where(r => notifications.Any(n => n.Id == r.NotificationId)));
        db.Notifications.RemoveRange(notifications);
        await db.SaveChangesAsync();
        if (attachments.Count > 0)
            await Attachments.AttachmentsController.RemoveObjectsAsync(HttpContext.RequestServices.GetRequiredService<Platform.ObjectStorage>(),
                attachments.Select(a => a.StorageKey), HttpContext.RequestServices.GetRequiredService<ILogger<T>>());
        return NoContent();
    }

    protected string? Fill(T e, TDto dto, bool creating)
    {
        if (string.IsNullOrWhiteSpace(dto.Title)) return "Le titre est obligatoire.";
        var error = Allowed.Check("Type de responsable", dto.OwnerType, ["User", "Group"], optional: true);
        if (error is not null) return error;
        e.Title = dto.Title.Trim();
        e.Description = dto.Description ?? "";
        var hasOwner = !string.IsNullOrWhiteSpace(dto.OwnerId);
        e.OwnerType = hasOwner ? dto.OwnerType ?? "User" : null;
        e.OwnerId = hasOwner ? dto.OwnerId : null;
        e.OwnerDisplayName = hasOwner ? dto.OwnerDisplayName ?? dto.OwnerId : null;
        return Apply(e, dto, creating) ?? Lengths.Check(e);
    }
}
