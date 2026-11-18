using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SAloha.Api.Tests;

/// <summary>
/// Demande modifiée après l'approbation (décision du 2026-11-09, #257) : retour à Soumise
/// (REQ-04), demande Satisfaite figée, bénéficiaire changé par le demandeur ou un gestionnaire.
/// </summary>
[Collection("api")]
public class RequestReworkTests(ApiFixture api)
{
    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode} : {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static int Id(JsonElement e) => e.GetProperty("id").GetInt32();
    private static string? Str(JsonElement e, string p) => e.GetProperty(p).GetString();

    private static object Request(string item, string? status = null, string? requestedFor = null, string? contestReason = null) => new
    {
        title = "Matériel", requestedItem = item, status, requestedFor, requestedForDisplayName = requestedFor,
        ownerType = "User", ownerId = "tech.req04r", ownerDisplayName = "Tech", contestReason
    };

    private HttpClient Manager => api.Client("Manager", "chef.req04r");

    private async Task<string> Approved(HttpClient requester, string item = "Souris")
    {
        var r = await Json(await requester.PostAsJsonAsync("/api/requests", Request(item)));
        var url = $"/api/requests/{Id(r)}";
        await Json(await Manager.PutAsJsonAsync(url, Request(item, "Approuvée")));
        return url;
    }

    [Fact]
    public async Task REQ_04_objet_change_apres_l_approbation_ramene_la_demande_a_soumise()
    {
        var requester = api.Client("User", "demandeur.req04r");
        // Approuvée : changer l'objet efface l'approbation.
        var url = await Approved(requester);
        var back = await Json(await requester.PutAsJsonAsync(url, Request("Serveur de calcul GPU", "En cours")));
        Assert.Equal("Soumise", Str(back, "status"));
        Assert.Equal(JsonValueKind.Null, back.GetProperty("approvedBy").ValueKind);
        Assert.Equal(HttpStatusCode.BadRequest, (await requester.PutAsJsonAsync(url, Request("Serveur de calcul GPU", "En cours"))).StatusCode);
        // Témoin : le titre seul ne touche pas à l'approbation.
        var other = await Approved(requester);
        var kept = await Json(await requester.PutAsJsonAsync(other, new
        {
            title = "Matériel (précisé)", requestedItem = "Souris", ownerType = "User", ownerId = "tech.req04r", ownerDisplayName = "Tech"
        }));
        Assert.Equal("Approuvée", Str(kept, "status"));
        // En cours : même retour, y compris par un gestionnaire.
        await Json(await requester.PutAsJsonAsync(other, Request("Souris", "En cours")));
        Assert.Equal("Soumise", Str(await Json(await Manager.PutAsJsonAsync(other, Request("Clavier", "En cours"))), "status"));
    }

    [Fact]
    public async Task REQ_10_le_beneficiaire_ne_change_que_par_le_demandeur_ou_un_gestionnaire()
    {
        var requester = api.Client("User", "demandeur.req10r");
        var url = await Approved(requester);
        var tiers = api.Client("User", "tiers.req10r");
        var refused = await tiers.PutAsJsonAsync(url, Request("Souris", requestedFor: "tiers.req10r"));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("bénéficiaire", await refused.Content.ReadAsStringAsync());
        // Le demandeur le change : la demande repart à l'approbation.
        var moved = await Json(await requester.PutAsJsonAsync(url, Request("Souris", requestedFor: "collegue.req10r")));
        Assert.Equal(("collegue.req10r", "Soumise"), (Str(moved, "requestedFor"), Str(moved, "status")));
    }

    [Fact]
    public async Task Demande_satisfaite_objet_et_beneficiaire_figes()
    {
        var requester = api.Client("User", "demandeur.satr");
        var url = await Approved(requester);
        await Json(await Manager.PutAsJsonAsync(url, Request("Souris", "En cours")));
        await Json(await Manager.PutAsJsonAsync(url, Request("Souris", "Satisfaite")));
        // Le détour de l'issue : un tiers se fait bénéficiaire pour contester.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await api.Client("User", "tiers.satr").PutAsJsonAsync(url, Request("Souris", requestedFor: "tiers.satr"))).StatusCode);
        // Ni le demandeur ni un gestionnaire ne changent l'objet d'une demande satisfaite.
        Assert.Equal(HttpStatusCode.BadRequest, (await requester.PutAsJsonAsync(url, Request("Clavier"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Manager.PutAsJsonAsync(url, Request("Souris", requestedFor: "collegue.satr"))).StatusCode);
        // La contestation, sans rien changer d'autre, reste possible (REQ-10).
        var contested = await Json(await requester.PutAsJsonAsync(url, Request("Souris", "En cours", contestReason: "Souris sans fil attendue")));
        Assert.Equal("En cours", Str(contested, "status"));
    }

    /// <summary>
    /// Corps de la fiche : les champs du formulaire et <c>serviceId</c>, jamais <c>affectedService</c>.
    /// </summary>
    private static object Form(string? status = null, string? contestReason = null, int? serviceId = null) => new
    {
        title = "Matériel", requestedItem = "Souris", status, serviceId,
        ownerType = "User", ownerId = "tech.req04r", ownerDisplayName = "Tech", contestReason
    };

    [Fact]
    public async Task REQ_04_la_fiche_enregistree_sans_rien_toucher_garde_un_service_hors_catalogue()
    {
        const string label = "Outil maison hors catalogue";
        var requester = api.Client("User", "demandeur.req04s");
        var r = await Json(await requester.PostAsJsonAsync("/api/requests", new { title = "Matériel", requestedItem = "Souris", affectedService = label }));
        var url = $"/api/requests/{Id(r)}";
        await Json(await Manager.PutAsJsonAsync(url, Form("Approuvée")));
        // La fiche ouverte puis enregistrée telle quelle : ni service effacé, ni approbation perdue.
        var saved = await Json(await requester.PutAsJsonAsync(url, Form()));
        Assert.Equal((label, "Approuvée"), (Str(saved, "affectedService"), Str(saved, "status")));
        await Json(await Manager.PutAsJsonAsync(url, Form("En cours")));
        await Json(await Manager.PutAsJsonAsync(url, Form("Satisfaite")));
        // REQ-10 : la contestation par la fiche reste possible.
        var contested = await Json(await requester.PutAsJsonAsync(url, Form("En cours", "Souris sans fil attendue")));
        Assert.Equal((label, "En cours"), (Str(contested, "affectedService"), Str(contested, "status")));
        // Cas voisin : un libellé vide envoyé exprès retire le service, et c'est une modification (REQ-04).
        var cleared = await Json(await requester.PutAsJsonAsync(url, new
        {
            title = "Matériel", requestedItem = "Souris", affectedService = "", ownerType = "User", ownerId = "tech.req04r", ownerDisplayName = "Tech"
        }));
        Assert.Equal(("", "Soumise"), (Str(cleared, "affectedService"), Str(cleared, "status")));
    }

    [Fact]
    public async Task REQ_04_la_fiche_retire_encore_un_service_du_catalogue()
    {
        var svc = await Json(await api.Client().PostAsJsonAsync("/api/services", new
        {
            title = "Poste de travail req04s", status = "En service", criticality = "Moyenne", ownerType = "User", ownerId = "o", ownerDisplayName = "O"
        }));
        var requester = api.Client("User", "demandeur.req04t");
        var r = await Json(await requester.PostAsJsonAsync("/api/requests", Form(serviceId: Id(svc))));
        Assert.Equal("Poste de travail req04s", Str(r, "affectedService"));
        // « Aucun service » choisi dans la fiche : le libellé recopié du catalogue ne le rattache pas de nouveau.
        var removed = await Json(await requester.PutAsJsonAsync($"/api/requests/{Id(r)}", Form()));
        Assert.Equal(JsonValueKind.Null, removed.GetProperty("serviceId").ValueKind);
        Assert.Equal("", Str(removed, "affectedService"));
    }
}
