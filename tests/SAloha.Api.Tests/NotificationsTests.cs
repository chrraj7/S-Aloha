using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SAloha.Api.Tests;

/// <summary>SOC-21 et SOC-23 : notifications dans l'application.</summary>
[Collection("api")]
public class NotificationsTests(ApiFixture api)
{
    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode} : {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static int Id(JsonElement e) => e.GetProperty("id").GetInt32();
    private static string Ref(JsonElement e) => e.GetProperty("reference").GetString()!;

    /// <summary>Événements reçus pour une référence.</summary>
    private static async Task<List<string>> Events(HttpClient c, string reference) =>
        (await Json(await c.GetAsync("/api/notifications"))).GetProperty("items").EnumerateArray()
            .Where(n => n.GetProperty("reference").GetString() == reference)
            .Select(n => n.GetProperty("event").GetString()!).ToList();

    [Fact]
    public async Task SOC_21_affectation_au_responsable_jamais_a_l_auteur()
    {
        var m = api.Client("Manager", "chef.n21");
        var tech = api.Client("User", "tech.n21");
        var inc = await Json(await m.PostAsJsonAsync("/api/incidents", new
        {
            title = "Imprimante", impact = "Faible", urgency = "Faible", ownerType = "User", ownerId = "Tech.N21", ownerDisplayName = "Tech"
        }));
        Assert.Equal(["Confié à vous"], await Events(tech, Ref(inc)));
        Assert.Empty(await Events(m, Ref(inc)));

        // Lu : le compteur baisse ; un autre compte ne peut pas lire cette notification.
        var list = await Json(await tech.GetAsync("/api/notifications"));
        var n = list.GetProperty("items").EnumerateArray().First(x => x.GetProperty("reference").GetString() == Ref(inc));
        var unread = list.GetProperty("unread").GetInt32();
        Assert.Equal(HttpStatusCode.NotFound, (await m.PostAsync($"/api/notifications/{n.GetProperty("id").GetInt64()}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await tech.PostAsync($"/api/notifications/{n.GetProperty("id").GetInt64()}/read", null)).StatusCode);
        Assert.Equal(unread - 1, (await Json(await tech.GetAsync("/api/notifications"))).GetProperty("unread").GetInt32());

        // Supprimer l'enregistrement retire ses notifications.
        Assert.Equal(HttpStatusCode.NoContent, (await api.Client().DeleteAsync($"/api/incidents/{Id(inc)}")).StatusCode);
        Assert.Empty(await Events(tech, Ref(inc)));
        await using var db = api.Database();
        Assert.False(db.Notifications.Any(x => x.TargetType == "incident" && x.TargetId == Id(inc)));
    }

    [Fact]
    public async Task SOC_21_groupe_et_tout_marquer_lu()
    {
        var m = api.Client("Manager", "chef.n21g");
        var membre = api.Client("User", "membre.n21g", "GRP-RESEAU-N21");
        var inc = await Json(await m.PostAsJsonAsync("/api/incidents", new
        {
            title = "Switch", impact = "Faible", urgency = "Faible", ownerType = "Group", ownerId = "GRP-RESEAU-N21", ownerDisplayName = "Réseau"
        }));
        Assert.Equal(["Confié à votre groupe"], await Events(membre, Ref(inc)));
        Assert.Equal(HttpStatusCode.NoContent, (await membre.PostAsync("/api/notifications/read-all", null)).StatusCode);
        Assert.Equal(0, (await Json(await membre.GetAsync("/api/notifications"))).GetProperty("unread").GetInt32());
    }

    [Fact]
    public async Task SOC_21_demande_soumise_aux_gestionnaires_decision_au_demandeur()
    {
        var demandeur = api.Client("User", "demandeur.n21");
        var m = api.Client("Manager", "approbateur.n21");
        object Req(string? status) => new { title = "Accès ERP", requestedItem = "ERP", status, requestedFor = "beneficiaire.n21" };
        var r = await Json(await demandeur.PostAsJsonAsync("/api/requests", Req(null)));
        Assert.Contains("Demande à approuver", await Events(m, Ref(r)));
        Assert.Empty(await Events(demandeur, Ref(r)));

        await Json(await m.PutAsJsonAsync($"/api/requests/{Id(r)}", Req("Approuvée")));
        Assert.Contains("Demande approuvée", await Events(demandeur, Ref(r)));
        Assert.Contains("Demande approuvée", await Events(api.Client("User", "beneficiaire.n21"), Ref(r)));
    }

    [Fact]
    public async Task SOC_23_rien_pour_un_enregistrement_hors_de_ses_perimetres()
    {
        var admin = api.Client();
        var usine = Id(await Json(await admin.PostAsJsonAsync("/api/admin/perimeters", new { name = "Usine N21" })));
        await Json(await admin.PostAsJsonAsync($"/api/admin/perimeters/{usine}/assignments",
            new { principalType = "User", principalId = "chef.usine.n21", principalDisplayName = "Chef usine", role = "Manager" }));
        var r = await Json(await admin.PostAsJsonAsync("/api/requests", new { title = "Badge usine", requestedItem = "Badge", perimeterId = usine }));
        Assert.Contains("Demande à approuver", await Events(api.Client("User", "chef.usine.n21"), Ref(r)));
        // Gestionnaire AD sans affectation : « Organisation » seulement.
        Assert.Empty(await Events(api.Client("Manager", "chef.siege.n21"), Ref(r)));
    }

    [Fact]
    public async Task SOC_23_les_notifications_suivent_l_enregistrement_change_de_perimetre()
    {
        var admin = api.Client();
        var siege = Id(await Json(await admin.PostAsJsonAsync("/api/admin/perimeters", new { name = "Siège N23" })));
        var usine = Id(await Json(await admin.PostAsJsonAsync("/api/admin/perimeters", new { name = "Usine N23" })));
        async Task Assign(int p, string id, string role) => await Json(await admin.PostAsJsonAsync($"/api/admin/perimeters/{p}/assignments",
            new { principalType = "User", principalId = id, principalDisplayName = id, role }));
        await Assign(siege, "tiana.n23", "User");
        await Assign(siege, "chef.siege.n23", "Manager");
        await Assign(usine, "chef.usine.n23", "Manager");
        object Incident(int p) => new
        {
            title = "Titre confidentiel N23", impact = "Faible", urgency = "Faible", perimeterId = p,
            ownerType = "User", ownerId = "tiana.n23", ownerDisplayName = "Tiana"
        };
        object Request(int p) => new { title = "Badge N23", requestedItem = "Badge", perimeterId = p };
        var inc = await Json(await admin.PostAsJsonAsync("/api/incidents", Incident(siege)));
        var req = await Json(await admin.PostAsJsonAsync("/api/requests", Request(siege)));
        Assert.Equal(["Confié à vous"], await Events(api.Client("User", "tiana.n23"), Ref(inc)));
        Assert.Contains("Demande à approuver", await Events(api.Client("User", "chef.siege.n23"), Ref(req)));

        // PER-07 : l'Admin les déplace à l'Usine ; leurs notifications passent avec eux.
        await Json(await admin.PutAsJsonAsync($"/api/incidents/{Id(inc)}", Incident(usine)));
        await Json(await admin.PutAsJsonAsync($"/api/requests/{Id(req)}", Request(usine)));
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client("User", "tiana.n23").GetAsync($"/api/incidents/{Id(inc)}")).StatusCode);
        Assert.Empty(await Events(api.Client("User", "tiana.n23"), Ref(inc)));
        Assert.Empty(await Events(api.Client("User", "chef.siege.n23"), Ref(req)));
        Assert.Contains("Demande à approuver", await Events(api.Client("User", "chef.usine.n23"), Ref(req)));
    }

    [Fact]
    public async Task PRB_31_action_affectee_au_R_et_au_A()
    {
        var m = api.Client("Manager", "chef.prb31");
        var p = await Json(await m.PostAsJsonAsync("/api/problems", new
        {
            title = "Lenteurs ERP", description = "d", impact = "Moyen", urgency = "Moyenne", category = "c", affectedService = "s"
        }));
        await Json(await m.PostAsJsonAsync($"/api/problems/{Id(p)}/actions", new
        {
            title = "Indexer la base", description = "", dueDate = (DateTime?)null,
            raci = new[]
            {
                new { role = "R", assigneeType = "User", assigneeId = "dba.prb31", assigneeDisplayName = "DBA" },
                new { role = "A", assigneeType = "Group", assigneeId = "GRP-DBA-PRB31", assigneeDisplayName = "DBA" },
                new { role = "I", assigneeType = "User", assigneeId = "info.prb31", assigneeDisplayName = "Info" }
            }
        }));
        Assert.Equal(["Action corrective affectée : Indexer la base"], await Events(api.Client("User", "dba.prb31"), Ref(p)));
        Assert.Single(await Events(api.Client("User", "membre.prb31", "GRP-DBA-PRB31"), Ref(p)));
        Assert.Empty(await Events(api.Client("User", "info.prb31"), Ref(p)));
    }
}
