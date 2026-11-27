using System.ComponentModel.DataAnnotations;
using SAloha.Api.Core.Records;

namespace SAloha.Api.Modules.ServiceConfigurationManagement;

/// <summary>
/// Élément de configuration (ITIL, gestion de la configuration des
/// services) : composant à gérer pour délivrer un service. Le titre porte le nom du CI.
/// </summary>
public class ConfigurationItem : Record
{
    public static readonly string[] Statuses = ["Planifié", "En service", "Hors service", "Retiré"];
    /// <summary>Transitions permises (SOC-05, § 4 de la pratique) ; liste vide : statut final.</summary>
    public static readonly Dictionary<string, string[]> Transitions = new()
    {
        ["Planifié"] = ["En service", "Retiré"],
        ["En service"] = ["Hors service", "Retiré"],
        ["Hors service"] = ["En service", "Retiré"],
        ["Retiré"] = [],
    };
    public static readonly string[] Types =
        ["Application", "Serveur", "Base de données", "Réseau", "Stockage", "Poste de travail", "Logiciel", "Autre"];
    public static readonly string[] Environments = ["Production", "Recette", "Développement", "Autre"];
    /// <summary>Statuts où les champs « En service » du portefeuille et du matériel sont exigés (CFG-43, CFG-44).</summary>
    public static readonly string[] InOperation = ["En service", "Hors service"];
    /// <summary>Types qui portent une identification réseau et matérielle (§ 3.2, § 3.4).</summary>
    public static readonly string[] HardwareTypes = ["Serveur", "Réseau", "Stockage", "Poste de travail"];
    /// <summary>CFG-44 : types dont le constructeur, le numéro de série et l'emplacement sont exigés (un Serveur peut être une VM).</summary>
    public static readonly string[] PhysicalTypes = ["Réseau", "Stockage", "Poste de travail"];
    public static readonly string[] Criticalities = ["Vitale", "Majeure", "Standard", "Mineure"];
    public static readonly string[] AppLifecycles = ["Étude", "Développement", "Homologation", "En production", "Dépréciée", "Retirée"];

    /// <summary>CFG-15 : dernière attestation de la fiche par le propriétaire ou un gestionnaire.</summary>
    public DateTime? LastVerifiedAt { get; set; }
    [MaxLength(200)] public string? LastVerifiedBy { get; set; }
    [MaxLength(30)] public string CiType { get; set; } = "Autre";
    [MaxLength(20)] public string Environment { get; set; } = "Production";
    [MaxLength(150)] public string Location { get; set; } = "";

    // Identification technique (§ 3.2) : critères de rapprochement de la découverte (CFG-41).
    [MaxLength(45)] public string? IpAddress { get; set; }
    [MaxLength(17)] public string? MacAddress { get; set; }
    [MaxLength(150)] public string? OperatingSystem { get; set; }

    // Portefeuille applicatif (§ 3.3, CFG-43, CFG-48).
    [MaxLength(20)] public string? AppCode { get; set; }
    [MaxLength(100)] public string? BusinessUnit { get; set; }
    [MaxLength(10)] public string? BusinessOwnerType { get; set; }
    [MaxLength(200)] public string? BusinessOwnerId { get; set; }
    [MaxLength(250)] public string? BusinessOwnerDisplayName { get; set; }
    [MaxLength(10)] public string? TechnicalOwnerType { get; set; }
    [MaxLength(200)] public string? TechnicalOwnerId { get; set; }
    [MaxLength(250)] public string? TechnicalOwnerDisplayName { get; set; }
    [MaxLength(10)] public string? Criticality { get; set; }
    public int? RtoHours { get; set; }
    public int? RpoHours { get; set; }
    public bool GdprSensitive { get; set; }
    public bool Nis2Scope { get; set; }
    [MaxLength(30)] public string? AppLifecycle { get; set; }

    // Actifs matériels (§ 3.4, CFG-44, CFG-47).
    [MaxLength(100)] public string? Vendor { get; set; }
    [MaxLength(100)] public string? Model { get; set; }
    [MaxLength(100)] public string? SerialNumber { get; set; }
    public DateTime? PurchaseDate { get; set; }
    public DateTime? WarrantyExpiry { get; set; }
    public DateTime? EndOfSupport { get; set; }

    /// <summary>CFG-16 : attributs propres au type (<see cref="CiAttributes"/>), en JSON.</summary>
    public Dictionary<string, string> Attributes { get; set; } = new();

    public List<CiRelationship> Outgoing { get; set; } = new();   // ce CI → autres
    public List<CiRelationship> Incoming { get; set; } = new();   // autres → ce CI
}

/// <summary>Relation orientée entre deux CI : « source » dépend de / héberge… « cible ».</summary>
public class CiRelationship
{
    public static readonly string[] Types = ["Dépend de", "Héberge", "Fait partie de", "Se connecte à"];

    /// <summary>
    /// Sens de propagation d'une panne. « Source dépend de / se connecte à
    /// cible » : la panne de la cible touche la source. « Source héberge /
    /// fait partie de cible » : la panne de la source touche la cible.
    /// </summary>
    public static bool FailureFlowsFromTarget(string type) => type is "Dépend de" or "Se connecte à";

    public int Id { get; set; }
    public int SourceId { get; set; }
    public ConfigurationItem? Source { get; set; }
    public int TargetId { get; set; }
    public ConfigurationItem? Target { get; set; }
    [MaxLength(30)] public string Type { get; set; } = "Dépend de";
}

public record ConfigurationItemDto(string Title, string? Description, string? Status,
    string? OwnerType, string? OwnerId, string? OwnerDisplayName,
    string? CiType, string? Environment, string? Location, int? PerimeterId = null,
    string? IpAddress = null, string? MacAddress = null, string? OperatingSystem = null,
    string? AppCode = null, string? BusinessUnit = null,
    string? BusinessOwnerType = null, string? BusinessOwnerId = null, string? BusinessOwnerDisplayName = null,
    string? TechnicalOwnerType = null, string? TechnicalOwnerId = null, string? TechnicalOwnerDisplayName = null,
    string? Criticality = null, int? RtoHours = null, int? RpoHours = null,
    bool GdprSensitive = false, bool Nis2Scope = false, string? AppLifecycle = null,
    string? Vendor = null, string? Model = null, string? SerialNumber = null,
    DateTime? PurchaseDate = null, DateTime? WarrantyExpiry = null, DateTime? EndOfSupport = null,
    Dictionary<string, string?>? Attributes = null) : IRecordDto;

/// <summary>CFG-30 : fichier CSV à importer et périmètre d'accueil.</summary>
public record CiImportDto(string? Csv, int? PerimeterId = null);

public record CiRelationshipDto(string TargetReference, string Type);
