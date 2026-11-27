using System.ComponentModel.DataAnnotations;
using SAloha.Api.Core.Records;

namespace SAloha.Api.Modules.KnowledgeManagement;

/// <summary>
/// Article de connaissance (ITIL, gestion des connaissances) : solution,
/// procédure, erreur connue documentée ou FAQ. La description porte le résumé.
/// </summary>
public class KnowledgeArticle : Record
{
    public static readonly string[] Statuses = ["Brouillon", "Publié", "Archivé"];
    /// <summary>Transitions permises (SOC-05, § 4 de la pratique) ; liste vide : statut final.</summary>
    public static readonly Dictionary<string, string[]> Transitions = new()
    {
        ["Brouillon"] = ["Publié", "Archivé"],
        ["Publié"] = ["Archivé"],   // retour en Brouillon : automatique, à la retouche (StatusAfterEdit)
        ["Archivé"] = ["Brouillon"],
    };
    public static readonly string[] Types = ["Solution", "Procédure", "Erreur connue", "FAQ"];

    [MaxLength(20)] public string ArticleType { get; set; } = "Solution";
    public string Content { get; set; } = "";
    [MaxLength(300)] public string Keywords { get; set; } = "";
    public DateTime? ReviewDate { get; set; }
    [MaxLength(200)] public string? PublishedBy { get; set; }
    public DateTime? PublishedAt { get; set; }
    /// <summary>KB-21 : brouillon soumis à publication (« prêt »), effacé à la publication.</summary>
    public DateTime? SubmittedAt { get; set; }
    [MaxLength(200)] public string? SubmittedBy { get; set; }
    /// <summary>KB-17 : dernière revue confirmée par le responsable ou un gestionnaire.</summary>
    public DateTime? LastReviewedAt { get; set; }
    [MaxLength(200)] public string? LastReviewedBy { get; set; }
    /// <summary>KB-17 : archivage demandé à la revue, en attente d'un gestionnaire.</summary>
    public DateTime? ArchiveRequestedAt { get; set; }
    [MaxLength(200)] public string? ArchiveRequestedBy { get; set; }
    [MaxLength(500)] public string? ArchiveRequestReason { get; set; }
    /// <summary>KB-15 : numéro de la version publiée, incrémenté à chaque publication (0 : jamais publié).</summary>
    public int Version { get; set; }
    /// <summary>KB-13 : retours des lecteurs sur la version publiée (un vote par utilisateur).</summary>
    public int HelpfulCount { get; set; }
    public int NotHelpfulCount { get; set; }

    /// <summary>KB-13 : écart de « pas utile » sur « utile » à partir duquel l'article est signalé.</summary>
    public const int UnhelpfulMargin = 3;
    /// <summary>KB-13 : article publié jugé peu utile, à revoir par son responsable.</summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool FlaggedUnhelpful => Status == "Publié" && NotHelpfulCount - HelpfulCount >= UnhelpfulMargin;
}

/// <summary>KB-15 : version publiée d'un article (contenu, rédacteurs, publication), conservée à chaque publication.</summary>
public class KnowledgeArticleVersion
{
    public int Id { get; set; }
    public int ArticleId { get; set; }
    public int Version { get; set; }
    [MaxLength(200)] public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    [MaxLength(20)] public string ArticleType { get; set; } = "";
    public string Content { get; set; } = "";
    /// <summary>Rédacteurs de la version (comptes), séparés par des virgules.</summary>
    [MaxLength(1000)] public string Authors { get; set; } = "";
    [MaxLength(200)] public string PublishedBy { get; set; } = "";
    public DateTime PublishedAt { get; set; }
}

/// <summary>KB-13 : vote d'un lecteur (« utile » ou non) sur la version publiée d'un article.</summary>
public class KnowledgeFeedback
{
    public int Id { get; set; }
    public int ArticleId { get; set; }
    [MaxLength(200)] public string Username { get; set; } = "";
    public bool Helpful { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
}

public record FeedbackDto(bool Helpful);

public record ReviewDto(DateTime? NextReviewDate);
public record ArchiveRequestDto(string? Reason);

public record KnowledgeArticleDto(string Title, string? Description, string? Status,
    string? OwnerType, string? OwnerId, string? OwnerDisplayName,
    string? ArticleType, string? Content, string? Keywords, DateTime? ReviewDate, int? PerimeterId = null) : IRecordDto;
