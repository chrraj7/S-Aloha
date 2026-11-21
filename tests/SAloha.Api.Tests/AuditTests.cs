using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SAloha.Api.Tests;

/// <summary>Journal d'audit (SOC-20) et transition forcée par un Admin (SOC-05).</summary>
[Collection("api")]
public class AuditTests(ApiFixture api)
{
    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode} : {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static int Id(JsonElement e) => e.GetProperty("id").GetInt32();
    private static string? S(JsonElement e, string p) =>
        e.TryGetProperty(p, out var v) && v.ValueKind != JsonValueKind.Null ? v.GetString() : null;

    private static async Task<JsonElement[]> History(HttpClient c, string type, int id) =>
        (await Json(await c.GetAsync($"/api/audit?type={type}&id={id}"))).EnumerateArray().ToArray();

    private static object Incident(string title, string impact = "Moyen", string? status = null) => new
    {
        title, impact, urgency = "Moyenne", status, resolution = "Redémarrage", resolutionCode = "Correctif appliqué",
        ownerType = "User", ownerId = "hery.rakoto", ownerDisplayName = "Hery Rakoto"
    };

    [Fact]
    public async Task Creation_champs_modifies_et_transition_sont_traces_avec_leur_auteur()
    {
        var c = api.Client("User", "fara.rasoa");
        var i = await Json(await c.PostAsJsonAsync("/api/incidents", Incident("Audit avant")));
        await Json(await c.PutAsJsonAsync($"/api/incidents/{Id(i)}", Incident("Audit après", "Élevé")));
        await Json(await c.PutAsJsonAsync($"/api/incidents/{Id(i)}", Incident("Audit après", "Élevé", "Résolu")));

        var h = await History(c, "incident", Id(i));
        Assert.All(h, e => Assert.Equal("fara.rasoa", S(e, "author")));
        Assert.Equal("Création", S(h.Last(), "action"));
        Assert.Equal("Nouveau", S(h.Last(), "newValue"));

        var title = h.Single(e => S(e, "field") == "title");
        Assert.Equal(("Modification", "Audit avant", "Audit après"), (S(title, "action"), S(title, "oldValue"), S(title, "newValue")));
        Assert.Contains(h, e => S(e, "field") == "impact" && S(e, "oldValue") == "Moyen" && S(e, "newValue") == "Élevé");
        Assert.Contains(h, e => S(e, "field") == "priority");   // priorité recalculée : tracée (SOC-17)

        var transition = h.First();
        Assert.Equal(("Transition", "status", "Nouveau", "Résolu"),
            (S(transition, "action"), S(transition, "field"), S(transition, "oldValue"), S(transition, "newValue")));
        // Les effets automatiques (ResolvedAt, UpdatedAt) ne sont pas des modifications.
        Assert.DoesNotContain(h, e => S(e, "field") is "resolvedAt" or "updatedAt");
    }

    [Fact]
    public async Task Un_lien_est_trace_des_deux_cotes_a_l_ajout_et_au_retrait()
    {
        var c = api.Client();
        var i = await Json(await c.PostAsJsonAsync("/api/incidents", Incident("Audit lien")));
        var ci = await Json(await c.PostAsJsonAsync("/api/configuration-items",
            new { title = "audit-srv", ciType = "Serveur", environment = "Production", ownerType = "User", ownerId = "hery.rakoto", ownerDisplayName = "Hery Rakoto" }));
        var link = await Json(await c.PostAsJsonAsync("/api/links",
            new { fromType = "incident", fromId = Id(i), toReference = S(ci, "reference") }));
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/links/{link.GetProperty("linkId").GetInt32()}")).StatusCode);

        var onIncident = await History(c, "incident", Id(i));
        Assert.Contains(onIncident, e => S(e, "action") == "Lien ajouté" && S(e, "newValue") == S(ci, "reference"));
        Assert.Contains(onIncident, e => S(e, "action") == "Lien retiré" && S(e, "oldValue") == S(ci, "reference"));
        var onCi = await History(c, "ci", Id(ci));
        Assert.Contains(onCi, e => S(e, "action") == "Lien ajouté" && S(e, "newValue") == S(i, "reference"));
    }

    [Fact]
    public async Task Une_suppression_reste_au_journal()
    {
        var c = api.Client();
        var i = await Json(await c.PostAsJsonAsync("/api/incidents", Incident("Audit suppression")));
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/incidents/{Id(i)}")).StatusCode);
        var h = await History(c, "incident", Id(i));
        Assert.Equal("Suppression", S(h.First(), "action"));
        Assert.Equal(S(i, "reference"), S(h.First(), "reference"));
    }

    [Fact]
    public async Task Action_corrective_tracee_sur_le_probleme()
    {
        var m = api.Client("Manager");
        var p = await Json(await m.PostAsJsonAsync("/api/problems", new
        {
            title = "Audit PRB", description = "d", impact = "Moyen", urgency = "Moyenne", category = "c", affectedService = "s", source = "Proactif"
        }));
        await Json(await m.PostAsJsonAsync($"/api/problems/{Id(p)}/actions", new
        {
            title = "Corriger la configuration", description = "", dueDate = (DateTime?)null,
            raci = new[]
            {
                new { role = "R", assigneeType = "User", assigneeId = "fara.rasoa", assigneeDisplayName = "Fara" },
                new { role = "A", assigneeType = "User", assigneeId = "lova.rabe", assigneeDisplayName = "Lova" }
            }
        }));
        var h = await History(m, "problem", Id(p));
        Assert.Contains(h, e => S(e, "action") == "Création" && S(e, "field") == "action « Corriger la configuration »"
                                && S(e, "reference") == S(p, "reference"));
    }

    [Fact]
    public async Task SOC_20_reaffectation_RACI_d_une_action_tracee_sur_le_probleme()
    {
        var m = api.Client("Manager");
        var p = await Json(await m.PostAsJsonAsync("/api/problems", new
        {
            title = "Audit RACI", description = "d", impact = "Moyen", urgency = "Moyenne", category = "c", affectedService = "s", source = "Proactif"
        }));
        object Action(string approverId, string approverName) => new
        {
            title = "Purger les journaux", description = "", status = (string?)null, dueDate = (DateTime?)null,
            raci = new[]
            {
                new { role = "R", assigneeType = "User", assigneeId = "fara.rasoa", assigneeDisplayName = "Fara Rasoa" },
                new { role = "A", assigneeType = "User", assigneeId = approverId, assigneeDisplayName = approverName }
            }
        };
        var a = await Json(await m.PostAsJsonAsync($"/api/problems/{Id(p)}/actions", Action("lova.rabe", "Lova Rabe")));
        Assert.DoesNotContain(await History(m, "problem", Id(p)), e => S(e, "field")?.EndsWith("RACI") == true);

        // R5 : la réaffectation garde qui, quand et l'ancienne valeur.
        await Json(await m.PutAsJsonAsync($"/api/actions/{Id(a)}", Action("hery.rakoto", "Hery Rakoto")));
        var raci = (await History(m, "problem", Id(p))).Where(e => S(e, "field") == "action « Purger les journaux » · RACI").ToArray();
        var entry = Assert.Single(raci);
        Assert.Equal("Modification", S(entry, "action"));
        Assert.Equal("R Fara Rasoa, A Lova Rabe", S(entry, "oldValue"));
        Assert.Equal("R Fara Rasoa, A Hery Rakoto", S(entry, "newValue"));
        Assert.False(string.IsNullOrEmpty(S(entry, "author")));

        // Le formulaire renvoie la RACI entière à chaque enregistrement : inchangée, elle n'est pas tracée.
        await Json(await m.PutAsJsonAsync($"/api/actions/{Id(a)}", Action("hery.rakoto", "Hery Rakoto")));
        Assert.Single(await History(m, "problem", Id(p)), e => S(e, "field") == "action « Purger les journaux » · RACI");
    }

    [Fact]
    public async Task Transition_forcee_par_un_Admin_avec_motif_trace()
    {
        var admin = api.Client("Admin");
        var i = await Json(await admin.PostAsJsonAsync("/api/incidents", Incident("Audit forcé")));
        foreach (var status in new[] { "Résolu", "Clos" })
            await Json(await admin.PutAsJsonAsync($"/api/incidents/{Id(i)}", Incident("Audit forcé", status: status)));
        var url = $"/api/incidents/{Id(i)}";
        var reopen = Incident("Audit forcé", status: "En cours");

        // Clos est final : sans forçage, refus ; un Manager ne peut pas forcer ; un Admin doit motiver.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(url, reopen)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.Client("Manager").PutAsJsonAsync(url + "?force=true&reason=x", reopen)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(url + "?force=true&reason=", reopen)).StatusCode);

        var forced = await Json(await admin.PutAsJsonAsync(url + "?force=true&reason=" + Uri.EscapeDataString("Clôture par erreur"), reopen));
        Assert.Equal("En cours", S(forced, "status"));
        var entry = (await History(admin, "incident", Id(i))).First();
        Assert.Equal(("Transition forcée", "Clos", "En cours", "Clôture par erreur"),
            (S(entry, "action"), S(entry, "oldValue"), S(entry, "newValue"), S(entry, "reason")));
    }

    [Fact]
    public async Task Transition_forcee_aussi_pour_un_probleme()
    {
        var admin = api.Client("Admin");
        object Dto(string? status) => new
        {
            title = "Audit PRB forcé", description = "d", status, impact = "Moyen", urgency = "Moyenne",
            category = "c", affectedService = "s", knownErrorWorkaround = (string?)null, rootCause = "Cause établie",
            source = "Proactif"
        };
        var p = await Json(await admin.PostAsJsonAsync("/api/problems", Dto(null)));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/problems/{Id(p)}", Dto("Résolu"))).StatusCode);
        await Json(await admin.PutAsJsonAsync($"/api/problems/{Id(p)}?force=true&reason=Reprise", Dto("Résolu")));
        var entry = (await History(admin, "problem", Id(p))).First();
        Assert.Equal(("Transition forcée", "Reprise"), (S(entry, "action"), S(entry, "reason")));
    }
}
