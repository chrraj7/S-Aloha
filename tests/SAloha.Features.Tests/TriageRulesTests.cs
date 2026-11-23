using SAloha.Features.Host;
using SAloha.Features.TicketTriage;

namespace SAloha.Features.Tests;

/// <summary>Règles du tri assisté des tickets : vote des voisins (TRI-03), impact (TRI-04), motifs (TRI-08).</summary>
public class TriageRulesTests
{
    private static readonly TriageSettings Defaults = new();
    private static readonly List<ServiceData> Services =
    [
        new(1, "SVC-1", "Messagerie", "En service", "Élevée"),
        new(2, "SVC-2", "Réseau", "En service", "Moyenne"),
        new(3, "SVC-3", "Ancien intranet", "Retiré", "Faible"),
    ];
    private static readonly List<CategoryData> Categories = [new("Messagerie", true), new("Réseau", true), new("Téléphonie", false)];

    private static int next = 100;

    private static IncidentData Incident(string? category = null, int? service = null, string? group = null, string? user = null,
        string impact = "Moyen", string status = "Résolu") =>
        new(next++, 1, $"INC-{next}", "Voisin", "", status, category, service, impact,
            group is not null ? "Group" : user is not null ? "User" : null, group ?? user, group is null ? user : $"Groupe {group}");

    private static SimilarResult Similar(bool semantic, params IncidentData[] neighbours) =>
        new(semantic, neighbours.Select(n => new Neighbour(0.5, true, semantic, n)).ToList());

    private static IReadOnlyList<Suggestion> Suggest(IncidentData draft, SimilarResult similar, List<CiData>? cis = null, List<CategoryData>? categories = null) =>
        TriageRules.Suggest(draft, similar, Services, categories ?? Categories, cis ?? [], Defaults);

    private static IncidentData Draft(string? category = null, int? service = null, string impact = "Moyen", string? ownerType = null, string? owner = null) =>
        new(1, 1, "INC-1", "Outlook ne reçoit plus les courriels", "", "Nouveau", category, service, impact, ownerType, owner, owner);

    [Fact]
    public void TRI_03_sept_voisins_sur_dix_suggerent_la_categorie_avec_son_motif()
    {
        var neighbours = Enumerable.Range(0, 7).Select(_ => Incident("Messagerie")).Concat(Enumerable.Range(0, 3).Select(_ => Incident("Réseau"))).ToArray();
        var category = Suggest(Draft(), Similar(true, neighbours)).Single(s => s.Field == "category");
        Assert.Equal("Messagerie", category.Value);
        // TRI-08 : le motif dit pourquoi et cite les voisins qui ont voté.
        Assert.Equal("7 des 10 incidents résolus les plus proches portent la catégorie Messagerie", category.Reasons);
        Assert.Equal(neighbours.Take(7).Select(n => new CitedRef("incident", n.Id)), category.Cited);
        Assert.Equal(0.7, category.Confidence);

        // Deux votants seulement : rien. Égalité en tête : rien. Déjà la valeur saisie : rien.
        Assert.DoesNotContain(Suggest(Draft(), Similar(true, Incident("Messagerie"), Incident("Messagerie"), Incident())), s => s.Field == "category");
        Assert.DoesNotContain(Suggest(Draft(), Similar(true, [.. Enumerable.Range(0, 3).Select(_ => Incident("Messagerie")),
            .. Enumerable.Range(0, 3).Select(_ => Incident("Réseau"))])), s => s.Field == "category");
        Assert.DoesNotContain(Suggest(Draft("messagerie"), Similar(true, neighbours)), s => s.Field == "category");
        // Moins de la moitié des votants : rien (3 sur 7, saisie libre : toutes les catégories votent).
        Assert.DoesNotContain(Suggest(Draft(), Similar(true, [.. Enumerable.Range(0, 3).Select(_ => Incident("Messagerie")),
            Incident("Réseau"), Incident("Réseau"), Incident("Téléphonie"), Incident("Autre")]), categories: []), s => s.Field == "category");
    }

    [Fact]
    public void TRI_03_seule_une_categorie_active_vote_sauf_quand_la_pratique_n_en_a_aucune()
    {
        var inactive = Enumerable.Range(0, 4).Select(_ => Incident("téléphonie")).ToArray();
        Assert.DoesNotContain(Suggest(Draft(), Similar(true, inactive)), s => s.Field == "category");
        // ADM-51 : sans catégorie active, la saisie est libre, et le vote aussi ; la casse ne sépare pas les voix.
        var free = Suggest(Draft(), Similar(true, [Incident("Imprimantes"), Incident("imprimantes"), Incident("Imprimantes")]), categories: []);
        Assert.Equal("Imprimantes", free.Single(s => s.Field == "category").Value);
        // Une catégorie active est ramenée à son nom exact.
        var exact = Suggest(Draft(), Similar(true, [Incident("messagerie"), Incident("MESSAGERIE"), Incident("Messagerie")]));
        Assert.Equal("Messagerie", exact.Single(s => s.Field == "category").Value);
    }

    [Fact]
    public void TRI_03_service_en_service_et_groupe_responsable_par_vote()
    {
        var neighbours = new[]
        {
            Incident(service: 1, group: "GRP-MESSAGERIE"), Incident(service: 1, group: "GRP-MESSAGERIE"), Incident(service: 1, group: "GRP-MESSAGERIE"),
            Incident(service: 3, user: "hery.rakoto"), Incident(service: 3, user: "hery.rakoto"), Incident(service: 3, user: "lova.rabe"),
        };
        var suggestions = Suggest(Draft(), Similar(false, neighbours));
        // Le service retiré ne vote pas : 3 voix sur 3 votants pour Messagerie.
        var service = suggestions.Single(s => s.Field == "serviceId");
        Assert.Equal("1", service.Value);
        // Sans service d'embeddings, le motif le dit (TRI-03, TRI-08).
        Assert.Equal("3 des 6 incidents résolus les plus proches portent le service Messagerie (par les mots seuls)", service.Reasons);
        // Un voisin confié à une personne ne vote pas pour le groupe.
        var group = suggestions.Single(s => s.Field == "owner");
        Assert.Equal("GRP-MESSAGERIE", group.Value);
        Assert.Equal("3 des 6 incidents résolus les plus proches ont été traités par Groupe GRP-MESSAGERIE (par les mots seuls)", group.Reasons);
        // Déjà confié à ce groupe, ou déjà sur ce service : rien.
        var already = Suggest(Draft(service: 1, ownerType: "Group", owner: "grp-messagerie"), Similar(false, neighbours));
        Assert.DoesNotContain(already, s => s.Field is "serviceId" or "owner");
    }

    [Fact]
    public void TRI_03_seuls_les_dix_plus_proches_votent()
    {
        var far = Enumerable.Range(0, 10).Select(_ => Incident("Réseau")).ToList();
        var near = Enumerable.Range(0, 4).Select(_ => Incident("Messagerie")).ToList();
        var only = Suggest(Draft(), Similar(true, [.. near, .. far])).Single(s => s.Field == "category");
        Assert.Equal("Réseau", only.Value);
        Assert.StartsWith("6 des 10 ", only.Reasons);
    }

    [Fact]
    public void TRI_04_l_impact_suit_la_plus_haute_criticite_du_service_ou_d_un_CI_relie()
    {
        // Service Messagerie, criticité Élevée → Élevé.
        var byService = Assert.Single(Suggest(Draft(service: 1), Similar(true)));
        Assert.Equal(("impact", "Élevé", "service Messagerie, criticité Élevée"), (byService.Field, byService.Value, byService.Reasons));
        Assert.Equal([new CitedRef("service", 1)], byService.Cited);

        // Service Réseau (Moyenne) mais un CI relié Vital : le plus haut l'emporte ; le CI est cité, pas nommé (TRI-11).
        var byCi = Assert.Single(Suggest(Draft(service: 2, impact: "Faible"), Similar(true), [new(7, "CI-7", "Exchange", "Vitale"), new(8, "CI-8", "Poste", "Mineure")]));
        Assert.Equal(("Élevé", "CI relié de criticité Vitale"), (byCi.Value, byCi.Reasons));
        Assert.Equal([new CitedRef("ci", 7)], byCi.Cited);
        Assert.DoesNotContain("Exchange", byCi.Reasons);

        // La valeur suggérée peut être plus basse que la saisie ; égale, rien ; sans criticité connue, rien.
        Assert.Equal("Moyen", Assert.Single(Suggest(Draft(service: 2, impact: "Élevé"), Similar(true))).Value);
        Assert.Empty(Suggest(Draft(service: 1, impact: "Élevé"), Similar(true)));
        Assert.Empty(Suggest(Draft(), Similar(true), [new(9, "CI-9", "Serveur", null)]));
    }
}
