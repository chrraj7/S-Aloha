using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Audit;
using SAloha.Api.Core.Features;

namespace SAloha.Api.Tests;

/// <summary>Propositions des fonctionnalités optionnelles : dépôt, décision, conservation (OPT-14, OPT-15, OPT-17).</summary>
[Collection("api")]
public class ProposalsTests(ApiFixture api, FakeFeaturesContainer container) : IClassFixture<FakeFeaturesContainer>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        // Le démarrage de l'API applique les migrations : lancé seul, ce test arrive avant elles.
        await api.Client().GetAsync("/health/live");
        await ResetAsync();
    }

    public Task DisposeAsync() => ResetAsync();

    private async Task ResetAsync()
    {
        container.Up = true;
        await using var db = api.Database();
        await db.OptionalFeatures.ExecuteDeleteAsync();
    }

    private WebApplicationFactory<Program> Factory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Default", api.Database().Database.GetConnectionString())
            .UseSetting("Jwt:Key", ApiFixture.JwtKey)
            .UseSetting("Jwt:Issuer", "S-Aloha")
            .UseSetting("Discovery:IntervalMinutes", "0")
            .UseSetting("Discovery:VaultKey", "cle-du-coffre-de-test-uniquement-0123456789")
            .UseSetting("Features:Url", container.Url)
            .UseSetting("Features:Token", FakeFeaturesContainer.Token)
            .UseSetting("Features:InternalPort", "8081"));

    private static HttpClient Admin(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFixture.Token("hery.rakoto", "Admin"));
        return client;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode} : {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement;
    }

    /// <summary>Dépôt par le conteneur, sur le port interne.</summary>
    private static async Task<(int Status, JsonElement? Body)> Propose(WebApplicationFactory<Program> factory, object input)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(input, JsonSerializerOptions.Web);
        var context = await factory.Server.SendAsync(c =>
        {
            c.Request.Method = "POST";
            c.Request.Path = "/internal/proposals";
            c.Connection.LocalPort = 8081;
            c.Request.ContentType = "application/json";
            c.Request.ContentLength = json.Length;
            c.Request.Body = new MemoryStream(json);
            c.Request.Headers.Authorization = $"Bearer {FakeFeaturesContainer.Token}";
        });
        var text = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return (context.Response.StatusCode, text.StartsWith('{') ? JsonDocument.Parse(text).RootElement : null);
    }

    private static object Triage(int incident, string field, string after, params CitedRef[] cited) =>
        new { feature = FeatureCatalog.AssistedTriage, targetType = "incident", targetId = incident, field, after,
              reasons = "7 des 10 incidents résolus les plus proches", cited, confidence = 0.7 };

    private static object Incident(string title, string impact = "Moyen", string category = "") => new
    {
        title, impact, urgency = "Moyenne", category, ownerType = "User", ownerId = "hery.rakoto", ownerDisplayName = "Hery Rakoto"
    };

    private static async Task<int> CreateIncident(HttpClient admin, string title) =>
        (await Json(await admin.PostAsJsonAsync("/api/incidents", Incident(title)))).GetProperty("id").GetInt32();

    private static async Task<WebApplicationFactory<Program>> WithTriage(Func<WebApplicationFactory<Program>> make)
    {
        var factory = make();
        var res = await Admin(factory).PutAsJsonAsync($"/api/admin/features/{FeatureCatalog.AssistedTriage}", new { enabled = true });
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return factory;
    }

    private static HttpRequestMessage Apply(int incident, int proposal, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/incidents/{incident}") { Content = JsonContent.Create(body) };
        req.Headers.Add(ProposalDecisions.Header, proposal.ToString());
        return req;
    }

    [Fact]
    public async Task OPT_14_le_conteneur_depose_une_proposition_verifiee_sans_doublon()
    {
        using var factory = await WithTriage(Factory);
        var admin = Admin(factory);
        var id = await CreateIncident(admin, "Messagerie lente (OPT-14)");

        var (status, body) = await Propose(factory, Triage(id, "impact", "Élevé"));
        Assert.Equal(201, status);
        var proposal = body!.Value.GetProperty("id").GetInt32();
        var (again, same) = await Propose(factory, Triage(id, "impact", "Élevé"));
        Assert.Equal(200, again);
        Assert.Equal(proposal, same!.Value.GetProperty("id").GetInt32());

        Assert.Equal(400, (await Propose(factory, Triage(id, "impact", "Énorme"))).Status);       // hors liste fermée
        Assert.Equal(400, (await Propose(factory, Triage(id, "impact", "Moyen"))).Status);        // déjà la valeur
        Assert.Equal(400, (await Propose(factory, Triage(id, "title", "Autre titre"))).Status);   // champ non proposable
        Assert.Equal(404, (await Propose(factory, Triage(999_999, "impact", "Élevé"))).Status);
        // Une fonctionnalité coupée (CMDB assistée) ne dépose rien.
        Assert.Equal(403, (await Propose(factory, new { feature = FeatureCatalog.AssistedCmdb, targetType = "ci", targetId = 1,
            field = "ciType", after = "Serveur" })).Status);

        await using var db = api.Database();
        var row = await db.Proposals.AsNoTracking().SingleAsync(p => p.Id == proposal);
        Assert.Equal(("Moyen", Proposal.Open), (row.Before, row.Status));
        Assert.Equal(1, await db.Proposals.CountAsync(p => p.TargetType == "incident" && p.TargetId == id));
    }

    [Fact]
    public async Task OPT_15_accepter_par_la_route_de_la_pratique_ou_corriger_la_valeur()
    {
        using var factory = await WithTriage(Factory);
        var admin = Admin(factory);
        var id = await CreateIncident(admin, "Outlook ne reçoit plus (OPT-15)");
        var neighbour = await CreateIncident(admin, "Voisin cité");
        var (_, created) = await Propose(factory, Triage(id, "impact", "Élevé", new CitedRef("incident", neighbour), new CitedRef("incident", 999_999)));
        var proposal = created!.Value.GetProperty("id").GetInt32();

        var listed = (await Json(await admin.GetAsync($"/api/incidents/{id}/proposals"))).EnumerateArray().Single();
        Assert.False(listed.GetProperty("stale").GetBoolean());
        // Un enregistrement cité qui n'existe pas n'est pas montré.
        Assert.Equal(neighbour, listed.GetProperty("cited").EnumerateArray().Single().GetProperty("id").GetInt32());

        var applied = await admin.SendAsync(Apply(id, proposal, Incident("Outlook ne reçoit plus (OPT-15)", "Élevé")));
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        Assert.Equal("P2", (await Json(applied)).GetProperty("priority").GetString());   // la matrice s'applique (SOC-16)

        await using (var db = api.Database())
        {
            var row = await db.Proposals.AsNoTracking().SingleAsync(p => p.Id == proposal);
            Assert.Equal((Proposal.Accepted, "Élevé"), (row.Status, row.FinalValue));
            var line = await db.AuditEntries.AsNoTracking().Where(a => a.EntityType == "incident" && a.EntityId == id && a.Field == "impact").SingleAsync();
            Assert.Equal(AuditEntry.AssistedTriage, line.Origin);
        }

        // Corrigée : l'utilisateur retient une autre valeur que celle proposée.
        var (_, second) = await Propose(factory, Triage(id, "category", "Messagerie"));
        var other = second!.Value.GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(Apply(id, other, Incident("Outlook ne reçoit plus (OPT-15)", "Élevé", "Réseau")))).StatusCode);
        await using (var db = api.Database())
        {
            var row = await db.Proposals.AsNoTracking().SingleAsync(p => p.Id == other);
            Assert.Equal((Proposal.Corrected, "Réseau"), (row.Status, row.FinalValue));
            var month = DateTime.UtcNow.ToString("yyyy-MM");
            Assert.True((await db.ProposalCounts.AsNoTracking().SingleAsync(c => c.Month == month && c.Feature == FeatureCatalog.AssistedTriage && c.Field == "category")).Corrected >= 1);
        }
        // Déjà décidée : plus rien à appliquer.
        Assert.Equal(HttpStatusCode.NotFound, (await admin.SendAsync(Apply(id, other, Incident("Outlook ne reçoit plus (OPT-15)", "Élevé", "Réseau")))).StatusCode);
    }

    [Fact]
    public async Task OPT_15_une_proposition_perimee_ne_s_accepte_plus()
    {
        using var factory = await WithTriage(Factory);
        var admin = Admin(factory);
        var id = await CreateIncident(admin, "Imprimante (périmée)");
        var (_, created) = await Propose(factory, Triage(id, "impact", "Élevé"));
        var proposal = created!.Value.GetProperty("id").GetInt32();
        await Json(await admin.PutAsJsonAsync($"/api/incidents/{id}", Incident("Imprimante du 2e étage (périmée)")));

        Assert.True((await Json(await admin.GetAsync($"/api/incidents/{id}/proposals"))).EnumerateArray().Single().GetProperty("stale").GetBoolean());
        var res = await admin.SendAsync(Apply(id, proposal, Incident("Imprimante du 2e étage (périmée)", "Élevé")));
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Contains("périmée", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OPT_15_une_proposition_perimee_recalculee_a_l_identique_redevient_acceptable()
    {
        using var factory = await WithTriage(Factory);
        var admin = Admin(factory);
        var id = await CreateIncident(admin, "Messagerie (recalcul)");
        var (_, created) = await Propose(factory, Triage(id, "impact", "Élevé"));
        var proposal = created!.Value.GetProperty("id").GetInt32();
        await Json(await admin.PutAsJsonAsync($"/api/incidents/{id}", Incident("Messagerie du siège (recalcul)")));
        Assert.True((await Json(await admin.GetAsync($"/api/incidents/{id}/proposals"))).EnumerateArray().Single().GetProperty("stale").GetBoolean());

        // Le conteneur recalcule après la modification : même proposition, rafraîchie, et acceptable de nouveau.
        var (status, again) = await Propose(factory, Triage(id, "impact", "Élevé"));
        Assert.Equal(200, status);
        Assert.Equal(proposal, again!.Value.GetProperty("id").GetInt32());
        Assert.False((await Json(await admin.GetAsync($"/api/incidents/{id}/proposals"))).EnumerateArray().Single().GetProperty("stale").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(Apply(id, proposal, Incident("Messagerie du siège (recalcul)", "Élevé")))).StatusCode);
        await using var db = api.Database();
        Assert.Equal(Proposal.Accepted, (await db.Proposals.AsNoTracking().SingleAsync(p => p.Id == proposal)).Status);
        Assert.Equal(1, await db.Proposals.CountAsync(p => p.TargetType == "incident" && p.TargetId == id));
    }

    [Fact]
    public async Task OPT_14_une_valeur_rejetee_ou_corrigee_n_est_pas_reproposee()
    {
        using var factory = await WithTriage(Factory);
        var admin = Admin(factory);
        var id = await CreateIncident(admin, "VPN (écartée)");
        var (_, rejected) = await Propose(factory, Triage(id, "impact", "Faible"));
        var rejectedId = rejected!.Value.GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/incidents/{id}/proposals/{rejectedId}/reject", null)).StatusCode);
        await Json(await admin.PutAsJsonAsync($"/api/incidents/{id}", Incident("VPN du site de Lyon (écartée)")));

        // Recalcul après la modification : la valeur rejetée ne revient pas.
        var (status, again) = await Propose(factory, Triage(id, "impact", "Faible"));
        Assert.Equal(200, status);
        Assert.Equal((rejectedId, false), (again!.Value.GetProperty("id").GetInt32(), again.Value.GetProperty("created").GetBoolean()));
        Assert.Empty((await Json(await admin.GetAsync($"/api/incidents/{id}/proposals"))).EnumerateArray());

        // Corrigée : l'agent a retenu une autre valeur ; la valeur proposée ne revient pas non plus.
        var (_, corrected) = await Propose(factory, Triage(id, "impact", "Élevé"));
        var correctedId = corrected!.Value.GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(Apply(id, correctedId, Incident("VPN du site de Lyon (écartée)", "Faible")))).StatusCode);
        Assert.Equal(correctedId, (await Propose(factory, Triage(id, "impact", "Élevé"))).Body!.Value.GetProperty("id").GetInt32());
        Assert.Empty((await Json(await admin.GetAsync($"/api/incidents/{id}/proposals"))).EnumerateArray());

        // Une autre valeur du même champ reste proposable.
        Assert.Equal(201, (await Propose(factory, Triage(id, "impact", "Moyen"))).Status);
    }

    [Fact]
    public async Task OPT_14_une_seule_proposition_ouverte_par_champ_la_nouvelle_valeur_remplace_l_ancienne()
    {
        using var factory = await WithTriage(Factory);
        var admin = Admin(factory);
        var id = await CreateIncident(admin, "Impression (une par champ)");
        // D'abord la criticité du service (Faible), puis une panne probable (Élevé) : une seule ligne pour l'impact.
        var (created, first) = await Propose(factory, Triage(id, "impact", "Faible"));
        Assert.Equal(201, created);
        var (replaced, second) = await Propose(factory, new { feature = FeatureCatalog.AssistedTriage, targetType = "incident", targetId = id,
            field = "impact", after = "Élevé", reasons = "Panne probable : 3 incidents ouverts sur le service Impression" });
        Assert.Equal(200, replaced);
        Assert.Equal(first!.Value.GetProperty("id").GetInt32(), second!.Value.GetProperty("id").GetInt32());
        var only = Assert.Single((await Json(await admin.GetAsync($"/api/incidents/{id}/proposals"))).EnumerateArray());
        Assert.Equal(("Élevé", "Panne probable : 3 incidents ouverts sur le service Impression"),
            (only.GetProperty("after").GetString(), only.GetProperty("reasons").GetString()));
        // La panne grossit : même valeur, motif à jour.
        await Propose(factory, new { feature = FeatureCatalog.AssistedTriage, targetType = "incident", targetId = id,
            field = "impact", after = "Élevé", reasons = "Panne probable : 5 incidents ouverts sur le service Impression" });
        Assert.Equal("Panne probable : 5 incidents ouverts sur le service Impression",
            Assert.Single((await Json(await admin.GetAsync($"/api/incidents/{id}/proposals"))).EnumerateArray()).GetProperty("reasons").GetString());
        // Un autre champ a sa propre ligne.
        Assert.Equal(201, (await Propose(factory, Triage(id, "owner", "GRP-IMPRESSION"))).Status);
        await using var db = api.Database();
        Assert.Equal(2, await db.Proposals.CountAsync(p => p.TargetType == "incident" && p.TargetId == id && p.Status == Proposal.Open));
    }

    [Fact]
    public async Task OPT_15_rejeter_et_OPT_04_coupee_la_proposition_disparait_sans_etre_effacee()
    {
        using var factory = await WithTriage(Factory);
        var admin = Admin(factory);
        var id = await CreateIncident(admin, "Wifi (rejet)");
        var (_, first) = await Propose(factory, Triage(id, "impact", "Faible"));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/incidents/{id}/proposals/{first!.Value.GetProperty("id").GetInt32()}/reject", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/api/incidents/{id}/proposals/{first.Value.GetProperty("id").GetInt32()}/reject", null)).StatusCode);
        Assert.Empty((await Json(await admin.GetAsync($"/api/incidents/{id}/proposals"))).EnumerateArray());
        // Une autre valeur du même champ reste proposable après le rejet.
        var (status, second) = await Propose(factory, Triage(id, "impact", "Élevé"));
        Assert.Equal(201, status);
        Assert.Single((await Json(await admin.GetAsync($"/api/incidents/{id}/proposals"))).EnumerateArray());

        // Coupée : la proposition restante n'apparaît plus, ne se rejette plus, et reparaît à la réactivation.
        await Json(await admin.PutAsJsonAsync($"/api/admin/features/{FeatureCatalog.AssistedTriage}", new { enabled = false }));
        Assert.Empty((await Json(await admin.GetAsync($"/api/incidents/{id}/proposals"))).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/api/incidents/{id}/proposals/{second!.Value.GetProperty("id").GetInt32()}/reject", null)).StatusCode);
        await Json(await admin.PutAsJsonAsync($"/api/admin/features/{FeatureCatalog.AssistedTriage}", new { enabled = true }));
        Assert.Equal(second.Value.GetProperty("id").GetInt32(),
            (await Json(await admin.GetAsync($"/api/incidents/{id}/proposals"))).EnumerateArray().Single().GetProperty("id").GetInt32());

        await using var db = api.Database();
        Assert.Equal(Proposal.Rejected, (await db.Proposals.AsNoTracking().SingleAsync(p => p.Id == first.Value.GetProperty("id").GetInt32())).Status);
    }

    [Fact]
    public async Task OPT_17_expiration_a_30_jours_purge_a_13_mois_et_suppression_avec_la_cible()
    {
        using var factory = await WithTriage(Factory);
        var admin = Admin(factory);
        var id = await CreateIncident(admin, "Conservation (OPT-17)");
        await using var db = api.Database();
        var old = new Proposal { Feature = FeatureCatalog.AssistedTriage, TargetType = "incident", TargetId = id, Field = "impact",
            After = "Élevé", CreatedAt = DateTime.UtcNow.AddDays(-31) };
        var decided = new Proposal { Feature = FeatureCatalog.AssistedTriage, TargetType = "incident", TargetId = id, Field = "category",
            After = "Réseau", Status = Proposal.Rejected, CreatedAt = DateTime.UtcNow.AddMonths(-15), DecidedAt = DateTime.UtcNow.AddMonths(-14) };
        db.Proposals.AddRange(old, decided);
        await db.SaveChangesAsync();

        var (expired, purged) = await ProposalsRetention.RunAsync(db, DateTime.UtcNow);
        Assert.True(expired >= 1 && purged >= 1);
        Assert.Equal(Proposal.Expired, (await db.Proposals.AsNoTracking().SingleAsync(p => p.Id == old.Id)).Status);
        Assert.False(await db.Proposals.AnyAsync(p => p.Id == decided.Id));

        // La cible supprimée emporte ses propositions.
        await Propose(factory, Triage(id, "impact", "Faible"));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/incidents/{id}")).StatusCode);
        Assert.False(await db.Proposals.AnyAsync(p => p.TargetType == "incident" && p.TargetId == id));
    }
}
