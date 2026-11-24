using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Auth;
using SAloha.Api.Core.Data;
using SAloha.Api.Core.Perimeters;
using SAloha.Api.Core.Records;
using SAloha.Api.Modules.ChangeEnablement;
using SAloha.Api.Modules.ContinualImprovement;
using SAloha.Api.Modules.IncidentManagement;
using SAloha.Api.Modules.KnowledgeManagement;
using SAloha.Api.Modules.ProblemManagement;
using SAloha.Api.Modules.ProjectManagement;
using SAloha.Api.Modules.ServiceLevelManagement;
using SAloha.Api.Modules.ServiceRequestManagement;

namespace SAloha.Api.Core.Pilotage;

/// <summary>Console personnalisée par rôle.</summary>
[ApiController]
[Route("api/console")]
[Authorize(Policy = "User")]
public class ConsoleController(AppDbContext db, PerimeterAccess access) : ControllerBase
{
    /// <summary>
    /// Console adaptée au rôle : User = mon activité, Manager/Admin = pilotage du processus.
    /// PER-09 : mon activité se limite à mes périmètres, le pilotage aux périmètres dont je
    /// suis gestionnaire ; l'Admin voit tout et peut filtrer par périmètre (<c>perimeter</c>).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int? perimeter)
    {
        var scope = await access.GetAsync();
        var all = scope.Global && perimeter is null;
        var seen = (perimeter is { } only ? (scope.Sees(only) ? [only] : []) : scope.Ids).ToList();
        var managed = (perimeter is { } one ? (scope.ManagerOn(one) ? [one] : []) : scope.ManagedIds).ToList();
        var me = User.Identity?.Name ?? "";
        // L'AD ignore la casse des identifiants : la comparaison aussi (M2,
        // revue du 2026-10-06). Couvre les données enregistrées avant que le
        // jeton ne porte le sAMAccountName de l'annuaire.
        var meKey = me.ToLowerInvariant();
        // SOC-10 : ce qui est confié à l'un de mes groupes AD est aussi à moi.
        var groups = User.GroupKeys();
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "User";
        // L'échéance est une date (stockée à minuit UTC) : une action n'est en
        // retard qu'à partir du LENDEMAIN de son échéance, pas dès le matin
        // du jour même (M6, revue du 2026-10-06).
        var today = DateTime.UtcNow.Date;

        // --- Bloc personnel (tous les rôles) ---
        var myProblems = await db.Problems.AsNoTracking()
            .Where(p => p.CreatedBy.ToLower() == meKey && p.Status != "Clos" && (all || seen.Contains(p.PerimeterId)))
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new { p.Id, p.Reference, p.Title, p.Status, p.Priority, p.CreatedAt })
            .Take(10).ToListAsync();

        var myActions = await db.Actions.AsNoTracking()
            .Include(a => a.Raci).Include(a => a.Problem)
            .Where(a => a.Status != "Terminée" && a.Status != "Annulée" && (all || seen.Contains(a.Problem!.PerimeterId))
                        && a.Raci.Any(r => r.AssigneeType == "Group"
                            ? groups.Contains(r.AssigneeId.ToLower()) : r.AssigneeId.ToLower() == meKey))
            .OrderBy(a => a.DueDate)
            .Select(a => new
            {
                a.Id, a.Title, a.Status, a.DueDate,
                MyRoles = a.Raci.Where(r => r.AssigneeType == "Group"
                    ? groups.Contains(r.AssigneeId.ToLower()) : r.AssigneeId.ToLower() == meKey).Select(r => r.Role).Distinct(),
                Problem = new { a.Problem!.Id, a.Problem.Reference, a.Problem.Title },
                Overdue = a.DueDate != null && a.DueDate < today
            }).ToListAsync();

        // Enregistrements des autres processus dont je suis responsable (assigné, propriétaire).
        var myAssignments = new List<Item>();
        // PRB-21 : les problèmes dont je suis le gestionnaire.
        myAssignments.AddRange(await Mine(db.Problems, "problem", meKey, groups, ["Résolu", "Clos"], all, seen));
        myAssignments.AddRange(await Mine(db.Incidents, "incident", meKey, groups, Incident.Done, all, seen));
        myAssignments.AddRange(await Mine(db.ServiceRequests, "request", meKey, groups, ServiceRequest.Done, all, seen));
        myAssignments.AddRange(await Mine(db.Changes, "change", meKey, groups, Change.Done, all, seen));
        myAssignments.AddRange(await Mine(db.Improvements, "improvement", meKey, groups, Improvement.Done, all, seen));
        // PRJ-34 : mes projets (chef de projet) dans Mon travail.
        myAssignments.AddRange(await Mine(db.Projects, "project", meKey, groups, Project.Done, all, seen));
        // Conformité NIS 2 : mes évaluations ouvertes (pilote), jusqu'à leur validation.
        myAssignments.AddRange(await Mine(db.Assessments, "assessment", meKey, groups, ["Validée"], all, seen));

        // PRJ-34 : projets hors tolérance (délai, coût, prévision de fin) ou à jalons en retard.
        var todayDate = DateTime.UtcNow.Date;
        var openProjects = await db.Projects.AsNoTracking()
            .Where(p => !Project.Done.Contains(p.Status) && (all || seen.Contains(p.PerimeterId))).ToListAsync();
        var projectIds = openProjects.Select(p => p.Id).ToList();
        var lateMilestones = (await db.Milestones.AsNoTracking()
                .Where(m => projectIds.Contains(m.ProjectId) && m.AchievedAt == null && m.DueDate != null && m.DueDate < todayDate)
                .Select(m => m.ProjectId).ToListAsync())
            .GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        var projectItems = (await db.WorkItems.AsNoTracking().Where(w => projectIds.Contains(w.ProjectId)).ToListAsync()).ToLookup(w => w.ProjectId);
        var projectSprints = (await db.Sprints.AsNoTracking().Where(x => projectIds.Contains(x.ProjectId)).ToListAsync()).ToLookup(x => x.ProjectId);
        var projectPhases = (await db.ProjectPhases.AsNoTracking().Where(x => projectIds.Contains(x.ProjectId)).ToListAsync()).ToLookup(x => x.ProjectId);
        var projectAlerts = openProjects.Select(p =>
        {
            var reasons = new List<string>();
            if (ProjectHealth.TimeTolerance(p, todayDate) == ProjectHealth.Out) reasons.Add("délai hors tolérance");
            if (ProjectHealth.CostTolerance(p) == ProjectHealth.Out) reasons.Add("coût hors tolérance");
            if (ProjectHealth.ForecastEnd(p, projectItems[p.Id].ToList(), projectSprints[p.Id].ToList(), projectPhases[p.Id].ToList(), todayDate).Tolerance == ProjectHealth.Out)
                reasons.Add("fin prévue hors tolérance");
            if (lateMilestones.GetValueOrDefault(p.Id) is var late and > 0) reasons.Add($"{late} jalon(s) en retard");
            return (Project: p, Reasons: reasons);
        }).Where(x => x.Reasons.Count > 0).ToList();
        Item AlertItem((Project Project, List<string> Reasons) x) =>
            new("project", x.Project.Id, x.Project.Reference, x.Project.Title, string.Join(", ", x.Reasons));
        var myProjectAlerts = projectAlerts.Where(x => x.Project.OwnerId != null && (x.Project.OwnerType == "Group"
                ? groups.Contains(x.Project.OwnerId.ToLower()) : x.Project.OwnerId.ToLower() == meKey))
            .Select(AlertItem).ToList();

        // INC-24 : incidents ouverts en risque ou hors délai — les miens (Urgent du responsable).
        var now = DateTime.UtcNow;
        var slaOpen = (await db.Incidents.AsNoTracking()
                .Where(i => !Incident.Done.Contains(i.Status) && i.Status != "En attente" && i.ResolutionDueAt != null
                            && (all || seen.Contains(i.PerimeterId)))
                .ToListAsync())
            .Select(i => new { i, State = IncidentSla.State(i, now) })
            .Where(x => x.State is IncidentSla.AtRisk or IncidentSla.Breached)
            .OrderBy(x => x.i.ResolutionDueAt).ToList();
        var mySla = slaOpen.Where(x => x.i.OwnerId != null && (x.i.OwnerType == "Group"
                ? groups.Contains(x.i.OwnerId.ToLower()) : x.i.OwnerId.ToLower() == meKey))
            .Select(x => new Item("incident", x.i.Id, x.i.Reference, x.i.Title, x.State)).ToList();

        // CHG-30, CHG-31 : changements à relancer — les miens (responsable).
        var followUps = (await db.Changes.AsNoTracking()
                .Where(c => (c.Status == "Planifié" || c.Status == "Mis en œuvre") && (all || seen.Contains(c.PerimeterId)))
                .ToListAsync())
            .Select(c => (c, Reason: c.FollowUp(now))).Where(x => x.Reason is not null).ToList();
        var myFollowUps = followUps.Where(x => x.c.OwnerId != null && (x.c.OwnerType == "Group"
                ? groups.Contains(x.c.OwnerId.ToLower()) : x.c.OwnerId.ToLower() == meKey))
            .Select(x => new Item("change", x.c.Id, x.c.Reference, x.c.Title, x.Reason!)).ToList();

        // INC-26 : incidents En attente depuis plus de 5 jours ouvrés — les miens (responsable).
        var heldLong = (await db.Incidents.AsNoTracking()
                .Where(i => i.Status == "En attente" && (all || seen.Contains(i.PerimeterId))).OrderBy(i => i.OnHoldSince).ToListAsync())
            .Select(i => (i, Reason: i.HoldFollowUp(now))).Where(x => x.Reason is not null).ToList();
        var myHeldLong = heldLong.Where(x => x.i.OwnerId != null && (x.i.OwnerType == "Group"
                ? groups.Contains(x.i.OwnerId.ToLower()) : x.i.OwnerId.ToLower() == meKey))
            .Select(x => new Item("incident", x.i.Id, x.i.Reference, x.i.Title, x.Reason!)).ToList();

        // KB-13 : articles publiés jugés peu utiles (3 « pas utile » de plus que d'« utile ») — les miens.
        var margin = Modules.KnowledgeManagement.KnowledgeArticle.UnhelpfulMargin;
        var unhelpful = await db.KnowledgeArticles.AsNoTracking()
            .Where(a => a.Status == "Publié" && a.NotHelpfulCount - a.HelpfulCount >= margin && (all || seen.Contains(a.PerimeterId)))
            .OrderByDescending(a => a.NotHelpfulCount - a.HelpfulCount).ToListAsync();
        Item Unhelpful(Modules.KnowledgeManagement.KnowledgeArticle a) =>
            new("article", a.Id, a.Reference, a.Title, $"Peu utile : {a.NotHelpfulCount} « pas utile », {a.HelpfulCount} « utile »");
        var myUnhelpful = unhelpful.Where(a => a.OwnerId != null && (a.OwnerType == "Group"
                ? groups.Contains(a.OwnerId.ToLower()) : a.OwnerId.ToLower() == meKey)).Select(Unhelpful).ToList();

        // CSI-15 : mes contrôles de durabilité arrivés à leur date.
        var myFollowUpChecks = (await db.Improvements.AsNoTracking()
                .Where(x => x.Status == "Réalisée" && x.FollowUpDate != null && x.FollowUpDate <= today && x.FollowUpDoneAt == null
                            && (all || seen.Contains(x.PerimeterId))).OrderBy(x => x.FollowUpDate).ToListAsync())
            .Where(x => x.OwnerId != null && (x.OwnerType == "Group" ? groups.Contains(x.OwnerId.ToLower()) : x.OwnerId.ToLower() == meKey))
            .Select(x => new Item("improvement", x.Id, x.Reference, x.Title, $"Contrôle de durabilité du {x.FollowUpDate:dd/MM/yyyy}")).ToList();

        // CFG-15 : CI En service non vérifiés depuis 12 mois — les miens (propriétaire), puis ceux des
        // périmètres gérés. Même calcul que la relance par notification (DatedEvents) : les deux
        // doivent rester alignés.
        var verifyLimit = today.AddMonths(-12);
        var toVerify = (await db.ConfigurationItems.AsNoTracking()
                .Where(c => c.Status == "En service" && (c.LastVerifiedAt ?? c.CreatedAt) < verifyLimit
                            && (all || seen.Contains(c.PerimeterId)))
                .OrderBy(c => c.LastVerifiedAt ?? c.CreatedAt).ToListAsync())
            .Select(c => (c, Item: new Item("ci", c.Id, c.Reference, c.Title,
                $"Dernière vérification : {c.LastVerifiedAt ?? c.CreatedAt:dd/MM/yyyy}"))).ToList();
        var myToVerify = toVerify.Where(x => x.c.OwnerId != null && (x.c.OwnerType == "Group"
                ? groups.Contains(x.c.OwnerId.ToLower()) : x.c.OwnerId.ToLower() == meKey))
            .Select(x => x.Item).ToList();

        object? management = null;
        // ADR-0014 D4 : le pilotage revient à qui est gestionnaire d'au moins un périmètre.
        if (all || managed.Count > 0)
        {
            // --- Bloc pilotage (Manager / Admin) ---
            var toQualify = await db.Problems.AsNoTracking()
                .Where(p => p.Status == "Nouveau" && (all || managed.Contains(p.PerimeterId))).OrderBy(p => p.CreatedAt)
                .Select(p => new { p.Id, p.Reference, p.Title, p.Priority, p.CreatedAt, p.CreatedByDisplayName })
                .ToListAsync();

            var inAnalysisNoRootCause = await db.Problems.AsNoTracking()
                .Where(p => p.Status == "En analyse" && (p.RootCause == null || p.RootCause == "") && (all || managed.Contains(p.PerimeterId)))
                .Select(p => new { p.Id, p.Reference, p.Title, p.Priority, AnalysesCount = p.Analyses.Count })
                .ToListAsync();

            var knownErrorsNoAction = await db.Problems.AsNoTracking()
                .Where(p => p.Status == "Erreur connue" && !p.Actions.Any(a => a.Status != "Annulée") && (all || managed.Contains(p.PerimeterId)))
                .Select(p => new { p.Id, p.Reference, p.Title, p.Priority })
                .ToListAsync();

            var overdue = await db.Actions.AsNoTracking()
                .Include(a => a.Raci).Include(a => a.Problem)
                .Where(a => a.Status != "Terminée" && a.Status != "Annulée"
                            && a.DueDate != null && a.DueDate < today && (all || managed.Contains(a.Problem!.PerimeterId)))
                .Select(a => new
                {
                    a.Id, a.Title, a.DueDate,
                    Problem = new { a.Problem!.Id, a.Problem.Reference },
                    Responsibles = a.Raci.Where(r => r.Role == "R").Select(r => r.AssigneeDisplayName)
                }).ToListAsync();

            var majorIncidents = await db.Incidents.AsNoTracking()
                .Where(i => i.IsMajor && !Incident.Done.Contains(i.Status) && (all || managed.Contains(i.PerimeterId)))
                // INC-13 : un majeur sans responsable est signalé « Responsable à désigner ».
                .Select(i => new Item("incident", i.Id, i.Reference, i.Title,
                    i.OwnerId == null || i.OwnerId == "" ? "Responsable à désigner" : i.Status)).ToListAsync();

            // Décisions de gestionnaire en attente : autoriser, approuver, valider.
            var decisions = new List<Item>();
            // PRB-22 : incident majeur résolu sans problème relié — un problème est à ouvrir.
            var withProblem = db.ItemLinks.Where(l => l.FromType == "incident" && l.ToType == "problem").Select(l => l.FromId)
                .Union(db.ItemLinks.Where(l => l.ToType == "incident" && l.FromType == "problem").Select(l => l.ToId));
            decisions.AddRange(await db.Incidents.AsNoTracking()
                .Where(i => i.IsMajor && Incident.Done.Contains(i.Status) && !withProblem.Contains(i.Id) && (all || managed.Contains(i.PerimeterId)))
                .OrderBy(i => i.ResolvedAt)
                .Select(i => new Item("incident", i.Id, i.Reference, i.Title, "Problème à ouvrir")).ToListAsync());
            decisions.AddRange(await InStatus(db.Changes, "change", all, managed, "Demandé", "Évalué"));
            decisions.AddRange(await InStatus(db.ServiceRequests, "request", all, managed, "Soumise"));
            // CSI-22 : revue du registre — une amélioration proposée depuis plus de 30 jours est signalée comme telle.
            decisions.AddRange((await db.Improvements.AsNoTracking()
                    .Where(x => x.Status == "Proposée" && (all || managed.Contains(x.PerimeterId))).OrderBy(x => x.CreatedAt).ToListAsync())
                .Select(x => new Item("improvement", x.Id, x.Reference, x.Title, (now - x.CreatedAt).TotalDays > 30
                    ? $"Proposée depuis {(int)(now - x.CreatedAt).TotalDays} jours : revue du registre" : x.Status)));
            // Connaissances : brouillons soumis à publication (KB-21), archivages demandés à la revue (KB-17).
            decisions.AddRange(await db.KnowledgeArticles.AsNoTracking()
                .Where(a => a.Status == "Brouillon" && a.SubmittedAt != null && (all || managed.Contains(a.PerimeterId))).OrderBy(a => a.SubmittedAt)
                .Select(a => new Item("article", a.Id, a.Reference, a.Title, "À publier")).ToListAsync());
            decisions.AddRange(await db.KnowledgeArticles.AsNoTracking()
                .Where(a => a.Status == "Publié" && a.ArchiveRequestedAt != null && (all || managed.Contains(a.PerimeterId))).OrderBy(a => a.ArchiveRequestedAt)
                .Select(a => new Item("article", a.Id, a.Reference, a.Title, "Archivage demandé")).ToListAsync());

            // REQ-30 : demandes en retard (le lendemain de l'échéance).
            var overdueRequests = await db.ServiceRequests.AsNoTracking()
                .Where(r => r.DueDate != null && r.DueDate < today && !ServiceRequest.Done.Contains(r.Status) && (all || managed.Contains(r.PerimeterId)))
                .OrderBy(r => r.DueDate)
                .Select(r => new Item("request", r.Id, r.Reference, r.Title, r.Status)).ToListAsync();

            // CHG-27 : modèles standard suspendus après un échec, à revoir (le changement en échec y mène).
            var suspended = await db.StandardChangeModels.AsNoTracking().Where(m => m.SuspendedAt != null && m.SuspendedByChangeId != null)
                .ToDictionaryAsync(m => m.SuspendedByChangeId!.Value, m => m.Name);
            decisions.AddRange((await db.Changes.AsNoTracking()
                    .Where(c => suspended.Keys.Contains(c.Id) && (all || managed.Contains(c.PerimeterId))).ToListAsync())
                .Select(c => new Item("change", c.Id, c.Reference, c.Title, $"Modèle standard « {suspended[c.Id]} » suspendu : revue à faire")));

            // PRJ-34 : projets en cadrage, à valider par un gestionnaire.
            decisions.AddRange(await db.Projects.AsNoTracking()
                .Where(p => p.Status == "Cadrage" && (all || managed.Contains(p.PerimeterId))).OrderBy(p => p.CreatedAt)
                .Select(p => new Item("project", p.Id, p.Reference, p.Title, "À valider")).ToListAsync());

            // Revues échues : articles publiés et SLA en vigueur dont la date de revue est passée.
            var reviewsDue = new List<Item>();
            reviewsDue.AddRange(await db.KnowledgeArticles.AsNoTracking()
                .Where(a => a.Status == "Publié" && a.ReviewDate != null && a.ReviewDate < today && (all || managed.Contains(a.PerimeterId)))
                .Select(a => new Item("article", a.Id, a.Reference, a.Title, a.Status)).ToListAsync());
            // KB-12 : article d'erreur connue dont le problème est clos « Corrigé » : à archiver.
            var fixedProblems = db.Problems.Where(p => p.Status == "Clos" && p.ClosureCode == "Corrigé").Select(p => p.Id);
            reviewsDue.AddRange(await db.KnowledgeArticles.AsNoTracking()
                .Where(a => a.Status == "Publié" && a.ArticleType == "Erreur connue" && (all || managed.Contains(a.PerimeterId))
                            && db.ItemLinks.Any(l => (l.FromType == "article" && l.FromId == a.Id && l.ToType == "problem" && fixedProblems.Contains(l.ToId))
                                                  || (l.ToType == "article" && l.ToId == a.Id && l.FromType == "problem" && fixedProblems.Contains(l.FromId))))
                .Select(a => new Item("article", a.Id, a.Reference, a.Title, "À archiver : problème corrigé")).ToListAsync());
            reviewsDue.AddRange(await db.Agreements.AsNoTracking()
                .Where(a => a.Status == "En vigueur" && a.ReviewDate != null && a.ReviewDate < today && (all || managed.Contains(a.PerimeterId)))
                .Select(a => new Item("agreement", a.Id, a.Reference, a.Title, a.Status)).ToListAsync());
            // SLM-24 : SLA en vigueur expirant dans les 30 jours, à renouveler avant l'échéance (comme la notification).
            var expiringLimit = today.AddDays(30);
            var expiringAgreements = (await db.Agreements.AsNoTracking()
                    .Where(a => a.Status == "En vigueur" && a.ValidTo != null && a.ValidTo >= today && a.ValidTo <= expiringLimit
                                && (all || managed.Contains(a.PerimeterId)))
                    .OrderBy(a => a.ValidTo).ToListAsync())
                .Select(a => new Item("agreement", a.Id, a.Reference, a.Title, $"Expire le {a.ValidTo:dd/MM/yyyy}")).ToList();
            // NIS-16 : évaluation NIS 2 validée arrivée à la date de la prochaine évaluation.
            reviewsDue.AddRange(await db.Assessments.AsNoTracking()
                .Where(a => a.Status == "Validée" && a.NextAssessmentDate != null && a.NextAssessmentDate <= today && (all || managed.Contains(a.PerimeterId)))
                .Select(a => new Item("assessment", a.Id, a.Reference, a.Title, "À renouveler")).ToListAsync());

            // INC-41 : incidents cyber pas encore qualifiés ; DRG-11 : échéances légales proches (moins de 6 h) ou dépassées.
            var regulatory = (await db.Incidents.AsNoTracking()
                    .Where(i => i.IsSecurity && i.QualifiedAt == null && i.Status != "Clos" && (all || managed.Contains(i.PerimeterId)))
                    .OrderBy(i => i.CreatedAt).ToListAsync())
                .Select(i => new Item("incident", i.Id, i.Reference, i.Title, $"À qualifier ({Elapsed(now - i.CreatedAt)} depuis la déclaration)"))
                .ToList();
            regulatory.AddRange((await db.RegulatoryNotifications.AsNoTracking()
                    .Where(d => !Modules.RegulatoryNotifications.RegulatoryNotification.Done.Contains(d.Status) && (all || managed.Contains(d.PerimeterId)))
                    .ToListAsync())
                .Select(d => (d, Next: d.NextDue()))
                .Where(x => x.Next is { } n && n.Due - now < TimeSpan.FromHours(6))
                .OrderBy(x => x.Next!.Value.Due)
                .Select(x => new Item("notification", x.d.Id, x.d.Reference, x.d.Title, x.Next!.Value.Due < now
                    ? $"{x.Next.Value.Step} : échéance dépassée de {Elapsed(now - x.Next.Value.Due)}"
                    : $"{x.Next.Value.Step} : {Elapsed(x.Next.Value.Due - now)} restantes")));

            // CFG-40 : sas de staging des périmètres gérés — actifs à rapprocher, en conflit, inconnus (CFG-42).
            var staged = await db.DiscoveredAssets.AsNoTracking()
                .Where(a => Modules.ServiceConfigurationManagement.DiscoveredAsset.Open.Contains(a.Status) && (all || managed.Contains(a.PerimeterId)))
                .Select(a => new { a.Status, a.InDirectory, a.MatchedCiId }).ToListAsync();
            var staging = new
            {
                ToReconcile = staged.Count(a => a.Status == Modules.ServiceConfigurationManagement.DiscoveredAsset.ToReconcile),
                Conflicts = staged.Count(a => a.Status == Modules.ServiceConfigurationManagement.DiscoveredAsset.InConflict),
                Unknown = staged.Count(a => !a.InDirectory && a.MatchedCiId == null),
            };

            management = new { Staging = staging, ToQualify = toQualify, InAnalysisNoRootCause = inAnalysisNoRootCause,
                               KnownErrorsNoAction = knownErrorsNoAction, OverdueActions = overdue,
                               MajorIncidents = majorIncidents, Decisions = decisions, ReviewsDue = reviewsDue,
                               OverdueRequests = overdueRequests, Regulatory = regulatory, ExpiringAgreements = expiringAgreements,
                               // NIS-23 : exigences du référentiel NIS 2 encore sans texte (Admin, qui l'importe).
                               ReferentialWithoutText = User.IsInRole("Admin")
                                   ? await db.SecurityRequirements.CountAsync(r => r.Text == null || r.Text == "") : 0,
                               // CHG-30, CHG-31 : changements à relancer des périmètres gérés.
                               ChangeFollowUps = followUps.Where(x => all || managed.Contains(x.c.PerimeterId))
                                   .Select(x => new Item("change", x.c.Id, x.c.Reference, x.c.Title, x.Reason!)).ToList(),
                               // KB-13 : articles jugés peu utiles des périmètres gérés.
                               UnhelpfulArticles = unhelpful.Where(a => all || managed.Contains(a.PerimeterId)).Select(Unhelpful).ToList(),
                               // PRB-32 : problèmes ouverts sans activité depuis 30 jours.
                               IdleProblems = (await ProblemRules.IdleAsync(db, now, id => all || managed.Contains(id)))
                                   .Select(x => new Item("problem", x.Problem.Id, x.Problem.Reference, x.Problem.Title,
                                       $"Sans activité depuis le {x.LastActivity:dd/MM/yyyy}")).ToList(),
                               // INC-26 : incidents En attente prolongée des périmètres gérés.
                               IncidentFollowUps = heldLong.Where(x => all || managed.Contains(x.i.PerimeterId))
                                   .Select(x => new Item("incident", x.i.Id, x.i.Reference, x.i.Title, x.Reason!)).ToList(),
                               // PRJ-34 : projets des périmètres gérés hors tolérance ou à jalons en retard.
                               ProjectAlerts = projectAlerts.Where(x => all || managed.Contains(x.Project.PerimeterId)).Select(AlertItem).ToList(),
                               // CFG-15 : CI à vérifier des périmètres gérés.
                               CisToVerify = toVerify.Where(x => all || managed.Contains(x.c.PerimeterId)).Select(x => x.Item).ToList(),
                               // INC-24 : incidents en risque ou hors délai des périmètres gérés.
                               SlaIncidents = slaOpen.Where(x => all || managed.Contains(x.i.PerimeterId))
                                   .Select(x => new Item("incident", x.i.Id, x.i.Reference, x.i.Title, x.State)).ToList() };
        }

        return Ok(new { Role = role, Username = me, MyProblems = myProblems, MyActions = myActions,
                        MyAssignments = myAssignments, MySlaIncidents = mySla, MyProjectAlerts = myProjectAlerts,
                        MyChangeFollowUps = myFollowUps, MyIncidentFollowUps = myHeldLong, MyUnhelpfulArticles = myUnhelpful, MyFollowUpChecks = myFollowUpChecks,
                        MyCisToVerify = myToVerify, Management = management });
    }

    /// <summary>Ligne de console désignant un enregistrement d'un processus.</summary>
    public record Item(string Type, int Id, string Reference, string Title, string Status);

    /// <summary>Durée lisible : « 5 h 20 », « 2 j 3 h ».</summary>
    public static string Elapsed(TimeSpan t) => t.TotalHours >= 48 ? $"{(int)t.TotalDays} j {t.Hours} h"
        : t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes:00}" : $"{Math.Max(0, t.Minutes)} min";

    private static Task<List<Item>> Mine<T>(DbSet<T> set, string type, string meKey, List<string> groups, string[] done,
        bool all, List<int> seen) where T : Record =>
        set.AsNoTracking()
            .Where(x => x.OwnerId != null && !done.Contains(x.Status) && (all || seen.Contains(x.PerimeterId))
                        && (x.OwnerType == "Group" ? groups.Contains(x.OwnerId.ToLower()) : x.OwnerId.ToLower() == meKey))
            .OrderBy(x => x.CreatedAt)
            .Select(x => new Item(type, x.Id, x.Reference, x.Title, x.Status)).ToListAsync();

    private static Task<List<Item>> InStatus<T>(DbSet<T> set, string type, bool all, List<int> managed, params string[] statuses) where T : Record =>
        set.AsNoTracking().Where(x => statuses.Contains(x.Status) && (all || managed.Contains(x.PerimeterId))).OrderBy(x => x.CreatedAt)
            .Select(x => new Item(type, x.Id, x.Reference, x.Title, x.Status)).ToListAsync();
}
