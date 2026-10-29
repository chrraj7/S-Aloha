using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SAloha.Api.Tests;

/// <summary>Changements, sortie du MVP : CAB (CHG-20), périodes de gel (CHG-25), modèles standard (CHG-26).</summary>
[Collection("api")]
public class ChangeGovernanceTests(ApiFixture api)
{
    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode} : {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static int Id(JsonElement e) => e.GetProperty("id").GetInt32();

    private static object Change(string type, string risk, string? status, DateTime start, int? model = null) => new
    {
        title = $"Gouvernance {type} {risk}", changeType = type, risk, status, standardModelId = model,
        plannedStart = start, plannedEnd = start.AddHours(2), implementationPlan = "Appliquer", backoutPlan = "Restaurer"
    };

    [Fact]
    public async Task CHG_20_un_changement_normal_a_risque_eleve_est_autorise_par_le_CAB()
    {
        var requester = api.Client("Manager", "chg20.demandeur");
        var slot = DateTime.UtcNow.Date.AddDays(70).AddHours(20);
        var c = await Json(await requester.PostAsJsonAsync("/api/changes", Change("Normal", "Élevé", null, slot)));
        var url = $"/api/changes/{Id(c)}";
        await ChangeTestData.LinkCiAsync(requester, Id(c));
        await Json(await requester.PutAsJsonAsync(url, Change("Normal", "Élevé", "Évalué", slot)));

        var a = api.Client("Manager", "chg20.cab.a");
        var b = api.Client("Manager", "chg20.cab.b");
        // Un seul gestionnaire n'autorise pas directement ; le demandeur ne siège pas ; un User non plus.
        Assert.Equal(HttpStatusCode.BadRequest, (await a.PutAsJsonAsync(url, Change("Normal", "Élevé", "Autorisé", slot))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await requester.PostAsJsonAsync($"{url}/approvals", new { approve = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.Client("User", "chg20.user").PostAsJsonAsync($"{url}/approvals", new { approve = true })).StatusCode);

        var first = await Json(await a.PostAsJsonAsync($"{url}/approvals", new { approve = true, comment = "Plan solide" }));
        Assert.Equal("Évalué", first.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await a.PostAsJsonAsync($"{url}/approvals", new { approve = true })).StatusCode);
        var second = await Json(await b.PostAsJsonAsync($"{url}/approvals", new { approve = true }));
        Assert.Equal("Autorisé", second.GetProperty("status").GetString());
        Assert.StartsWith("CAB : ", second.GetProperty("authorizedBy").GetString());
        Assert.Equal(2, (await Json(await requester.GetAsync($"{url}/approvals"))).GetArrayLength());

        // Un seul rejet, motivé, suffit.
        var other = await Json(await requester.PostAsJsonAsync("/api/changes", Change("Normal", "Élevé", null, slot.AddDays(1))));
        await ChangeTestData.LinkCiAsync(requester, Id(other));
        await Json(await requester.PutAsJsonAsync($"/api/changes/{Id(other)}", Change("Normal", "Élevé", "Évalué", slot.AddDays(1))));
        Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync($"/api/changes/{Id(other)}/approvals", new { approve = false })).StatusCode);
        var rejected = await Json(await a.PostAsJsonAsync($"/api/changes/{Id(other)}/approvals", new { approve = false, comment = "Fenêtre trop courte" }));
        Assert.Equal("Rejeté", rejected.GetProperty("status").GetString());
    }

    [Fact]
    public async Task CHG_07_la_modification_par_un_gestionnaire_ne_contourne_ni_le_CAB_ni_la_separation_des_taches()
    {
        var requester = api.Client("Manager", "chg07.demandeur");
        var a = api.Client("Manager", "chg07.autorite");
        var c = api.Client("Manager", "chg07.autre");
        var slot = DateTime.UtcNow.Date.AddDays(75).AddHours(20);
        object Body(string risk, string? status, string plan = "Appliquer") => new
        {
            title = "Gouvernance CHG-07", changeType = "Normal", risk, status,
            plannedStart = slot, plannedEnd = slot.AddHours(2), implementationPlan = plan, backoutPlan = "Restaurer"
        };
        async Task<string> Authorized()
        {
            var e = await Json(await requester.PostAsJsonAsync("/api/changes", Body("Moyen", null)));
            await ChangeTestData.LinkCiAsync(requester, Id(e));
            await Json(await requester.PutAsJsonAsync($"/api/changes/{Id(e)}", Body("Moyen", "Évalué")));
            var ok = await Json(await a.PutAsJsonAsync($"/api/changes/{Id(e)}", Body("Moyen", "Autorisé")));
            Assert.Equal(("Autorisé", "chg07.autorite"), (ok.GetProperty("status").GetString(), ok.GetProperty("authorizedBy").GetString()));
            return $"/api/changes/{Id(e)}";
        }
        static (string?, bool) State(JsonElement e) =>
            (e.GetProperty("status").GetString(), e.GetProperty("authorizedAt").ValueKind == JsonValueKind.Null);

        // #209, CHG-20 l'emporte : un gestionnaire qui passe le risque à Élevé renvoie le changement au CAB.
        var url = await Authorized();
        Assert.Equal(("Évalué", true), State(await Json(await a.PutAsJsonAsync(url, Body("Élevé", "Autorisé")))));
        Assert.Equal(HttpStatusCode.BadRequest, (await a.PutAsJsonAsync(url, Body("Élevé", "Autorisé"))).StatusCode);

        // #210 : le demandeur gestionnaire qui réécrit son changement autorisé le renvoie à l'évaluation.
        url = await Authorized();
        Assert.Equal(("Évalué", true), State(await Json(await requester.PutAsJsonAsync(url, Body("Moyen", "Autorisé", "Tout autre plan")))));

        // Un autre gestionnaire garde l'exception de CHG-07 : sa modification vaut autorisation, à son nom.
        url = await Authorized();
        var kept = await Json(await c.PutAsJsonAsync(url, Body("Moyen", "Autorisé", "Plan revu")));
        Assert.Equal(("Autorisé", false), State(kept));
        Assert.Equal("chg07.autre", kept.GetProperty("authorizedBy").GetString());
    }

    [Fact]
    public async Task CHG_25_pas_de_planification_dans_une_periode_de_gel_sauf_urgent()
    {
        var m = api.Client("Manager", "chg25.manager");
        var start = DateTime.UtcNow.Date.AddDays(90);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.Client("User", "chg25.user").PostAsJsonAsync("/api/change-freezes",
            new { start, end = start.AddDays(2), reason = "Clôture" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await m.PostAsJsonAsync("/api/change-freezes", new { start, end = start.AddDays(2), reason = "" })).StatusCode);
        var freeze = await Json(await m.PostAsJsonAsync("/api/change-freezes", new { start, end = start.AddDays(2), reason = "Clôture annuelle" }));
        Assert.Contains((await Json(await api.Client("User", "chg25.user").GetAsync($"/api/change-freezes?from={start:yyyy-MM-dd}"))).EnumerateArray(), f => Id(f) == Id(freeze));

        async Task<string> Plan(string type, DateTime slot)
        {
            var requester = api.Client("Manager", $"chg25.{type.ToLower()}");
            var c = await Json(await requester.PostAsJsonAsync("/api/changes", Change(type, "Moyen", null, slot)));
            var url = $"/api/changes/{Id(c)}";
            await ChangeTestData.LinkCiAsync(requester, Id(c));
            await Json(await requester.PutAsJsonAsync(url, Change(type, "Moyen", "Évalué", slot)));
            await Json(await m.PutAsJsonAsync(url, Change(type, "Moyen", "Autorisé", slot)));
            var res = await requester.PutAsJsonAsync(url, Change(type, "Moyen", "Planifié", slot));
            return res.StatusCode == HttpStatusCode.OK ? "Planifié" : (await res.Content.ReadAsStringAsync());
        }
        Assert.Contains("période de gel", await Plan("Normal", start.AddDays(1)));
        Assert.Equal("Planifié", await Plan("Urgent", start.AddDays(1)));
        Assert.Equal("Planifié", await Plan("Normal", start.AddDays(3)));
        Assert.Equal(HttpStatusCode.NoContent, (await m.DeleteAsync($"/api/change-freezes/{Id(freeze)}")).StatusCode);
    }

    [Fact]
    public async Task CHG_26_un_changement_standard_se_cree_depuis_un_modele_actif()
    {
        var m = api.Client("Manager", "chg26.manager");
        var u = api.Client("User", "chg26.user");
        var slot = DateTime.UtcNow.Date.AddDays(100).AddHours(20);
        Assert.Equal(HttpStatusCode.Forbidden, (await u.PostAsJsonAsync("/api/standard-changes",
            new { name = "x", implementationPlan = "p", backoutPlan = "b", risk = "Faible" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await m.PostAsJsonAsync("/api/standard-changes",
            new { name = "Sans plan", implementationPlan = "", backoutPlan = "", risk = "Faible" })).StatusCode);
        var model = await Json(await m.PostAsJsonAsync("/api/standard-changes", new
        {
            name = "Redémarrage planifié d'un serveur", implementationPlan = "Arrêt propre puis redémarrage", backoutPlan = "Rallumer l'ancien nœud", risk = "Faible"
        }));

        // Sans modèle : refusé ; avec : pré-autorisé, plans et risque repris du modèle.
        Assert.Equal(HttpStatusCode.BadRequest, (await u.PostAsJsonAsync("/api/changes", Change("Standard", "Moyen", null, slot))).StatusCode);
        var c = await Json(await u.PostAsJsonAsync("/api/changes", Change("Standard", "Élevé", null, slot, Id(model))));
        Assert.Equal("Autorisé", c.GetProperty("status").GetString());
        Assert.Equal("Arrêt propre puis redémarrage", c.GetProperty("implementationPlan").GetString());
        Assert.Equal("Faible", c.GetProperty("risk").GetString());

        // Désactivé : plus choisi ; jamais supprimé tant qu'un changement en vient.
        await Json(await m.PutAsJsonAsync($"/api/standard-changes/{Id(model)}", new
        {
            name = "Redémarrage planifié d'un serveur", implementationPlan = "Arrêt propre puis redémarrage", backoutPlan = "Rallumer l'ancien nœud", risk = "Faible", active = false
        }));
        Assert.Equal(HttpStatusCode.BadRequest, (await u.PostAsJsonAsync("/api/changes", Change("Standard", "Faible", null, slot, Id(model)))).StatusCode);
        Assert.DoesNotContain((await Json(await u.GetAsync("/api/standard-changes"))).EnumerateArray(), x => Id(x) == Id(model));
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client().DeleteAsync($"/api/standard-changes/{Id(model)}")).StatusCode);
    }
}
