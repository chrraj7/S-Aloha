using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Data;
using SAloha.Api.Core.Records;

namespace SAloha.Api.Modules.ServiceLevelManagement;

/// <summary>Catalogue des services : référentiel tenu par les gestionnaires.</summary>
[Route("api/services")]
public class ServicesController(AppDbContext db) : RecordController<ItService, ItServiceDto, ItServiceView>(db)
{
    protected override ItServiceView View(ItService e) => ItServiceView.From(e);
    protected override string Code => "SVC";
    protected override string LinkType => "service";
    protected override string[] Statuses => ItService.Statuses;
    protected override IReadOnlyDictionary<string, string[]> Transitions => ItService.Transitions;
    protected override bool ManagerOnly => true;
    // ADR-0014 D2 : le catalogue des services se consulte dans toute l'organisation.
    protected override System.Linq.Expressions.Expression<Func<ItService, bool>> SharedWithAll => _ => true;
    protected override bool StatusChosenAtCreation => true;

    // SLM-11 : un service en service a un propriétaire (responsable du service).
    // SLM-16 : un service ne se retire pas tant qu'il porte un SLA En vigueur, un incident ou une
    // demande ouverts ; contrôlé au retrait seulement, un service déjà retiré reste modifiable.
    protected override async Task<string?> CheckStatusAsync(ItService e, string status)
    {
        if (status == "En service" && string.IsNullOrWhiteSpace(e.OwnerId))
            return "Désigner le responsable du service : il est obligatoire pour un service en service.";
        if (status != "Retiré" || e.Status == "Retiré") return null;

        var agreements = await Db.Agreements.AsNoTracking()
            .Where(a => a.ServiceId == e.Id && a.Status == "En vigueur").Select(a => a.Reference).ToListAsync();
        if (agreements.Count > 0)
            return $"Ce service porte un SLA En vigueur ({string.Join(", ", agreements)}) : le faire expirer avant de retirer le service.";
        var incidents = await Db.Incidents.AsNoTracking()
            .Where(i => i.ServiceId == e.Id && !IncidentManagement.Incident.Done.Contains(i.Status)).Select(i => i.Reference).ToListAsync();
        var requests = await Db.ServiceRequests.AsNoTracking()
            .Where(r => r.ServiceId == e.Id && !ServiceRequestManagement.ServiceRequest.Done.Contains(r.Status)).Select(r => r.Reference).ToListAsync();
        var open = incidents.Concat(requests).ToList();
        return open.Count > 0
            ? $"Ce service porte des enregistrements encore ouverts : {string.Join(", ", open)}. Les traiter avant de retirer le service."
            : null;
    }

    protected override string? Apply(ItService e, ItServiceDto dto, bool creating)
    {
        var error = Allowed.Check("Criticité", dto.Criticality, ItService.Criticalities);
        if (error is not null) return error;
        e.Criticality = dto.Criticality!;
        e.ServiceHours = dto.ServiceHours ?? "";
        // SLM-14 : heures de service structurées, contrôlées avant d'être enregistrées.
        var (_, invalid) = SAloha.Api.Core.Itil.ServiceCalendar.Parse(dto.WorkingDays, dto.OpeningTime, dto.ClosingTime, dto.Holidays, dto.TimeZone);
        if (invalid is not null) return invalid;
        static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        e.WorkingDays = (dto.WorkingDays ?? "").Replace(" ", ""); e.OpeningTime = Blank(dto.OpeningTime);
        e.ClosingTime = Blank(dto.ClosingTime); e.Holidays = (dto.Holidays ?? "").Trim(); e.TimeZone = Blank(dto.TimeZone);
        return null;
    }

    protected override IQueryable<ItService> WithDetails(IQueryable<ItService> q) => q.Include(s => s.Agreements);

    /// <summary>
    /// CFG-18 : un service En service dont un CI relié est Hors service est « dégradé » ; la
    /// fiche du service le signale, avec les CI en cause. Un CI d'un autre périmètre compte
    /// dans la dégradation sans être décrit (PER-08).
    /// </summary>
    [HttpGet("{id:int}/degradation")]
    public async Task<IActionResult> Degradation(int id)
    {
        var service = await Db.Services.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (service is null || !await VisibleAsync(service)) return NotFound();
        var ciIds = await Db.ItemLinks.AsNoTracking()
            .Where(l => (l.FromType == "service" && l.FromId == id && l.ToType == "ci") || (l.ToType == "service" && l.ToId == id && l.FromType == "ci"))
            .Select(l => l.FromType == "ci" ? l.FromId : l.ToId).ToListAsync();
        var outOfService = service.Status != "En service" ? []
            : await Db.ConfigurationItems.AsNoTracking().Where(c => ciIds.Contains(c.Id) && c.Status == "Hors service")
                .OrderBy(c => c.Reference).Select(c => new { c.Id, c.Reference, c.Title, c.PerimeterId }).ToListAsync();
        var scope = await ScopeAsync();
        return Ok(new
        {
            Degraded = outOfService.Count > 0,
            Cis = outOfService.Select(c => scope.Sees(c.PerimeterId)
                ? new { c.Id, c.Reference, c.Title, Hidden = false }
                : new { Id = 0, Reference = "", Title = "CI d'un autre périmètre", Hidden = true })
        });
    }
}

/// <summary>Accords de niveau de service (SLA), rattachés à un service du catalogue.</summary>
[Route("api/agreements")]
public class AgreementsController(AppDbContext db) : RecordController<ServiceLevelAgreement, ServiceLevelAgreementDto, ServiceLevelAgreementView>(db)
{
    protected override ServiceLevelAgreementView View(ServiceLevelAgreement e) => ServiceLevelAgreementView.From(e);
    protected override string Code => "SLA";
    protected override string LinkType => "agreement";
    protected override string[] Statuses => ServiceLevelAgreement.Statuses;
    protected override IReadOnlyDictionary<string, string[]> Transitions => ServiceLevelAgreement.Transitions;
    protected override bool ManagerOnly => true;

    protected override string? Apply(ServiceLevelAgreement e, ServiceLevelAgreementDto dto, bool creating)
    {
        if (dto.AvailabilityTarget is < 0 or > 100) return "La disponibilité cible est un pourcentage (0 à 100).";
        if (dto.ComplianceTarget is <= 0 or > 100) return "La cible de respect est un pourcentage (au-dessus de 0, jusqu'à 100).";
        int?[] hours = [dto.ResolutionHoursP1, dto.ResolutionHoursP2, dto.ResolutionHoursP3, dto.ResolutionHoursP4];
        if (hours.Any(h => h is <= 0)) return "Les délais de résolution sont des nombres d'heures positifs.";
        int?[] response = [dto.ResponseHoursP1, dto.ResponseHoursP2, dto.ResponseHoursP3, dto.ResponseHoursP4];
        if (response.Any(h => h is <= 0)) return "Les délais de prise en charge sont des nombres d'heures positifs.";
        if (dto.ValidFrom is not null && dto.ValidTo is not null && dto.ValidTo < dto.ValidFrom)
            return "La fin de validité précède son début.";
        e.ServiceId = dto.ServiceId;
        e.Customer = dto.Customer ?? "";
        e.AvailabilityTarget = dto.AvailabilityTarget;
        e.ComplianceTarget = dto.ComplianceTarget;
        e.ResolutionHoursP1 = dto.ResolutionHoursP1; e.ResolutionHoursP2 = dto.ResolutionHoursP2;
        e.ResolutionHoursP3 = dto.ResolutionHoursP3; e.ResolutionHoursP4 = dto.ResolutionHoursP4;
        e.ResponseHoursP1 = dto.ResponseHoursP1; e.ResponseHoursP2 = dto.ResponseHoursP2;
        e.ResponseHoursP3 = dto.ResponseHoursP3; e.ResponseHoursP4 = dto.ResponseHoursP4;
        e.ValidFrom = Dates.Utc(dto.ValidFrom); e.ValidTo = Dates.Utc(dto.ValidTo);
        e.ReviewDate = Dates.Utc(dto.ReviewDate);
        return null;
    }

    protected override async Task<string?> ValidateAsync(ServiceLevelAgreement e)
    {
        var status = await Db.Services.Where(s => s.Id == e.ServiceId).Select(s => s.Status).FirstOrDefaultAsync();
        if (status is null) return "Choisir un service du catalogue.";
        // SLM-15 : un service retiré ne reçoit plus de nouvel accord.
        var changed = e.Id == 0 || !Equals(Db.Entry(e).Property(x => x.ServiceId).OriginalValue, e.ServiceId);
        return changed && status == "Retiré" ? "Le service est retiré : il ne reçoit plus de nouvel accord." : null;
    }

    /// <summary>
    /// SLM-03 : un accord en vigueur a des cibles complètes et croissantes
    /// (P1 ≤ P2 ≤ P3 ≤ P4), une date de début, un service en service, et il est
    /// le seul en vigueur pour ce service et ce client.
    /// </summary>
    protected override async Task<string?> CheckStatusAsync(ServiceLevelAgreement e, string status)
    {
        if (status != "En vigueur") return null;
        int?[] hours = [e.ResolutionHoursP1, e.ResolutionHoursP2, e.ResolutionHoursP3, e.ResolutionHoursP4];
        if (hours.Any(h => h is null))
            return "Renseigner les délais de résolution P1 à P4 pour mettre l'accord en vigueur.";
        for (var i = 1; i < hours.Length; i++)
            if (hours[i] < hours[i - 1])
                return $"Les délais de résolution doivent croître de P1 à P4 (P{i} : {hours[i - 1]} h, P{i + 1} : {hours[i]} h).";
        // SLM-13 : délais de prise en charge facultatifs ; donnés, ils sont complets, croissants
        // et ne dépassent pas le délai de résolution de la même priorité.
        int?[] response = [e.ResponseHoursP1, e.ResponseHoursP2, e.ResponseHoursP3, e.ResponseHoursP4];
        if (response.Any(h => h is not null))
        {
            if (response.Any(h => h is null))
                return "Renseigner les délais de prise en charge P1 à P4, ou aucun.";
            for (var i = 0; i < response.Length; i++)
            {
                if (i > 0 && response[i] < response[i - 1])
                    return $"Les délais de prise en charge doivent croître de P1 à P4 (P{i} : {response[i - 1]} h, P{i + 1} : {response[i]} h).";
                if (response[i] > hours[i])
                    return $"Le délai de prise en charge P{i + 1} ({response[i]} h) dépasse son délai de résolution ({hours[i]} h).";
            }
        }
        if (e.ValidFrom is null) return "Renseigner la date de début de validité.";
        if (!await Db.Services.AnyAsync(s => s.Id == e.ServiceId && s.Status == "En service"))
            return "Le service de l'accord doit être en service.";
        var customer = e.Customer.ToLower();
        var other = await Db.Agreements.AsNoTracking()
            .Where(a => a.Id != e.Id && a.Status == "En vigueur" && a.ServiceId == e.ServiceId && a.Customer.ToLower() == customer)
            .Select(a => a.Reference).FirstOrDefaultAsync();
        return other is null ? null : $"Un accord est déjà en vigueur pour ce service et ce client ({other}).";
    }

    protected override IQueryable<ServiceLevelAgreement> Filter(IQueryable<ServiceLevelAgreement> q) =>
        q.Include(a => a.Service);

    protected override IQueryable<ServiceLevelAgreement> WithDetails(IQueryable<ServiceLevelAgreement> q) =>
        q.Include(a => a.Service).Include(a => a.Reviews.OrderByDescending(r => r.ReviewedAt));

    /// <summary>
    /// SLM-20 : respect mesuré, mois par mois (format AAAA-MM, six derniers mois par défaut,
    /// vingt-quatre au plus), avec le détail par priorité et la cible.
    /// </summary>
    [HttpGet("{id:int}/compliance")]
    public async Task<IActionResult> Compliance(int id, [FromQuery] string? from, [FromQuery] string? to)
    {
        var e = await Db.Agreements.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (e is null || !await VisibleAsync(e)) return NotFound();
        static DateTime? Month(string? s) => DateTime.TryParseExact(s, "yyyy-MM", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var d)
            ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : null;
        var now = DateTime.UtcNow;
        var last = Month(to) ?? new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var first = Month(from) ?? last.AddMonths(-5);
        if ((from is not null && Month(from) is null) || (to is not null && Month(to) is null))
            return BadRequest(new { message = "Mois au format AAAA-MM." });
        if (first > last || first < last.AddMonths(-23)) return BadRequest(new { message = "Période de 1 à 24 mois." });
        var months = new List<SAloha.Api.Core.Itil.SlaCompliance.Month>();
        for (var m = first; m <= last; m = m.AddMonths(1)) months.Add(await SAloha.Api.Core.Itil.SlaCompliance.MonthAsync(Db, e, m, now));
        return Ok(new { e.ComplianceTarget, e.AvailabilityTarget, Months = months });
    }

    /// <summary>
    /// SLM-22 : le gestionnaire enregistre la revue du SLA (constats, décisions) ; la
    /// prochaine revue est reportée de 12 mois, sauf date donnée.
    /// </summary>
    [HttpPost("{id:int}/reviews")]
    public async Task<IActionResult> Review(int id, AgreementReviewDto dto)
    {
        var e = await Db.Agreements.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null || !await VisibleAsync(e)) return NotFound();
        ActOn(e.PerimeterId);
        if (!IsManager) return Forbid();
        if (e.Status == "Expiré") return BadRequest(new { message = "Un SLA expiré ne se revoit plus : créer un nouvel accord." });
        if (string.IsNullOrWhiteSpace(dto.Findings) && string.IsNullOrWhiteSpace(dto.Decisions))
            return BadRequest(new { message = "Renseigner les constats ou les décisions de la revue." });
        var next = Dates.Utc(dto.NextReviewDate) ?? DateTime.UtcNow.Date.AddMonths(12);
        if (next <= DateTime.UtcNow.Date) return BadRequest(new { message = "La prochaine revue doit être dans le futur." });
        var review = new AgreementReview
        {
            AgreementId = id, ReviewedBy = MyDisplay, NextReviewDate = next,
            Findings = dto.Findings?.Trim() ?? "", Decisions = dto.Decisions?.Trim() ?? ""
        };
        var error = Lengths.Check(review);
        if (error is not null) return BadRequest(new { message = error });
        Db.AgreementReviews.Add(review);
        e.ReviewDate = next; e.UpdatedAt = DateTime.UtcNow;
        await Db.SaveChangesAsync();
        return Ok(AgreementReviewView.From(review));
    }
}
