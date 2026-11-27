using System.ComponentModel.DataAnnotations;
using SAloha.Api.Core.Records;

namespace SAloha.Api.Modules.ChangeEnablement;

/// <summary>
/// Changement (ITIL, habilitation des changements) : évalué, autorisé puis
/// planifié au calendrier des changements.
/// </summary>
public class Change : Record
{
    public static readonly string[] Statuses = ["Demandé", "Évalué", "Autorisé", "Rejeté", "Planifié", "Mis en œuvre", "Clos"];
    /// <summary>Transitions permises (SOC-05, § 4 de la pratique) ; liste vide : statut final.</summary>
    public static readonly Dictionary<string, string[]> Transitions = new()
    {
        ["Demandé"] = ["Évalué"],
        ["Évalué"] = ["Autorisé", "Rejeté"],
        ["Autorisé"] = ["Planifié"],
        ["Planifié"] = ["Mis en œuvre"],
        ["Mis en œuvre"] = ["Clos"],
        ["Rejeté"] = [],
        ["Clos"] = [],
    };
    public static readonly string[] Done = ["Rejeté", "Clos"];
    public static readonly string[] Types = ["Standard", "Normal", "Urgent"];
    public static readonly string[] Risks = ["Faible", "Moyen", "Élevé"];
    public static readonly string[] Outcomes = ["Réussi", "Échoué"];

    [MaxLength(10)] public string ChangeType { get; set; } = "Normal"; // Standard (pré-autorisé), Normal, Urgent
    [MaxLength(10)] public string Risk { get; set; } = "Moyen";
    public DateTime? PlannedStart { get; set; }
    public DateTime? PlannedEnd { get; set; }
    public string ImplementationPlan { get; set; } = "";
    public string BackoutPlan { get; set; } = "";                     // retour arrière
    [MaxLength(10)] public string? Outcome { get; set; }              // Réussi, Échoué
    public string? RejectionReason { get; set; }                      // motif communiqué au demandeur (CHG-17)
    [MaxLength(200)] public string? AuthorizedBy { get; set; }
    public DateTime? AuthorizedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    /// <summary>CHG-14 : dates réelles de mise en œuvre.</summary>
    public DateTime? ActualStart { get; set; }
    public DateTime? ActualEnd { get; set; }
    /// <summary>CHG-15 : revue post-implémentation (ce qui s'est passé, causes, enseignements).</summary>
    public string? PirNotes { get; set; }
    /// <summary>CHG-22 : commentaire de l'autorité qui autorise un changement en conflit de calendrier.</summary>
    public string? ConflictComment { get; set; }
    /// <summary>CHG-31 : entrée dans Mis en œuvre (relance après 2 jours ouvrés sans clôture).</summary>
    public DateTime? ImplementedAt { get; set; }
    /// <summary>CHG-26 : modèle standard dont un changement Standard reprend les plans.</summary>
    public int? StandardModelId { get; set; }

    /// <summary>CHG-20 : un changement Normal à risque Élevé est autorisé par le CAB (deux gestionnaires).</summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool RequiresCab => ChangeType == "Normal" && Risk == "Élevé";

    /// <summary>CHG-14 : la mise en œuvre a dépassé la fin planifiée (terminée après, ou toujours en cours après).</summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool Overrun => PlannedEnd is { } end
        && (ActualEnd is { } actual ? actual > end : Status == "Mis en œuvre" && DateTime.UtcNow > end);

    /// <summary>
    /// CHG-30, CHG-31 : relance due — Planifié dont la fin planifiée est passée sans mise en œuvre ;
    /// Mis en œuvre depuis plus de 2 jours ouvrés sans être Clos. Null sinon.
    /// </summary>
    public string? FollowUp(DateTime now) =>
        Status == "Planifié" && PlannedEnd is { } end && end < now ? "Fin planifiée passée sans mise en œuvre"
        : Status == "Mis en œuvre" && (ImplementedAt ?? ActualStart ?? UpdatedAt) is var since
          && now > Core.Records.Dates.AddBusinessDays(since, 2) ? "Mis en œuvre depuis plus de 2 jours ouvrés, sans clôture"
        : null;
}

public record ChangeDto(string Title, string? Description, string? Status,
    string? OwnerType, string? OwnerId, string? OwnerDisplayName,
    string? ChangeType, string? Risk, DateTime? PlannedStart, DateTime? PlannedEnd,
    string? ImplementationPlan, string? BackoutPlan, string? Outcome, string? RejectionReason = null, int? PerimeterId = null,
    DateTime? ActualStart = null, DateTime? ActualEnd = null, string? PirNotes = null, int? StandardModelId = null,
    string? ConflictComment = null) : IRecordDto;

/// <summary>CHG-20 : décision d'un membre du CAB sur un changement Normal à risque Élevé.</summary>
public class ChangeApproval
{
    public int Id { get; set; }
    public int ChangeId { get; set; }
    [MaxLength(200)] public string Approver { get; set; } = "";
    [MaxLength(250)] public string ApproverDisplayName { get; set; } = "";
    public bool Approved { get; set; }
    [MaxLength(2000)] public string Comment { get; set; } = "";
    public DateTime At { get; set; } = DateTime.UtcNow;
}

/// <summary>CHG-25 : période de gel — aucun changement (sauf Urgent) ne se planifie dessus.</summary>
public class ChangeFreeze
{
    public int Id { get; set; }
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    [MaxLength(500)] public string Reason { get; set; } = "";
    [MaxLength(200)] public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>CHG-26 : modèle de changement standard — plans types et risque, déjà évalués.</summary>
public class StandardChangeModel
{
    public int Id { get; set; }
    [MaxLength(200)] public string Name { get; set; } = "";
    [MaxLength(2000)] public string Description { get; set; } = "";
    public string ImplementationPlan { get; set; } = "";
    public string BackoutPlan { get; set; } = "";
    [MaxLength(10)] public string Risk { get; set; } = "Faible";
    public bool Active { get; set; } = true;
    [MaxLength(200)] public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>CHG-27 : suspendu à l'échec d'un changement créé depuis lui, jusqu'à la revue d'un gestionnaire.</summary>
    public DateTime? SuspendedAt { get; set; }
    public int? SuspendedByChangeId { get; set; }
    [MaxLength(20)] public string? SuspendedByChange { get; set; }
    /// <summary>CHG-27 : dernière revue après suspension (gestionnaire, date, conclusion).</summary>
    public DateTime? ReviewedAt { get; set; }
    [MaxLength(200)] public string? ReviewedBy { get; set; }
    public string? ReviewNote { get; set; }
}

public record ApprovalDto(bool Approve, string? Comment);
public record FreezeDto(DateTime Start, DateTime End, string? Reason);
/// <summary>CHG-27 : revue d'un modèle suspendu ; <c>Keep</c> faux : le modèle est désactivé.</summary>
public record StandardModelReviewDto(string Note, bool Keep = true);

public record StandardChangeModelDto(string Name, string? Description, string? ImplementationPlan, string? BackoutPlan,
    string? Risk, bool Active = true);
