using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Data;
using SAloha.Api.Modules.ChangeEnablement;
using SAloha.Api.Modules.IncidentManagement;
using SAloha.Api.Modules.ProblemManagement;
using SAloha.Api.Modules.ServiceLevelManagement;
using SAloha.Api.Modules.ServiceRequestManagement;

namespace SAloha.Api.Core.Notifications;

/// <summary>
/// Événements datés (SOC-21) : ceux qu'aucune action ne déclenche, relevés par une tâche
/// périodique. Chaque événement n'est notifié qu'une fois par enregistrement (son libellé
/// porte l'échéance quand elle peut changer).
/// </summary>
public class DatedEvents(AppDbContext db, Notifier notify, MailDispatch mail)
{
    private static string Day(DateTime d) => d.ToString("dd/MM/yyyy");

    public async Task<int> RunAsync(DateTime now)
    {
        notify.AsSystem = true;
        var today = now.Date;
        // Déjà notifiés (événements de la tâche, auteur « système ») : un même événement ne se répète pas.
        var since = now.AddDays(-400);
        var done = (await db.Notifications.AsNoTracking().Where(n => n.Actor == "système" && n.CreatedAt >= since)
                .Select(n => new { n.TargetType, n.TargetId, n.Event }).Distinct().ToListAsync())
            .Select(n => (n.TargetType, n.TargetId, n.Event)).ToHashSet();
        var count = 0;
        bool Fresh(string type, int id, string ev)
        {
            if (!done.Add((type, id, ev))) return false;
            count++;
            return true;
        }

        // INC-24, INC-25 : SLA en risque (moins de 25 % du délai) → responsable ;
        // INC-18 : SLA dépassé, ou incident P1 ouvert → responsable et gestionnaires.
        foreach (var i in await db.Incidents.AsNoTracking().Where(i => !Incident.Done.Contains(i.Status)).ToListAsync())
        {
            // INC-22 : horloge arrêtée En attente : ni risque ni dépassement relevés pendant l'attente.
            if (i.Status != "En attente" && i.ResolutionDueAt is { } due)
            {
                if (now > due && Fresh("incident", i.Id, "SLA dépassé"))
                {
                    notify.ToOwner(i, "incident", "SLA dépassé");
                    notify.ToManagers(i, "incident", "SLA dépassé");
                }
                else if (now <= due && (due - now).TotalMinutes < (due - i.CreatedAt).TotalMinutes * 0.25
                         && Fresh("incident", i.Id, "SLA en risque"))
                    notify.ToOwner(i, "incident", "SLA en risque");
            }
            if (i.Priority == "P1" && Fresh("incident", i.Id, "Incident P1 ouvert"))
                notify.ToManagers(i, "incident", "Incident P1 ouvert");
            // INC-26 : En attente depuis plus de 5 jours ouvrés → responsable, une fois par attente.
            if (i.HoldFollowUp(now) is { } held && Fresh("incident", i.Id, held))
                notify.ToOwner(i, "incident", held);
        }

        // DRG-13 : échéance légale à moins de 6 h → responsable et gestionnaires ; dépassée → idem,
        // chaque heure jusqu'à l'envoi (le libellé porte l'heure de la relance).
        foreach (var d in await db.RegulatoryNotifications.AsNoTracking()
                     .Where(d => !Modules.RegulatoryNotifications.RegulatoryNotification.Done.Contains(d.Status)).ToListAsync())
        {
            if (d.NextDue() is not { } next) continue;
            var ev = next.Due < now
                ? $"{next.Step} : échéance dépassée (relance de {now.ToLocalTime():dd/MM HH} h)"
                : next.Due - now < TimeSpan.FromHours(6) ? $"{next.Step} : échéance dans moins de 6 h ({next.Due.ToLocalTime():dd/MM HH:mm})" : null;
            if (ev is null || !Fresh("notification", d.Id, ev)) continue;
            notify.ToOwner(d, "notification", ev);
            notify.ToManagers(d, "notification", ev);
        }

        // CHG-30, CHG-31 : changement à relancer → responsable (une fois par motif).
        foreach (var c in await db.Changes.AsNoTracking().Where(c => c.Status == "Planifié" || c.Status == "Mis en œuvre").ToListAsync())
            if (c.FollowUp(now) is { } reason && Fresh("change", c.Id, reason))
                notify.ToOwner(c, "change", reason);

        // CHG-24 : la veille de la mise en œuvre → responsable.
        foreach (var c in await db.Changes.AsNoTracking()
                     .Where(c => (c.Status == "Autorisé" || c.Status == "Planifié") && c.PlannedStart > now && c.PlannedStart <= now.AddDays(1))
                     .ToListAsync())
        {
            var ev = $"Mise en œuvre le {Day(c.PlannedStart!.Value)}";
            if (Fresh("change", c.Id, ev)) notify.ToOwner(c, "change", ev);
        }

        // CSI-15 : contrôle de durabilité arrivé à sa date → porteur, une fois.
        foreach (var x in await db.Improvements.AsNoTracking()
                     .Where(x => x.Status == "Réalisée" && x.FollowUpDate != null && x.FollowUpDate <= today && x.FollowUpDoneAt == null).ToListAsync())
        {
            var ev = $"Contrôle de durabilité à faire (prévu le {Day(x.FollowUpDate!.Value)})";
            if (Fresh("improvement", x.Id, ev)) notify.ToOwner(x, "improvement", ev);
        }

        // PRB-32 : problème ouvert sans activité depuis 30 jours → gestionnaires, une fois par période d'inactivité.
        foreach (var (p, last) in await ProblemRules.IdleAsync(db, now, _ => true))
        {
            var ev = $"Sans activité depuis le {Day(last)}";
            if (Fresh("problem", p.Id, ev)) notify.ToManagers(p, "problem", ev);
        }

        // PRB-31 : action en retard → R, A et gestionnaires du problème.
        foreach (var a in await db.Actions.AsNoTracking().Include(a => a.Raci).Include(a => a.Problem)
                     .Where(a => a.Status != "Terminée" && a.Status != "Annulée" && a.DueDate != null && a.DueDate < today)
                     .ToListAsync())
        {
            var ev = $"Action en retard : {a.Title} (échéance {Day(a.DueDate!.Value)})";
            ev = ev.Length > 200 ? ev[..200] : ev;
            if (!Fresh("problem", a.ProblemId, ev)) continue;
            foreach (var r in a.Raci.Where(r => r.Role is "R" or "A"))
                notify.ToPrincipal(a.Problem!, "problem", r.AssigneeType, r.AssigneeId, ev);
            notify.ToManagers(a.Problem!, "problem", ev);
        }

        // KB-20 : revue échue → responsable de l'article (à défaut, son rédacteur).
        foreach (var k in await db.KnowledgeArticles.AsNoTracking()
                     .Where(k => k.Status == "Publié" && k.ReviewDate != null && k.ReviewDate < today).ToListAsync())
        {
            var ev = $"Revue de l'article échue ({Day(k.ReviewDate!.Value)})";
            if (!Fresh("article", k.Id, ev)) continue;
            if (!string.IsNullOrWhiteSpace(k.OwnerId)) notify.ToOwner(k, "article", ev);
            else notify.ToCreator(k, "article", ev);
        }

        // CSI-21 : amélioration En cours dont l'échéance est passée → porteur (à défaut, proposant).
        foreach (var i in await db.Improvements.AsNoTracking()
                     .Where(i => i.Status == "En cours" && i.DueDate != null && i.DueDate < today).ToListAsync())
        {
            var ev = $"Amélioration en retard (échéance {Day(i.DueDate!.Value)})";
            if (!Fresh("improvement", i.Id, ev)) continue;
            if (!string.IsNullOrWhiteSpace(i.OwnerId)) notify.ToOwner(i, "improvement", ev);
            else notify.ToCreator(i, "improvement", ev);
        }

        // CFG-15 : CI En service non vérifié depuis 12 mois → propriétaire.
        var limit = today.AddMonths(-12);
        foreach (var c in await db.ConfigurationItems.AsNoTracking()
                     .Where(c => c.Status == "En service" && (c.LastVerifiedAt ?? c.CreatedAt) < limit).ToListAsync())
        {
            var ev = $"Fiche à vérifier (dernière vérification : {Day(c.LastVerifiedAt ?? c.CreatedAt)})";
            if (Fresh("ci", c.Id, ev)) notify.ToOwner(c, "ci", ev);
        }

        // NIS-16 : évaluation validée arrivée à la date de la prochaine évaluation → responsable.
        foreach (var a in await db.Assessments.AsNoTracking()
                     .Where(a => a.Status == "Validée" && a.NextAssessmentDate != null && a.NextAssessmentDate <= today).ToListAsync())
        {
            var ev = $"Évaluation à renouveler (échéance : {Day(a.NextAssessmentDate!.Value)})";
            if (Fresh("assessment", a.Id, ev)) notify.ToOwner(a, "assessment", ev);
        }

        // REQ-30 : demande en retard (le lendemain de l'échéance) → responsable et gestionnaires.
        foreach (var r in await db.ServiceRequests.AsNoTracking()
                     .Where(r => r.DueDate != null && r.DueDate < today && !ServiceRequest.Done.Contains(r.Status)).ToListAsync())
        {
            var ev = $"Demande en retard (échéance : {Day(r.DueDate!.Value)})";
            if (!Fresh("request", r.Id, ev)) continue;
            notify.ToOwner(r, "request", ev);
            notify.ToManagers(r, "request", ev);
        }

        // SLM-04 : un SLA En vigueur passe Expiré le lendemain de sa fin de validité → gestionnaires.
        // Le journal d'audit trace le changement de statut (auteur : système, ou l'Admin qui lance le relevé).
        var expired = await db.Agreements.Where(s => s.Status == "En vigueur" && s.ValidTo != null && s.ValidTo < today).ToListAsync();
        foreach (var s in expired)
        {
            s.Status = "Expiré"; s.UpdatedAt = now;
            notify.ToManagers(s, "agreement", $"SLA expiré (fin de validité : {Day(s.ValidTo!.Value)})");
            count++;
        }
        if (expired.Count > 0) await db.SaveChangesAsync();

        // SLM-24 : SLA expirant dans 30 jours → gestionnaires ; revue échue → propriétaire du service.
        foreach (var s in await db.Agreements.AsNoTracking().Include(s => s.Service).Where(s => s.Status == "En vigueur").ToListAsync())
        {
            if (s.ValidTo is { } end && end >= today && end <= today.AddDays(30))
            {
                var ev = $"SLA expirant le {Day(end)}";
                if (Fresh("agreement", s.Id, ev)) notify.ToManagers(s, "agreement", ev);
            }
            if (s.ReviewDate is { } review && review < today)
            {
                var ev = $"Revue du SLA échue ({Day(review)})";
                if (Fresh("agreement", s.Id, ev)) notify.ToPrincipal(s, "agreement", s.Service?.OwnerType, s.Service?.OwnerId, ev);
            }
        }

        // REQ-11 : clôture automatique des demandes satisfaites sans contestation depuis 5 jours ouvrés.
        count += await Modules.ServiceRequestManagement.RequestAutoClose.RunAsync(db, notify, now);

        // INC-10 : clôture automatique des incidents résolus au terme de la fenêtre de réouverture (INC-11).
        count += await Modules.IncidentManagement.IncidentAutoClose.RunAsync(db, notify, now);

        // SLM-23, CSI-20 : mois précédent sous la cible → gestionnaires, amélioration proposée.
        count += await SAloha.Api.Core.Itil.SlaCompliance.AlertAsync(db, notify, now, (t, id, ev) => done.Add((t, id, ev)));

        await notify.SaveAsync();
        // SOC-22 : les notifications du relevé, et celles des actions depuis le précédent, partent par courriel.
        await mail.RunAsync(now);
        return count;
    }
}

/// <summary>Relève les événements datés à intervalle régulier (Notifications:IntervalMinutes, 15 par défaut ; 0 : jamais).</summary>
public class DatedEventsWorker(IServiceScopeFactory scopes, IConfiguration config, Administration.Heartbeats heartbeats, ILogger<DatedEventsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var minutes = config.GetValue("Notifications:IntervalMinutes", 15);
        if (minutes <= 0) return;
        var every = TimeSpan.FromMinutes(minutes);
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(every, ct);
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var n = await scope.ServiceProvider.GetRequiredService<DatedEvents>().RunAsync(DateTime.UtcNow);
                if (n > 0) logger.LogInformation("Notifications datées : {Count} événement(s).", n);
                heartbeats.Record("notifications", true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Notifications datées : relevé en échec.");
                heartbeats.Record("notifications", false, ex.Message.Split('\n')[0]);
            }
        }
    }
}
