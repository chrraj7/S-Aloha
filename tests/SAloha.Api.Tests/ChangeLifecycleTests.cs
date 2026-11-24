using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SAloha.Api.Tests;

/// <summary>Changements, sortie du MVP : dates réelles (CHG-14), revue post-implémentation (CHG-15), CI reliés (CHG-16), séparation des tâches (CHG-21).</summary>
[Collection("api")]
public class ChangeLifecycleTests(ApiFixture api)
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
    private static readonly DateTime Slot = DateTime.UtcNow.Date.AddDays(60).AddHours(20);

    private static object Change(string type, string? status, DateTime? actualStart = null, DateTime? actualEnd = null,
        string? outcome = null, string? pir = null, string? ownerType = null, string? ownerId = null) => new
    {
        title = "CHG cycle", changeType = type, risk = "Moyen", status, outcome, pirNotes = pir,
        ownerType, ownerId, ownerDisplayName = ownerId,
        plannedStart = Slot, plannedEnd = Slot.AddHours(2), implementationPlan = "Appliquer", backoutPlan = "Restaurer",
        actualStart, actualEnd
    };

    /// <summary>Changement Normal ou Urgent conduit jusqu'à Planifié : demandeur, CI relié, autorisé par un autre gestionnaire.</summary>
    private async Task<(HttpClient Requester, string Url)> Planned(string type, string tag, string? ownerType = null, string? ownerId = null)
    {
        var requester = api.Client("Manager", $"demandeur.{tag}");
        var c = await Json(await requester.PostAsJsonAsync("/api/changes", Change(type, null, ownerType: ownerType, ownerId: ownerId)));
        var url = $"/api/changes/{Id(c)}";
        await ChangeTestData.LinkCiAsync(requester, Id(c));
        await Json(await requester.PutAsJsonAsync(url, Change(type, "Évalué", ownerType: ownerType, ownerId: ownerId)));
        await Json(await api.Client("Manager", $"autorite.{tag}").PutAsJsonAsync(url, Change(type, "Autorisé", ownerType: ownerType, ownerId: ownerId)));
        await Json(await requester.PutAsJsonAsync(url, Change(type, "Planifié", ownerType: ownerType, ownerId: ownerId)));
        return (requester, url);
    }

    [Fact]
    public async Task CHG_16_un_changement_normal_s_evalue_sur_ses_CI()
    {
        var m = api.Client("Manager", "chg16.manager");
        var c = await Json(await m.PostAsJsonAsync("/api/changes", Change("Normal", null)));
        Assert.Contains("CI", await Refused(await m.PutAsJsonAsync($"/api/changes/{Id(c)}", Change("Normal", "Évalué"))));
        await ChangeTestData.LinkCiAsync(m, Id(c));
        await Json(await m.PutAsJsonAsync($"/api/changes/{Id(c)}", Change("Normal", "Évalué")));
    }

    [Fact]
    public async Task CHG_21_le_demandeur_n_autorise_pas_son_propre_changement()
    {
        var m = api.Client("Manager", "chg21.manager");
        var c = await Json(await m.PostAsJsonAsync("/api/changes", Change("Normal", null)));
        var url = $"/api/changes/{Id(c)}";
        await ChangeTestData.LinkCiAsync(m, Id(c));
        await Json(await m.PutAsJsonAsync(url, Change("Normal", "Évalué")));
        Assert.Contains("ne peut pas l'autoriser lui-même", await Refused(await m.PutAsJsonAsync(url, Change("Normal", "Autorisé"))));
        // L'Admin, lui, le peut (sa demande et son autorisation restent tracées).
        var admin = api.Client("Admin", "chg21.admin");
        var own = await Json(await admin.PostAsJsonAsync("/api/changes", Change("Normal", null)));
        await ChangeTestData.LinkCiAsync(admin, Id(own));
        await Json(await admin.PutAsJsonAsync($"/api/changes/{Id(own)}", Change("Normal", "Évalué")));
        await Json(await admin.PutAsJsonAsync($"/api/changes/{Id(own)}", Change("Normal", "Autorisé")));
    }

    [Fact]
    public async Task CHG_14_dates_reelles_et_depassement()
    {
        var (r, url) = await Planned("Normal", "chg14");
        Assert.Contains("début réel", await Refused(await r.PutAsJsonAsync(url, Change("Normal", "Mis en œuvre"))));
        Assert.Contains("fin réelle précède", await Refused(await r.PutAsJsonAsync(url, Change("Normal", "Mis en œuvre", Slot, Slot.AddHours(-1)))));
        await Json(await r.PutAsJsonAsync(url, Change("Normal", "Mis en œuvre", Slot)));
        Assert.Contains("fin réelle", await Refused(await r.PutAsJsonAsync(url, Change("Normal", "Clos", Slot, outcome: "Réussi"))));
        // Fini une heure après la fin planifiée : dépassement signalé.
        var closed = await Json(await r.PutAsJsonAsync(url, Change("Normal", "Clos", Slot, Slot.AddHours(3), "Réussi")));
        Assert.True(closed.GetProperty("overrun").GetBoolean());
        // Aussi au registre.
        var listed = (await Json(await r.GetAsync("/api/changes"))).EnumerateArray().Single(c => Id(c) == Id(closed));
        Assert.True(listed.GetProperty("overrun").GetBoolean());
    }

    [Fact]
    public async Task CHG_15_revue_post_implementation_d_un_changement_urgent_ou_echoue()
    {
        var (r, url) = await Planned("Normal", "chg15");
        await Json(await r.PutAsJsonAsync(url, Change("Normal", "Mis en œuvre", Slot)));
        Assert.Contains("revue post-implémentation", await Refused(await r.PutAsJsonAsync(url, Change("Normal", "Clos", Slot, Slot.AddHours(1), "Échoué"))));
        var closed = await Json(await r.PutAsJsonAsync(url, Change("Normal", "Clos", Slot, Slot.AddHours(1), "Échoué", "Retour arrière appliqué ; test de charge manquant.")));
        Assert.False(closed.GetProperty("overrun").GetBoolean());

        var (u, urgent) = await Planned("Urgent", "chg15u");
        await Json(await u.PutAsJsonAsync(urgent, Change("Urgent", "Mis en œuvre", Slot)));
        Assert.Contains("revue post-implémentation", await Refused(await u.PutAsJsonAsync(urgent, Change("Urgent", "Clos", Slot, Slot.AddHours(1), "Réussi"))));
    }

    [Fact]
    public async Task CHG_15_un_changement_urgent_mis_en_oeuvre_ne_quitte_pas_Urgent_pour_eviter_la_revue()
    {
        object Body(string type, string status, DateTime? end = null, string? pir = null) =>
            Change(type, status, Slot, end, end is null ? null : "Réussi", pir, "User", "resp.chg15t");
        var (_, url) = await Planned("Urgent", "chg15t", "User", "resp.chg15t");
        var owner = api.Client("User", "resp.chg15t");
        await Json(await owner.PutAsJsonAsync(url, Body("Urgent", "Mis en œuvre")));

        // En un envoi : clore en le disant Normal, sans revue, est refusé.
        Assert.Contains("reste Urgent", await Refused(await owner.PutAsJsonAsync(url, Body("Normal", "Clos", Slot.AddHours(1)))));
        // En deux envois : le type seul ne change pas non plus.
        Assert.Contains("reste Urgent", await Refused(await owner.PutAsJsonAsync(url, Body("Normal", "Mis en œuvre"))));
        // Un gestionnaire non plus.
        Assert.Contains("reste Urgent", await Refused(await api.Client("Manager", "gest.chg15t").PutAsJsonAsync(url, Body("Normal", "Mis en œuvre"))));
        var kept = await Json(await owner.GetAsync(url));
        Assert.Equal(("Urgent", "Mis en œuvre"), (kept.GetProperty("changeType").GetString(), kept.GetProperty("status").GetString()));
        // Témoin : toujours Urgent, la revue est exigée, puis la clôture passe avec elle.
        Assert.Contains("revue post-implémentation", await Refused(await owner.PutAsJsonAsync(url, Body("Urgent", "Clos", Slot.AddHours(1)))));
        await Json(await owner.PutAsJsonAsync(url, Body("Urgent", "Clos", Slot.AddHours(1), "Correctif d'urgence appliqué ; supervision à renforcer.")));
    }

    [Fact]
    public async Task CHG_14_seuls_le_responsable_et_un_gestionnaire_mettent_en_oeuvre_et_closent()
    {
        object Body(string status, string? ownerId = "resp.chg259", string ownerType = "User", DateTime? end = null) =>
            Change("Normal", status, Slot, end, end is null ? null : "Réussi", ownerType: ownerType, ownerId: ownerId);
        var (_, url) = await Planned("Normal", "chg259", "User", "resp.chg259");
        var tiers = api.Client("User", "tiers.chg259");
        var owner = api.Client("User", "resp.chg259");

        // Un User tiers ne met pas en œuvre (§ 4 : Responsable, Manager), même en se désignant dans le même envoi.
        Assert.Contains("responsable du changement", await Refused(await tiers.PutAsJsonAsync(url, Body("Mis en œuvre"))));
        Assert.Contains("responsable du changement", await Refused(await tiers.PutAsJsonAsync(url, Body("Mis en œuvre", "tiers.chg259"))));
        Assert.Equal("Planifié", (await Json(await owner.GetAsync(url))).GetProperty("status").GetString());
        // Le responsable, oui.
        await Json(await owner.PutAsJsonAsync(url, Body("Mis en œuvre")));
        // Ni la clôture par un tiers.
        Assert.Contains("responsable du changement", await Refused(await tiers.PutAsJsonAsync(url, Body("Clos", end: Slot.AddHours(1)))));
        Assert.Equal("Clos", (await Json(await owner.PutAsJsonAsync(url, Body("Clos", end: Slot.AddHours(1))))).GetProperty("status").GetString());

        // Responsable de type groupe : un membre du groupe met en œuvre.
        var (_, grp) = await Planned("Normal", "chg259g", "Group", "GRP-CHG259");
        await Json(await api.Client("User", "membre.chg259", "GRP-CHG259").PutAsJsonAsync(grp, Body("Mis en œuvre", "GRP-CHG259", "Group")));

        // Un gestionnaire du périmètre, qui n'est pas le responsable, oui.
        var (_, m) = await Planned("Normal", "chg259m", "User", "resp.chg259");
        await Json(await api.Client("Manager", "gest.chg259").PutAsJsonAsync(m, Body("Mis en œuvre")));
    }
}
