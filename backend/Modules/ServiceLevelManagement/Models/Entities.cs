using System.ComponentModel.DataAnnotations;
using SAloha.Api.Core.Records;

namespace SAloha.Api.Modules.ServiceLevelManagement;

/// <summary>Service du catalogue (ITIL, gestion des niveaux de service). Le titre porte le nom du service.</summary>
public class ItService : Record
{
    public static readonly string[] Statuses = ["En conception", "En service", "Retiré"];
    /// <summary>Transitions permises (SOC-05, § 4 de la pratique) ; liste vide : statut final.</summary>
    public static readonly Dictionary<string, string[]> Transitions = new()
    {
        ["En conception"] = ["En service"],
        ["En service"] = ["Retiré"],
        ["Retiré"] = [],
    };
    public static readonly string[] Criticalities = ["Faible", "Moyenne", "Élevée"];

    [MaxLength(10)] public string Criticality { get; set; } = "Moyenne";
    [MaxLength(150)] public string ServiceHours { get; set; } = "";    // ex. Lun–Ven 8h–18h (libellé)
    // SLM-14 : heures de service structurées (ServiceCalendar) ; toutes vides : 24 h/24, 7 j/7.
    [MaxLength(20)] public string WorkingDays { get; set; } = "";      // « 1,2,3,4,5 » (1 = lundi)
    [MaxLength(5)] public string? OpeningTime { get; set; }           // « 08:00 »
    [MaxLength(5)] public string? ClosingTime { get; set; }           // « 18:00 »
    [MaxLength(2000)] public string Holidays { get; set; } = "";      // « 2026-12-25 2027-01-01 »
    [MaxLength(64)] public string? TimeZone { get; set; }             // IANA, UTC par défaut

    public SAloha.Api.Core.Itil.ServiceCalendar Calendar() =>
        SAloha.Api.Core.Itil.ServiceCalendar.Parse(WorkingDays, OpeningTime, ClosingTime, Holidays, TimeZone).Calendar
        ?? SAloha.Api.Core.Itil.ServiceCalendar.AlwaysOpen;
    public List<ServiceLevelAgreement> Agreements { get; set; } = new();
}

/// <summary>
/// Accord de niveau de service : cibles convenues avec un client pour un
/// service (disponibilité, délais de résolution des incidents par priorité).
/// </summary>
public class ServiceLevelAgreement : Record
{
    public static readonly string[] Statuses = ["Brouillon", "En vigueur", "Expiré"];
    /// <summary>Transitions permises (SOC-05, § 4 de la pratique) ; liste vide : statut final.</summary>
    public static readonly Dictionary<string, string[]> Transitions = new()
    {
        ["Brouillon"] = ["En vigueur"],
        ["En vigueur"] = ["Expiré", "Brouillon"],
        ["Expiré"] = [],   // un nouvel accord se crée
    };

    public int ServiceId { get; set; }
    public ItService? Service { get; set; }
    [MaxLength(200)] public string Customer { get; set; } = "";
    public decimal? AvailabilityTarget { get; set; }                  // en %, ex. 99.5
    /// <summary>SLM-20, SLM-23 : part cible des incidents résolus dans le délai, en % ; sans cible, pas d'alerte.</summary>
    public decimal? ComplianceTarget { get; set; }
    public int? ResolutionHoursP1 { get; set; }
    public int? ResolutionHoursP2 { get; set; }
    public int? ResolutionHoursP3 { get; set; }
    public int? ResolutionHoursP4 { get; set; }
    /// <summary>SLM-13 : délais de prise en charge par priorité (heures), mesurés sur FirstResponseAt.</summary>
    public int? ResponseHoursP1 { get; set; }
    public int? ResponseHoursP2 { get; set; }
    public int? ResponseHoursP3 { get; set; }
    public int? ResponseHoursP4 { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public DateTime? ReviewDate { get; set; }
    public List<AgreementReview> Reviews { get; set; } = new();
}

/// <summary>SLM-22 : revue d'un SLA — constats et décisions ; reporte la prochaine revue.</summary>
public class AgreementReview
{
    public int Id { get; set; }
    public int AgreementId { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public ServiceLevelAgreement? Agreement { get; set; }
    public DateTime ReviewedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(200)] public string ReviewedBy { get; set; } = "";
    [MaxLength(4000)] public string Findings { get; set; } = "";
    [MaxLength(4000)] public string Decisions { get; set; } = "";
    public DateTime? NextReviewDate { get; set; }
}

public record ItServiceDto(string Title, string? Description, string? Status,
    string? OwnerType, string? OwnerId, string? OwnerDisplayName,
    string? Criticality, string? ServiceHours, int? PerimeterId = null,
    string? WorkingDays = null, string? OpeningTime = null, string? ClosingTime = null, string? Holidays = null, string? TimeZone = null) : IRecordDto;

public record ServiceLevelAgreementDto(string Title, string? Description, string? Status,
    string? OwnerType, string? OwnerId, string? OwnerDisplayName,
    int ServiceId, string? Customer, decimal? AvailabilityTarget,
    int? ResolutionHoursP1, int? ResolutionHoursP2, int? ResolutionHoursP3, int? ResolutionHoursP4,
    DateTime? ValidFrom, DateTime? ValidTo, DateTime? ReviewDate, int? PerimeterId = null,
    int? ResponseHoursP1 = null, int? ResponseHoursP2 = null, int? ResponseHoursP3 = null, int? ResponseHoursP4 = null,
    decimal? ComplianceTarget = null) : IRecordDto;

public record AgreementReviewDto(string? Findings, string? Decisions, DateTime? NextReviewDate);
