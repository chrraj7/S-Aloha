using SAloha.Api.Core.Itil;
using System.ComponentModel.DataAnnotations;
using SAloha.Api.Core.Records;

namespace SAloha.Api.Modules.ProblemManagement;

/// <summary>
/// Problème (ITIL, gestion des problèmes) : cause d'un ou plusieurs incidents.
/// Enregistrement du socle commun (référence, statut, responsable = gestionnaire du
/// problème, périmètre), avec ses analyses de cause racine et ses actions correctives.
/// </summary>
public class Problem : Record, IServiceBound
{
    public static readonly string[] Statuses = ["Nouveau", "En analyse", "Erreur connue", "Résolu", "Clos"];
    /// <summary>
    /// Transitions permises (SOC-05, PRB-10) ; liste vide : statut final. Le
    /// passage Nouveau → En analyse est aussi automatique à la première analyse.
    /// </summary>
    public static readonly Dictionary<string, string[]> Transitions = new()
    {
        ["Nouveau"] = ["En analyse", "Clos"],
        ["En analyse"] = ["Erreur connue", "Résolu"],
        ["Erreur connue"] = ["Résolu", "Clos"],
        ["Résolu"] = ["Clos", "En analyse"],
        ["Clos"] = ["En analyse"],
    };

    [MaxLength(10)] public string Impact { get; set; } = "Moyen";   // Faible, Moyen, Élevé
    [MaxLength(10)] public string Urgency { get; set; } = "Moyenne";// Faible, Moyenne, Élevée
    [MaxLength(10)] public string Priority { get; set; } = "P3";    // P1..P4 (dérivée impact x urgence)
    [MaxLength(100)] public string Category { get; set; } = "";     // Réseau, Serveur, Application...
    [MaxLength(150)] public string AffectedService { get; set; } = "";
    public int? ServiceId { get; set; }                             // SLM-10 : service du catalogue
    public string? KnownErrorWorkaround { get; set; }               // contournement (Known Error)
    public string? RootCause { get; set; }                          // cause racine validée
    /// <summary>PRB-20 : Réactif (issu d'incidents, défaut) ou Proactif (tendance, analyse de risque).</summary>
    [MaxLength(10)] public string Source { get; set; } = "Réactif";
    public static readonly string[] Sources = ["Réactif", "Proactif"];
    [MaxLength(30)] public string? ClosureCode { get; set; }        // code de clôture (PRB-13)
    public DateTime? ResolvedAt { get; set; }                       // entrée en Résolu (PRB-14)
    public string? ReopenReason { get; set; }                       // motif de la dernière réouverture (PRB-10)
    public DateTime? ClosedAt { get; set; }
    public List<RcaAnalysis> Analyses { get; set; } = new();
    public List<CorrectiveAction> Actions { get; set; } = new();
}

public class RcaAnalysis
{
    public int Id { get; set; }
    public int ProblemId { get; set; }
    public Problem? Problem { get; set; }
    [MaxLength(20)] public string Method { get; set; } = "FIVE_WHYS"; // FIVE_WHYS, ISHIKAWA, FTA
    public string DataJson { get; set; } = "{}";                      // structure propre à la méthode
    public string? Conclusion { get; set; }
    /// <summary>PRB-07 : conclusion validée comme cause racine du problème.</summary>
    public DateTime? ValidatedAt { get; set; }
    [MaxLength(200)] public string? ValidatedBy { get; set; }
    [MaxLength(100)] public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class CorrectiveAction
{
    public int Id { get; set; }
    public int ProblemId { get; set; }
    public Problem? Problem { get; set; }
    [MaxLength(200)] public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    [MaxLength(20)] public string Status { get; set; } = "À faire";  // À faire, En cours, Terminée, Annulée
    public DateTime? DueDate { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? ApprovalRequestedAt { get; set; }              // approbation demandée à l'A (PRB-25)
    [MaxLength(200)] public string? ApprovalRequestedBy { get; set; }
    public int? ChangeId { get; set; }                              // changement qui met en œuvre le correctif (PRB-27)
    [MaxLength(30)] public string? ChangeReference { get; set; }
    [MaxLength(100)] public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<RaciAssignment> Raci { get; set; } = new();
}

public class RaciAssignment
{
    public int Id { get; set; }
    public int CorrectiveActionId { get; set; }
    public CorrectiveAction? Action { get; set; }
    [MaxLength(1)] public string Role { get; set; } = "R";           // R, A, C, I
    [MaxLength(10)] public string AssigneeType { get; set; } = "User"; // User | Group
    [MaxLength(200)] public string AssigneeId { get; set; } = "";    // sAMAccountName ou nom du groupe AD
    [MaxLength(250)] public string AssigneeDisplayName { get; set; } = "";
}
