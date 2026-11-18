using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Search;

namespace SAloha.Api.Tests;

/// <summary>Recherche hybride — règles RAG-xx de docs/reference/processus/recherche.md.</summary>
[Collection("api")]
public class SearchTests(ApiFixture api)
{
    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode} : {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    /// <summary>L'indexation se fait en tâche de fond : on attend que la recherche voie la cible.</summary>
    private async Task<JsonElement> WaitFor(string query, string reference, string? types = null)
    {
        var client = api.Client("User", "tiana.ravelo");
        for (var i = 0; i < 50; i++)
        {
            var hits = await Json(await client.GetAsync($"/api/search?q={Uri.EscapeDataString(query)}" + (types is null ? "" : $"&types={types}")));
            var hit = hits.EnumerateArray().FirstOrDefault(h => h.GetProperty("reference").GetString() == reference);
            if (hit.ValueKind != JsonValueKind.Undefined) return hit;
            await Task.Delay(200);
        }
        throw new Xunit.Sdk.XunitException($"« {query} » ne trouve pas {reference}");
    }

    private async Task<bool> Finds(string query, string reference)
    {
        var hits = await Json(await api.Client().GetAsync($"/api/search?q={Uri.EscapeDataString(query)}"));
        return hits.EnumerateArray().Any(h => h.GetProperty("reference").GetString() == reference);
    }

    private async Task<string> PublishedArticle(string title, string content)
    {
        var manager = api.Client("Manager", "lova.rabe");
        // KB-16 : un autre compte rédige, le gestionnaire publie.
        var a = await Json(await api.Client("User", "redacteur.search").PostAsJsonAsync("/api/knowledge", new { title, description = "", articleType = "Solution", content, keywords = "" }));
        await Json(await manager.PutAsJsonAsync($"/api/knowledge/{a.GetProperty("id").GetInt32()}",
            new { title, description = "", status = "Publié", articleType = "Solution", content, keywords = "" }));
        return a.GetProperty("reference").GetString()!;
    }

    [Fact]
    public void Decoupage_en_passages_avec_recouvrement()
    {
        Assert.Empty(Chunker.Split("  "));
        Assert.Single(Chunker.Split("Un.\n\nDeux.\n\nTrois."));
        var longText = new string('a', 1500);
        var chunks = Chunker.Split(longText, 800, 100);
        Assert.Equal(2, chunks.Count);
        Assert.Equal(800, chunks[0].Length);
        // Le second passage reprend les 100 derniers caractères du premier.
        Assert.Equal(1500 - 700, chunks[1].Length);
    }

    [Fact]
    public async Task Seul_le_contenu_valide_est_indexe()
    {
        var reference = await PublishedArticle("Redémarrer le connecteur SAP-RFC9 bloqué",
            "Arrêter le service SAP-RFC9, vider la file, puis redémarrer.");
        var hit = await WaitFor("SAP-RFC9", reference);
        Assert.True(hit.GetProperty("lexical").GetBoolean());

        // Un brouillon n'est pas indexé (RAG-01).
        var manager = api.Client("Manager", "lova.rabe");
        var draft = await Json(await manager.PostAsJsonAsync("/api/knowledge", new { title = "Brouillon ZK47Q", description = "", articleType = "FAQ", content = "ZK47Q", keywords = "" }));
        await WaitFor("SAP-RFC9", reference);    // la file a traité les enregistrements suivants
        Assert.False(await Finds("ZK47Q", draft.GetProperty("reference").GetString()!));

        // Un incident n'entre dans l'index qu'une fois résolu, avec sa résolution.
        var user = api.Client("User", "tiana.ravelo");
        object Incident(string status, string? resolution) => new
        {
            title = "Imprimante HP-M607 du 3e étage hors ligne", description = "Bourrage récurrent", status,
            impact = "Faible", urgency = "Moyenne", category = "Poste de travail", affectedService = "Impression", isMajor = false, resolution,
            resolutionCode = resolution is null ? null : "Correctif appliqué"
        };
        var inc = await Json(await user.PostAsJsonAsync("/api/incidents", Incident("Nouveau", null)));
        var incRef = inc.GetProperty("reference").GetString()!;
        await Task.Delay(1000);
        Assert.False(await Finds("HP-M607", incRef));
        await Json(await user.PutAsJsonAsync($"/api/incidents/{inc.GetProperty("id").GetInt32()}", Incident("Résolu", "Remplacement du rouleau d'entraînement.")));
        var resolved = await WaitFor("HP-M607", incRef);
        Assert.Contains("rouleau", (await Json(await user.GetAsync("/api/search?q=rouleau"))).ToString());
        Assert.Equal("Résolu", resolved.GetProperty("status").GetString());
    }

    /// <summary>Attend que la recherche NE voie PLUS la cible (retrait traité en tâche de fond).</summary>
    private async Task WaitGone(string query, string reference)
    {
        for (var i = 0; i < 50; i++)
        {
            if (!await Finds(query, reference)) return;
            await Task.Delay(200);
        }
        throw new Xunit.Sdk.XunitException($"« {query} » trouve encore {reference}");
    }

    [Fact]
    public async Task Un_enregistrement_qui_quitte_un_statut_indexe_sort_de_l_index()
    {
        var manager = api.Client("Manager", "lova.rabe");
        // Article publié puis archivé (RAG-01).
        var reference = await PublishedArticle("Procédure XJ-ARCHIVE", "Contenu XJ-ARCHIVE");
        await WaitFor("XJ-ARCHIVE", reference);
        var article = await Json(await manager.GetAsync("/api/knowledge?q=XJ-ARCHIVE"));
        var articleId = article[0].GetProperty("id").GetInt32();
        await Json(await manager.PutAsJsonAsync($"/api/knowledge/{articleId}",
            new { title = "Procédure XJ-ARCHIVE", description = "", status = "Archivé", articleType = "Solution", content = "Contenu XJ-ARCHIVE", keywords = "" }));
        await WaitGone("XJ-ARCHIVE", reference);

        // Problème établi puis rouvert.
        var p = await Json(await manager.PostAsJsonAsync("/api/problems", new
        {
            title = "Problème QW-ROUVERT", description = "d", impact = "Moyen", urgency = "Moyenne", category = "Test", affectedService = "Test", source = "Proactif"
        }));
        var pid = p.GetProperty("id").GetInt32();
        var pref = p.GetProperty("reference").GetString()!;
        object Problem(string status) => new
        {
            title = "Problème QW-ROUVERT", description = "d", status, impact = "Moyen", urgency = "Moyenne", category = "Test",
            affectedService = "Test", knownErrorWorkaround = "Relancer le service", rootCause = "Fuite mémoire du service", reopenReason = "Réouverture motivée (PRB-10)"
        };
        await Json(await manager.PutAsJsonAsync($"/api/problems/{pid}", Problem("En analyse")));
        await Json(await manager.PutAsJsonAsync($"/api/problems/{pid}", Problem("Erreur connue")));
        await WaitFor("QW-ROUVERT", pref);
        // Réouverture permise depuis Résolu (PRB-10) : Erreur connue → Résolu → En analyse.
        // Une erreur connue se résout par une action corrective terminée (PRB-12).
        var raci = new[]
        {
            new { role = "R", assigneeType = "User", assigneeId = "lova.rabe", assigneeDisplayName = "Lova" },
            new { role = "A", assigneeType = "User", assigneeId = "lova.rabe", assigneeDisplayName = "Lova" }
        };
        var action = await Json(await manager.PostAsJsonAsync($"/api/problems/{pid}/actions",
            new { title = "Corriger la fuite", description = "", dueDate = (DateTime?)null, raci }));
        await Json(await manager.PutAsJsonAsync($"/api/actions/{action.GetProperty("id").GetInt32()}",
            new { title = "Corriger la fuite", description = "", status = "Terminée", dueDate = (DateTime?)null, raci }));
        await Json(await manager.PutAsJsonAsync($"/api/problems/{pid}", Problem("Résolu")));
        await Json(await manager.PutAsJsonAsync($"/api/problems/{pid}", Problem("En analyse")));
        await WaitGone("QW-ROUVERT", pref);

        // Incident résolu puis supprimé (Admin).
        var user = api.Client("User", "tiana.ravelo");
        var inc = await Json(await user.PostAsJsonAsync("/api/incidents", new
        {
            title = "Incident ZD-SUPPRIME", description = "d", status = "Nouveau", impact = "Faible", urgency = "Faible",
            category = "Test", affectedService = "Test", isMajor = false, resolution = (string?)null
        }));
        var iid = inc.GetProperty("id").GetInt32();
        var iref = inc.GetProperty("reference").GetString()!;
        await Json(await user.PutAsJsonAsync($"/api/incidents/{iid}", new
        {
            title = "Incident ZD-SUPPRIME", description = "d", status = "Résolu", impact = "Faible", urgency = "Faible",
            category = "Test", affectedService = "Test", isMajor = false, resolution = "Corrigé", resolutionCode = "Correctif appliqué"
        }));
        await WaitFor("ZD-SUPPRIME", iref);
        Assert.Equal(HttpStatusCode.NoContent, (await api.Client().DeleteAsync($"/api/incidents/{iid}")).StatusCode);
        await WaitGone("ZD-SUPPRIME", iref);
    }

    [Fact]
    public async Task La_recherche_vectorielle_trouve_les_reformulations()
    {
        var reference = await PublishedArticle("Lenteurs de la messagerie le lundi matin",
            "La purge hebdomadaire fragmente l'index de la base Exchange. Replanifier la purge la nuit.");
        await WaitFor("messagerie", reference);
        // « courriel lent » ne partage aucune racine avec le texte : seul le vecteur le rapproche (RAG-05).
        var hit = await WaitFor("courriel lent", reference);
        Assert.True(hit.GetProperty("semantic").GetBoolean());
        Assert.False(hit.GetProperty("lexical").GetBoolean());
    }

    [Fact]
    public async Task Un_vecteur_ecrit_en_base_par_un_autre_processus_est_vu_sans_rechargement()
    {
        // ADR-0022 : la similarité est calculée dans PostgreSQL (pgvector). Un vecteur écrit
        // par un autre processus (autre instance de l'API, restauration, futur serveur MCP)
        // compte dès son écriture, sans recharger de copie en mémoire.
        var reference = await PublishedArticle("Badge refusé au portique ZB-NORD", "Réenrôler le badge au poste de garde ZB-NORD.");
        await WaitFor("ZB-NORD", reference);
        var vecteur = (await new FakeEmbeddingClient().EmbedAsync(["imprimante"], TimeSpan.FromSeconds(1)))![0];
        await using (var db = api.Database())
            await db.SearchPassages.Where(p => p.Reference == reference)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Embedding, vecteur));
        // « impression » ne partage aucun mot avec l'article : seul le vecteur écrit en base le rapproche.
        var hit = await WaitFor("impression", reference);
        Assert.True(hit.GetProperty("semantic").GetBoolean());
        Assert.False(hit.GetProperty("lexical").GetBoolean());
    }

    [Fact]
    public async Task Sans_service_d_embeddings_la_recherche_reste_lexicale()
    {
        var reference = await PublishedArticle("Certificat expiré sur le portail QX-PORTAIL", "Renouveler le certificat du portail QX-PORTAIL.");
        await WaitFor("QX-PORTAIL", reference);
        FakeEmbeddingClient.Available = false;
        try
        {
            // RAG-02 : pas d'erreur, le lexical répond seul.
            var hit = await WaitFor("certificat QX-PORTAIL", reference);
            Assert.False(hit.GetProperty("semantic").GetBoolean());
        }
        finally { FakeEmbeddingClient.Available = true; }
    }

    [Fact]
    public async Task Cas_similaires_a_un_incident()
    {
        var reference = await PublishedArticle("VPN : déconnexions des agents nomades",
            "Mettre à jour le client VPN ; augmenter le délai d'inactivité de la passerelle.");
        await WaitFor("VPN", reference);
        // Ne partage avec l'incident que des mots secondaires de sa description.
        var noise = await PublishedArticle("Toner de l'imprimante du hall", "Mettre en place le toner ; plusieurs minutes de chauffe.");
        await WaitFor("toner", noise);
        var user = api.Client("User", "tiana.ravelo");
        var inc = await Json(await user.PostAsJsonAsync("/api/incidents", new
        {
            title = "Coupures du VPN en télétravail", description = "Plusieurs agents perdent la connexion distante ; il faut plusieurs minutes pour se remettre en place", status = "Nouveau",
            impact = "Moyen", urgency = "Moyenne", category = "Réseau", affectedService = "VPN", isMajor = false, resolution = (string?)null
        }));
        var id = inc.GetProperty("id").GetInt32();
        var similar = await Json(await user.GetAsync($"/api/search/similar?type=incident&id={id}"));
        Assert.Contains(similar.EnumerateArray(), h => h.GetProperty("reference").GetString() == reference);
        Assert.DoesNotContain(similar.EnumerateArray(), h => h.GetProperty("type").GetString() == "incident" && h.GetProperty("id").GetInt32() == id);
        // Lexical sur le titre seul : « minutes », « mettre » ne suffisent pas.
        Assert.DoesNotContain(similar.EnumerateArray(), h => h.GetProperty("reference").GetString() == noise);
    }

    [Fact]
    public async Task Etat_et_reconstruction_reserves_a_l_admin()
    {
        var user = api.Client("User", "tiana.ravelo");
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/search/status")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsync("/api/search/reindex", null)).StatusCode);
        var status = await Json(await api.Client().GetAsync("/api/search/status"));
        Assert.True(status.GetProperty("embeddings").GetProperty("reachable").GetBoolean());
        Assert.Equal(HttpStatusCode.Accepted, (await api.Client().PostAsync("/api/search/reindex", null)).StatusCode);
    }

    [Fact]
    public async Task Plancher_de_similarite_calibre_a_0_47()
    {
        // RAG-07, RAG-10 : vecteur du passage = 3 (imprimer) + 3 (messagerie) + 3 (lenteur) + 3 (vpn)
        // + 1 (bureau, haché) ; requête « impression » = le seul groupe « imprimer ». Similarité :
        // 3 / √37 ≈ 0,493, sous l'ancien plancher (0,5), au-dessus du plancher calibré (0,47).
        // Aucun mot commun (« impress » contre « imprim ») : seul le sens peut le trouver.
        var reference = await PublishedArticle("Imprimer", "messagerie lenteur vpn bureau");
        await WaitFor("bureau", reference);
        var hit = await WaitFor("impression", reference);
        Assert.True(hit.GetProperty("semantic").GetBoolean());
        Assert.False(hit.GetProperty("lexical").GetBoolean());
    }

    /// <summary>
    /// Le simulacre d'embeddings place un mot à la même dimension d'une exécution à l'autre.
    /// Haché par <c>string.GetHashCode()</c>, aléatoire par processus, un mot d'un autre test
    /// tombait parfois sur la dimension du mot cherché et ajoutait un résultat (RAG-13 instable).
    /// </summary>
    [Fact]
    public async Task Le_simulacre_d_embeddings_est_le_meme_d_une_execution_a_l_autre()
    {
        var vecteur = (await new FakeEmbeddingClient().EmbedAsync(["kq77"], TimeSpan.FromSeconds(1)))![0];
        // FNV-1a 32 bits de « kq77 », modulo les 1020 dimensions hors synonymes, après les 4 des synonymes.
        Assert.Equal(447, Array.FindIndex(vecteur, x => x != 0));
        Assert.Equal(1, vecteur.Count(x => x != 0));
    }
}
