using SAloha.Features.Host;
using SAloha.Features.TicketTriage;

namespace SAloha.Features.Tests;

/// <summary>Corrélation du tri assisté : par règles (TRI-05), par le sens (TRI-06), pannes probables (TRI-07, TRI-11).</summary>
public class CorrelationTests
{
    private static readonly CorrelationSettings Defaults = new();
    private static readonly DateTime T0 = new(2026, 11, 23, 9, 12, 0, DateTimeKind.Utc);
    private static readonly Dictionary<int, string> Titles = new() { [1] = "Messagerie" };

    private static OpenEntry Entry(int id, int minutes, int perimeter = 1, int? service = null, int[]? cis = null, float[]? vector = null) =>
        new(id, perimeter, service, cis ?? [], T0.AddMinutes(minutes), "Nouveau", "Moyen", $"Incident {id}", vector);

    private static float[] V(params float[] v) => EmbeddingClient.Normalize(v);

    [Fact]
    public void TRI_05_meme_service_ou_meme_CI_dans_la_fenetre_et_le_meme_perimetre()
    {
        Assert.True(OpenIncidents.Linked(Entry(1, 0, service: 1), Entry(2, 59, service: 1), Defaults));
        Assert.True(OpenIncidents.Linked(Entry(1, 0, cis: [7, 8]), Entry(2, 30, cis: [8]), Defaults));
        Assert.False(OpenIncidents.Linked(Entry(1, 0, service: 1), Entry(2, 61, service: 1), Defaults));                 // hors fenêtre
        Assert.False(OpenIncidents.Linked(Entry(1, 0, service: 1), Entry(2, 10, perimeter: 2, service: 1), Defaults));   // autre périmètre
        Assert.False(OpenIncidents.Linked(Entry(1, 0, service: 1), Entry(2, 10, service: 2), Defaults));
        Assert.False(OpenIncidents.Linked(Entry(1, 0), Entry(2, 10), Defaults));                                         // ni service ni CI
        // La fenêtre se règle (Triage:CorrelationWindowMinutes, TRI-13).
        Assert.True(OpenIncidents.Linked(Entry(1, 0, service: 1), Entry(2, 90, service: 1), Defaults with { WindowMinutes = 120 }));
    }

    [Fact]
    public void TRI_06_par_le_sens_au_dessus_du_seuil_sans_service_commun()
    {
        var vpn1 = Entry(1, 0, vector: V(1, 0.2f, 0));
        var vpn2 = Entry(2, 15, vector: V(1, 0.1f, 0.1f));
        var other = Entry(3, 5, vector: V(0, 0, 1));
        Assert.True(OpenIncidents.Links(vpn1, vpn2, Defaults).Sens);
        Assert.False(OpenIncidents.Linked(vpn1, other, Defaults));
        // Sans vecteur (service d'embeddings absent), seule TRI-05 joue.
        Assert.False(OpenIncidents.Linked(vpn1 with { Vector = null }, vpn2, Defaults));
        Assert.False(OpenIncidents.Linked(vpn1, vpn2, Defaults with { MinSimilarity = 0.999 }));
    }

    [Fact]
    public void TRI_07_trois_incidents_lies_forment_une_panne_probable_d_un_seul_perimetre()
    {
        // Scénario de TRI-05 : trois incidents ouverts sur Messagerie en 20 minutes au Siège ; un quatrième à l'Usine.
        var siege = new[] { Entry(10, 0, service: 1), Entry(11, 10, service: 1), Entry(12, 20, service: 1), Entry(13, 300, service: 1) };
        var outage = Assert.Single(OpenIncidents.Outages(siege, Defaults, Titles));
        Assert.Equal([10, 11, 12], outage.Members);                     // du plus ancien au plus récent ; le quatrième, hors fenêtre
        Assert.Equal(("service", (int?)1, "3 incidents ouverts sur le service Messagerie"), (outage.Basis, outage.ServiceId, outage.Reasons));
        Assert.Empty(OpenIncidents.Outages(siege.Take(2).ToList(), Defaults, Titles));   // deux ne suffisent pas
        Assert.Single(OpenIncidents.Outages(siege.Take(2).ToList(), Defaults with { OutageThreshold = 2 }, Titles));

        // Liés de proche en proche par un CI commun.
        var byCi = new[] { Entry(20, 0, cis: [5]), Entry(21, 30, cis: [5, 6]), Entry(22, 50, cis: [5]) };
        Assert.Equal(("ci", "3 incidents ouverts reliés à un même CI"), (OpenIncidents.Outages(byCi, Defaults, Titles).Single().Basis,
            OpenIncidents.Outages(byCi, Defaults, Titles).Single().Reasons));

        // Par le sens seul ; un lien mêlé (service, puis sens) le dit autrement.
        var bySens = new[] { Entry(30, 0, vector: V(1, 0)), Entry(31, 5, vector: V(1, 0.1f)), Entry(32, 9, vector: V(1, 0.05f)) };
        Assert.Equal("3 incidents ouverts aux descriptions proches", OpenIncidents.Outages(bySens, Defaults, Titles).Single().Reasons);
        var mixed = new[] { Entry(40, 0, service: 1, vector: V(0, 1)), Entry(41, 5, service: 1, vector: V(1, 0)), Entry(42, 9, vector: V(1, 0.01f)) };
        Assert.Equal("3 incidents ouverts liés par leur service, un CI ou leur description", OpenIncidents.Outages(mixed, Defaults, Titles).Single().Reasons);
    }
}
