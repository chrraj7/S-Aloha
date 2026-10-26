using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SAloha.Api.Tests;

/// <summary>Étape 6 du lot 1, droits par périmètre (PER-03 à PER-07).</summary>
[Collection("api")]
public class PerimeterRightsTests(ApiFixture api)
{
    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode} : {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static int Id(JsonElement e) => e.GetProperty("id").GetInt32();
    private static string Ref(JsonElement e) => e.GetProperty("reference").GetString()!;
    private static IEnumerable<string> Refs(JsonElement list) => list.EnumerateArray().Select(Ref);

    /// <summary>Siège et Usine ; Lova Manager du Siège et User de l'Usine ; Tiana User du Siège ; le groupe GRP-USINE Manager de l'Usine.</summary>
    private async Task<(int Siege, int Usine, string Lova, string Tiana, string Group)> Setup(string tag)
    {
        var admin = api.Client();
        var siege = Id(await Json(await admin.PostAsJsonAsync("/api/admin/perimeters", new { name = $"Siège {tag}" })));
        var usine = Id(await Json(await admin.PostAsJsonAsync("/api/admin/perimeters", new { name = $"Usine {tag}" })));
        string lova = $"lova.{tag}", tiana = $"tiana.{tag}", group = $"GRP-USINE-{tag}";
        async Task Assign(int p, string type, string id, string role) =>
            await Json(await admin.PostAsJsonAsync($"/api/admin/perimeters/{p}/assignments",
                new { principalType = type, principalId = id, principalDisplayName = id, role }));
        await Assign(siege, "User", lova, "Manager");
        await Assign(usine, "User", lova, "User");
        await Assign(siege, "User", tiana, "User");
        await Assign(usine, "Group", group, "Manager");
        return (siege, usine, lova, tiana, group);
    }

    private static object Change(string title, int perimeter, string? status = null) => new
    {
        title, changeType = "Normal", risk = "Faible", status, perimeterId = perimeter,
        plannedStart = DateTime.UtcNow.AddDays(3), plannedEnd = DateTime.UtcNow.AddDays(3).AddHours(2),
        implementationPlan = "Plan", backoutPlan = "Retour arrière"
    };

    [Fact]
    public async Task PER_06_decisions_de_gestionnaire_limitees_au_perimetre()
    {
        var (siege, usine, lova, _, group) = await Setup("per06");
        var admin = api.Client();
        var cs = await Json(await admin.PostAsJsonAsync("/api/changes", Change("Change Siège", siege)));
        var cu = await Json(await admin.PostAsJsonAsync("/api/changes", Change("Change Usine", usine)));
        await ChangeTestData.LinkCiAsync(admin, Id(cs));   // CHG-16
        await ChangeTestData.LinkCiAsync(admin, Id(cu));
        await Json(await admin.PutAsJsonAsync($"/api/changes/{Id(cs)}", Change("Change Siège", siege, "Évalué")));
        await Json(await admin.PutAsJsonAsync($"/api/changes/{Id(cu)}", Change("Change Usine", usine, "Évalué")));

        // Lova : Manager du Siège, User de l'Usine — son rôle AD (User) ne compte pas (D4).
        var l = api.Client("User", lova);
        Assert.Equal(HttpStatusCode.Forbidden, (await l.PutAsJsonAsync($"/api/changes/{Id(cu)}", Change("Change Usine", usine, "Autorisé"))).StatusCode);
        Assert.Equal("Autorisé", (await Json(await l.PutAsJsonAsync($"/api/changes/{Id(cs)}", Change("Change Siège", siege, "Autorisé"))))
            .GetProperty("status").GetString());

        // Un Manager AD sans affectation reste sur « Organisation » : rien à l'Usine.
        var chef = api.Client("Manager", "chef.per06");
        Assert.Equal(HttpStatusCode.NotFound, (await chef.GetAsync($"/api/changes/{Id(cu)}")).StatusCode);

        // Le groupe GRP-USINE est Manager de l'Usine (SOC-10).
        var membre = api.Client("User", "membre.per06", group);
        Assert.Equal("Autorisé", (await Json(await membre.PutAsJsonAsync($"/api/changes/{Id(cu)}", Change("Change Usine", usine, "Autorisé"))))
            .GetProperty("status").GetString());
    }

    [Fact]
    public async Task PER_04_PER_05_creation_et_visibilite_par_perimetre()
    {
        var (siege, usine, lova, tiana, _) = await Setup("per05");
        var t = api.Client("User", tiana);
        var l = api.Client("User", lova);

        // Tiana n'a que le Siège : il est proposé d'office ; l'Usine lui est fermée.
        var mine = await Json(await t.PostAsJsonAsync("/api/incidents", new { title = "Imprimante Siège", impact = "Faible", urgency = "Faible" }));
        Assert.Equal(siege, mine.GetProperty("perimeterId").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, (await t.PostAsJsonAsync("/api/incidents",
            new { title = "Hors périmètre", impact = "Faible", urgency = "Faible", perimeterId = usine })).StatusCode);
        // Lova a deux périmètres : elle choisit.
        Assert.Equal(HttpStatusCode.BadRequest, (await l.PostAsJsonAsync("/api/incidents",
            new { title = "Sans périmètre", impact = "Faible", urgency = "Faible" })).StatusCode);
        var usineInc = await Json(await l.PostAsJsonAsync("/api/incidents",
            new { title = "Presse Usine", impact = "Faible", urgency = "Faible", perimeterId = usine }));

        // Visibilité : Tiana ne voit pas l'incident de l'Usine.
        var list = await Json(await t.GetAsync("/api/incidents"));
        Assert.Contains(Ref(mine), Refs(list));
        Assert.DoesNotContain(Ref(usineInc), Refs(list));
        Assert.Equal(HttpStatusCode.NotFound, (await t.GetAsync($"/api/incidents/{Id(usineInc)}")).StatusCode);

        // Problèmes : même règle.
        var pu = await Json(await l.PostAsJsonAsync("/api/problems", new
        {
            title = "Problème Usine", description = "d", impact = "Faible", urgency = "Faible",
            category = "c", affectedService = "s", perimeterId = usine
        }));
        Assert.DoesNotContain(Ref(pu), Refs(await Json(await t.GetAsync("/api/problems"))));
        Assert.Equal(HttpStatusCode.NotFound, (await t.GetAsync($"/api/problems/{Id(pu)}")).StatusCode);
        // Lova est User de l'Usine : elle ne qualifie pas ce problème.
        Assert.Equal(HttpStatusCode.Forbidden, (await l.PutAsJsonAsync($"/api/problems/{Id(pu)}", new
        {
            title = "Problème Usine", description = "d", status = "En analyse", impact = "Faible", urgency = "Faible",
            category = "c", affectedService = "s"
        })).StatusCode);

        // Référentiels partagés (D2) : services et articles publiés de l'Usine lus de tous.
        var admin = api.Client();
        var svc = await Json(await admin.PostAsJsonAsync("/api/services", new
        {
            title = "Supervision Usine per05", status = "En conception", criticality = "Moyenne", serviceHours = "24/7", perimeterId = usine
        }));
        Assert.Equal(HttpStatusCode.OK, (await t.GetAsync($"/api/services/{Id(svc)}")).StatusCode);
        var article = await Json(await admin.PostAsJsonAsync("/api/knowledge", new
        {
            title = "Procédure Usine", articleType = "Procédure", content = "Étapes", perimeterId = usine
        }));
        Assert.Equal(HttpStatusCode.NotFound, (await t.GetAsync($"/api/knowledge/{Id(article)}")).StatusCode);
        await Json(await admin.PutAsJsonAsync($"/api/knowledge/{Id(article)}", new
        {
            title = "Procédure Usine", articleType = "Procédure", content = "Étapes", status = "Publié"
        }));
        Assert.Contains(Ref(article), Refs(await Json(await t.GetAsync("/api/knowledge"))));
        // Lu, mais pas modifiable hors de son périmètre.
        Assert.Equal(HttpStatusCode.Forbidden, (await t.PutAsJsonAsync($"/api/knowledge/{Id(article)}", new
        {
            title = "Procédure Usine retouchée", articleType = "Procédure", content = "Étapes"
        })).StatusCode);
    }

    [Fact]
    public async Task PER_07_changer_de_perimetre_exige_d_etre_gestionnaire_des_deux()
    {
        var (siege, usine, lova, _, _) = await Setup("per07");
        var l = api.Client("User", lova);
        var inc = await Json(await l.PostAsJsonAsync("/api/incidents",
            new { title = "À déplacer", impact = "Faible", urgency = "Faible", perimeterId = siege }));
        object Move(int p) => new { title = "À déplacer", impact = "Faible", urgency = "Faible", perimeterId = p };

        // Manager du Siège mais User de l'Usine : refusé.
        Assert.Equal(HttpStatusCode.Forbidden, (await l.PutAsJsonAsync($"/api/incidents/{Id(inc)}", Move(usine))).StatusCode);
        // Admin : permis, et tracé au journal.
        var moved = await Json(await api.Client().PutAsJsonAsync($"/api/incidents/{Id(inc)}", Move(usine)));
        Assert.Equal(usine, moved.GetProperty("perimeterId").GetInt32());
        var journal = await Json(await api.Client().GetAsync($"/api/audit?type=incident&id={Id(inc)}"));
        Assert.Contains(journal.EnumerateArray(), e => e.GetProperty("field").GetString() == "perimeterId"
            && e.GetProperty("oldValue").GetString() == siege.ToString() && e.GetProperty("newValue").GetString() == usine.ToString());
    }

    [Fact]
    public async Task PER_05_historique_soumis_a_la_visibilite_de_la_fiche()
    {
        var (siege, usine, lova, tiana, _) = await Setup("per05h");
        var t = api.Client("User", tiana);
        var l = api.Client("User", lova);
        var inc = await Json(await l.PostAsJsonAsync("/api/incidents",
            new { title = "Presse Usine", impact = "Faible", urgency = "Faible", perimeterId = usine }));
        Assert.Equal(HttpStatusCode.NotFound, (await t.GetAsync($"/api/incidents/{Id(inc)}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await t.GetAsync($"/api/audit?type=incident&id={Id(inc)}")).StatusCode);
        Assert.NotEmpty((await Json(await l.GetAsync($"/api/audit?type=incident&id={Id(inc)}"))).EnumerateArray());

        // Référentiel partagé (D2) : l'historique suit la fiche, lisible de tous.
        var svc = await Json(await api.Client().PostAsJsonAsync("/api/services", new
        {
            title = "Supervision Usine per05h", status = "En conception", criticality = "Moyenne", serviceHours = "24/7", perimeterId = usine
        }));
        Assert.Equal(HttpStatusCode.OK, (await t.GetAsync($"/api/audit?type=service&id={Id(svc)}")).StatusCode);

        // Types hors enregistrements (périmètres, paramètres) : Admin seulement.
        Assert.Equal(HttpStatusCode.NotFound, (await t.GetAsync($"/api/audit?type=perimeter&id={siege}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client("Manager", "chef.per05h").GetAsync($"/api/audit?type=perimeter&id={siege}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.Client().GetAsync($"/api/audit?type=perimeter&id={siege}")).StatusCode);
    }

    [Fact]
    public async Task PER_10_un_compte_sans_affectation_garde_Organisation()
    {
        await Setup("per10");   // des affectations existent pour d'autres comptes
        var u = api.Client("User", "sans.affectation.per10");
        var inc = await Json(await u.PostAsJsonAsync("/api/incidents", new { title = "PER-10", impact = "Faible", urgency = "Faible" }));
        Assert.Equal(1, inc.GetProperty("perimeterId").GetInt32());
    }
}
