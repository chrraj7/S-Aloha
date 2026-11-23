using System.ComponentModel.DataAnnotations;

namespace SAloha.Api.Core.Features;

/// <summary>
/// Une fonctionnalité optionnelle (OPT-01, ADR-0023) : sa clé stable, son conteneur et ses
/// dépendances. Le calcul de la fonctionnalité s'exécute dans son conteneur, jamais dans l'API.
/// </summary>
public record FeatureDefinition(string Key, string Name, string Container, string[] Requires, string[] Optional);

public static class FeatureCatalog
{
    public const string AssistedTriage = "assisted-ticket-triage", AssistedCmdb = "assisted-cmdb", McpServer = "mcp-server";

    /// <summary>Conteneur commun des fonctionnalités optionnelles, et conteneur propre du serveur MCP (ADR-0024).</summary>
    public const string FeaturesContainer = "features", McpContainer = "mcp";

    /// <summary>Dépendances : service d'embeddings (facultatif pour le tri), modèle de langage (obligatoire pour la CMDB).</summary>
    public const string Embeddings = "embeddings", LanguageModel = "language-model";

    public static readonly FeatureDefinition[] All =
    [
        new(AssistedTriage, "Tri assisté des tickets", FeaturesContainer, [], [Embeddings]),
        new(AssistedCmdb, "CMDB assistée", FeaturesContainer, [LanguageModel], []),
        new(McpServer, "Serveur MCP", McpContainer, [], []),
    ];

    public static FeatureDefinition? ByKey(string? key) => All.FirstOrDefault(f => f.Key == key);
}

/// <summary>État d'une fonctionnalité choisi par l'Admin ; une ligne par fonctionnalité activée une fois au moins.</summary>
public class OptionalFeature
{
    [Key, MaxLength(40)] public string Key { get; set; } = "";
    public bool Enabled { get; set; }
    public DateTime? UpdatedAt { get; set; }
    [MaxLength(200)] public string? UpdatedBy { get; set; }
    /// <summary>OPT-02, TRI-13 : réglages propres de la fonctionnalité, en JSON ; null : valeurs par défaut.</summary>
    public string? SettingsJson { get; set; }
}

public record FeatureSwitchDto(bool Enabled);

/// <summary>État d'un conteneur au dernier contrôle (OPT-01, OPT-02).</summary>
public record ContainerState(string Container, bool Configured, bool Reachable, bool TokenAccepted, string? Version,
    string[] Features, string Message, DateTime CheckedAt)
{
    /// <summary>État propre à chaque fonctionnalité, tel que le conteneur le rend (index du tri, TRI-06) ; vide sinon.</summary>
    public Dictionary<string, System.Text.Json.JsonElement> Status { get; init; } = [];

    public bool Declares(string key) => Configured && Reachable && TokenAccepted && Features.Contains(key);
}

public record DependencyState(string Key, string Name, bool Required, bool Ok, string Message);

/// <summary>Une fonctionnalité telle que la page d'administration la montre (OPT-02).</summary>
public record FeatureView(string Key, string Name, string Container, string State, bool Available, ContainerState ContainerState,
    List<DependencyState> Dependencies, DateTime? UpdatedAt, string? UpdatedBy)
{
    public const string Unavailable = "Indisponible", Off = "Coupée", On = "Activée";
}
