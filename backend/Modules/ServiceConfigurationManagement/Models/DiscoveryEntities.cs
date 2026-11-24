using System.ComponentModel.DataAnnotations;
using SAloha.Api.Core.Perimeters;

namespace SAloha.Api.Modules.ServiceConfigurationManagement;

/// <summary>
/// Actif découvert (CFG-40) : attend dans le sas de staging d'être rapproché d'un CI, ou
/// exclu. Un même équipement vu par plusieurs sources (balayage IP, annuaire, SNMP) n'y
/// figure qu'une fois : ses observations sont fusionnées.
/// </summary>
public class DiscoveredAsset : IHasPerimeter
{
    public const string ToReconcile = "À rapprocher";
    public const string Reconciled = "Rapproché";
    public const string Ignored = "Ignoré";
    public const string InConflict = "En conflit";
    public static readonly string[] Statuses = [ToReconcile, InConflict, Reconciled, Ignored];
    /// <summary>Statuts qui attendent une décision du gestionnaire.</summary>
    public static readonly string[] Open = [ToReconcile, InConflict];

    public int Id { get; set; }
    public int PerimeterId { get; set; } = Perimeter.DefaultId;

    [MaxLength(45)] public string? IpAddress { get; set; }
    [MaxLength(17)] public string? MacAddress { get; set; }
    [MaxLength(200)] public string? Hostname { get; set; }
    [MaxLength(100)] public string? SerialNumber { get; set; }
    [MaxLength(100)] public string? Vendor { get; set; }
    [MaxLength(100)] public string? Model { get; set; }
    [MaxLength(150)] public string? DetectedOs { get; set; }
    /// <summary>Type de CI déduit (valeurs de <see cref="ConfigurationItem.Types"/>).</summary>
    [MaxLength(30)] public string DetectedType { get; set; } = "Autre";
    [MaxLength(150)] public string? Location { get; set; }
    [MaxLength(250)] public string? OpenPorts { get; set; }
    /// <summary>CFG-49 : bannières lues sur les ports ouverts, une par ligne (« 22 SSH-2.0-OpenSSH_9.6 »).</summary>
    [MaxLength(500)] public string? Banners { get; set; }
    /// <summary>CFG-51 : interfaces lues par SNMP (<c>ifTable</c>), une par ligne.</summary>
    [MaxLength(8000)] public string? Interfaces { get; set; }
    /// <summary>CFG-52 : services en cours d'exécution lus par SSH (systemd), un par ligne.</summary>
    [MaxLength(8000)] public string? Services { get; set; }
    /// <summary>CFG-52 : ports TCP en écoute hors boucle locale, lus par SSH (« 22,80,5432 »).</summary>
    [MaxLength(250)] public string? ListeningPorts { get; set; }
    /// <summary>CFG-52 : logiciels installés, relevés par SSH ; remplacés à chaque relevé.</summary>
    public List<DiscoveredSoftware> Software { get; set; } = [];
    /// <summary>Sources qui l'ont vu, séparées par des virgules (IpSweep, Snmp, ActiveDirectory).</summary>
    [MaxLength(100)] public string Sources { get; set; } = "";
    /// <summary>Compte d'ordinateur présent dans l'annuaire (CFG-42).</summary>
    public bool InDirectory { get; set; }
    public string? RawData { get; set; }

    [MaxLength(30)] public string Status { get; set; } = ToReconcile;
    /// <summary>Score du meilleur rapprochement (0 à 100, CFG-41).</summary>
    public int ConfidenceScore { get; set; }
    /// <summary>Critère du rapprochement : SerialNumber, MacAddress, Hostname, IpAddress ou None.</summary>
    [MaxLength(20)] public string MatchRule { get; set; } = "None";
    /// <summary>Action suggérée : UpdateExisting, Conflict ou CreateNew (CFG-41).</summary>
    [MaxLength(20)] public string SuggestedAction { get; set; } = "CreateNew";
    [MaxLength(300)] public string? ConflictReason { get; set; }

    /// <summary>CI rapproché (statut Rapproché) ou proposé (à rapprocher, en conflit).</summary>
    public int? MatchedCiId { get; set; }
    public ConfigurationItem? MatchedCi { get; set; }
    public int? JobId { get; set; }

    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReconciledAt { get; set; }
    [MaxLength(200)] public string? ReconciledBy { get; set; }

    /// <summary>
    /// CFG-42 : actif inconnu (Shadow IT) — vu sur le réseau, sans compte d'ordinateur dans
    /// l'annuaire ni CI rapproché, et pas encore traité.
    /// </summary>
    public bool Unknown => !InDirectory && MatchedCiId is null && Open.Contains(Status);
}

/// <summary>Tâche de découverte : cible, fréquence et fenêtre de scan, identifiant du coffre (CFG-46).</summary>
public class DiscoveryJob : IHasPerimeter
{
    /// <summary>Types de scan disponibles ; WinRM, hyperviseurs et agent viendront en phases 3 et 4 du chantier F13.</summary>
    public static readonly string[] ScanTypes = ["IpSweep", "Snmp", "ActiveDirectory", "Ssh", "Proxmox", "VCenter", "WinRm"];
    /// <summary>Types dont la cible est l'URL d'une API (hyperviseurs, CFG-54), et non une plage IP.</summary>
    public static readonly string[] ApiScanTypes = ["Proxmox", "VCenter"];
    public const string NeverRun = "Jamais lancée";
    public const string Queued = "En file";
    public const string Running = "En cours";
    public const string Succeeded = "Réussie";
    public const string Failed = "En échec";
    public const string DefaultPorts = "22,80,443,135,445,3389,5985,9100";

    public int Id { get; set; }
    public int PerimeterId { get; set; } = Perimeter.DefaultId;

    [MaxLength(150)] public string Name { get; set; } = "";
    /// <summary>Plage CIDR ou adresse IP (balayage, SNMP) ; BaseDN facultatif (annuaire).</summary>
    [MaxLength(250)] public string TargetRange { get; set; } = "";
    [MaxLength(30)] public string ScanType { get; set; } = "IpSweep";
    [MaxLength(250)] public string PortList { get; set; } = DefaultPorts;
    /// <summary>Lancement automatique toutes les N heures ; 0 : lancement manuel seulement.</summary>
    public int FrequencyHours { get; set; } = 24;
    /// <summary>CMA-08 : à la fin de la tâche, la CMDB assistée (activée) aide au rapprochement des actifs qu'elle laisse au sas.</summary>
    public bool AssistedCmdb { get; set; }
    /// <summary>Fenêtre de scan, en heure locale de l'organisation ; sans fenêtre, à toute heure.</summary>
    public TimeOnly? WindowStart { get; set; } = new(20, 0);
    public TimeOnly? WindowEnd { get; set; } = new(6, 0);

    public int? CredentialId { get; set; }
    public DiscoveryCredential? Credential { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime? LastRunAt { get; set; }
    [MaxLength(30)] public string LastStatus { get; set; } = NeverRun;
    [MaxLength(500)] public string? LastError { get; set; }
    public int? LastFoundCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(100)] public string CreatedBy { get; set; } = "";
}

/// <summary>
/// Identifiant du coffre (CFG-45) : communauté SNMP, compte de domaine… Le secret est chiffré
/// au repos et n'est jamais renvoyé par l'API.
/// </summary>
public class DiscoveryCredential : IHasPerimeter
{
    /// <summary>Types utilisables par les collecteurs actuels ; clés SSH et API cloud viendront avec leurs collecteurs.</summary>
    public static readonly string[] Types = ["SnmpV2c", "SnmpV3", "WindowsDomain", "Ssh", "ProxmoxToken", "VCenter"];

    public int Id { get; set; }
    public int PerimeterId { get; set; } = Perimeter.DefaultId;

    [MaxLength(150)] public string Name { get; set; } = "";
    [MaxLength(30)] public string CredentialType { get; set; } = "SnmpV2c";
    [MaxLength(150)] public string? Username { get; set; }
    [MaxLength(150)] public string? Domain { get; set; }
    /// <summary>SNMP v3 (CFG-51) : protocole d'authentification (<see cref="Discovery.SnmpAccess.AuthProtocols"/>).</summary>
    [MaxLength(10)] public string? AuthProtocol { get; set; }
    /// <summary>SNMP v3 : protocole de chiffrement (<see cref="Discovery.SnmpAccess.PrivProtocols"/>) ; None : authentification seule.</summary>
    [MaxLength(10)] public string? PrivProtocol { get; set; }
    public string EncryptedSecret { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(100)] public string CreatedBy { get; set; } = "";
    public DateTime? SecretChangedAt { get; set; }
}

/// <summary>Logiciel installé sur un actif découvert (CFG-52) : paquet du système et sa version.</summary>
public class DiscoveredSoftware
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    [MaxLength(200)] public string Name { get; set; } = "";
    [MaxLength(100)] public string? Version { get; set; }
}

/// <summary>
/// CFG-53 : clé d'hôte SSH, ou certificat TLS d'une API d'hyperviseur que l'autorité de
/// certification ne garantit pas (CFG-54), retenu au premier contact. Une clé différente
/// ensuite est refusée avant tout envoi d'identifiant, et attend dans
/// <see cref="PendingFingerprint"/> que l'Admin l'accepte.
/// </summary>
public class SshHostKey : IHasPerimeter
{
    public const string Ssh = "ssh", Tls = "tls";

    public int Id { get; set; }
    public int PerimeterId { get; set; } = Perimeter.DefaultId;
    /// <summary>ssh (clé d'hôte) ou tls (certificat d'une API d'hyperviseur).</summary>
    [MaxLength(10)] public string Protocol { get; set; } = Ssh;
    [MaxLength(45)] public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    [MaxLength(50)] public string Algorithm { get; set; } = "";
    /// <summary>Empreinte SHA-256 (base64, comme <c>ssh-keygen -l</c>).</summary>
    [MaxLength(100)] public string Fingerprint { get; set; } = "";
    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    [MaxLength(50)] public string? PendingAlgorithm { get; set; }
    [MaxLength(100)] public string? PendingFingerprint { get; set; }
    public DateTime? PendingSeenAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    [MaxLength(100)] public string? AcceptedBy { get; set; }
}

/// <summary>
/// CFG-55 : relation déduite par la découverte entre deux actifs du sas (« hyperviseur Héberge
/// VM »). Jamais créée seule dans la CMDB : le gestionnaire l'accepte, une fois les deux actifs
/// rapprochés de leurs CI, ou l'écarte (elle n'est alors plus reproposée).
/// </summary>
public class RelationProposal : IHasPerimeter
{
    public const string Proposed = "Proposée", Accepted = "Acceptée", Dismissed = "Écartée";
    public static readonly string[] Statuses = [Proposed, Accepted, Dismissed];

    public int Id { get; set; }
    public int PerimeterId { get; set; } = Perimeter.DefaultId;
    public int SourceAssetId { get; set; }
    public DiscoveredAsset? SourceAsset { get; set; }
    public int TargetAssetId { get; set; }
    public DiscoveredAsset? TargetAsset { get; set; }
    /// <summary>Type de relation de la CMDB (<see cref="CiRelationship.Types"/>).</summary>
    [MaxLength(30)] public string Type { get; set; } = "Héberge";
    [MaxLength(300)] public string Reason { get; set; } = "";
    [MaxLength(20)] public string Status { get; set; } = Proposed;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime? DecidedAt { get; set; }
    [MaxLength(200)] public string? DecidedBy { get; set; }
    public int? RelationId { get; set; }
}

/// <summary>
/// Fin de support d'un produit (CFG-47) : un CI dont le système (ou constructeur et modèle)
/// contient <see cref="Product"/> est en fin de support à partir de <see cref="EndOfSupport"/>.
/// Référentiel partagé, tenu par les gestionnaires.
/// </summary>
public class EndOfLifeRule
{
    public int Id { get; set; }
    [MaxLength(150)] public string Product { get; set; } = "";
    public DateTime EndOfSupport { get; set; }
    [MaxLength(300)] public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(100)] public string CreatedBy { get; set; } = "";
}

public record ReconcileAssetDto(string Action, int? TargetCiId = null, ConfigurationItemDto? Ci = null);

public record DiscoveryJobDto(
    string Name, string ScanType, string? TargetRange, string? PortList, int? CredentialId,
    int FrequencyHours = 24, string? WindowStart = "20:00", string? WindowEnd = "06:00",
    bool IsActive = true, int? PerimeterId = null, bool AssistedCmdb = false);

/// <summary>
/// Identifiant du coffre ; en SNMP v3, <see cref="Secret"/> est la phrase d'authentification et
/// <see cref="PrivacySecret"/> celle du chiffrement ; en SSH, <see cref="Secret"/> est le mot de
/// passe, ou <see cref="PrivateKey"/> la clé privée (OpenSSH ou PEM) et <see cref="KeyPassphrase"/> sa phrase.
/// </summary>
public record DiscoveryCredentialDto(
    string Name, string CredentialType, string? Username, string? Domain, string? Secret, int? PerimeterId = null,
    string? AuthProtocol = null, string? PrivProtocol = null, string? PrivacySecret = null,
    string? PrivateKey = null, string? KeyPassphrase = null);

public record EndOfLifeRuleDto(string Product, DateTime? EndOfSupport, string? Notes);
