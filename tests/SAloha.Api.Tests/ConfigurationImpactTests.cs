using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace SAloha.Api.Tests;

/// <summary>Configuration : services touchés (CFG-21), impact d'un changement (CFG-20), notifications (CFG-22), vérification (CFG-15).</summary>
[Collection("api")]
public class ConfigurationImpactTests(ApiFixture api)
{
    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode} : {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static int Id(JsonElement e) => e.GetProperty("id").GetInt32();
    private static string Ref(JsonElement e) => e.GetProperty("reference").GetString()!;
    private static List<string> Refs(JsonElement list) => list.EnumerateArray().Select(Ref).ToList();

    /// <summary>Serveur ← application (« Dépend de ») ; l'application supporte un service.</summary>
    private async Task<(JsonElement Srv, JsonElement App, JsonElement Svc, HttpClient M)> Setup(string tag)
    {
        var m = api.Client("Manager", $"chef.{tag}");
        object Ci(string title) => new
        {
            title = $"{title}-{tag}", ciType = "Serveur", environment = "Production", status = "En service",
            ownerType = "User", ownerId = $"owner.{tag}", ownerDisplayName = "Owner"
        };
        var srv = await Json(await m.PostAsJsonAsync("/api/configuration-items", Ci("srv")));
        var app = await Json(await m.PostAsJsonAsync("/api/configuration-items", Ci("app")));
        await Json(await m.PostAsJsonAsync($"/api/configuration-items/{Id(app)}/relations", new { type = "Dépend de", targetReference = Ref(srv) }));
        var svc = await Json(await m.PostAsJsonAsync("/api/services", new
        {
            title = $"Paie {tag}", status = "En service", criticality = "Élevée", serviceHours = "24/7",
            ownerType = "User", ownerId = $"svcowner.{tag}", ownerDisplayName = "Responsable du service"
        }));
        await Json(await m.PostAsJsonAsync("/api/links", new { fromType = "ci", fromId = Id(app), toReference = Ref(svc) }));
        return (srv, app, svc, m);
    }

    [Fact]
    public async Task CFG_21_CFG_20_services_touches_et_impact_d_un_changement()
    {
        var (srv, app, svc, m) = await Setup("cfg21");
        var impact = await Json(await m.GetAsync($"/api/configuration-items/{Id(srv)}/impact"));
        Assert.Equal([Ref(app)], Refs(impact.GetProperty("downstream")));
        Assert.Equal([Ref(svc)], Refs(impact.GetProperty("services")));

        var change = await Json(await m.PostAsJsonAsync("/api/changes", new
        {
            title = "Patch noyau", changeType = "Normal", risk = "Faible", implementationPlan = "Plan", backoutPlan = "Retour"
        }));
        await Json(await m.PostAsJsonAsync("/api/links", new { fromType = "change", fromId = Id(change), toReference = Ref(srv) }));
        var of = await Json(await m.GetAsync($"/api/configuration-items/impact-of?type=change&id={Id(change)}"));
        Assert.Equal([Ref(srv)], Refs(of.GetProperty("cis")));
        Assert.Equal([Ref(app)], Refs(of.GetProperty("downstream")));
        Assert.Equal([Ref(svc)], Refs(of.GetProperty("services")));
        Assert.Equal(HttpStatusCode.NotFound, (await m.GetAsync("/api/configuration-items/impact-of?type=change&id=999999")).StatusCode);
    }

    [Fact]
    public async Task MCP_05_l_impact_d_un_CI_rend_ses_incidents_et_changements_ouverts_visibles()
    {
        var (srv, _, _, m) = await Setup("mcp05");
        async Task<JsonElement> Incident(string title)
        {
            var e = await Json(await m.PostAsJsonAsync("/api/incidents", new { title, impact = "Faible", urgency = "Faible" }));
            await Json(await m.PostAsJsonAsync("/api/links", new { fromType = "incident", fromId = Id(e), toReference = Ref(srv) }));
            return e;
        }
        async Task<JsonElement> Change(string title)
        {
            var e = await Json(await m.PostAsJsonAsync("/api/changes", new
            {
                title, changeType = "Normal", risk = "Faible", implementationPlan = "Plan", backoutPlan = "Retour"
            }));
            // Lien dans l'autre sens : du CI vers le changement.
            await Json(await m.PostAsJsonAsync("/api/links", new { fromType = "ci", fromId = Id(srv), toReference = Ref(e) }));
            return e;
        }
        var open = await Incident("Serveur lent");
        var resolved = await Incident("Serveur arrêté");
        var elsewhere = await Incident("Serveur d'un autre périmètre");
        var planned = await Change("Patch noyau");
        var closed = await Change("Ancien patch");
        var hidden = Id(await Json(await api.Client().PostAsJsonAsync("/api/admin/perimeters", new { name = "Périmètre MCP-05" })));
        await using (var db = api.Database())
        {
            (await db.Incidents.SingleAsync(i => i.Id == Id(resolved))).Status = "Résolu";
            (await db.Incidents.SingleAsync(i => i.Id == Id(elsewhere))).PerimeterId = hidden;
            (await db.Changes.SingleAsync(c => c.Id == Id(closed))).Status = "Clos";
            await db.SaveChangesAsync();
        }

        var impact = await Json(await m.GetAsync($"/api/configuration-items/{Id(srv)}/impact"));
        Assert.Equal([Ref(open)], Refs(impact.GetProperty("openIncidents")));
        Assert.Equal([Ref(planned)], Refs(impact.GetProperty("openChanges")));
        // L'Admin voit tous les périmètres : l'incident déplacé y figure aussi.
        var all = await Json(await api.Client().GetAsync($"/api/configuration-items/{Id(srv)}/impact"));
        Assert.Equal([Ref(open), Ref(elsewhere)], Refs(all.GetProperty("openIncidents")));
    }

    [Fact]
    public async Task CFG_22_CI_hors_service_les_responsables_des_services_touches_sont_notifies()
    {
        var (srv, _, svc, m) = await Setup("cfg22");
        await Json(await m.PutAsJsonAsync($"/api/configuration-items/{Id(srv)}", new
        {
            title = "srv-cfg22", ciType = "Serveur", environment = "Production", status = "Hors service",
            ownerType = "User", ownerId = "owner.cfg22", ownerDisplayName = "Owner"
        }));
        var events = (await Json(await api.Client("User", "svcowner.cfg22").GetAsync("/api/notifications"))).GetProperty("items")
            .EnumerateArray().Where(n => n.GetProperty("reference").GetString() == Ref(srv)).Select(n => n.GetProperty("event").GetString()).ToList();
        Assert.Contains($"CI passé hors service : service {Ref(svc)} touché", events);
    }

    [Fact]
    public async Task CFG_15_verification_par_le_proprietaire_et_relance_a_12_mois()
    {
        var (srv, _, _, m) = await Setup("cfg15");
        var owner = api.Client("User", "owner.cfg15");
        Assert.Equal(HttpStatusCode.Forbidden, (await api.Client("User", "autre.cfg15").PostAsync($"/api/configuration-items/{Id(srv)}/verify", null)).StatusCode);
        var verified = await Json(await owner.PostAsync($"/api/configuration-items/{Id(srv)}/verify", null));
        Assert.Equal("owner.cfg15", verified.GetProperty("lastVerifiedBy").GetString());

        await using (var db = api.Database())
        {
            (await db.ConfigurationItems.FirstAsync(c => c.Id == Id(srv))).LastVerifiedAt = DateTime.UtcNow.AddMonths(-13);
            await db.SaveChangesAsync();
        }
        await Json(await api.Client().PostAsync("/api/notifications/run", null));
        var events = (await Json(await owner.GetAsync("/api/notifications"))).GetProperty("items").EnumerateArray()
            .Where(n => n.GetProperty("reference").GetString() == Ref(srv)).Select(n => n.GetProperty("event").GetString()!).ToList();
        Assert.Contains(events, e => e.StartsWith("Fiche à vérifier"));
    }

    [Fact]
    public async Task CFG_15_console_CI_a_verifier_pour_le_proprietaire_et_le_gestionnaire()
    {
        var (srv, app, _, m) = await Setup("cfg15c");
        var owner = api.Client("User", "owner.cfg15c");
        await using (var db = api.Database())
        {
            (await db.ConfigurationItems.FirstAsync(c => c.Id == Id(srv))).LastVerifiedAt = DateTime.UtcNow.AddMonths(-13);
            await db.SaveChangesAsync();
        }
        async Task<(List<string> Mine, List<string> Managed)> Console(HttpClient client)
        {
            var c = await Json(await client.GetAsync("/api/console"));
            var managed = c.GetProperty("management").ValueKind == JsonValueKind.Object
                ? Refs(c.GetProperty("management").GetProperty("cisToVerify")) : [];
            return (Refs(c.GetProperty("myCisToVerify")), managed);
        }

        // Le serveur non vérifié depuis 13 mois apparaît ; l'application, créée à l'instant, non.
        var (mine, _) = await Console(owner);
        Assert.Contains(Ref(srv), mine);
        Assert.DoesNotContain(Ref(app), mine);
        var (_, managed) = await Console(m);
        Assert.Contains(Ref(srv), managed);
        Assert.DoesNotContain(Ref(app), managed);
        Assert.DoesNotContain(Ref(srv), (await Console(api.Client("User", "autre.cfg15c"))).Mine);

        // Vérifié : il sort des deux listes.
        await Json(await owner.PostAsync($"/api/configuration-items/{Id(srv)}/verify", null));
        Assert.DoesNotContain(Ref(srv), (await Console(owner)).Mine);
        Assert.DoesNotContain(Ref(srv), (await Console(m)).Managed);
    }
}
