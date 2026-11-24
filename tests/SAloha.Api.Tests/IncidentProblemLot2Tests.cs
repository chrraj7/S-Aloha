using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace SAloha.Api.Tests;

/// <summary>Incidents et problèmes, lot 2 : INC-04, INC-05, INC-10, INC-15, INC-17, PRB-07, PRB-20, PRB-22.</summary>
[Collection("api")]
public class IncidentProblemLot2Tests(ApiFixture api)
{
    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode} : {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static int Id(JsonElement e) => e.GetProperty("id").GetInt32();
    private static string? Str(JsonElement e, string p) => e.GetProperty(p).GetString();

    private static object Incident(string title, string? status = null, bool major = false, string ownerType = "User", string owner = "tech.inc",
        string? channel = null, string? caller = null, string? escalation = null) => new
    {
        title, description = "d", impact = "Moyen", urgency = "Moyenne", category = "", affectedService = "", status, isMajor = major,
        ownerType, ownerId = owner, ownerDisplayName = owner, channel, callerId = caller, callerDisplayName = caller,
        escalationReason = escalation, resolution = status is "Résolu" or "Clos" ? "Rétabli" : null, resolutionCode = status is "Résolu" or "Clos" ? "Correctif appliqué" : null
    };

    private static object Problem(string title, string? status = null, string? source = null, string? rootCause = null) => new
    {
        title, description = "d", status, impact = "Moyen", urgency = "Moyenne", category = "", affectedService = "", source, rootCause
    };

    [Fact]
    public async Task INC_04_INC_05_canal_et_utilisateur_affecte_notifie_de_la_resolution()
    {
        var agent = api.Client("User", "agent.inc05");
        Assert.Equal(HttpStatusCode.BadRequest, (await agent.PostAsJsonAsync("/api/incidents", Incident("Canal inconnu", channel: "Pigeon"))).StatusCode);
        var i = await Json(await agent.PostAsJsonAsync("/api/incidents", Incident("Messagerie bloquée", channel: "Téléphone", caller: "utilisateur.inc05")));
        Assert.Equal(("Téléphone", "utilisateur.inc05", "agent.inc05"), (Str(i, "channel"), Str(i, "callerId"), Str(i, "createdBy")));
        await Json(await agent.PutAsJsonAsync($"/api/incidents/{Id(i)}", Incident("Messagerie bloquée", "Résolu", channel: "Téléphone", caller: "utilisateur.inc05")));
        await using var db = api.Database();
        Assert.True(await db.Notifications.AnyAsync(n => n.TargetType == "incident" && n.TargetId == Id(i)
                                                         && n.RecipientId == "utilisateur.inc05" && n.Event == "Incident résolu"));
    }

    [Fact]
    public async Task INC_10_seul_un_gestionnaire_clot_un_incident_resolu()
    {
        var user = api.Client("User", "user.inc10");
        var i = await Json(await user.PostAsJsonAsync("/api/incidents", Incident("Boîte pleine INC-10", owner: "user.inc10")));
        var url = $"/api/incidents/{Id(i)}";
        await Json(await user.PutAsJsonAsync(url, Incident("Boîte pleine INC-10", "Résolu", owner: "user.inc10")));

        // Le User résout, mais ne clôt pas : la clôture est une décision du gestionnaire.
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PutAsJsonAsync(url, Incident("Boîte pleine INC-10", "Clos", owner: "user.inc10"))).StatusCode);
        var closed = await Json(await api.Client("Manager", "chef.inc10").PutAsJsonAsync(url, Incident("Boîte pleine INC-10", "Clos", owner: "user.inc10")));
        Assert.Equal("Clos", Str(closed, "status"));
    }

    [Fact]
    public async Task INC_10_un_incident_resolu_se_clot_seul_apres_la_fenetre_de_reouverture()
    {
        var user = api.Client("User", "user.inc10a");
        var admin = api.Client("Admin", "admin.inc10a");
        async Task<int> Resolved(string title, int daysAgo, bool security = false)
        {
            var i = await Json(await (security ? admin : user).PostAsJsonAsync("/api/incidents", new
            {
                title, impact = "Élevé", urgency = "Élevée", isSecurity = security,
                ownerType = "User", ownerId = "user.inc10a", ownerDisplayName = "user.inc10a", callerId = "appelant.inc10a", callerDisplayName = "appelant"
            }));
            if (security)
                await Json(await admin.PostAsJsonAsync($"/api/incidents/{Id(i)}/qualification",
                    new { nis2Important = true, personalDataBreach = false, personalDataBreachReason = "Aucune donnée" }));
            await Json(await (security ? admin : user).PutAsJsonAsync($"/api/incidents/{Id(i)}", new
            {
                title, description = "d", impact = "Élevé", urgency = "Élevée", status = "Résolu", isSecurity = security,
                ownerType = "User", ownerId = "user.inc10a", ownerDisplayName = "user.inc10a", callerId = "appelant.inc10a", callerDisplayName = "appelant",
                resolution = "Rétabli", resolutionCode = "Correctif appliqué"
            }));
            await using var db = api.Database();
            var e = await db.Incidents.SingleAsync(x => x.Id == Id(i));
            e.ResolvedAt = DateTime.UtcNow.AddDays(-daysAgo);
            await db.SaveChangesAsync();
            return Id(i);
        }
        var old = await Resolved("Imprimante INC-10 ancienne", 20);
        // #335 : 8 jours calendaires, au moins 6 jours ouvrés — la réouverture d'INC-11 reste possible.
        var recent = await Resolved("Imprimante INC-10 récente", 8);
        // INC-43 : un incident cyber ne se clôt pas, même d'office, avec une déclaration en cours.
        var cyber = await Resolved("Intrusion INC-10", 20, security: true);

        await Json(await admin.PostAsync("/api/notifications/run", null));
        await using var check = api.Database();
        var states = await check.Incidents.Where(x => x.Id == old || x.Id == recent || x.Id == cyber).ToDictionaryAsync(x => x.Id);
        Assert.Equal("Clos", states[old].Status);
        Assert.NotNull(states[old].ClosedAt);
        Assert.Equal("Résolu", states[recent].Status);
        Assert.Equal("Résolu", states[cyber].Status);
        Assert.True(await check.Notifications.AnyAsync(n => n.TargetType == "incident" && n.TargetId == old
                                                            && n.RecipientId == "appelant.inc10a" && n.Event == "Incident clos automatiquement"));
        // Clos, il ne se rouvre plus (INC-11) : la clôture d'office n'a lieu qu'au-delà de la fenêtre.
        Assert.False((await user.PutAsJsonAsync($"/api/incidents/{old}", new
        {
            title = "Imprimante INC-10 ancienne", description = "d", impact = "Élevé", urgency = "Élevée", status = "En cours", reopenReason = "Revenu",
            ownerType = "User", ownerId = "user.inc10a", ownerDisplayName = "user.inc10a", resolution = "Rétabli", resolutionCode = "Correctif appliqué"
        })).IsSuccessStatusCode);
    }

    [Fact]
    public async Task INC_15_PRB_22_un_incident_majeur_se_resout_relie_a_un_probleme()
    {
        var manager = api.Client("Manager", "chef.inc15");
        var i = await Json(await manager.PostAsJsonAsync("/api/incidents", Incident("Panne générale du SI", major: true, owner: "chef.inc15")));
        var url = $"/api/incidents/{Id(i)}";
        var refused = await manager.PutAsJsonAsync(url, Incident("Panne générale du SI", "Résolu", major: true, owner: "chef.inc15"));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("problème", await refused.Content.ReadAsStringAsync());
        var p = await Json(await manager.PostAsJsonAsync("/api/problems", Problem("Cause de la panne générale", source: "Proactif")));
        await Json(await manager.PostAsJsonAsync("/api/links", new { fromType = "incident", fromId = Id(i), toReference = Str(p, "reference") }));
        await Json(await manager.PutAsJsonAsync(url, Incident("Panne générale du SI", "Résolu", major: true, owner: "chef.inc15")));

        // PRB-22 : un incident majeur résolu sans problème (d'avant la règle) appelle un problème à ouvrir.
        var legacy = await Json(await manager.PostAsJsonAsync("/api/incidents", Incident("Ancien majeur", major: true, owner: "chef.inc15")));
        await using (var db = api.Database())
        {
            var e = await db.Incidents.SingleAsync(x => x.Id == Id(legacy));
            (e.Status, e.ResolvedAt) = ("Résolu", DateTime.UtcNow);
            await db.SaveChangesAsync();
        }
        var decisions = (await Json(await manager.GetAsync("/api/console"))).GetProperty("management").GetProperty("decisions").EnumerateArray().ToList();
        Assert.Contains(decisions, d => Str(d, "type") == "incident" && Id(d) == Id(legacy) && Str(d, "status") == "Problème à ouvrir");
        Assert.DoesNotContain(decisions, d => Str(d, "type") == "incident" && Id(d) == Id(i));
    }

    [Fact]
    public async Task INC_17_escalade_vers_un_autre_groupe_motivee()
    {
        var agent = api.Client("User", "agent.inc17");
        var i = await Json(await agent.PostAsJsonAsync("/api/incidents", Incident("VPN instable", ownerType: "Group", owner: "GRP-N1")));
        var url = $"/api/incidents/{Id(i)}";
        Assert.Equal(HttpStatusCode.BadRequest, (await agent.PutAsJsonAsync(url, Incident("VPN instable", ownerType: "Group", owner: "GRP-RESEAU"))).StatusCode);
        var escalated = await Json(await agent.PutAsJsonAsync(url, Incident("VPN instable", ownerType: "Group", owner: "GRP-RESEAU",
            escalation: "Diagnostic réseau requis")));
        Assert.Equal(("GRP-RESEAU", "Diagnostic réseau requis"), (Str(escalated, "ownerId"), Str(escalated, "escalationReason")));
        // Une nouvelle escalade demande un nouveau motif ; le même groupe garde le motif sans en demander.
        Assert.Equal(HttpStatusCode.BadRequest, (await agent.PutAsJsonAsync(url, Incident("VPN instable", ownerType: "Group", owner: "GRP-SYSTEME",
            escalation: "Diagnostic réseau requis"))).StatusCode);
        await Json(await agent.PutAsJsonAsync(url, Incident("VPN instable (précisé)", ownerType: "Group", owner: "GRP-RESEAU", escalation: "Diagnostic réseau requis")));
        var history = await Json(await agent.GetAsync($"/api/audit?type=incident&id={Id(i)}"));
        Assert.Contains("Diagnostic réseau requis", history.GetRawText());
    }

    [Fact]
    public async Task PRB_20_un_probleme_reactif_quitte_Nouveau_relie_a_un_incident()
    {
        var m = api.Client("Manager", "chef.prb20");
        var p = await Json(await m.PostAsJsonAsync("/api/problems", Problem("Lenteurs récurrentes")));
        Assert.Equal("Réactif", Str(p, "source"));
        var url = $"/api/problems/{Id(p)}";
        Assert.Equal(HttpStatusCode.BadRequest, (await m.PutAsJsonAsync(url, Problem("Lenteurs récurrentes", "En analyse"))).StatusCode);
        // Une analyse ne le fait pas passer En analyse tant qu'aucun incident n'est relié.
        await Json(await m.PostAsJsonAsync($"/api/problems/{Id(p)}/analyses", new { method = "FIVE_WHYS", dataJson = "{}", conclusion = (string?)null }));
        Assert.Equal("Nouveau", Str(await Json(await m.GetAsync(url)), "status"));
        var i = await Json(await m.PostAsJsonAsync("/api/incidents", Incident("Lenteur du portail")));
        await Json(await m.PostAsJsonAsync("/api/links", new { fromType = "problem", fromId = Id(p), toReference = Str(i, "reference") }));
        Assert.Equal("En analyse", Str(await Json(await m.PutAsJsonAsync(url, Problem("Lenteurs récurrentes", "En analyse"))), "status"));
        // Proactif : pas d'incident exigé.
        var proactive = await Json(await m.PostAsJsonAsync("/api/problems", Problem("Fin de support du SGBD", source: "Proactif")));
        await Json(await m.PutAsJsonAsync($"/api/problems/{Id(proactive)}", Problem("Fin de support du SGBD", "En analyse", "Proactif")));
        Assert.Equal(HttpStatusCode.BadRequest, (await m.PostAsJsonAsync("/api/problems", Problem("Source inconnue", source: "Curatif"))).StatusCode);
    }

    [Fact]
    public async Task PRB_07_valider_la_conclusion_d_une_analyse_comme_cause_racine()
    {
        var m = api.Client("Manager", "chef.prb07");
        var p = await Json(await m.PostAsJsonAsync("/api/problems", Problem("Coupures Wi-Fi", source: "Proactif")));
        var empty = await Json(await m.PostAsJsonAsync($"/api/problems/{Id(p)}/analyses", new { method = "FIVE_WHYS", dataJson = "{}", conclusion = (string?)null }));
        var a = await Json(await m.PostAsJsonAsync($"/api/problems/{Id(p)}/analyses",
            new { method = "ISHIKAWA", dataJson = "{}", conclusion = "Micrologiciel des bornes défaillant" }));
        Assert.Equal(HttpStatusCode.Forbidden, (await api.Client("User", "agent.prb07").PostAsync($"/api/problems/{Id(p)}/analyses/{Id(a)}/validate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await m.PostAsync($"/api/problems/{Id(p)}/analyses/{Id(empty)}/validate", null)).StatusCode);
        var validated = await Json(await m.PostAsync($"/api/problems/{Id(p)}/analyses/{Id(a)}/validate", null));
        Assert.NotEqual(JsonValueKind.Null, validated.GetProperty("validatedAt").ValueKind);
        Assert.Equal("Micrologiciel des bornes défaillant", Str(await Json(await m.GetAsync($"/api/problems/{Id(p)}")), "rootCause"));
    }
}
