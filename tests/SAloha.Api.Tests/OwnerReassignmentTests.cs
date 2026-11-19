using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SAloha.Api.Tests;

/// <summary>
/// Réaffectation d'un enregistrement engagé (CHG-07, REQ-25, CSI-11) : seul le responsable
/// d'avant l'envoi, un membre du groupe responsable ou un gestionnaire en change le responsable.
/// </summary>
[Collection("api")]
public class OwnerReassignmentTests(ApiFixture api)
{
    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode} : {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static async Task<string> Refused(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.StatusCode == HttpStatusCode.BadRequest, $"Attendu 400, reçu {(int)res.StatusCode} : {body}");
        return JsonDocument.Parse(body).RootElement.GetProperty("message").GetString()!;
    }

    private static int Id(JsonElement e) => e.GetProperty("id").GetInt32();
    private static string? Str(JsonElement e, string p) => e.GetProperty(p).GetString();
    private static readonly DateTime Slot = DateTime.UtcNow.Date.AddDays(75).AddHours(20);

    /// <summary>
    /// Le scénario commun : le tiers ne se désigne pas (ni ne retire le responsable), le
    /// responsable en place et un gestionnaire réaffectent, et le tiers devenu responsable agit.
    /// </summary>
    private async Task Reassignment(string url, Func<string?, string, string?, object> body, string status, string next,
        string owner, string tiers, string message)
    {
        var third = api.Client("User", tiers);
        // Le détour en deux envois : se désigner sans changer le statut, puis transiter.
        Assert.Contains(message, await Refused(await third.PutAsJsonAsync(url, body(status, "User", tiers))));
        Assert.Contains(message, await Refused(await third.PutAsJsonAsync(url, body(status, "Group", "GRP-TIERS"))));
        Assert.Contains(message, await Refused(await third.PutAsJsonAsync(url, body(status, "User", null))));
        var kept = await Json(await third.GetAsync(url));
        Assert.Equal((owner, status), (Str(kept, "ownerId"), Str(kept, "status")));
        // Le tiers modifie toujours la fiche sans toucher au responsable (la casse du compte ne compte pas).
        await Json(await third.PutAsJsonAsync(url, body(status, "User", owner.ToUpperInvariant())));
        // Le responsable en place passe la main ; le nouveau responsable fait alors la transition.
        Assert.Equal(tiers, Str(await Json(await api.Client("User", owner).PutAsJsonAsync(url, body(status, "User", tiers))), "ownerId"));
        Assert.Contains(message, await Refused(await api.Client("User", owner).PutAsJsonAsync(url, body(status, "User", owner))));
        // Un gestionnaire du périmètre réaffecte aussi.
        Assert.Equal(owner, Str(await Json(await api.Client("Manager", $"gest.{tiers}").PutAsJsonAsync(url, body(status, "User", owner))), "ownerId"));
        Assert.Equal(next, Str(await Json(await api.Client("User", owner).PutAsJsonAsync(url, body(next, "User", owner))), "status"));
    }

    [Fact]
    public async Task CHG_07_un_changement_autorise_n_est_reaffecte_que_par_son_responsable_ou_un_gestionnaire()
    {
        object Body(string? status, string ownerType, string? ownerId) => new
        {
            title = "CHG réaffectation", changeType = "Normal", risk = "Moyen", status, ownerType, ownerId, ownerDisplayName = ownerId,
            plannedStart = Slot, plannedEnd = Slot.AddHours(2), implementationPlan = "Appliquer", backoutPlan = "Restaurer",
            actualStart = status == "Mis en œuvre" ? Slot : (DateTime?)null
        };
        var requester = api.Client("Manager", "demandeur.chg269");
        var c = await Json(await requester.PostAsJsonAsync("/api/changes", Body(null, "User", "resp.chg269")));
        var url = $"/api/changes/{Id(c)}";
        await ChangeTestData.LinkCiAsync(requester, Id(c));
        await Json(await requester.PutAsJsonAsync(url, Body("Évalué", "User", "resp.chg269")));
        // Avant l'autorisation, le responsable se modifie librement.
        await Json(await api.Client("User", "tiers.chg269").PutAsJsonAsync(url, Body("Évalué", "User", "tiers.chg269")));
        await Json(await api.Client("User", "tiers.chg269").PutAsJsonAsync(url, Body("Évalué", "User", "resp.chg269")));
        await Json(await api.Client("Manager", "autorite.chg269").PutAsJsonAsync(url, Body("Autorisé", "User", "resp.chg269")));
        await Json(await requester.PutAsJsonAsync(url, Body("Planifié", "User", "resp.chg269")));

        await Reassignment(url, Body, "Planifié", "Mis en œuvre", "resp.chg269", "tiers.chg269", "le réaffecte");
    }

    [Fact]
    public async Task REQ_25_une_demande_en_cours_n_est_reaffectee_que_par_son_responsable_ou_un_gestionnaire()
    {
        object Body(string? status, string ownerType, string? ownerId) => new
        {
            title = "Accès à l'outil de paie", requestedItem = "Accès applicatif", status, ownerType, ownerId, ownerDisplayName = ownerId
        };
        var manager = api.Client("Manager", "req269.manager");
        var r = await Json(await api.Client("User", "req269.demandeur").PostAsJsonAsync("/api/requests", Body("Soumise", "User", "tech.req269")));
        var url = $"/api/requests/{Id(r)}";
        await Json(await manager.PutAsJsonAsync(url, Body("Approuvée", "User", "tech.req269")));
        // Avant le traitement, le responsable se modifie librement.
        await Json(await api.Client("User", "tiers.req269").PutAsJsonAsync(url, Body("Approuvée", "User", "tiers.req269")));
        await Json(await api.Client("User", "tiers.req269").PutAsJsonAsync(url, Body("En cours", "User", "tech.req269")));

        await Reassignment(url, Body, "En cours", "Satisfaite", "tech.req269", "tiers.req269", "la réaffecte");
    }

    [Fact]
    public async Task CSI_11_une_amelioration_validee_ne_change_de_porteur_que_par_son_porteur_ou_un_gestionnaire()
    {
        object Body(string? status, string ownerType, string? ownerId) => new
        {
            title = "Supervision des sauvegardes", step = 2, priority = "Moyenne", status, benefit = "b", baseline = "5 j", target = "2 j",
            value = 2, effort = 2, ownerType, ownerId, ownerDisplayName = ownerId
        };
        var manager = api.Client("Manager", "csi269.manager");
        var a = await Json(await manager.PostAsJsonAsync("/api/improvements", Body("Proposée", "User", "porteur.csi269")));
        var url = $"/api/improvements/{Id(a)}";
        // Proposée : le porteur se modifie librement.
        await Json(await api.Client("User", "tiers.csi269").PutAsJsonAsync(url, Body("Proposée", "User", "tiers.csi269")));
        await Json(await manager.PutAsJsonAsync(url, Body("Validée", "User", "porteur.csi269")));

        await Reassignment(url, Body, "Validée", "En cours", "porteur.csi269", "tiers.csi269", "en change le porteur");

        // Membre du groupe porteur : il réaffecte.
        var g = await Json(await manager.PostAsJsonAsync("/api/improvements", Body("Proposée", "Group", "GRP-CSI269")));
        var gurl = $"/api/improvements/{Id(g)}";
        await Json(await manager.PutAsJsonAsync(gurl, Body("Validée", "Group", "GRP-CSI269")));
        Assert.Contains("en change le porteur", await Refused(await api.Client("User", "tiers.csi269").PutAsJsonAsync(gurl, Body("Validée", "User", "tiers.csi269"))));
        await Json(await api.Client("User", "membre.csi269", "GRP-CSI269").PutAsJsonAsync(gurl, Body("Validée", "User", "tiers.csi269")));

        // Sans porteur : un User ne se désigne pas (ni seul, ni en engageant d'un même envoi), il
        // modifie encore le reste de la fiche ; un gestionnaire fait la première désignation.
        var free = await Json(await manager.PostAsJsonAsync("/api/improvements", Body("Proposée", "User", null)));
        var furl = $"/api/improvements/{Id(free)}";
        await Json(await manager.PutAsJsonAsync(furl, Body("Validée", "User", null)));
        var third = api.Client("User", "tiers.csi269");
        Assert.Contains("un gestionnaire le désigne", await Refused(await third.PutAsJsonAsync(furl, Body("Validée", "User", "tiers.csi269"))));
        Assert.Contains("Seul le porteur", await Refused(await third.PutAsJsonAsync(furl, Body("En cours", "User", "tiers.csi269"))));
        Assert.Contains("un gestionnaire le désigne", await Refused(await third.PutAsJsonAsync(furl, Body("Validée", "Group", "GRP-TIERS"))));
        await Json(await third.PutAsJsonAsync(furl, Body("Validée", "User", null)));
        Assert.Equal(JsonValueKind.Null, (await Json(await third.GetAsync(furl))).GetProperty("ownerId").ValueKind);
        Assert.Equal("tiers.csi269", Str(await Json(await manager.PutAsJsonAsync(furl, Body("Validée", "User", "tiers.csi269"))), "ownerId"));
        Assert.Equal("En cours", Str(await Json(await third.PutAsJsonAsync(furl, Body("En cours", "User", "tiers.csi269"))), "status"));
    }

    [Fact]
    public async Task CHG_07_un_changement_autorise_sans_responsable_est_confie_par_un_gestionnaire()
    {
        object Body(string? status, string? ownerId) => new
        {
            title = "CHG sans responsable", changeType = "Normal", risk = "Moyen", status,
            ownerType = ownerId is null ? null : "User", ownerId, ownerDisplayName = ownerId,
            plannedStart = Slot, plannedEnd = Slot.AddHours(2), implementationPlan = "Appliquer", backoutPlan = "Restaurer",
            actualStart = status == "Mis en œuvre" ? Slot : (DateTime?)null
        };
        var requester = api.Client("Manager", "demandeur.chg269s");
        var c = await Json(await requester.PostAsJsonAsync("/api/changes", Body(null, null)));
        var url = $"/api/changes/{Id(c)}";
        await ChangeTestData.LinkCiAsync(requester, Id(c));
        await Json(await requester.PutAsJsonAsync(url, Body("Évalué", null)));
        await Json(await api.Client("Manager", "autorite.chg269s").PutAsJsonAsync(url, Body("Autorisé", null)));
        await Json(await requester.PutAsJsonAsync(url, Body("Planifié", null)));

        // Le détour sur un changement sans titulaire : se désigner, puis mettre en œuvre.
        var third = api.Client("User", "tiers.chg269s");
        Assert.Contains("un gestionnaire le désigne", await Refused(await third.PutAsJsonAsync(url, Body("Planifié", "tiers.chg269s"))));
        var kept = await Json(await third.GetAsync(url));
        Assert.Equal(("Planifié", JsonValueKind.Null), (Str(kept, "status"), kept.GetProperty("ownerId").ValueKind));
        // Un gestionnaire le confie ; le responsable désigné met alors en œuvre.
        await Json(await requester.PutAsJsonAsync(url, Body("Planifié", "tiers.chg269s")));
        Assert.Equal("Mis en œuvre", Str(await Json(await third.PutAsJsonAsync(url, Body("Mis en œuvre", "tiers.chg269s"))), "status"));
    }
}
