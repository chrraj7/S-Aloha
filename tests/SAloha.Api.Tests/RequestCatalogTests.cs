using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace SAloha.Api.Tests;

/// <summary>Demandes, sortie du MVP : catalogue (REQ-20), pré-approbation (REQ-21), échéance (REQ-22), groupe d'exécution (REQ-23), retard (REQ-30).</summary>
[Collection("api")]
public class RequestCatalogTests(ApiFixture api)
{
    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode} : {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static int Id(JsonElement e) => e.GetProperty("id").GetInt32();

    private static object Item(string name, bool approval, int? hours = null, string? group = null, int? serviceId = null, bool active = true) =>
        new { name, description = "Modèle de test", serviceId, requiresApproval = approval, targetHours = hours, fulfillmentGroup = group, active };

    [Fact]
    public async Task REQ_20_catalogue_tenu_par_les_gestionnaires()
    {
        var m = api.Client("Manager", "req20.manager");
        var u = api.Client("User", "req20.user");
        Assert.Equal(HttpStatusCode.Forbidden, (await u.PostAsJsonAsync("/api/request-catalog", Item("Accès VPN", true))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await m.PostAsJsonAsync("/api/request-catalog", Item("", true))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await m.PostAsJsonAsync("/api/request-catalog", Item("Délai nul", true, hours: 0))).StatusCode);
        var item = await Json(await m.PostAsJsonAsync("/api/request-catalog", Item("REQ-20 Accès VPN", true)));

        // Une demande depuis le modèle en reprend l'objet demandé.
        var req = await Json(await u.PostAsJsonAsync("/api/requests", new { title = "VPN pour le télétravail", catalogItemId = Id(item) }));
        Assert.Equal("REQ-20 Accès VPN", req.GetProperty("requestedItem").GetString());
        Assert.Equal("Soumise", req.GetProperty("status").GetString());

        // Désactivé : plus proposé, plus choisi ; jamais supprimé tant qu'une demande en vient.
        await Json(await m.PutAsJsonAsync($"/api/request-catalog/{Id(item)}", Item("REQ-20 Accès VPN", true, active: false)));
        Assert.DoesNotContain((await Json(await u.GetAsync("/api/request-catalog"))).EnumerateArray(), x => Id(x) == Id(item));
        Assert.Contains((await Json(await u.GetAsync("/api/request-catalog?all=true"))).EnumerateArray(), x => Id(x) == Id(item));
        Assert.Equal(HttpStatusCode.BadRequest, (await u.PostAsJsonAsync("/api/requests", new { title = "Encore", catalogItemId = Id(item) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client().DeleteAsync($"/api/request-catalog/{Id(item)}")).StatusCode);
        // Sans modèle ni objet : 400 ; sans modèle mais avec objet : demande libre.
        Assert.Equal(HttpStatusCode.BadRequest, (await u.PostAsJsonAsync("/api/requests", new { title = "Rien" })).StatusCode);
        await Json(await u.PostAsJsonAsync("/api/requests", new { title = "Libre", requestedItem = "Écran" }));
    }

    [Fact]
    public async Task REQ_21_22_23_pre_approuvee_echeance_et_groupe_d_execution()
    {
        var m = api.Client("Manager", "req21.manager");
        var u = api.Client("User", "req21.user");
        var item = await Json(await m.PostAsJsonAsync("/api/request-catalog", Item("REQ-21 Clavier", false, hours: 24, group: "GRP-SUPPORT-REQ21")));

        var before = DateTime.UtcNow;
        var req = await Json(await u.PostAsJsonAsync("/api/requests", new { title = "Clavier cassé", catalogItemId = Id(item) }));
        Assert.Equal("Approuvée", req.GetProperty("status").GetString());
        Assert.Equal("Modèle pré-approuvé", req.GetProperty("approvedBy").GetString());
        Assert.Equal("GRP-SUPPORT-REQ21", req.GetProperty("ownerId").GetString());
        Assert.Equal("Group", req.GetProperty("ownerType").GetString());
        Assert.InRange(req.GetProperty("dueDate").GetDateTime().ToUniversalTime(), before.AddHours(24).AddSeconds(-5), DateTime.UtcNow.AddHours(24).AddSeconds(5));

        // REQ-22 : seul un gestionnaire modifie l'échéance.
        object Dto(DateTime? due) => new { title = "Clavier cassé", requestedItem = "REQ-21 Clavier", dueDate = due, status = "Approuvée",
            ownerType = "Group", ownerId = "GRP-SUPPORT-REQ21", ownerDisplayName = "GRP-SUPPORT-REQ21" };
        var later = DateTime.UtcNow.Date.AddDays(10);
        Assert.Equal(HttpStatusCode.BadRequest, (await u.PutAsJsonAsync($"/api/requests/{Id(req)}", Dto(later))).StatusCode);
        Assert.Equal(later, (await Json(await m.PutAsJsonAsync($"/api/requests/{Id(req)}", Dto(later)))).GetProperty("dueDate").GetDateTime().Date);

        // Avec approbation : pas d'échéance avant l'approbation, puis approbation + délai.
        var approval = await Json(await m.PostAsJsonAsync("/api/request-catalog", Item("REQ-22 Poste", true, hours: 48, group: "GRP-POSTES-REQ22")));
        var r2 = await Json(await u.PostAsJsonAsync("/api/requests", new { title = "Nouveau poste", catalogItemId = Id(approval) }));
        Assert.Equal(JsonValueKind.Null, r2.GetProperty("dueDate").ValueKind);
        var approved = await Json(await m.PutAsJsonAsync($"/api/requests/{Id(r2)}", new { title = "Nouveau poste", requestedItem = "REQ-22 Poste", status = "Approuvée" }));
        Assert.InRange(approved.GetProperty("dueDate").GetDateTime().ToUniversalTime(), DateTime.UtcNow.AddHours(47.9), DateTime.UtcNow.AddHours(48.1));
        Assert.Equal("GRP-POSTES-REQ22", approved.GetProperty("ownerId").GetString());
    }

    [Fact]
    public async Task REQ_22_echeance_renvoyee_au_jour_pres_par_le_formulaire()
    {
        // Le formulaire de la fiche ne montre que le jour de l'échéance et la renvoie à minuit UTC.
        var m = api.Client("Manager", "req22j.manager");
        var u = api.Client("User", "req22j.user");
        var item = await Json(await m.PostAsJsonAsync("/api/request-catalog", Item("REQ-22 Écran", false, hours: 24, group: "GRP-ECRANS-REQ22J")));
        var req = await Json(await u.PostAsJsonAsync("/api/requests", new { title = "Écran en panne", catalogItemId = Id(item) }));
        // Valeur relue en base (précision de PostgreSQL), pour la comparer aux réponses suivantes.
        var due = (await Json(await u.GetAsync($"/api/requests/{Id(req)}"))).GetProperty("dueDate").GetDateTime().ToUniversalTime();
        object Dto(string status) => new { title = "Écran en panne", requestedItem = "REQ-22 Écran", status,
            dueDate = DateTime.SpecifyKind(due.Date, DateTimeKind.Utc),
            ownerType = "Group", ownerId = "GRP-ECRANS-REQ22J", ownerDisplayName = "GRP-ECRANS-REQ22J" };

        // Le User d'exécution prend la demande en charge : même jour, échéance inchangée, heure comprise.
        var taken = await Json(await u.PutAsJsonAsync($"/api/requests/{Id(req)}", Dto("En cours")));
        Assert.Equal("En cours", taken.GetProperty("status").GetString());
        Assert.Equal(due, taken.GetProperty("dueDate").GetDateTime().ToUniversalTime());
        // Le gestionnaire qui enregistre la fiche sans toucher l'échéance ne la ramène pas à minuit.
        var saved = await Json(await m.PutAsJsonAsync($"/api/requests/{Id(req)}", Dto("En cours")));
        Assert.Equal(due, saved.GetProperty("dueDate").GetDateTime().ToUniversalTime());
    }

    [Fact]
    public async Task REQ_30_demande_en_retard_signalee()
    {
        var m = api.Client("Manager", "req30.manager");
        var req = await Json(await m.PostAsJsonAsync("/api/requests", new
        {
            title = "REQ-30", requestedItem = "Badge", ownerType = "User", ownerId = "req30.tech", ownerDisplayName = "Tech"
        }));
        await using (var db = api.Database())
        {
            var r = await db.ServiceRequests.FirstAsync(x => x.Id == Id(req));
            r.DueDate = DateTime.UtcNow.Date.AddDays(-1);
            await db.SaveChangesAsync();
        }
        Assert.True((await Json(await m.GetAsync($"/api/requests/{Id(req)}"))).GetProperty("overdue").GetBoolean());
        var console = await Json(await m.GetAsync("/api/console"));
        Assert.Contains(console.GetProperty("management").GetProperty("overdueRequests").EnumerateArray(), r => Id(r) == Id(req));

        await Json(await api.Client().PostAsync("/api/notifications/run", null));
        await Json(await api.Client().PostAsync("/api/notifications/run", null));
        var events = (await Json(await api.Client("User", "req30.tech").GetAsync("/api/notifications"))).GetProperty("items").EnumerateArray()
            .Where(n => n.GetProperty("reference").GetString() == req.GetProperty("reference").GetString())
            .Select(n => n.GetProperty("event").GetString()!).ToList();
        Assert.Single(events, e => e.StartsWith("Demande en retard"));
    }
}
