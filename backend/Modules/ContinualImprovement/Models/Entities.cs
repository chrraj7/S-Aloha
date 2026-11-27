using System.ComponentModel.DataAnnotations;
using SAloha.Api.Core.Records;

namespace SAloha.Api.Modules.ContinualImprovement;

/// <summary>
/// Initiative d'amélioration (ITIL, amélioration continue), inscrite au
/// registre d'amélioration continue (CIR) et suivie selon les sept étapes du
/// modèle d'amélioration continue. La description porte l'opportunité.
/// </summary>
public class Improvement : Record
{
    public static readonly string[] Statuses = ["Proposée", "Validée", "En cours", "Réalisée", "Abandonnée"];
    /// <summary>Transitions permises (SOC-05, § 4 de la pratique) ; liste vide : statut final.</summary>
    public static readonly Dictionary<string, string[]> Transitions = new()
    {
        ["Proposée"] = ["Validée", "Abandonnée"],
        ["Validée"] = ["En cours", "Abandonnée"],
        ["En cours"] = ["Réalisée", "Abandonnée"],
        ["Abandonnée"] = ["Proposée"],
        ["Réalisée"] = [],
    };
    public static readonly string[] Done = ["Réalisée", "Abandonnée"];
    public static readonly string[] Priorities = ["Faible", "Moyenne", "Élevée"];
    /// <summary>CSI-10 : origine de l'amélioration.</summary>
    public static readonly string[] Sources = ["Suggestion", "Incident", "Problème", "SLA", "Audit", "Retour client"];

    /// <summary>CSI-12 : priorité tirée de la valeur et de l'effort (1 à 3).</summary>
    public static string PriorityOf(int value, int effort) =>
        value == 3 && effort <= 2 ? "Élevée" : value == 1 && effort >= 2 ? "Faible" : "Moyenne";
    /// <summary>Modèle d'amélioration continue ITIL, étapes 1 à 7.</summary>
    public static readonly string[] Steps =
    [
        "Quelle est la vision ?", "Où en sommes-nous ?", "Où voulons-nous être ?",
        "Comment y parvenir ?", "Passer à l'action", "Y sommes-nous parvenus ?",
        "Comment maintenir la dynamique ?"
    ];

    public int Step { get; set; } = 1;
    [MaxLength(10)] public string Priority { get; set; } = "Moyenne";
    public string Benefit { get; set; } = "";              // valeur attendue
    [MaxLength(300)] public string Baseline { get; set; } = "";   // mesure de départ
    [MaxLength(300)] public string Target { get; set; } = "";     // mesure cible
    public string? Outcome { get; set; }                   // résultat constaté
    public string? AbandonReason { get; set; }             // motif d'abandon (CSI-13)
    [MaxLength(20)] public string Source { get; set; } = "Suggestion";   // CSI-10
    public int? Value { get; set; }                        // CSI-12 : valeur métier, 1 à 3
    public int? Effort { get; set; }                       // CSI-12 : effort estimé, 1 à 3
    public DateTime? DueDate { get; set; }
    [MaxLength(200)] public string? ValidatedBy { get; set; }
    public DateTime? ValidatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    /// <summary>CSI-15 : contrôle de la durabilité (étape 7) — date, puis conclusion du porteur.</summary>
    public DateTime? FollowUpDate { get; set; }
    public DateTime? FollowUpDoneAt { get; set; }
    [MaxLength(200)] public string? FollowUpDoneBy { get; set; }
    public bool? FollowUpHolds { get; set; }
    public string? FollowUpNote { get; set; }
}

public record ImprovementDto(string Title, string? Description, string? Status,
    string? OwnerType, string? OwnerId, string? OwnerDisplayName,
    int Step, string? Priority, string? Benefit, string? Baseline, string? Target,
    string? Outcome, DateTime? DueDate, string? AbandonReason = null, int? PerimeterId = null,
    string? Source = null, int? Value = null, int? Effort = null, string? OriginReference = null,
    DateTime? FollowUpDate = null) : IRecordDto;

/// <summary>CSI-15 : conclusion du contrôle de durabilité ; si le résultat ne tient pas, une nouvelle amélioration est ouverte.</summary>
public record FollowUpDto(bool Holds, string? Note);
