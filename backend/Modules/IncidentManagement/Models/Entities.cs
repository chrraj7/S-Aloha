using SAloha.Api.Core.Itil;
using System.ComponentModel.DataAnnotations;
using SAloha.Api.Core.Records;

namespace SAloha.Api.Modules.IncidentManagement;

/// <summary>
/// Incident (ITIL, gestion des incidents) : interruption ou dégradation
/// d'un service, à rétablir au plus vite.
/// </summary>
public class Incident : Record, IServiceBound
{
    /// <summary>INC-11 : fenêtre de réouverture ; INC-10 : clôture automatique à son terme (#335).</summary>
    public const int ReopenBusinessDays = 10;

    public static readonly string[] Statuses = ["Nouveau", "En cours", "En attente", "Résolu", "Clos"];
    /// <summary>Transitions permises (SOC-05, § 4 de la pratique) ; liste vide : statut final.</summary>
    public static readonly Dictionary<string, string[]> Transitions = new()
    {
        ["Nouveau"] = ["En cours", "Résolu"],
        ["En cours"] = ["En attente", "Résolu"],
        ["En attente"] = ["En cours"],
        ["Résolu"] = ["En cours", "Clos"],
        ["Clos"] = [],   // un incident clos ne se rouvre pas : on en crée un nouveau, relié
    };
    public static readonly string[] Done = ["Résolu", "Clos"];
    /// <summary>Codes de résolution (INC-08).</summary>
    public static readonly string[] ResolutionCodes =
        ["Correctif appliqué", "Contournement", "Résolu sans action", "Non reproductible", "Doublon"];

    [MaxLength(10)] public string Impact { get; set; } = "Moyen";    // Faible, Moyen, Élevé
    [MaxLength(10)] public string Urgency { get; set; } = "Moyenne"; // Faible, Moyenne, Élevée
    [MaxLength(10)] public string Priority { get; set; } = "P3";     // P1..P4 (dérivée impact x urgence)
    [MaxLength(100)] public string Category { get; set; } = "";
    [MaxLength(150)] public string AffectedService { get; set; } = "";
    public int? ServiceId { get; set; }                                  // SLM-10, INC-20 : service du catalogue
    public bool IsMajor { get; set; }                                // incident majeur
    /// <summary>INC-04 : canal de signalement.</summary>
    public static readonly string[] Channels = ["Portail", "Téléphone", "Courriel", "Supervision", "Sur place"];
    [MaxLength(20)] public string? Channel { get; set; }
    /// <summary>INC-05 : utilisateur affecté (déclarant métier), distinct de l'agent qui enregistre.</summary>
    [MaxLength(200)] public string? CallerId { get; set; }
    [MaxLength(250)] public string? CallerDisplayName { get; set; }
    /// <summary>INC-17 : motif de la dernière escalade fonctionnelle vers un autre groupe.</summary>
    public string? EscalationReason { get; set; }
    /// <summary>INC-40 : incident de sécurité (cyber), marqué par tout rôle.</summary>
    public bool IsSecurity { get; set; }
    /// <summary>INC-41 : qualification réglementaire par un gestionnaire ; null tant qu'elle n'est pas faite.</summary>
    public DateTime? QualifiedAt { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(200)] public string? QualifiedBy { get; set; }
    /// <summary>INC-41 : incident important au sens de NIS 2, et son motif.</summary>
    public bool? Nis2Important { get; set; }
    public string? Nis2Reason { get; set; }
    /// <summary>INC-41 : violation de données à caractère personnel (RGPD), et son motif.</summary>
    public bool? PersonalDataBreach { get; set; }
    public string? PersonalDataBreachReason { get; set; }
    public string? Resolution { get; set; }
    [MaxLength(30)] public string? ResolutionCode { get; set; }         // INC-08
    public DateTime? FirstResponseAt { get; set; }                       // première prise en charge (INC-06)
    public DateTime? ResolvedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public int ReopenCount { get; set; }                                 // INC-11 : nombre de réouvertures
    /// <summary>INC-27 : début constaté de l'interruption, facultatif (MTTD = création − début).</summary>
    public DateTime? OccurredAt { get; set; }
    /// <summary>INC-21 : échéance de résolution tirée du SLA en vigueur du service ; null sans SLA.</summary>
    public DateTime? ResolutionDueAt { get; set; }
    public DateTime? ResponseDueAt { get; set; }                         // SLM-13 : échéance de prise en charge
    /// <summary>Le responsable a changé depuis la création (INC-23).</summary>
    public bool OwnerChanged { get; set; }
    /// <summary>INC-23 : résolu sans passer par En cours ni changer de responsable ; null tant que non résolu.</summary>
    public bool? FirstContactResolution { get; set; }
    public string? ReopenReason { get; set; }                            // INC-11 : motif de réouverture
    /// <summary>INC-07 : motifs d'attente (valeurs fermées).</summary>
    public static readonly string[] OnHoldReasons = ["Attente utilisateur", "Attente fournisseur", "Attente changement"];
    [MaxLength(30)] public string? OnHoldReason { get; set; }            // INC-07
    public DateTime? OnHoldSince { get; set; }                           // INC-22 : début de l'attente en cours
    public int HeldMinutes { get; set; }                                 // INC-22 : attente cumulée, en minutes de service
    /// <summary>INC-16 : incident parent (même panne) ; un seul niveau : un parent n'a pas lui-même de parent.</summary>
    public int? ParentId { get; set; }

    /// <summary>INC-26 : délai, en jours ouvrés, au-delà duquel un incident En attente remonte au responsable.</summary>
    public const int HoldFollowUpBusinessDays = 5;

    /// <summary>INC-26 : motif de relance d'un incident En attente depuis plus de 5 jours ouvrés, sinon null.</summary>
    public string? HoldFollowUp(DateTime now) =>
        Status == "En attente" && OnHoldSince is { } since && now > Dates.AddBusinessDays(since, HoldFollowUpBusinessDays)
            ? $"En attente depuis le {since:dd/MM/yyyy} ({OnHoldReason ?? "sans motif"})" : null;

    /// <summary>INC-24 : état SLA calculé à la lecture (fiche, registre, console, reporting).</summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string SlaState => IncidentSla.State(this, DateTime.UtcNow);
}

public record IncidentDto(string Title, string? Description, string? Status,
    string? OwnerType, string? OwnerId, string? OwnerDisplayName,
    string? Impact, string? Urgency, string? Category, string? AffectedService,
    bool IsMajor, string? Resolution, string? ResolutionCode = null,
    string? ReopenReason = null, DateTime? OccurredAt = null, int? PerimeterId = null, int? ServiceId = null,
    string? OnHoldReason = null, bool? IsSecurity = null, string? Channel = null, string? CallerId = null,
    string? CallerDisplayName = null, string? EscalationReason = null) : IRecordDto;

/// <summary>INC-16 : rattachement à un incident parent, désigné par sa référence ; vide : détaché.</summary>
public record IncidentParentDto(string? ParentReference);

/// <summary>INC-16 : un incident de la famille (parent ou enfant).</summary>
public record IncidentRelative(int Id, string Reference, string Title, string Status, string Priority);

/// <summary>INC-41 : qualification réglementaire d'un incident cyber ; <c>AwareAt</c> : prise de connaissance, si autre que la création.</summary>
public record IncidentQualificationDto(bool Nis2Important, string? Nis2Reason, bool PersonalDataBreach, string? PersonalDataBreachReason,
    DateTime? AwareAt = null);
