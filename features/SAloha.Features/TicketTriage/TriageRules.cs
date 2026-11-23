using SAloha.Features.Host;

namespace SAloha.Features.TicketTriage;

/// <summary>Une suggestion du tri : champ proposable (OPT-14), valeur, motif en clair et enregistrements cités (TRI-08).</summary>
public record Suggestion(string Field, string Value, string Reasons, IReadOnlyList<CitedRef> Cited, double? Confidence);

/// <summary>
/// Réglages du vote (TRI-03) et suggestions activées, réglés par l'Admin (TRI-13) : une suggestion coupée n'est plus
/// proposée, ni à la saisie ni en tâche de fond ; la panne probable coupée, aucune n'est plus signalée.
/// </summary>
public record TriageSettings(int Neighbours = 10, int MinVoters = 3, double MinShare = 0.5, bool Category = true, bool Service = true,
    bool Group = true, bool Impact = true, bool Outage = true);

/// <summary>
/// Règles du tri assisté des tickets, sans effet de bord : vote des voisins (TRI-03), impact par la
/// criticité (TRI-04), motifs (TRI-08). Aucune suggestion ne s'applique seule : elles deviennent des
/// propositions, que l'agent décide (OPT-15).
/// </summary>
public static class TriageRules
{
    /// <summary>TRI-01 : un incident Nouveau ou En cours.</summary>
    public static readonly string[] InScope = ["Nouveau", "En cours"];

    public const string InService = "En service";
    private static readonly string[] Impacts = ["Faible", "Moyen", "Élevé"];

    /// <summary>TRI-04 : criticité d'un service (Faible, Moyenne, Élevée) et d'un CI (CFG-43) en impact.</summary>
    private static readonly Dictionary<string, string> ServiceImpact = new() { ["Élevée"] = "Élevé", ["Moyenne"] = "Moyen", ["Faible"] = "Faible" };
    private static readonly Dictionary<string, string> CiImpact = new()
        { ["Vitale"] = "Élevé", ["Majeure"] = "Élevé", ["Standard"] = "Moyen", ["Mineure"] = "Faible" };

    public static IReadOnlyList<Suggestion> Suggest(IncidentData incident, SimilarResult similar, IReadOnlyList<ServiceData> services,
        IReadOnlyList<CategoryData> categories, IReadOnlyList<CiData> cis, TriageSettings settings, OutageGroup? outage = null)
    {
        var list = new List<Suggestion>();
        var neighbours = similar.Neighbours.Take(settings.Neighbours).Select(n => n.Incident).ToList();
        var words = similar.Semantic ? "" : " (par les mots seuls)";
        var byId = services.ToDictionary(s => s.Id);

        // TRI-03 : catégorie active (ADM-51 : toute catégorie tant que la pratique n'en a aucune d'active).
        var active = categories.Where(c => c.Active).Select(c => c.Name).ToList();
        string? CategoryOf(IncidentData n)
        {
            var name = n.Category?.Trim();
            if (string.IsNullOrEmpty(name)) return null;
            return active.Count == 0 ? name : active.FirstOrDefault(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        }
        if (settings.Category && Vote(neighbours, CategoryOf, incident.Category, settings) is { } category)
            list.Add(new("category", category.Value,
                $"{category.Count} des {neighbours.Count} incidents résolus les plus proches portent la catégorie {category.Value}{words}",
                category.Voters, category.Share));

        // TRI-03 : service en service au catalogue.
        string? ServiceOf(IncidentData n) =>
            n.ServiceId is { } id && byId.TryGetValue(id, out var s) && s.Status == InService ? id.ToString() : null;
        if (settings.Service && Vote(neighbours, ServiceOf, incident.ServiceId?.ToString(), settings) is { } service)
            list.Add(new("serviceId", service.Value,
                $"{service.Count} des {neighbours.Count} incidents résolus les plus proches portent le service {byId[int.Parse(service.Value)].Title}{words}",
                service.Voters, service.Share));

        // TRI-03 : groupe responsable ; un voisin confié à une personne ne vote pas pour le groupe.
        string? GroupOf(IncidentData n) => n.OwnerType == "Group" && !string.IsNullOrWhiteSpace(n.OwnerId) ? n.OwnerId.Trim() : null;
        var currentGroup = incident.OwnerType == "Group" ? incident.OwnerId : null;
        if (settings.Group && Vote(neighbours, GroupOf, currentGroup, settings) is { } group)
        {
            var name = neighbours.Where(n => string.Equals(GroupOf(n), group.Value, StringComparison.OrdinalIgnoreCase))
                .Select(n => n.OwnerDisplayName).FirstOrDefault(d => !string.IsNullOrWhiteSpace(d)) ?? group.Value;
            list.Add(new("owner", group.Value,
                $"{group.Count} des {neighbours.Count} incidents résolus les plus proches ont été traités par {name}{words}",
                group.Voters, group.Share));
        }

        if (settings.Impact && Impact(incident, byId, cis, settings.Outage ? outage : null) is { } impact) list.Add(impact);
        return list;
    }

    private sealed record Tally(string Value, int Count, double Share, IReadOnlyList<CitedRef> Voters);

    /// <summary>
    /// TRI-03 : une valeur n'est suggérée que si elle réunit au moins <c>MinVoters</c> votants et la part
    /// <c>MinShare</c> des votants, et si elle diffère de la valeur saisie. Deux valeurs à égalité en tête
    /// ne départagent rien : aucune suggestion.
    /// </summary>
    private static Tally? Vote(List<IncidentData> neighbours, Func<IncidentData, string?> valueOf, string? current, TriageSettings settings)
    {
        var votes = neighbours.Select(n => (Neighbour: n, Value: valueOf(n))).Where(v => v.Value is not null).ToList();
        if (votes.Count == 0) return null;
        var ranked = votes.GroupBy(v => v.Value!, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Value: g.First().Value!, Members: g.ToList())).OrderByDescending(g => g.Members.Count).ToList();
        var best = ranked[0];
        if (ranked.Count > 1 && ranked[1].Members.Count == best.Members.Count) return null;
        var count = best.Members.Count;
        if (count < settings.MinVoters || count < settings.MinShare * votes.Count) return null;
        if (string.Equals(best.Value, current?.Trim(), StringComparison.OrdinalIgnoreCase)) return null;
        return new Tally(best.Value, count, Math.Round((double)count / votes.Count, 2),
            best.Members.Select(m => new CitedRef("incident", m.Neighbour.Id)).Take(20).ToList());
    }

    /// <summary>TRI-04 : impact Élevé d'un incident d'une panne probable, motivé par elle ; null s'il l'est déjà.</summary>
    public static Suggestion? OutageImpact(int incidentId, string? current, OutageGroup outage) =>
        current == "Élevé" ? null : new("impact", "Élevé", $"Panne probable : {outage.Reasons}",
            outage.Members.Where(m => m != incidentId).Take(20).Select(m => new CitedRef("incident", m)).ToList(), null);

    /// <summary>
    /// TRI-04 : la plus haute des valeurs données par la criticité du service et par celle des CI reliés,
    /// si elle diffère de l'impact saisi. Le motif nomme le service, partagé par toute l'organisation ;
    /// un CI est cité sans être nommé, pour qu'un lecteur qui ne le voit pas ne le lise pas (TRI-11).
    /// </summary>
    public static Suggestion? Impact(IncidentData incident, IReadOnlyDictionary<int, ServiceData> services, IReadOnlyList<CiData> cis,
        OutageGroup? outage = null)
    {
        // TRI-04 : une panne probable (TRI-07) suggère Élevé ; c'est la plus haute valeur, elle seule motive.
        if (outage is not null) return OutageImpact(incident.Id, incident.Impact, outage);
        var sources = new List<(string Impact, string Reason, CitedRef Cited)>();
        if (incident.ServiceId is { } id && services.TryGetValue(id, out var service)
            && service.Criticality is { } sc && ServiceImpact.TryGetValue(sc, out var fromService))
            sources.Add((fromService, $"service {service.Title}, criticité {sc}", new CitedRef("service", service.Id)));
        foreach (var ci in cis)
            if (ci.Criticality is { } cc && CiImpact.TryGetValue(cc, out var fromCi))
                sources.Add((fromCi, $"CI relié de criticité {cc}", new CitedRef("ci", ci.Id)));
        if (sources.Count == 0) return null;
        var highest = sources.MaxBy(s => Array.IndexOf(Impacts, s.Impact)).Impact;
        if (highest == incident.Impact) return null;
        var top = sources.Where(s => s.Impact == highest).ToList();
        return new("impact", highest, string.Join(" ; ", top.Select(s => s.Reason).Distinct()), top.Select(s => s.Cited).Distinct().ToList(), null);
    }
}
