using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace SAloha.Api.Tests;

/// <summary>Changements, lot 2 : conflit commenté (CHG-22), relances (CHG-30, CHG-31), part des urgents (CHG-32).</summary>
[Collection("api")]
public class ChangeFollowUpsTests(ApiFixture api)
{
    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode} : {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static int Id(JsonElement e) => e.GetProperty("id").GetInt32();
    private static string Ref(JsonElement e) => e.GetProperty("reference").GetString()!;

    private static object Change(string title, string type, string risk, string? status, DateTime start, string owner,
        int? model = null, string? conflictComment = null, DateTime? actualStart = null) => new
    {
        title, changeType = type, risk, status, standardModelId = model, ownerType = "User", ownerId = owner, ownerDisplayName = owner,
        plannedStart = start, plannedEnd = start.AddHours(2), implementationPlan = "Appliquer", backoutPlan = "Restaurer",
        conflictComment, actualStart
    };

    private static async Task Link(HttpClient c, int change, string ciReference) =>
        await Json(await c.PostAsJsonAsync("/api/links", new { fromType = "change", fromId = change, toReference = ciReference }));

    private static async Task<string> Ci(HttpClient c, string name) => Ref(await Json(await c.PostAsJsonAsync("/api/configuration-items", new
    {
        title = name, ciType = "Serveur", environment = "Production", location = "Test", status = "En service",
        ownerType = "User", ownerId = "ci.owner", ownerDisplayName = "Owner"
    })));

    [Fact]
    public async Task CHG_22_autoriser_un_changement_en_conflit_exige_un_commentaire()
    {
        var requester = api.Client("Manager", "chg22.demandeur");
        var authority = api.Client("Manager", "chg22.autorite");
        var slot = DateTime.UtcNow.Date.AddDays(80).AddHours(20);
        var ci = await Ci(requester, $"chg22-{Guid.NewGuid():N}"[..14]);
        var other = await Json(await requester.PostAsJsonAsync("/api/changes", Change("Conflit 22 autre", "Normal", "Moyen", null, slot.AddHours(1), "chg22.demandeur")));
        await Link(requester, Id(other), ci);
        var c = await Json(await requester.PostAsJsonAsync("/api/changes", Change("Conflit 22", "Normal", "Moyen", null, slot, "chg22.demandeur")));
        var url = $"/api/changes/{Id(c)}";
        await Link(requester, Id(c), ci);
        await Json(await requester.PutAsJsonAsync(url, Change("Conflit 22", "Normal", "Moyen", "Évalué", slot, "chg22.demandeur")));

        var refused = await authority.PutAsJsonAsync(url, Change("Conflit 22", "Normal", "Moyen", "Autorisé", slot, "chg22.demandeur"));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains(Ref(other), await refused.Content.ReadAsStringAsync());
        var ok = await Json(await authority.PutAsJsonAsync(url, Change("Conflit 22", "Normal", "Moyen", "Autorisé", slot, "chg22.demandeur",
            conflictComment: "Même équipe d'intervention, enchaînement prévu")));
        Assert.Equal(("Autorisé", "Même équipe d'intervention, enchaînement prévu"),
            (ok.GetProperty("status").GetString(), ok.GetProperty("conflictComment").GetString()));

        // CAB : une approbation d'un changement en conflit se commente aussi.
        var cab = await Json(await requester.PostAsJsonAsync("/api/changes", Change("Conflit 22 CAB", "Normal", "Élevé", null, slot, "chg22.demandeur")));
        await Link(requester, Id(cab), ci);
        await Json(await requester.PutAsJsonAsync($"/api/changes/{Id(cab)}", Change("Conflit 22 CAB", "Normal", "Élevé", "Évalué", slot, "chg22.demandeur")));
        Assert.Equal(HttpStatusCode.BadRequest, (await authority.PostAsJsonAsync($"/api/changes/{Id(cab)}/approvals", new { approve = true })).StatusCode);
        await Json(await authority.PostAsJsonAsync($"/api/changes/{Id(cab)}/approvals", new { approve = true, comment = "Conflit accepté" }));
    }

    [Fact]
    public async Task CHG_22_un_gestionnaire_qui_replanifie_en_conflit_sans_commentaire_renvoie_a_l_evaluation()
    {
        var requester = api.Client("Manager", "chg22r.demandeur");
        var authority = api.Client("Manager", "chg22r.autorite");
        var slot = DateTime.UtcNow.Date.AddDays(85).AddHours(20);
        var ci = await Ci(requester, $"chg22r-{Guid.NewGuid():N}"[..15]);
        var other = await Json(await requester.PostAsJsonAsync("/api/changes", Change("Replanif 22 autre", "Normal", "Moyen", null, slot, "chg22r.demandeur")));
        await Link(requester, Id(other), ci);
        async Task<string> Authorized()
        {
            var c = await Json(await requester.PostAsJsonAsync("/api/changes", Change("Replanif 22", "Normal", "Moyen", null, slot.AddDays(1), "chg22r.demandeur")));
            await Link(requester, Id(c), ci);
            await Json(await requester.PutAsJsonAsync($"/api/changes/{Id(c)}", Change("Replanif 22", "Normal", "Moyen", "Évalué", slot.AddDays(1), "chg22r.demandeur")));
            await Json(await authority.PutAsJsonAsync($"/api/changes/{Id(c)}", Change("Replanif 22", "Normal", "Moyen", "Autorisé", slot.AddDays(1), "chg22r.demandeur")));
            return $"/api/changes/{Id(c)}";
        }

        // Replanifié sur le créneau de l'autre changement, sans commentaire : retour à Évalué.
        var url = await Authorized();
        var moved = await Json(await authority.PutAsJsonAsync(url, Change("Replanif 22", "Normal", "Moyen", "Autorisé", slot.AddHours(1), "chg22r.demandeur")));
        Assert.Equal("Évalué", moved.GetProperty("status").GetString());
        // Avec le commentaire de l'autorité, la modification vaut autorisation (CHG-07).
        url = await Authorized();
        var commented = await Json(await authority.PutAsJsonAsync(url, Change("Replanif 22", "Normal", "Moyen", "Autorisé", slot.AddHours(1), "chg22r.demandeur",
            conflictComment: "Enchaînement prévu avec l'autre intervention")));
        Assert.Equal("Autorisé", commented.GetProperty("status").GetString());
    }

    [Fact]
    public async Task CHG_30_CHG_31_changements_a_relancer_au_responsable_et_aux_gestionnaires()
    {
        var owner = api.Client("User", "chg30.responsable");
        var model = await ChangeTestData.StandardModelIdAsync(api);
        // CHG-30 : planifié, fin planifiée passée, jamais mis en œuvre.
        var past = DateTime.UtcNow.AddDays(-1).AddHours(-3);
        var late = await Json(await owner.PostAsJsonAsync("/api/changes", Change("Relance 30", "Standard", "Faible", null, past, "chg30.responsable", model)));
        await Json(await owner.PutAsJsonAsync($"/api/changes/{Id(late)}", Change("Relance 30", "Standard", "Faible", "Planifié", past, "chg30.responsable", model)));
        // CHG-31 : mis en œuvre il y a plus de 2 jours ouvrés, pas clos.
        var start = DateTime.UtcNow.AddDays(-6);
        var open = await Json(await owner.PostAsJsonAsync("/api/changes", Change("Relance 31", "Standard", "Faible", null, start, "chg30.responsable", model)));
        await Json(await owner.PutAsJsonAsync($"/api/changes/{Id(open)}", Change("Relance 31", "Standard", "Faible", "Planifié", start, "chg30.responsable", model)));
        await Json(await owner.PutAsJsonAsync($"/api/changes/{Id(open)}",
            Change("Relance 31", "Standard", "Faible", "Mis en œuvre", start, "chg30.responsable", model, actualStart: start)));
        await using (var db = api.Database())
        {
            var c = await db.Changes.SingleAsync(x => x.Id == Id(open));
            c.ImplementedAt = start;
            await db.SaveChangesAsync();
        }
        // Un changement récent ne se relance pas.
        var fresh = await Json(await owner.PostAsJsonAsync("/api/changes", Change("Pas de relance", "Standard", "Faible", null,
            DateTime.UtcNow.AddDays(3), "chg30.responsable", model)));

        var mine = (await Json(await owner.GetAsync("/api/console"))).GetProperty("myChangeFollowUps").EnumerateArray().ToList();
        Assert.Contains(mine, i => Id(i) == Id(late) && i.GetProperty("status").GetString()!.StartsWith("Fin planifiée passée"));
        Assert.Contains(mine, i => Id(i) == Id(open) && i.GetProperty("status").GetString()!.StartsWith("Mis en œuvre depuis plus de 2 jours ouvrés"));
        Assert.DoesNotContain(mine, i => Id(i) == Id(fresh));
        var managed = (await Json(await api.Client("Manager", "chg30.chef").GetAsync("/api/console")))
            .GetProperty("management").GetProperty("changeFollowUps").EnumerateArray();
        Assert.Contains(managed, i => Id(i) == Id(late));
        Assert.Equal("Fin planifiée passée sans mise en œuvre",
            (await Json(await owner.GetAsync($"/api/changes/{Id(late)}"))).GetProperty("followUp").GetString());

        // Notification au responsable, une fois par motif.
        await Json(await api.Client("Admin", "admin.chg30").PostAsync("/api/notifications/run", null));
        await Json(await api.Client("Admin", "admin.chg30").PostAsync("/api/notifications/run", null));
        await using var check = api.Database();
        Assert.Equal(1, await check.Notifications.CountAsync(n => n.TargetType == "change" && n.TargetId == Id(late)
                                                                 && n.Event == "Fin planifiée passée sans mise en œuvre"));
    }

    [Fact]
    public async Task CHG_32_part_des_changements_urgents_par_mois()
    {
        var c = api.Client("Manager", "chg32.chef");
        await Json(await c.PostAsJsonAsync("/api/changes", Change("Urgent 32", "Urgent", "Moyen", null, DateTime.UtcNow.AddDays(2), "chg32.chef")));
        var kpis = (await Json(await c.GetAsync("/api/reports/dashboard/changes-projects"))).GetProperty("kpis");
        Assert.True(kpis.GetProperty("urgentShare").GetDouble() > 0);
        var month = kpis.GetProperty("urgentByMonth").EnumerateArray().Single(m => m.GetProperty("month").GetString() == DateTime.UtcNow.ToString("yyyy-MM"));
        Assert.True(month.GetProperty("urgent").GetInt32() >= 1 && month.GetProperty("total").GetInt32() >= month.GetProperty("urgent").GetInt32());
    }
}
