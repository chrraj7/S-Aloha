using SAloha.Api.Core.Itil;
using System.ComponentModel.DataAnnotations;
using SAloha.Api.Core.Records;

namespace SAloha.Api.Modules.ServiceRequestManagement;

/// <summary>
/// Demande de service (ITIL, gestion des demandes de service) : demande
/// prédéfinie initiée par un utilisateur (accès, matériel, information…).
/// </summary>
public class ServiceRequest : Record, IServiceBound
{
    public static readonly string[] Statuses = ["Soumise", "Approuvée", "Rejetée", "Annulée", "En cours", "Satisfaite", "Close"];
    /// <summary>Transitions permises (SOC-05, § 4 de la pratique) ; liste vide : statut final.</summary>
    public static readonly Dictionary<string, string[]> Transitions = new()
    {
        ["Soumise"] = ["Approuvée", "Rejetée", "Annulée"],
        ["Approuvée"] = ["En cours", "Annulée"],
        ["En cours"] = ["Satisfaite"],
        ["Satisfaite"] = ["En cours", "Close"],
        ["Rejetée"] = [],
        ["Annulée"] = [],
        ["Close"] = [],
    };
    public static readonly string[] Done = ["Rejetée", "Annulée", "Satisfaite", "Close"];

    [MaxLength(200)] public string RequestedItem { get; set; } = "";          // objet demandé
    [MaxLength(150)] public string AffectedService { get; set; } = "";        // SLM-10 : nom du service, recopié du catalogue
    public int? ServiceId { get; set; }                                       // SLM-10 : service du catalogue
    [MaxLength(200)] public string? RequestedFor { get; set; }                // bénéficiaire (sAMAccountName)
    [MaxLength(250)] public string? RequestedForDisplayName { get; set; }
    public DateTime? DueDate { get; set; }
    public string? RejectionReason { get; set; }                          // motif communiqué au demandeur (REQ-06)
    [MaxLength(200)] public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? FulfilledAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public DateTime? CancelledAt { get; set; }                            // REQ-09 : annulation par le demandeur
    public int? CatalogItemId { get; set; }                               // REQ-20 : modèle du catalogue de demandes
    /// <summary>REQ-10 : motif de la contestation d'une demande satisfaite.</summary>
    public string? ContestReason { get; set; }
    /// <summary>REQ-12 : motif d'annulation (requalification vers un changement ou un incident).</summary>
    public string? CancelReason { get; set; }

    /// <summary>REQ-10 : délai de contestation d'une demande satisfaite, en jours ouvrés.</summary>
    public const int ContestBusinessDays = 5;
    /// <summary>REQ-32 : enquête de satisfaction (note 1 à 5, commentaire), facultative, à la clôture.</summary>
    public int? SatisfactionScore { get; set; }
    public string? SatisfactionComment { get; set; }
    public DateTime? SatisfactionAt { get; set; }
    /// <summary>REQ-11 : clôture automatique, 5 jours ouvrés après la satisfaction sans contestation.</summary>
    public bool AutoClosed { get; set; }

    /// <summary>REQ-30 : en retard le lendemain de l'échéance, tant qu'elle n'est ni satisfaite, ni close, ni rejetée, ni annulée.</summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool Overdue => DueDate is { } due && due.Date < DateTime.UtcNow.Date && !Done.Contains(Status);
}

public record ServiceRequestDto(string Title, string? Description, string? Status,
    string? OwnerType, string? OwnerId, string? OwnerDisplayName,
    string? RequestedItem, string? RequestedFor, string? RequestedForDisplayName,
    DateTime? DueDate, string? RejectionReason = null, int? PerimeterId = null,
    int? ServiceId = null, string? AffectedService = null, int? CatalogItemId = null, string? ContestReason = null) : IRecordDto;

/// <summary>REQ-12 : requalification d'une demande en changement (« change ») ou en incident (« incident »).</summary>
public record RequalifyDto(string Target, string? Reason);

/// <summary>
/// REQ-20 : modèle du catalogue de demandes, tenu par les gestionnaires — objet demandé,
/// service du catalogue, approbation requise (REQ-21), délai cible en heures de service
/// (REQ-22), groupe d'exécution AD (REQ-23), actif ou non.
/// </summary>
public class RequestCatalogItem
{
    public int Id { get; set; }
    [MaxLength(200)] public string Name { get; set; } = "";
    [MaxLength(2000)] public string Description { get; set; } = "";
    public int? ServiceId { get; set; }
    public bool RequiresApproval { get; set; } = true;
    public int? TargetHours { get; set; }
    [MaxLength(200)] public string? FulfillmentGroup { get; set; }
    public bool Active { get; set; } = true;
    [MaxLength(200)] public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>REQ-24 : tâches prédéfinies, dans l'ordre.</summary>
    public List<RequestCatalogTask> Tasks { get; set; } = [];
}

public record RequestCatalogItemDto(string Name, string? Description, int? ServiceId, bool RequiresApproval,
    int? TargetHours, string? FulfillmentGroup, bool Active = true, List<RequestCatalogTaskDto>? Tasks = null);
