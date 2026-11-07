using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

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
    public async Task PER_13_referentiels_sans_perimetre_geres_par_un_gestionnaire_d_au_moins_un_perimetre()
    {
        var (siege, _, lova, _, group) = await Setup("per13");
        var admin = api.Client();
        await Json(await admin.PostAsJsonAsync($"/api/admin/perimeters/{siege}/assignments",
            new { principalType = "User", principalId = "rado.per13", principalDisplayName = "Rado", role = "User" }));
        // Corps vides : une écriture autorisée échoue à la validation (400), jamais à l'autorisation.
        var writes = new (string Method, string Url)[]
        {
            ("POST", "/api/change-freezes"), ("POST", "/api/standard-changes"), ("PUT", "/api/standard-changes/0"),
            ("POST", "/api/standard-changes/0/review"), ("POST", "/api/request-catalog"), ("POST", "/api/discovery/end-of-life"),
            ("POST", "/api/categories"), ("DELETE", "/api/categories/0"),
        };
        async Task<HttpStatusCode[]> Try(HttpClient c) => await Task.WhenAll(writes.Select(async w =>
            (await c.SendAsync(new HttpRequestMessage(new HttpMethod(w.Method), w.Url) { Content = JsonContent.Create(new { }) })).StatusCode));

        // Rado : groupe AD Manager, mais seulement User du Siège (D4) — le rôle AD ne suffit pas.
        Assert.All(await Try(api.Client("Manager", "rado.per13")), s => Assert.Equal(HttpStatusCode.Forbidden, s));
        Assert.All(await Try(api.Client("User", "anonyme.per13")), s => Assert.Equal(HttpStatusCode.Forbidden, s));
        // Gestionnaire d'un périmètre, directement ou par son groupe, quel que soit son rôle AD ; Manager AD sans affectation (PER-10) ; Admin.
        foreach (var c in new[] { api.Client("User", lova), api.Client("User", "membre.per13", group), api.Client("Manager", "chef.per13"), admin })
            Assert.All(await Try(c), s => Assert.NotEqual(HttpStatusCode.Forbidden, s));
    }

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
    public async Task PER_07_INC_16_un_enfant_rattache_ne_change_pas_de_perimetre_avant_d_etre_detache()
    {
        var (siege, usine, _, tiana, _) = await Setup("per07e");
        var t = api.Client("User", tiana);
        var admin = api.Client();
        object Inc(string title, int p) => new
        {
            title, impact = "Faible", urgency = "Faible", perimeterId = p, ownerType = "User", ownerId = tiana, ownerDisplayName = tiana
        };
        var parent = await Json(await t.PostAsJsonAsync("/api/incidents", Inc("Coupure réseau Siège", siege)));
        var child = await Json(await t.PostAsJsonAsync("/api/incidents", Inc("Poste injoignable", siege)));
        await Json(await t.PutAsJsonAsync($"/api/incidents/{Id(child)}/parent", new { parentReference = Ref(parent) }));

        // Rattaché à un parent du Siège : le déplacement vers l'Usine est refusé, rien ne change.
        var refused = await admin.PutAsJsonAsync($"/api/incidents/{Id(child)}", Inc("Poste injoignable", usine));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains(Ref(parent), await refused.Content.ReadAsStringAsync());
        var kept = await Json(await admin.GetAsync($"/api/incidents/{Id(child)}"));
        Assert.Equal((siege, Id(parent)), (kept.GetProperty("perimeterId").GetInt32(), kept.GetProperty("parentId").GetInt32()));

        // Une modification sans changement de périmètre reste permise.
        await Json(await admin.PutAsJsonAsync($"/api/incidents/{Id(child)}", Inc("Poste injoignable (2e étage)", siege)));

        // Détaché, il se déplace.
        await Json(await t.PutAsJsonAsync($"/api/incidents/{Id(child)}/parent", new { parentReference = "" }));
        var moved = await Json(await admin.PutAsJsonAsync($"/api/incidents/{Id(child)}", Inc("Poste injoignable", usine)));
        Assert.Equal(usine, moved.GetProperty("perimeterId").GetInt32());
    }

    [Fact]
    public async Task PER_07_INC_16_la_resolution_du_parent_ne_touche_pas_un_enfant_d_un_autre_perimetre()
    {
        var (siege, usine, _, tiana, _) = await Setup("per07c");
        var t = api.Client("User", tiana);
        object Inc(string title, int p, string? status = null) => new
        {
            title, impact = "Faible", urgency = "Faible", perimeterId = p, status,
            ownerType = "User", ownerId = tiana, ownerDisplayName = tiana,
            resolution = status == "Résolu" ? "Commutateur remplacé" : null, resolutionCode = status == "Résolu" ? "Correctif appliqué" : null
        };
        var parent = await Json(await t.PostAsJsonAsync("/api/incidents", Inc("Coupure réseau Siège", siege)));
        var child = await Json(await t.PostAsJsonAsync("/api/incidents", Inc("Poste injoignable", siege)));
        await Json(await t.PutAsJsonAsync($"/api/incidents/{Id(child)}/parent", new { parentReference = Ref(parent) }));

        // Lien hérité d'avant PER-07 (un enfant rattaché ne change plus de périmètre) : l'enfant est à l'Usine.
        await using (var db = api.Database())
            await db.Incidents.Where(x => x.Id == Id(child)).ExecuteUpdateAsync(u => u.SetProperty(x => x.PerimeterId, usine));
        Assert.Equal(HttpStatusCode.NotFound, (await t.GetAsync($"/api/incidents/{Id(child)}")).StatusCode);

        await Json(await t.PutAsJsonAsync($"/api/incidents/{Id(parent)}", Inc("Coupure réseau Siège", siege, "En cours")));
        var resolved = await Json(await t.PutAsJsonAsync($"/api/incidents/{Id(parent)}", Inc("Coupure réseau Siège", siege, "Résolu")));
        Assert.Equal("Résolu", resolved.GetProperty("status").GetString());

        // La cascade (INC-16) ne franchit pas le périmètre : l'enfant de l'Usine reste ouvert, intact.
        var c = await Json(await api.Client().GetAsync($"/api/incidents/{Id(child)}"));
        Assert.Equal("Nouveau", c.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, c.GetProperty("resolution").ValueKind);
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
