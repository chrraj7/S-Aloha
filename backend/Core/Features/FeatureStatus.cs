using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Data;
using SAloha.Api.Core.Search;

namespace SAloha.Api.Core.Features;

/// <summary>
/// État des fonctionnalités optionnelles (OPT-01 à OPT-03, OPT-05) : conteneur contrôlé, dépendances,
/// choix de l'Admin. Partagé par la page d'administration et la page Santé (OPT-07).
/// </summary>
public class FeatureStatus(AppDbContext db, ContainerProbe probe, IEmbeddingClient embeddings)
{
    public async Task<Dictionary<string, ContainerState>> ContainersAsync(CancellationToken ct = default)
    {
        var states = new Dictionary<string, ContainerState>();
        foreach (var container in FeatureCatalog.All.Select(f => f.Container).Distinct())
            states[container] = await probe.ProbeAsync(container, ct);
        return states;
    }

    public async Task<DependencyState> DependencyAsync(string key, bool required, CancellationToken ct = default)
    {
        switch (key)
        {
            case FeatureCatalog.Embeddings:
                if (!embeddings.Enabled)
                    return new(key, "Service d'embeddings", required, false, "Non configuré : voisins par les mots seuls, pas de corrélation par le sens.");
                var ok = await embeddings.EmbedAsync(["test"], TimeSpan.FromSeconds(5), ct) is not null;
                return new(key, "Service d'embeddings", required, ok, ok ? $"Modèle {embeddings.Model} joignable."
                    : $"Modèle {embeddings.Model} injoignable : voisins par les mots seuls, pas de corrélation par le sens.");
            case FeatureCatalog.LanguageModel:
                // OPT-20 : réglé et activé par l'Admin ; l'essai (OPT-26) dit s'il répond.
                var lm = await db.LanguageModelSettings.AsNoTracking().FirstOrDefaultAsync(ct);
                return lm is { Ready: true }
                    ? new(key, "Modèle de langage", required, true, $"Modèle {lm.Model} réglé ({lm.Location.ToLowerInvariant()}).")
                    : new(key, "Modèle de langage", required, false, lm is null || lm.Url == "" ? "Non réglé." : "Réglé mais désactivé.");
            default:
                return new(key, key, required, false, "Dépendance inconnue.");
        }
    }

    public async Task<List<DependencyState>> DependenciesAsync(FeatureDefinition feature, CancellationToken ct = default)
    {
        var list = new List<DependencyState>();
        foreach (var key in feature.Requires) list.Add(await DependencyAsync(key, true, ct));
        foreach (var key in feature.Optional) list.Add(await DependencyAsync(key, false, ct));
        return list;
    }

    public async Task<List<FeatureView>> ViewsAsync(CancellationToken ct = default)
    {
        var containers = await ContainersAsync(ct);
        var rows = await db.OptionalFeatures.AsNoTracking().ToDictionaryAsync(f => f.Key, ct);
        var views = new List<FeatureView>();
        foreach (var feature in FeatureCatalog.All)
        {
            var container = containers[feature.Container];
            var row = rows.GetValueOrDefault(feature.Key);
            var available = container.Declares(feature.Key);
            // OPT-05 : activée reste activée, même si le conteneur ne répond plus.
            var state = row?.Enabled == true ? FeatureView.On : available ? FeatureView.Off : FeatureView.Unavailable;
            views.Add(new FeatureView(feature.Key, feature.Name, feature.Container, state, available, container,
                await DependenciesAsync(feature, ct), row?.UpdatedAt, row?.UpdatedBy));
        }
        return views;
    }

    /// <summary>OPT-03 : motif du refus d'activer, ou null si l'activation est permise.</summary>
    public static string? ActivationRefusal(FeatureDefinition feature, ContainerState container, List<DependencyState> dependencies)
    {
        var name = $"Conteneur « {feature.Container} »";
        if (!container.Configured)
            return $"{name} non configuré ({(feature.Container == FeatureCatalog.McpContainer ? "Mcp:Url" : "Features:Url")}) : fonctionnalité indisponible.";
        if (!container.Reachable) return $"{name} injoignable : {container.Message}";
        if (!container.TokenAccepted) return $"{name} : {container.Message}";
        if (!container.Features.Contains(feature.Key))
            return $"{name}{(container.Version is null ? "" : $" (version {container.Version})")} : il ne propose pas « {feature.Name} ».";
        var missing = dependencies.Where(d => d.Required && !d.Ok).ToList();
        return missing.Count == 0 ? null : $"Dépendance manquante : {string.Join(" ; ", missing.Select(d => $"{d.Name} — {d.Message}"))}";
    }
}
