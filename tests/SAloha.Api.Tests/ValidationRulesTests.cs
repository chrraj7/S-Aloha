using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SAloha.Api.Tests;

/// <summary>Étape 3 du lot 1 : validations et champs obligatoires des pratiques.</summary>
[Collection("api")]
public class ValidationRulesTests(ApiFixture api)
{
    private const string Owner = "hery.rakoto";

    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode} : {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static async Task<string> Refused(HttpResponseMessage res, HttpStatusCode code = HttpStatusCode.BadRequest)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.StatusCode == code, $"Attendu {(int)code}, reçu {(int)res.StatusCode} : {body}");
        return JsonDocument.Parse(body).RootElement.GetProperty("message").GetString()!;
    }

    private static int Id(JsonElement e) => e.GetProperty("id").GetInt32();
    private static string Ref(JsonElement e) => e.GetProperty("reference").GetString()!;
    private static bool IsNull(JsonElement e, string p) => e.GetProperty(p).ValueKind == JsonValueKind.Null;
    /// <summary>Date à la microseconde : la réponse d'un enregistrement porte la valeur en mémoire, une relecture celle de PostgreSQL.</summary>
    private static long Micro(string? iso) => DateTime.Parse(iso!).ToUniversalTime().Ticks / 10;

    // ---------- Incidents ----------

    private static object Incident(string title, string? status = null, string? owner = null,
        string? code = null, string? resolution = "Redémarrage", string? reopenReason = null) => new
    {
        title, impact = "Moyen", urgency = "Moyenne", status, resolution, resolutionCode = code,
        reopenReason,
        ownerType = owner is null ? null : "User", ownerId = owner, ownerDisplayName = owner
    };

    [Fact]
    public async Task INC_06_prise_en_charge_avec_responsable_et_premiere_reponse_horodatee()
    {
        var c = api.Client();
        var i = await Json(await c.PostAsJsonAsync("/api/incidents", Incident("INC-06")));
        Assert.Contains("responsable", await Refused(await c.PutAsJsonAsync($"/api/incidents/{Id(i)}", Incident("INC-06", "En cours"))));

        var taken = await Json(await c.PutAsJsonAsync($"/api/incidents/{Id(i)}", Incident("INC-06", "En cours", Owner)));
        var first = taken.GetProperty("firstResponseAt").GetString();
        Assert.NotNull(first);
        await Json(await c.PutAsJsonAsync($"/api/incidents/{Id(i)}", Incident("INC-06", "Résolu", Owner, "Correctif appliqué")));
        var reopened = await Json(await c.PutAsJsonAsync($"/api/incidents/{Id(i)}", Incident("INC-06", "En cours", Owner, "Correctif appliqué", reopenReason: "Symptôme réapparu")));
        Assert.Equal(Micro(first), Micro(reopened.GetProperty("firstResponseAt").GetString()));   // première prise en charge, jamais réécrite
    }

    [Fact]
    public async Task INC_08_code_de_resolution_ferme_et_doublon_relie()
    {
        var c = api.Client();
        var kept = await Json(await c.PostAsJsonAsync("/api/incidents", Incident("INC-08 conservé")));
        var dup = await Json(await c.PostAsJsonAsync("/api/incidents", Incident("INC-08 doublon")));
        var url = $"/api/incidents/{Id(dup)}";

        Assert.Contains("code de résolution", await Refused(await c.PutAsJsonAsync(url, Incident("INC-08 doublon", "Résolu"))));
        await Refused(await c.PutAsJsonAsync(url, Incident("INC-08 doublon", "Résolu", code: "Réparé")));
        Assert.Contains("relié", await Refused(await c.PutAsJsonAsync(url, Incident("INC-08 doublon", "Résolu", code: "Doublon"))));

        await Json(await c.PostAsJsonAsync("/api/links", new { fromType = "incident", fromId = Id(dup), toReference = Ref(kept) }));
        var resolved = await Json(await c.PutAsJsonAsync(url, Incident("INC-08 doublon", "Résolu", code: "Doublon")));
        Assert.Equal("Doublon", resolved.GetProperty("resolutionCode").GetString());
    }

    // ---------- Demandes ----------

    [Fact]
    public async Task REQ_06_rejet_motive()
    {
        var m = api.Client("Manager");
        object Dto(string? status, string? reason = null) => new { title = "REQ-06", requestedItem = "Accès", status, rejectionReason = reason };
        var r = await Json(await m.PostAsJsonAsync("/api/requests", Dto(null)));
        Assert.Contains("Motiver le rejet", await Refused(await m.PutAsJsonAsync($"/api/requests/{Id(r)}", Dto("Rejetée"))));
        var rejected = await Json(await m.PutAsJsonAsync($"/api/requests/{Id(r)}", Dto("Rejetée", "Accès non justifié")));
        Assert.Equal("Accès non justifié", rejected.GetProperty("rejectionReason").GetString());
    }

    // ---------- Changements ----------

    private static readonly DateTime Slot = DateTime.UtcNow.Date.AddDays(40).AddHours(20);

    private static object Change(string? status, string plan = "", string? reason = null, bool end = true) => new
    {
        title = "CHG-12", changeType = "Normal", risk = "Moyen", status,
        implementationPlan = plan, backoutPlan = plan, rejectionReason = reason,
        plannedStart = Slot, plannedEnd = end ? Slot.AddHours(1) : (DateTime?)null
    };

    [Fact]
    public async Task CHG_12_13_17_plans_a_l_evaluation_fin_planifiee_et_rejet_motive()
    {
        var m = api.Client("Manager");
        var c = await Json(await m.PostAsJsonAsync("/api/changes", Change(null)));
        var url = $"/api/changes/{Id(c)}";
        await ChangeTestData.LinkCiAsync(m, Id(c));   // CHG-16
        Assert.Contains("plan de mise en œuvre", await Refused(await m.PutAsJsonAsync(url, Change("Évalué"))));
        await Json(await m.PutAsJsonAsync(url, Change("Évalué", "Procédure")));
        Assert.Contains("Motiver le rejet", await Refused(await m.PutAsJsonAsync(url, Change("Rejeté", "Procédure"))));

        var authority = api.Client("Manager", "chef.chg12");
        await Json(await authority.PutAsJsonAsync(url, Change("Autorisé", "Procédure")));   // CHG-21
        // L'autorité, pas le demandeur : le créneau modifié par le demandeur renverrait à Évalué (CHG-07).
        Assert.Contains("fin planifiés", await Refused(await authority.PutAsJsonAsync(url, Change("Planifié", "Procédure", end: false))));
        await Json(await m.PutAsJsonAsync(url, Change("Planifié", "Procédure")));

        var other = await Json(await m.PostAsJsonAsync("/api/changes", Change(null, "Procédure")));
        await ChangeTestData.LinkCiAsync(m, Id(other));
        await Json(await m.PutAsJsonAsync($"/api/changes/{Id(other)}", Change("Évalué", "Procédure")));
        await Json(await m.PutAsJsonAsync($"/api/changes/{Id(other)}", Change("Rejeté", "Procédure", "Créneau de gel")));
    }

    // ---------- Configuration ----------

    private static object Ci(string title, string env = "Production", string? status = null, string? owner = Owner) => new
    {
        title, ciType = "Serveur", environment = env, status,
        ownerType = owner is null ? null : "User", ownerId = owner, ownerDisplayName = owner
    };

    [Fact]
    public async Task CFG_11_proprietaire_obligatoire_en_service()
    {
        var c = api.Client();
        Assert.Contains("propriétaire", await Refused(await c.PostAsJsonAsync("/api/configuration-items", Ci("cfg11-a", owner: null))));
        var planned = await Json(await c.PostAsJsonAsync("/api/configuration-items", Ci("cfg11-b", status: "Planifié", owner: null)));
        await Refused(await c.PutAsJsonAsync($"/api/configuration-items/{Id(planned)}", Ci("cfg11-b", status: "En service", owner: null)));
        await Json(await c.PutAsJsonAsync($"/api/configuration-items/{Id(planned)}", Ci("cfg11-b", status: "En service")));
    }

    [Fact]
    public async Task CFG_17_nom_unique_par_environnement_hors_CI_retires()
    {
        var c = api.Client("Manager");
        var first = await Json(await c.PostAsJsonAsync("/api/configuration-items", Ci("SRV-UNIQUE-01")));
        Assert.Contains("existe déjà", await Refused(await c.PostAsJsonAsync("/api/configuration-items", Ci("srv-unique-01")), HttpStatusCode.Conflict));
        await Json(await c.PostAsJsonAsync("/api/configuration-items", Ci("SRV-UNIQUE-01", "Recette")));

        await Json(await c.PutAsJsonAsync($"/api/configuration-items/{Id(first)}", Ci("SRV-UNIQUE-01", status: "Retiré")));
        await Json(await c.PostAsJsonAsync("/api/configuration-items", Ci("SRV-UNIQUE-01")));   // l'ancien est retiré
    }

    // ---------- Niveaux de service ----------

    [Fact]
    public async Task SLM_11_et_SLM_03_service_proprietaire_et_SLA_coherent()
    {
        var m = api.Client("Manager");
        object Svc(string? status, string? owner) => new
        {
            title = "SLM ERP", criticality = "Élevée", status,
            ownerType = owner is null ? null : "User", ownerId = owner, ownerDisplayName = owner
        };
        Assert.Contains("responsable du service", await Refused(await m.PostAsJsonAsync("/api/services", Svc("En service", null))));
        var designing = await Json(await m.PostAsJsonAsync("/api/services", Svc(null, null)));
        var running = await Json(await m.PostAsJsonAsync("/api/services", Svc("En service", Owner)));

        object Sla(int serviceId, string? status, int? p4 = 48, int p3 = 16) => new
        {
            title = "SLA", serviceId, customer = "Finance", status, validFrom = DateTime.UtcNow.Date,
            resolutionHoursP1 = 2, resolutionHoursP2 = 6, resolutionHoursP3 = p3, resolutionHoursP4 = p4
        };
        async Task<string> Activate(int serviceId, object dto)
        {
            var a = await Json(await m.PostAsJsonAsync("/api/agreements", Sla(serviceId, null)));
            return await Refused(await m.PutAsJsonAsync($"/api/agreements/{Id(a)}", dto));
        }
        Assert.Contains("P1 à P4", await Activate(Id(running), Sla(Id(running), "En vigueur", p4: null)));
        Assert.Contains("croître", await Activate(Id(running), Sla(Id(running), "En vigueur", p3: 60)));
        Assert.Contains("en service", await Activate(Id(designing), Sla(Id(designing), "En vigueur")));

        var ok = await Json(await m.PostAsJsonAsync("/api/agreements", Sla(Id(running), null)));
        await Json(await m.PutAsJsonAsync($"/api/agreements/{Id(ok)}", Sla(Id(running), "En vigueur")));
        Assert.Contains("déjà en vigueur", await Activate(Id(running), Sla(Id(running), "En vigueur")));
    }

    // ---------- Amélioration continue ----------

    [Fact]
    public async Task CSI_13_14_16_mesures_etape_et_abandon_motive()
    {
        var m = api.Client("Manager");
        object Dto(string? status, int step = 1, bool measured = true, string? abandon = null, string? outcome = null) => new
        {
            title = "CSI", step, priority = "Moyenne", status, outcome, abandonReason = abandon,
            benefit = measured ? "Délai réduit" : "", baseline = measured ? "5 j" : "", target = measured ? "2 j" : "", value = 2, effort = 2
        };
        var a = await Json(await m.PostAsJsonAsync("/api/improvements", Dto(null, measured: false)));
        var url = $"/api/improvements/{Id(a)}";
        Assert.Contains("se mesure", await Refused(await m.PutAsJsonAsync(url, Dto("Validée", measured: false))));
        await Json(await m.PutAsJsonAsync(url, Dto("Validée")));
        await Json(await m.PutAsJsonAsync(url, Dto("En cours", 5)));
        Assert.Contains("étape 6", await Refused(await m.PutAsJsonAsync(url, Dto("Réalisée", 5, outcome: "2,5 j"))));
        Assert.Contains("Motiver l'abandon", await Refused(await m.PutAsJsonAsync(url, Dto("Abandonnée", 5))));
        await Json(await m.PutAsJsonAsync(url, Dto("Réalisée", 6, outcome: "2,5 j")));
    }

    // ---------- Problèmes ----------

    private static object Problem(string? status = null, string impact = "Moyen", string? workaround = null,
        string? rootCause = null, string? closure = null) => new
    {
        title = "PRB", description = "d", status, impact, urgency = "Moyenne", category = "c", affectedService = "s",
        knownErrorWorkaround = workaround, rootCause, closureCode = closure, reopenReason = "Réouverture motivée (PRB-10)", source = "Proactif"
    };

    private static object Action(string? status = null, string a = "lova.rabe", int approvers = 1) => new
    {
        title = "Corriger", description = "", status, dueDate = (DateTime?)null,
        raci = new[] { new { role = "R", assigneeType = "User", assigneeId = "fara.rasoa", assigneeDisplayName = "Fara" } }
            .Concat(Enumerable.Range(0, approvers).Select(i => new { role = "A", assigneeType = "User", assigneeId = a + i, assigneeDisplayName = "A" }))
            .ToArray()
    };

    [Fact]
    public async Task PRB_02_valeurs_fermees_du_module_problemes()
    {
        var m = api.Client("Manager");
        await Refused(await m.PostAsJsonAsync("/api/problems", Problem(impact: "Énorme")));
        var p = await Json(await m.PostAsJsonAsync("/api/problems", Problem()));
        await Refused(await m.PutAsJsonAsync($"/api/problems/{Id(p)}", Problem(impact: "Énorme")));
        await Refused(await m.PostAsJsonAsync($"/api/problems/{Id(p)}/analyses", new { method = "PARETO", dataJson = "{}", conclusion = (string?)null }));
        var bad = new { title = "x", description = "", status = (string?)null, dueDate = (DateTime?)null,
            raci = new[] { new { role = "X", assigneeType = "User", assigneeId = "a", assigneeDisplayName = "a" } } };
        await Refused(await m.PostAsJsonAsync($"/api/problems/{Id(p)}/actions", bad));
        var action = await Json(await m.PostAsJsonAsync($"/api/problems/{Id(p)}/actions", Action()));
        await Refused(await m.PutAsJsonAsync($"/api/actions/{Id(action)}", Action("Finie")));
    }

    [Fact]
    public async Task PRB_11_12_13_14_conditions_du_cycle_de_vie()
    {
        var m = api.Client("Manager");
        var p = await Json(await m.PostAsJsonAsync("/api/problems", Problem()));
        var url = $"/api/problems/{Id(p)}";
        await Json(await m.PutAsJsonAsync(url, Problem("En analyse")));
        Assert.Contains("contournement", await Refused(await m.PutAsJsonAsync(url, Problem("Erreur connue"))));
        await Json(await m.PutAsJsonAsync(url, Problem("Erreur connue", workaround: "Relancer")));

        var action = await Json(await m.PostAsJsonAsync($"{url}/actions", Action()));
        Assert.Contains("cause racine", await Refused(await m.PutAsJsonAsync(url, Problem("Résolu", workaround: "Relancer"))));
        Assert.Contains("encore ouvertes", await Refused(await m.PutAsJsonAsync(url, Problem("Résolu", workaround: "Relancer", rootCause: "Fuite"))));
        await Json(await m.PutAsJsonAsync($"/api/actions/{Id(action)}", Action("Annulée")));
        Assert.Contains("au moins une action", await Refused(await m.PutAsJsonAsync(url, Problem("Résolu", workaround: "Relancer", rootCause: "Fuite"))));
        await Json(await m.PutAsJsonAsync($"/api/actions/{Id(action)}", Action("Terminée")));
        var resolved = await Json(await m.PutAsJsonAsync(url, Problem("Résolu", workaround: "Relancer", rootCause: "Fuite")));
        Assert.False(IsNull(resolved, "resolvedAt"));   // PRB-14

        Assert.Contains("Corrigé", await Refused(await m.PutAsJsonAsync(url, Problem("Clos", workaround: "Relancer", rootCause: "Fuite", closure: "Doublon"))));
        var closed = await Json(await m.PutAsJsonAsync(url, Problem("Clos", workaround: "Relancer", rootCause: "Fuite", closure: "Corrigé")));
        Assert.Equal("Corrigé", closed.GetProperty("closureCode").GetString());

        var reopened = await Json(await m.PutAsJsonAsync(url, Problem("En analyse", workaround: "Relancer", rootCause: "Fuite", closure: "Corrigé")));
        Assert.True(IsNull(reopened, "resolvedAt") && IsNull(reopened, "closureCode") && IsNull(reopened, "closedAt"));
    }

    [Fact]
    public async Task PRB_13_doublon_relie_au_probleme_conserve()
    {
        var m = api.Client("Manager");
        var kept = await Json(await m.PostAsJsonAsync("/api/problems", Problem()));
        var dup = await Json(await m.PostAsJsonAsync("/api/problems", Problem()));
        var url = $"/api/problems/{Id(dup)}";
        Assert.Contains("relié", await Refused(await m.PutAsJsonAsync(url, Problem("Clos", closure: "Doublon"))));
        await Json(await m.PostAsJsonAsync("/api/links", new { fromType = "problem", fromId = Id(dup), toReference = Ref(kept) }));
        await Json(await m.PutAsJsonAsync(url, Problem("Clos", closure: "Doublon")));
    }

    [Fact]
    public async Task PRB_17_18_RACI_a_la_modification_et_date_d_achevement()
    {
        var m = api.Client("Manager");
        var p = await Json(await m.PostAsJsonAsync("/api/problems", Problem()));
        var a = await Json(await m.PostAsJsonAsync($"/api/problems/{Id(p)}/actions", Action()));
        var url = $"/api/actions/{Id(a)}";
        Assert.Contains("exactement un Approbateur", await Refused(await m.PutAsJsonAsync(url, Action(approvers: 2))));

        var done = await Json(await m.PutAsJsonAsync(url, Action("Terminée")));
        var completedAt = done.GetProperty("completedAt").GetString();
        Assert.NotNull(completedAt);
        var renamed = await Json(await m.PutAsJsonAsync(url, Action("Terminée")));
        Assert.Equal(Micro(completedAt), Micro(renamed.GetProperty("completedAt").GetString()));   // pas réécrite (#24)
        var reopened = await Json(await m.PutAsJsonAsync(url, Action("En cours")));
        Assert.True(IsNull(reopened, "completedAt"));
    }
}
