using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Data;
using SAloha.Api.Core.Links;
using SAloha.Api.Core.Records;
using SAloha.Api.Core.Notifications;
using SAloha.Api.Modules.ContinualImprovement;

namespace SAloha.Api.Modules.ComplianceAssessment;

/// <summary>
/// Évaluations de conformité NIS 2 (docs/reference/processus/conformite-nis2.md).
/// Créer, valider, rouvrir : gestionnaires (NIS-02, NIS-07). Répondre : tout
/// rôle, tant que l'évaluation n'est pas validée (NIS-04, NIS-06).
/// </summary>
[Route("api/assessments")]
public class AssessmentsController(AppDbContext db) : RecordController<Assessment, AssessmentDto, AssessmentView>(db)
{
    protected override AssessmentView View(Assessment e) => AssessmentView.From(e);
    protected override string Code => "EVA";
    protected override string LinkType => "assessment";
    protected override string[] Statuses => Assessment.Statuses;
    protected override bool ManagerOnly => true;
    protected override bool RequiresManager(string status) => status == "Validée";

    protected override string? Apply(Assessment e, AssessmentDto dto, bool creating)
    {
        var error = Allowed.Check("Catégorie d'entité", dto.EntityCategory, Assessment.Categories);
        if (error is not null) return error;
        e.EntityCategory = dto.EntityCategory!;
        // NIS-16 : date de la prochaine évaluation, fixée par le gestionnaire (sinon à la validation).
        e.NextAssessmentDate = Dates.Utc(dto.NextAssessmentDate);
        return null;
    }

    protected override async Task<string?> CheckStatusAsync(Assessment e, string status)
    {
        // NIS-07 : une évaluation validée est figée ; seul un gestionnaire la
        // rouvre (ManagerOnly), en changeant son statut. La date de la prochaine évaluation
        // reste modifiable : elle planifie la suite, sans toucher au résultat (NIS-16).
        if (e.Status == "Validée" && status == "Validée" && e.Id != 0
            && Db.Entry(e).Properties.Any(p => p.IsModified && p.Metadata.Name is not (nameof(Record.UpdatedAt) or nameof(Assessment.NextAssessmentDate))))
            return "Une évaluation validée est figée : la rouvrir (statut « En cours ») pour la modifier.";
        if (status != "Validée" || e.Status == "Validée") return null;

        // NIS-05 : toutes les exigences applicables ont une réponse, et toute
        // exigence déclarée non applicable est justifiée.
        var (missing, unjustified) = await Completeness(e);
        if (missing > 0)
            return $"Validation impossible : {missing} exigence(s) applicable(s) sans réponse.";
        if (unjustified > 0)
            return $"Validation impossible : {unjustified} exigence(s) déclarée(s) non applicable(s) sans justification.";
        return null;
    }

    // NIS-18 : version évaluée et exigences figées à la validation, libérées à la réouverture.
    protected override Task BeforeSaveAsync(Assessment e, bool creating) => AssessmentReferential.AlignAsync(Db, e);

    // NIS-24 : validation ou réouverture → responsable (la création lui est notifiée par le socle).
    protected override void OnNotify(Assessment e, string? from, Notifier n)
    {
        if (from == "Validée") n.ToOwner(e, LinkType, "Évaluation rouverte");
        else if (from is not null && from != "" && e.Status == "Validée") n.ToOwner(e, LinkType, "Évaluation validée");
    }

    protected override void OnStatusChanged(Assessment e, string from)
    {
        if (e.Status == "Validée")
        {
            e.ValidatedAt = DateTime.UtcNow; e.ValidatedBy = MyDisplay;
            e.NextAssessmentDate ??= e.ValidatedAt.Value.Date.AddMonths(12);   // NIS-16
        }
        else { e.ValidatedAt = null; e.ValidatedBy = null; }
    }

    private async Task<(int Missing, int Unjustified)> Completeness(Assessment e)
    {
        var requirements = await Db.SecurityRequirements.AsNoTracking().ToListAsync();
        var applicable = requirements.Where(e.Applies).Select(r => r.Id).ToHashSet();
        var responses = await Db.AssessmentResponses.AsNoTracking()
            .Where(r => r.AssessmentId == e.Id && applicable.Contains(r.RequirementId)).ToListAsync();
        var answered = responses.Where(r => r.NotApplicable || r.Score.HasValue).Select(r => r.RequirementId).ToHashSet();
        return (applicable.Count - answered.Count,
            responses.Count(r => r.NotApplicable && string.IsNullOrWhiteSpace(r.Justification)));
    }

    /// <summary>Questionnaire : exigences applicables, par objectif et thématique, avec leur réponse.</summary>
    [HttpGet("{id:int}/questionnaire")]
    public async Task<IActionResult> Questionnaire(int id)
    {
        var e = await Db.Assessments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (e is null || !await VisibleAsync(e)) return NotFound();
        // NIS-18 : les exigences évaluées (figées si validée) ; le texte vient du référentiel importé.
        var evaluated = await AssessmentReferential.RequirementsAsync(Db, e);
        var details = await Db.SecurityRequirements.AsNoTracking().ToDictionaryAsync(r => r.Id);
        var responses = await Db.AssessmentResponses.AsNoTracking()
            .Where(r => r.AssessmentId == id).ToDictionaryAsync(r => r.RequirementId);
        var evidence = await EvidenceAsync(id);

        return Ok(new
        {
            e.ReferentialVersion,
            CurrentReferentialVersion = ReferentialStore.Version,
            Frozen = e.FrozenRequirementsJson is not null,
            Objectives = evaluated.GroupBy(r => r.ObjectiveId).OrderBy(g => g.Key).Select(o => new
            {
                Id = o.Key, Title = o.First().ObjectiveTitle, o.First().Pillar,
                Themes = o.OrderBy(r => r.Order).GroupBy(r => r.Theme).Select(g => new
                {
                    Theme = g.Key,
                    Requirements = g.Select(r =>
                    {
                        responses.TryGetValue(r.Id, out var resp);
                        details.TryGetValue(r.Id, out var d);
                        return new
                        {
                            r.Id, r.Code, d?.IsoControls, d?.Text,
                            resp?.Score, NotApplicable = resp?.NotApplicable ?? false,
                            Justification = resp?.Justification ?? "", resp?.UpdatedBy, resp?.UpdatedAt,
                            Evidence = evidence.GetValueOrDefault(r.Id) ?? []
                        };
                    })
                })
            })
        });
    }

    /// <summary>Réponse à une exigence (NIS-03, NIS-04) : score 0 à 3, ou non applicable.</summary>
    [HttpPut("{id:int}/responses/{requirementId:int}")]
    public async Task<IActionResult> Answer(int id, int requirementId, ResponseDto dto)
    {
        var e = await Db.Assessments.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null || !await VisibleAsync(e)) return NotFound();
        if (e.Status == "Validée")
            return BadRequest(new { message = "Une évaluation validée est figée : un gestionnaire doit la rouvrir." });
        var requirement = await Db.SecurityRequirements.FindAsync(requirementId);
        if (requirement is null) return NotFound();
        if (!e.Applies(requirement))
            return BadRequest(new { message = $"L'exigence {requirement.Code} ne s'applique pas à une {e.EntityCategory.ToLower()}." });
        if (!dto.NotApplicable && dto.Score is not (null or >= 0 and <= Scoring.Max))
            return BadRequest(new { message = $"Le score va de 0 à {Scoring.Max}." });

        var r = await Db.AssessmentResponses.FirstOrDefaultAsync(x => x.AssessmentId == id && x.RequirementId == requirementId);
        if (r is null) Db.AssessmentResponses.Add(r = new AssessmentResponse { AssessmentId = id, RequirementId = requirementId });
        r.NotApplicable = dto.NotApplicable;
        r.Score = dto.NotApplicable ? null : dto.Score;
        r.Justification = dto.Justification?.Trim() ?? "";
        r.UpdatedBy = MyDisplay; r.UpdatedAt = DateTime.UtcNow;
        // Une première réponse engage l'évaluation (NIS-03).
        if (e.Status == "Brouillon") e.Status = "En cours";
        e.UpdatedAt = DateTime.UtcNow;
        await Db.SaveChangesAsync();
        return Ok(new { r.RequirementId, r.Score, r.NotApplicable, r.Justification, r.UpdatedBy, r.UpdatedAt, AssessmentStatus = e.Status });
    }

    /// <summary>Synthèse (NIS-10 à NIS-13) : scores, maturité, couverture et écarts.</summary>
    [HttpGet("{id:int}/score")]
    public async Task<IActionResult> Score(int id)
    {
        var e = await Db.Assessments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (e is null || !await VisibleAsync(e)) return NotFound();
        var items = await ItemsAsync(e);

        var actions = await GapActionsAsync(id);
        var global = Scoring.Percent(items.Select(i => i.Score));
        return Ok(new
        {
            Percent = global,
            Maturity = global is null ? null : Scoring.MaturityOf(global.Value),
            Applicable = items.Count,
            Answered = items.Count(i => i.Score.HasValue || i.Na),
            NotApplicable = items.Count(i => i.Na),
            ReferentialVersion = e.ReferentialVersion,
            Pillars = items.GroupBy(i => i.Req.Pillar).Select(g => new
            {
                Pillar = g.Key, Percent = Scoring.Percent(g.Select(i => i.Score))
            }),
            Objectives = items.GroupBy(i => i.Req.ObjectiveId).OrderBy(g => g.Key).Select(g => new
            {
                Id = g.Key, Title = g.First().Req.ObjectiveTitle, g.First().Req.Pillar,
                Percent = Scoring.Percent(g.Select(i => i.Score)),
                Applicable = g.Count(), Answered = g.Count(i => i.Score.HasValue || i.Na),
                Themes = g.GroupBy(i => i.Req.Theme).Select(t => new
                {
                    Theme = t.Key, Percent = Scoring.Percent(t.Select(i => i.Score)),
                    Applicable = t.Count(), Answered = t.Count(i => i.Score.HasValue || i.Na)
                })
            }),
            Gaps = items.Where(i => i.Score is < Scoring.Max)
                .OrderByDescending(i => Scoring.Gap(i.Score!.Value)).ThenBy(i => i.Req.Order)
                .Select(i => new
                {
                    RequirementId = i.Req.Id, i.Req.Code, i.Req.Theme, ObjectiveId = i.Req.ObjectiveId,
                    Score = i.Score!.Value, Gap = Scoring.Gap(i.Score!.Value),
                    // NIS-14 : plan de traitement, l'action d'amélioration liée et son statut.
                    Action = actions.GetValueOrDefault(i.Req.Code)
                })
        });
    }

    public record GapAction(int Id, string Reference, string Status, bool Hidden = false);

    private const string HiddenAction = "Action d'un autre périmètre";

    /// <summary>
    /// NIS-14 : actions d'amélioration reliées à l'évaluation, par code d'exigence (titre
    /// « NIS 2 {code} — … » posé par NIS-15). Une action d'un autre périmètre tient l'écart
    /// pour traité sans être décrite, comme tout enregistrement lié (PER-08).
    /// </summary>
    private async Task<Dictionary<string, GapAction>> GapActionsAsync(int id)
    {
        var linked = await Db.ItemLinks
            .Where(l => (l.FromType == LinkType && l.FromId == id && l.ToType == "improvement")
                        || (l.ToType == LinkType && l.ToId == id && l.FromType == "improvement"))
            .Select(l => l.FromType == LinkType ? l.ToId : l.FromId).ToListAsync();
        var improvements = await Db.Improvements.AsNoTracking()
            .Where(i => linked.Contains(i.Id) && i.Title.StartsWith("NIS 2 "))
            .OrderBy(i => i.Id).Select(i => new { i.Id, i.Reference, i.Status, i.Title, i.PerimeterId }).ToListAsync();
        var scope = await ScopeAsync();
        var map = new Dictionary<string, GapAction>();
        foreach (var i in improvements)
        {
            var code = i.Title["NIS 2 ".Length..].Split(" — ")[0];
            map.TryAdd(code, scope.Sees(i.PerimeterId) ? new GapAction(i.Id, i.Reference, i.Status) : new GapAction(0, "", "", true));
        }
        return map;
    }

    /// <summary>
    /// NIS-17 : lignes de l'export pour les auditeurs — chaque exigence évaluée avec son
    /// score, sa justification, son écart et l'action liée. L'interface en fait un CSV.
    /// </summary>
    [HttpGet("{id:int}/export")]
    public async Task<IActionResult> Export(int id)
    {
        var e = await Db.Assessments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (e is null || !await VisibleAsync(e)) return NotFound();
        var requirements = await AssessmentReferential.RequirementsAsync(Db, e);
        var responses = await Db.AssessmentResponses.AsNoTracking()
            .Where(r => r.AssessmentId == id).ToDictionaryAsync(r => r.RequirementId);
        var actions = await GapActionsAsync(id);
        var evidence = await EvidenceAsync(id);
        return Ok(new
        {
            e.Reference, e.Title, e.EntityCategory, e.Status, e.ReferentialVersion, e.ValidatedAt, e.NextAssessmentDate,
            Rows = requirements.OrderBy(r => r.ObjectiveId).ThenBy(r => r.Order).Select(r =>
            {
                responses.TryGetValue(r.Id, out var resp);
                var score = resp is { NotApplicable: false } ? resp.Score : null;
                var action = actions.GetValueOrDefault(r.Code);
                return new
                {
                    r.Code, r.ObjectiveId, r.ObjectiveTitle, r.Pillar, r.Theme, Score = score,
                    NotApplicable = resp?.NotApplicable ?? false, Justification = resp?.Justification ?? "",
                    Gap = score is { } s ? Scoring.Gap(s) : (int?)null,
                    Action = action is { Hidden: true } ? HiddenAction : action?.Reference, ActionStatus = action?.Status,
                    Evidence = string.Join(" ", (evidence.GetValueOrDefault(r.Id) ?? []).Select(x => x.Reference))
                };
            })
        });
    }

    /// <summary>
    /// NIS-15 : traite un écart par une action d'amélioration (registre
    /// d'amélioration continue), reliée à l'évaluation. Une seule par exigence.
    /// </summary>
    [HttpPost("{id:int}/improvements")]
    public async Task<IActionResult> CreateImprovement(int id, GapActionDto dto)
    {
        var e = await Db.Assessments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (e is null || !await VisibleAsync(e)) return NotFound();
        // Décision de gestionnaire, sur le périmètre de l'évaluation (PER-06).
        ActOn(e.PerimeterId);
        if (!IsManager) return Forbid();
        var requirement = await Db.SecurityRequirements.Include(r => r.Objective).FirstOrDefaultAsync(r => r.Code == dto.Code);
        if (requirement is null) return NotFound();
        var response = await Db.AssessmentResponses.AsNoTracking()
            .FirstOrDefaultAsync(r => r.AssessmentId == id && r.RequirementId == requirement.Id);
        if (response is not { NotApplicable: false, Score: < Scoring.Max })
            return BadRequest(new { message = $"L'exigence {requirement.Code} ne présente pas d'écart noté." });

        var prefix = $"NIS 2 {requirement.Code} — ";
        var linked = await Db.ItemLinks.Where(l => l.FromType == LinkType && l.FromId == id && l.ToType == "improvement")
            .Select(l => l.ToId).ToListAsync();
        var existing = await Db.Improvements.AsNoTracking()
            .FirstOrDefaultAsync(i => linked.Contains(i.Id) && i.Title.StartsWith(prefix));
        if (existing is not null)
            return (await ScopeAsync()).Sees(existing.PerimeterId)
                ? Conflict(new { message = $"Cet écart est déjà traité par {existing.Reference}.", existing.Id, existing.Reference })
                : Conflict(new { message = "Cet écart est déjà traité par une action d'un autre périmètre." });

        var improvement = await References.CreateAsync(Db, "AMI", () => new Improvement
        {
            PerimeterId = e.PerimeterId,
            Title = (prefix + requirement.Theme).Length > 200 ? (prefix + requirement.Theme)[..200] : prefix + requirement.Theme,
            Description = $"Écart relevé par l'évaluation {e.Reference} sur l'exigence {requirement.Code} "
                + $"(objectif de sécurité {requirement.ObjectiveId} — {requirement.Objective!.Title}) : "
                + $"score {response.Score}/{Scoring.Max}."
                + (string.IsNullOrWhiteSpace(response.Justification) ? "" : $"\n\nConstat : {response.Justification}"),
            Status = Improvement.Statuses[0], Step = 2, Priority = response.Score <= 1 ? "Élevée" : "Moyenne",
            Baseline = $"Score {response.Score}/{Scoring.Max}", Target = $"Score {Scoring.Max}/{Scoring.Max}",
            CreatedBy = Me, CreatedByDisplayName = MyDisplay
        });
        Db.ItemLinks.Add(new ItemLink { FromType = LinkType, FromId = id, ToType = "improvement", ToId = improvement.Id, CreatedBy = Me });
        await Db.SaveChangesAsync();
        return Ok(new { improvement.Id, improvement.Reference });
    }

    private record Item(EvaluatedRequirement Req, int? Score, bool Na);

    /// <summary>Exigences évaluées (figées si l'évaluation est validée, NIS-18) et leur réponse.</summary>
    private async Task<List<Item>> ItemsAsync(Assessment e)
    {
        var requirements = await AssessmentReferential.RequirementsAsync(Db, e);
        var responses = await Db.AssessmentResponses.AsNoTracking()
            .Where(r => r.AssessmentId == e.Id).ToDictionaryAsync(r => r.RequirementId);
        return requirements.Select(r =>
        {
            responses.TryGetValue(r.Id, out var resp);
            return new Item(r, resp is { NotApplicable: false } ? resp.Score : null, resp?.NotApplicable ?? false);
        }).ToList();
    }

    public record Evidence(int Id, string TargetType, int TargetId, string Reference, string Title);

    private async Task<Dictionary<int, List<Evidence>>> EvidenceAsync(int id) =>
        (await Db.AssessmentEvidences.AsNoTracking().Where(x => x.AssessmentId == id).OrderBy(x => x.Id).ToListAsync())
            .GroupBy(x => x.RequirementId)
            .ToDictionary(g => g.Key, g => g.Select(x => new Evidence(x.Id, x.TargetType, x.TargetId, x.Reference, x.Title)).ToList());

    /// <summary>
    /// NIS-08 : preuve d'une réponse — un enregistrement désigné par sa référence (changement,
    /// article, CI…), visible de l'utilisateur. Comme répondre : tout rôle, tant que l'évaluation
    /// n'est pas validée (NIS-06).
    /// </summary>
    [HttpPost("{id:int}/responses/{requirementId:int}/evidence")]
    public async Task<IActionResult> AddEvidence(int id, int requirementId, EvidenceDto dto)
    {
        var e = await Db.Assessments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (e is null || !await VisibleAsync(e)) return NotFound();
        if (e.Status == "Validée")
            return BadRequest(new { message = "Une évaluation validée est figée : un gestionnaire doit la rouvrir." });
        var requirement = await Db.SecurityRequirements.FindAsync(requirementId);
        if (requirement is null) return NotFound();
        if (!e.Applies(requirement))
            return BadRequest(new { message = $"L'exigence {requirement.Code} ne s'applique pas à une {e.EntityCategory.ToLower()}." });

        var reference = (dto.Reference ?? "").Trim().ToUpperInvariant();
        var kind = ItemLinks.ByReference(reference);
        var target = kind is null || kind.Type == LinkType ? null
            : await kind.Query(Db).FirstOrDefaultAsync(t => t.Reference == reference);
        if (target is null || !(await ScopeAsync()).Sees(target))
            return BadRequest(new { message = $"Aucun enregistrement {reference} : une preuve est un changement, un article, un CI… existant." });
        if (await Db.AssessmentEvidences.AnyAsync(x => x.AssessmentId == id && x.RequirementId == requirementId
                                                     && x.TargetType == kind!.Type && x.TargetId == target.Id))
            return Conflict(new { message = $"{target.Reference} est déjà une preuve de cette exigence." });

        var evidence = new AssessmentEvidence
        {
            AssessmentId = id, RequirementId = requirementId, TargetType = kind!.Type, TargetId = target.Id,
            Reference = target.Reference, Title = target.Title.Length > 200 ? target.Title[..200] : target.Title,
            CreatedBy = MyDisplay
        };
        Db.AssessmentEvidences.Add(evidence);
        await Db.SaveChangesAsync();
        return Ok(new Evidence(evidence.Id, evidence.TargetType, evidence.TargetId, evidence.Reference, evidence.Title));
    }

    [HttpDelete("{id:int}/evidence/{evidenceId:int}")]
    public async Task<IActionResult> RemoveEvidence(int id, int evidenceId)
    {
        var e = await Db.Assessments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        var evidence = await Db.AssessmentEvidences.FirstOrDefaultAsync(x => x.Id == evidenceId && x.AssessmentId == id);
        if (e is null || evidence is null || !await VisibleAsync(e)) return NotFound();
        if (e.Status == "Validée")
            return BadRequest(new { message = "Une évaluation validée est figée : un gestionnaire doit la rouvrir." });
        Db.AssessmentEvidences.Remove(evidence);
        await Db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// NIS-09 : comparaison avec une autre évaluation du même périmètre — scores par pilier,
    /// objectif et thématique, et exigences dont le score a changé (écart : actuel − autre).
    /// </summary>
    [HttpGet("{id:int}/compare/{otherId:int}")]
    public async Task<IActionResult> Compare(int id, int otherId)
    {
        var e = await Db.Assessments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        var o = await Db.Assessments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == otherId);
        if (e is null || o is null || !await VisibleAsync(e) || !await VisibleAsync(o)) return NotFound();
        if (id == otherId) return BadRequest(new { message = "Choisir une autre évaluation à comparer." });
        if (e.PerimeterId != o.PerimeterId)
            return BadRequest(new { message = "Seules deux évaluations du même périmètre se comparent." });

        var current = await ItemsAsync(e);
        var other = await ItemsAsync(o);
        static double? Delta(double? a, double? b) => a is null || b is null ? null : Math.Round(a.Value - b.Value, 1);
        object Row(IEnumerable<Item> a, IEnumerable<Item> b) =>
            new { Current = Scoring.Percent(a.Select(i => i.Score)), Other = Scoring.Percent(b.Select(i => i.Score)),
                  Delta = Delta(Scoring.Percent(a.Select(i => i.Score)), Scoring.Percent(b.Select(i => i.Score))) };

        var otherByCode = other.ToDictionary(i => i.Req.Code);
        var currentByCode = current.ToDictionary(i => i.Req.Code);
        var all = current.Select(i => i.Req).Concat(other.Select(i => i.Req).Where(r => !currentByCode.ContainsKey(r.Code))).ToList();
        string Show(Item? i) => i is null ? "Non évaluée" : i.Na ? "Non applicable" : i.Score?.ToString() ?? "Sans réponse";

        return Ok(new
        {
            Current = new { e.Id, e.Reference, e.Title, e.Status, e.ValidatedAt, e.ReferentialVersion },
            Other = new { o.Id, o.Reference, o.Title, o.Status, o.ValidatedAt, o.ReferentialVersion },
            Global = Row(current, other),
            Pillars = all.Select(r => r.Pillar).Distinct().Select(p => new
            {
                Pillar = p, Score = Row(current.Where(i => i.Req.Pillar == p), other.Where(i => i.Req.Pillar == p))
            }),
            Objectives = all.GroupBy(r => r.ObjectiveId).OrderBy(g => g.Key).Select(g => new
            {
                Id = g.Key, Title = g.First().ObjectiveTitle,
                Score = Row(current.Where(i => i.Req.ObjectiveId == g.Key), other.Where(i => i.Req.ObjectiveId == g.Key)),
                Themes = g.Select(r => r.Theme).Distinct().Select(t => new
                {
                    Theme = t,
                    Score = Row(current.Where(i => i.Req.ObjectiveId == g.Key && i.Req.Theme == t),
                                        other.Where(i => i.Req.ObjectiveId == g.Key && i.Req.Theme == t))
                })
            }),
            // Exigences dont la réponse diffère (score, non-applicabilité, ou évaluée d'un seul côté).
            Requirements = all.OrderBy(r => r.ObjectiveId).ThenBy(r => r.Order).Select(r =>
            {
                var a = currentByCode.GetValueOrDefault(r.Code);
                var b = otherByCode.GetValueOrDefault(r.Code);
                return new { r.Code, r.ObjectiveId, r.Theme, Current = Show(a), Other = Show(b),
                             Delta = a?.Score is { } x && b?.Score is { } y ? x - y : (int?)null };
            }).Where(x => x.Current != x.Other)
        });
    }
}
