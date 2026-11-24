using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Data;
using SAloha.Api.Core.Features;

namespace SAloha.Api.Modules.ServiceConfigurationManagement;

/// <summary>
/// CMA-10 : mesure de la CMDB assistée, par tâche, sur 30 et 90 jours : la part des propositions acceptées, corrigées,
/// rejetées et expirées (les ouvertes attendent leur décision et ne comptent pas encore), la part des réponses non
/// conformes du modèle et la durée moyenne de ses appels (traces OPT-27), et la durée des analyses terminées.
/// Administration › Fonctionnalités optionnelles › CMDB assistée.
/// </summary>
[ApiController]
[Route("api/admin/assisted-cmdb/metrics")]
[Authorize(Policy = "Admin")]
[RequiresFeature(FeatureCatalog.AssistedCmdb)]
public class AssistedCmdbMetricsController(AppDbContext db) : ControllerBase
{
    /// <summary>L'aide au sas (CMA-08) : tâche des traces du conteneur, et des propositions sur les actifs.</summary>
    public const string Staging = "staging";

    /// <summary>Tâches mesurées, dans l'ordre de la page, et leur libellé.</summary>
    private static readonly (string Task, string Label)[] Measured =
        [.. AssistedCmdbTasks.Labels.Select(t => (t.Key, t.Value)), (Staging, "Aide au sas")];

    /// <summary>Tâche d'une proposition : d'après son champ sur un CI (CMA-02), l'aide au sas sur un actif.</summary>
    public static string? TaskOf(string targetType, string field) => targetType switch
    {
        "ci" => AssistedCmdbProposalsController.TaskOfField.GetValueOrDefault(field),
        "asset" => field is "matchedCiId" or "ciType" ? Staging : null,
        _ => null,
    };

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var now = DateTime.UtcNow;
        var since = now.AddDays(-90);
        var ct = HttpContext.RequestAborted;
        var proposals = (await db.Proposals.AsNoTracking().Where(p => p.Feature == FeatureCatalog.AssistedCmdb && p.CreatedAt >= since)
            .Select(p => new { p.TargetType, p.Field, p.Status, p.CreatedAt }).ToListAsync(ct))
            .Select(p => (Task: TaskOf(p.TargetType, p.Field), p.Status, p.CreatedAt)).ToList();
        var calls = await db.LanguageModelCalls.AsNoTracking().Where(c => c.Feature == FeatureCatalog.AssistedCmdb && c.At >= since)
            .Select(c => new { c.Task, c.Outcome, c.Rejected, c.DurationMs, c.At }).ToListAsync(ct);
        var analyses = await db.AssistedCmdbAnalyses.AsNoTracking()
            .Where(a => a.RequestedAt >= since && (a.Status == AssistedCmdbAnalysis.Done || a.Status == AssistedCmdbAnalysis.Failed))
            .Select(a => new { a.Status, a.RequestedAt, a.StartedAt, a.FinishedAt }).ToListAsync(ct);
        static int Share(int n, int total) => total == 0 ? 0 : (int)Math.Round(n * 100.0 / total);

        object Window(int days)
        {
            var from = now.AddDays(-days);
            var durations = analyses.Where(a => a.RequestedAt >= from && a.Status == AssistedCmdbAnalysis.Done && a.FinishedAt is not null)
                .Select(a => (a.FinishedAt!.Value - (a.StartedAt ?? a.RequestedAt)).TotalSeconds).ToList();
            return new
            {
                Days = days,
                Tasks = Measured.Select(m =>
                {
                    var mine = proposals.Where(p => p.Task == m.Task && p.CreatedAt >= from).ToList();
                    int accepted = mine.Count(p => p.Status == Proposal.Accepted), corrected = mine.Count(p => p.Status == Proposal.Corrected),
                        rejected = mine.Count(p => p.Status == Proposal.Rejected), expired = mine.Count(p => p.Status == Proposal.Expired),
                        pending = mine.Count(p => p.Status == Proposal.Open);
                    var decided = accepted + corrected + rejected + expired;
                    // OPT-25 : chaque appel valide rend une réponse conforme ; ses réponses écartées sont comptées à part.
                    var traced = calls.Where(c => c.Task == m.Task && c.At >= from).ToList();
                    var nonConforming = traced.Sum(c => c.Rejected);
                    var responses = nonConforming + traced.Count(c => c.Outcome == LanguageModelCall.Valid);
                    return new
                    {
                        m.Task, m.Label, Decided = decided, Accepted = accepted, Corrected = corrected, Rejected = rejected, Expired = expired,
                        Pending = pending, AcceptedShare = Share(accepted, decided), CorrectedShare = Share(corrected, decided),
                        RejectedShare = Share(rejected, decided), ExpiredShare = Share(expired, decided),
                        Calls = traced.Count, Responses = responses, NonConforming = nonConforming, NonConformingShare = Share(nonConforming, responses),
                        FailedCalls = traced.Count(c => c.Outcome is LanguageModelCall.TimedOut or LanguageModelCall.Failed),
                        AverageCallMs = traced.Count == 0 ? 0 : (int)Math.Round(traced.Average(c => c.DurationMs)),
                    };
                }).ToList(),
                Analyses = new
                {
                    Done = durations.Count,
                    Failed = analyses.Count(a => a.RequestedAt >= from && a.Status == AssistedCmdbAnalysis.Failed),
                    AverageSeconds = durations.Count == 0 ? 0 : (int)Math.Round(durations.Average()),
                    LongestSeconds = durations.Count == 0 ? 0 : (int)Math.Round(durations.Max()),
                },
            };
        }
        return Ok(new { Windows = new[] { Window(30), Window(90) } });
    }
}
