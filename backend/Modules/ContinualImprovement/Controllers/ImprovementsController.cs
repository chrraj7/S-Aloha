using Microsoft.AspNetCore.Mvc;
using SAloha.Api.Core.Auth;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Data;
using SAloha.Api.Core.Links;
using SAloha.Api.Core.Records;
using SAloha.Api.Core.Notifications;

namespace SAloha.Api.Modules.ContinualImprovement;

[Route("api/improvements")]
public class ImprovementsController(AppDbContext db) : RecordController<Improvement, ImprovementDto, ImprovementView>(db)
{
    protected override ImprovementView View(Improvement e) => ImprovementView.From(e);
    protected override string Code => "AMI";
    protected override string LinkType => "improvement";
    protected override string[] Statuses => Improvement.Statuses;
    protected override IReadOnlyDictionary<string, string[]> Transitions => Improvement.Transitions;

    protected override string? Apply(Improvement e, ImprovementDto dto, bool creating)
    {
        var error = Allowed.Check("Priorité", dto.Priority, Improvement.Priorities)
                    ?? Allowed.Check("Source", dto.Source, Improvement.Sources, optional: true);
        if (error is not null) return error;
        if (dto.Step is < 1 or > 7) return "L'étape du modèle d'amélioration va de 1 à 7.";
        if (dto.Value is < 1 or > 3 || dto.Effort is < 1 or > 3) return "La valeur et l'effort se notent de 1 à 3.";
        e.Step = dto.Step; e.Priority = dto.Priority!;
        // CSI-10 : la source se choisit à la proposition (« Suggestion » par défaut).
        if (creating || dto.Source is not null) e.Source = dto.Source ?? "Suggestion";
        // CSI-12 : valeur et effort notés, la priorité en découle.
        e.Value = dto.Value; e.Effort = dto.Effort;
        if (e.Value is { } v && e.Effort is { } eff) e.Priority = Improvement.PriorityOf(v, eff);
        _origin = creating && !string.IsNullOrWhiteSpace(dto.OriginReference) ? dto.OriginReference.Trim().ToUpper() : null;
        e.Benefit = dto.Benefit ?? ""; e.Baseline = dto.Baseline ?? ""; e.Target = dto.Target ?? "";
        e.Outcome = dto.Outcome;
        e.DueDate = Dates.Utc(dto.DueDate);
        // CSI-15 : la date de contrôle se fixe librement ; une fois le contrôle conclu, elle ne change plus.
        if (e.FollowUpDoneAt is null) e.FollowUpDate = Dates.Utc(dto.FollowUpDate);
        e.AbandonReason = string.IsNullOrWhiteSpace(dto.AbandonReason) ? null : dto.AbandonReason.Trim();
        return null;
    }

    private string? _origin;
    private (string Type, int Id)? _originTarget;

    /// <summary>CSI-10 : l'enregistrement d'origine existe et l'utilisateur le voit.</summary>
    protected override async Task<string?> ValidateAsync(Improvement e)
    {
        if (_origin is null) return null;
        var kind = ItemLinks.ByReference(_origin);
        var t = kind is null ? null : await kind.Query(Db).FirstOrDefaultAsync(x => x.Reference == _origin);
        if (kind is null || t is null || !(await ScopeAsync()).Sees(t))
            return $"Aucun enregistrement de référence « {_origin} ».";
        _originTarget = (kind.Type, t.Id);
        return null;
    }

    /// <summary>CSI-10 : l'amélioration est reliée à son enregistrement d'origine.</summary>
    protected override async Task OnCreatedAsync(Improvement e)
    {
        if (_originTarget is not { } o) return;
        Db.ItemLinks.Add(new ItemLink { FromType = LinkType, FromId = e.Id, ToType = o.Type, ToId = o.Id, CreatedBy = Me });
        await Db.SaveChangesAsync();
    }

    // Engager des moyens ou y renoncer est une décision de gestionnaire.
    protected override bool RequiresManager(string status) => status is "Validée" or "Abandonnée";

    protected override string? CheckStatus(Improvement e, string status)
    {
        if (status is "En cours" or "Réalisée" && e.ValidatedAt is null)
            return "L'amélioration doit d'abord être validée par un gestionnaire.";
        // CSI-11, § 4 : engager l'amélioration et la déclarer réalisée reviennent à son porteur
        // (membre du groupe porteur) ou à un gestionnaire. Porteur d'avant la modification : se
        // désigner et transiter d'un même envoi ne suffit pas (comme REQ-25, #249).
        var entering = e.Status != status;
        if (entering && status is ("En cours" or "Réalisée") && e.Id != 0 && !IsManager)
        {
            var owner = Db.Entry(e);
            if (!User.Designates(owner.Property(x => x.OwnerType).OriginalValue, owner.Property(x => x.OwnerId).OriginalValue))
                return "Seul le porteur de l'amélioration ou un gestionnaire l'engage et la déclare réalisée.";
        }
        // CSI-11 : une amélioration validée ou en cours ne change de porteur que par son porteur ou
        // un gestionnaire (sans porteur, par un gestionnaire) ; sinon la règle ci-dessus se
        // contournait en deux envois.
        if (e.Status is "Validée" or "En cours" && ReassignedByThirdParty(e))
            return "Seul le porteur de l'amélioration ou un gestionnaire en change le porteur une fois l'amélioration validée ; sans porteur, un gestionnaire le désigne.";
        // CSI-11, § 4 : une amélioration s'engage avec un porteur.
        if (entering && status == "En cours" && string.IsNullOrWhiteSpace(e.OwnerId))
            return "Désigner le porteur de l'amélioration avant de l'engager.";
        // CSI-12 : valider, c'est arbitrer — valeur et effort notés. Contrôlé à l'entrée dans
        // Validée seulement : une amélioration validée avant la règle reste modifiable.
        if (status == "Validée" && e.Status != "Validée" && (e.Value is null || e.Effort is null))
            return "Noter la valeur et l'effort (1 à 3) pour valider : la priorité en découle.";
        // CSI-14 : une amélioration se mesure — valeur, point de départ et cible avant de l'engager.
        if (status is "Validée" or "En cours" or "Réalisée"
            && (string.IsNullOrWhiteSpace(e.Benefit) || string.IsNullOrWhiteSpace(e.Baseline) || string.IsNullOrWhiteSpace(e.Target)))
            return "Renseigner la valeur attendue, la mesure de départ et la mesure cible : une amélioration se mesure.";
        // CSI-16 : réalisée à l'étape 6 au moins, quand le résultat a été comparé à la cible.
        if (status == "Réalisée" && e.Step < 6)
            return "Une amélioration se déclare réalisée à l'étape 6 (« Y sommes-nous parvenus ? ») au moins.";
        // CSI-13 : un abandon se motive.
        if (status == "Abandonnée" && e.AbandonReason is null)
            return "Motiver l'abandon : le motif reste visible sur la fiche.";
        if (status == "Réalisée" && string.IsNullOrWhiteSpace(e.Outcome))
            return "Décrire le résultat constaté avant de déclarer l'amélioration réalisée.";
        return null;
    }

    // CSI-23 : proposition → gestionnaires ; validation ou abandon → proposant et porteur.
    protected override void OnNotify(Improvement e, string? from, Notifier n)
    {
        if (from is null) return;
        if (e.Status == "Proposée") n.ToManagers(e, LinkType, "Amélioration proposée");
        var ev = e.Status switch { "Validée" => "Amélioration validée", "Abandonnée" => "Amélioration abandonnée", _ => null };
        if (ev is null) return;
        n.ToCreator(e, LinkType, ev);
        n.ToOwner(e, LinkType, ev);
    }

    protected override void OnStatusChanged(Improvement e, string from)
    {
        if (e.Status == "Validée") { e.ValidatedAt = DateTime.UtcNow; e.ValidatedBy = MyDisplay; }
        // Abandonner retire la validation : relancer l'amélioration demande une
        // nouvelle décision de gestionnaire.
        else if (e.Status is "Proposée" or "Abandonnée") { e.ValidatedAt = null; e.ValidatedBy = null; }
        e.CompletedAt = e.Status == "Réalisée" ? DateTime.UtcNow : null;
    }

    /// <summary>
    /// CSI-15 : à la date de contrôle, le porteur (ou un gestionnaire) confirme que le résultat
    /// tient, ou constate qu'il ne tient pas : une nouvelle amélioration Proposée, reliée, reprend
    /// le sujet avec le constat.
    /// </summary>
    [HttpPost("{id:int}/follow-up")]
    public async Task<IActionResult> FollowUp(int id, FollowUpDto dto)
    {
        var e = await Db.Improvements.FindAsync(id);
        if (e is null || !await VisibleAsync(e)) return NotFound();
        await ScopeAsync();
        ActOn(e.PerimeterId);
        if (!IsManager && !User.Designates(e.OwnerType, e.OwnerId)) return Forbid();
        if (e.Status != "Réalisée" || e.FollowUpDate is null) return BadRequest(new { message = "Seule une amélioration Réalisée avec une date de contrôle se contrôle." });
        if (e.FollowUpDoneAt is not null) return BadRequest(new { message = "Le contrôle de durabilité est déjà conclu." });
        if (e.FollowUpDate.Value.Date > DateTime.UtcNow.Date) return BadRequest(new { message = $"Contrôle prévu le {e.FollowUpDate:dd/MM/yyyy}." });
        var note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
        if (!dto.Holds && note is null) return BadRequest(new { message = "Décrire ce qui ne tient pas : le constat ouvre la nouvelle amélioration." });
        (e.FollowUpDoneAt, e.FollowUpDoneBy, e.FollowUpHolds, e.FollowUpNote) = (DateTime.UtcNow, MyDisplay, dto.Holds, note);
        e.UpdatedAt = DateTime.UtcNow;
        await Db.SaveChangesAsync();
        Improvement? next = null;
        if (!dto.Holds)
        {
            var title = $"Reprise : {e.Title}";
            next = await References.CreateAsync(Db, Code, () => new Improvement
            {
                Title = title.Length > 200 ? title[..200] : title,
                Description = $"Le résultat de {e.Reference} ne tient pas au contrôle du {DateTime.UtcNow:dd/MM/yyyy} : {note}",
                Status = "Proposée", Step = 2, Priority = e.Priority, Source = e.Source, PerimeterId = e.PerimeterId,
                Baseline = e.Outcome ?? "", Target = e.Target,
                OwnerType = e.OwnerType, OwnerId = e.OwnerId, OwnerDisplayName = e.OwnerDisplayName,
                CreatedBy = Me, CreatedByDisplayName = MyDisplay
            });
            Db.ItemLinks.Add(new ItemLink { FromType = LinkType, FromId = next.Id, ToType = LinkType, ToId = e.Id, CreatedBy = Me });
            await Db.SaveChangesAsync();
            Notify.ToManagers(next, LinkType, "Amélioration proposée");
            await Notify.SaveAsync();
        }
        return Ok(new { Improvement = View(e), Created = next?.Reference });
    }
}
