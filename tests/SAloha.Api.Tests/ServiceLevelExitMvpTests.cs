using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace SAloha.Api.Tests;

/// <summary>Niveaux de service : expiration (SLM-04), service du catalogue (SLM-10, SLM-15, INC-20), délais de prise en charge (SLM-13), heures de service et attente (SLM-14, INC-07, INC-22), respect mesuré et alerte (SLM-20, SLM-23, CSI-20), revue de SLA (SLM-22), SLA expirant dans la console (SLM-24).</summary>
[Collection("api")]
public class ServiceLevelExitMvpTests(ApiFixture api)
{
    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode} : {body}");
        return body == "" ? default : JsonDocument.Parse(body).RootElement;
    }

    private static int Id(JsonElement e) => e.GetProperty("id").GetInt32();

    private async Task<int> Service(HttpClient m, string name) =>
        Id(await Json(await m.PostAsJsonAsync("/api/services", new
        {
            title = name, status = "En service", criticality = "Moyenne", ownerType = "User", ownerId = "svc.owner", ownerDisplayName = "Owner"
        })));

    private static object Sla(int serviceId, string customer, string? status, int?[]? response = null, DateTime? validTo = null) => new
    {
        title = $"SLA {customer}", status, serviceId, customer, resolutionHoursP1 = 4, resolutionHoursP2 = 8,
        resolutionHoursP3 = 24, resolutionHoursP4 = 48, validFrom = DateTime.UtcNow.Date.AddMonths(-6), validTo,
        responseHoursP1 = response?[0], responseHoursP2 = response?[1], responseHoursP3 = response?[2], responseHoursP4 = response?[3]
    };

    [Fact]
    public async Task SLM_13_delais_de_prise_en_charge_et_echeance_de_l_incident()
    {
        var m = api.Client("Manager", "slm13.manager");
        var svc = await Service(m, "Service SLM-13");
        var sla = await Json(await m.PostAsJsonAsync("/api/agreements", Sla(svc, "SLM-13", null)));
        var url = $"/api/agreements/{Id(sla)}";
        // Incomplets, décroissants, ou au-delà du délai de résolution : refusés à la mise en vigueur.
        Assert.Equal(HttpStatusCode.BadRequest, (await m.PutAsJsonAsync(url, Sla(svc, "SLM-13", "En vigueur", [1, null, null, null]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await m.PutAsJsonAsync(url, Sla(svc, "SLM-13", "En vigueur", [2, 1, 4, 8]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await m.PutAsJsonAsync(url, Sla(svc, "SLM-13", "En vigueur", [5, 6, 8, 8]))).StatusCode);
        await Json(await m.PutAsJsonAsync(url, Sla(svc, "SLM-13", "En vigueur", [1, 2, 4, 8])));

        // Incident P4 sur ce service : prise en charge sous 8 h, résolution sous 48 h.
        var inc = await Json(await m.PostAsJsonAsync("/api/incidents", new { title = "SLM-13", impact = "Faible", urgency = "Faible", affectedService = "service slm-13" }));
        var created = inc.GetProperty("createdAt").GetDateTime();
        Assert.Equal(created.AddHours(8), inc.GetProperty("responseDueAt").GetDateTime(), TimeSpan.FromSeconds(1));
        Assert.Equal(created.AddHours(48), inc.GetProperty("resolutionDueAt").GetDateTime(), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task SLM_04_un_SLA_dont_la_validite_est_passee_expire()
    {
        var m = api.Client("Manager", "slm04.manager");
        var svc = await Service(m, "Service SLM-04");
        var sla = await Json(await m.PostAsJsonAsync("/api/agreements", Sla(svc, "SLM-04", null)));
        await Json(await m.PutAsJsonAsync($"/api/agreements/{Id(sla)}", Sla(svc, "SLM-04", "En vigueur", validTo: DateTime.UtcNow.Date.AddDays(10))));
        // Fin de validité hier : la règle de cohérence de l'API refuserait une date passée à la mise en vigueur.
        await using (var db = api.Database())
        {
            var a = await db.Agreements.FirstAsync(x => x.Id == Id(sla));
            a.ValidTo = DateTime.UtcNow.Date.AddDays(-1);
            await db.SaveChangesAsync();
        }
        await Json(await api.Client().PostAsync("/api/notifications/run", null));
        var after = await Json(await m.GetAsync($"/api/agreements/{Id(sla)}"));
        Assert.Equal("Expiré", after.GetProperty("status").GetString());
        var events = (await Json(await api.Client("Manager", "slm04.autre").GetAsync("/api/notifications"))).GetProperty("items").EnumerateArray()
            .Where(n => n.GetProperty("reference").GetString() == sla.GetProperty("reference").GetString())
            .Select(n => n.GetProperty("event").GetString()).ToList();
        Assert.Contains(events, e => e!.StartsWith("SLA expiré"));
        // Le journal d'audit garde le passage à Expiré.
        var history = await Json(await m.GetAsync($"/api/audit?type=agreement&id={Id(sla)}"));
        Assert.Contains(history.EnumerateArray(), h => h.GetProperty("field").GetString() == "status" && h.GetProperty("newValue").GetString() == "Expiré");
    }

    [Fact]
    public async Task SLM_22_revue_du_SLA_reporte_la_prochaine_revue()
    {
        var m = api.Client("Manager", "slm22.manager");
        var svc = await Service(m, "Service SLM-22");
        var sla = await Json(await m.PostAsJsonAsync("/api/agreements", Sla(svc, "SLM-22", null)));
        var url = $"/api/agreements/{Id(sla)}/reviews";

        Assert.Equal(HttpStatusCode.Forbidden, (await api.Client("User", "slm22.user").PostAsJsonAsync(url, new { findings = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await m.PostAsJsonAsync(url, new { findings = "", decisions = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await m.PostAsJsonAsync(url, new { findings = "x", nextReviewDate = DateTime.UtcNow.Date })).StatusCode);

        var review = await Json(await m.PostAsJsonAsync(url, new { findings = "Délais P3 tenus à 92 %", decisions = "Renforcer l'astreinte" }));
        Assert.Equal(DateTime.UtcNow.Date.AddMonths(12), review.GetProperty("nextReviewDate").GetDateTime().Date);
        await Json(await m.PostAsJsonAsync(url, new { decisions = "Revue anticipée", nextReviewDate = DateTime.UtcNow.Date.AddMonths(3) }));

        var after = await Json(await m.GetAsync($"/api/agreements/{Id(sla)}"));
        Assert.Equal(DateTime.UtcNow.Date.AddMonths(3), after.GetProperty("reviewDate").GetDateTime().Date);
        var reviews = after.GetProperty("reviews").EnumerateArray().ToList();
        Assert.Equal(2, reviews.Count);
        Assert.Equal("Revue anticipée", reviews[0].GetProperty("decisions").GetString());   // la plus récente d'abord
    }

    [Fact]
    public async Task SLM_10_service_du_catalogue_sur_incident_probleme_et_demande()
    {
        var m = api.Client("Manager", "slm10.manager");
        var u = api.Client("User", "slm10.user");
        var svc = await Service(m, "Service SLM-10");
        var sla = await Json(await m.PostAsJsonAsync("/api/agreements", Sla(svc, "SLM-10", null)));
        await Json(await m.PutAsJsonAsync($"/api/agreements/{Id(sla)}", Sla(svc, "SLM-10", "En vigueur")));
        object Inc(string title, int? serviceId = null, string? affectedService = null) =>
            new { title, impact = "Faible", urgency = "Faible", serviceId, affectedService };

        // Choisi par son identifiant : le nom est recopié, l'échéance vient de son SLA.
        var byId = await Json(await u.PostAsJsonAsync("/api/incidents", Inc("Par identifiant", svc)));
        Assert.Equal("Service SLM-10", byId.GetProperty("affectedService").GetString());
        Assert.NotEqual(JsonValueKind.Null, byId.GetProperty("resolutionDueAt").ValueKind);
        // Donné par son nom (clients de l'API) : retrouvé dans le catalogue, casse ignorée.
        var byName = await Json(await u.PostAsJsonAsync("/api/incidents", Inc("Par nom", affectedService: "  service slm-10 ")));
        Assert.Equal(svc, byName.GetProperty("serviceId").GetInt32());
        Assert.Equal("Service SLM-10", byName.GetProperty("affectedService").GetString());
        // Inconnu du catalogue : simple libellé, sans SLA. Identifiant inconnu : 400.
        var label = await Json(await u.PostAsJsonAsync("/api/incidents", Inc("Libellé", affectedService: "Hors catalogue SLM-10")));
        Assert.Equal(JsonValueKind.Null, label.GetProperty("serviceId").ValueKind);
        Assert.Equal(JsonValueKind.Null, label.GetProperty("resolutionDueAt").ValueKind);
        Assert.Equal(HttpStatusCode.BadRequest, (await u.PostAsJsonAsync("/api/incidents", Inc("Inconnu", 999999))).StatusCode);

        // Problème et demande portent aussi leur service.
        var problem = await Json(await u.PostAsJsonAsync("/api/problems", new
        {
            title = "SLM-10", description = "d", impact = "Faible", urgency = "Faible", category = "c", affectedService = "", serviceId = svc
        }));
        Assert.Equal("Service SLM-10", problem.GetProperty("affectedService").GetString());
        var request = await Json(await u.PostAsJsonAsync("/api/requests", new { title = "SLM-10", requestedItem = "Accès", serviceId = svc }));
        Assert.Equal(svc, request.GetProperty("serviceId").GetInt32());
        Assert.Equal("Service SLM-10", request.GetProperty("affectedService").GetString());
    }

    [Fact]
    public async Task SLM_10_INC_20_la_fiche_d_un_incident_enregistree_telle_quelle_garde_un_service_hors_catalogue()
    {
        const string label = "Outil maison slm-10";
        var m = api.Client("Manager", "slm10f.manager");
        var u = api.Client("User", "slm10f.user");
        // Corps de la fiche : les champs du formulaire et `serviceId`, jamais `affectedService`.
        object Form(int? serviceId = null) => new { title = "Fiche", description = "d", impact = "Faible", urgency = "Faible", category = "", serviceId };

        var inc = await Json(await u.PostAsJsonAsync("/api/incidents", new { title = "Fiche", impact = "Faible", urgency = "Faible", affectedService = label }));
        var url = $"/api/incidents/{Id(inc)}";
        // La fiche ouverte puis enregistrée sans rien toucher : le libellé hors catalogue reste.
        var saved = await Json(await u.PutAsJsonAsync(url, Form()));
        Assert.Equal(label, saved.GetProperty("affectedService").GetString());
        Assert.Equal(JsonValueKind.Null, saved.GetProperty("serviceId").ValueKind);
        // Un service du catalogue choisi dans la fiche remplace le libellé…
        var svc = await Service(m, "Service SLM-10 fiche");
        var chosen = await Json(await u.PutAsJsonAsync(url, Form(svc)));
        Assert.Equal("Service SLM-10 fiche", chosen.GetProperty("affectedService").GetString());
        // … et « Aucun service » le retire avec son nom recopié : l'ancien libellé ne revient pas.
        var removed = await Json(await u.PutAsJsonAsync(url, Form()));
        Assert.Equal(JsonValueKind.Null, removed.GetProperty("serviceId").ValueKind);
        Assert.Equal("", removed.GetProperty("affectedService").GetString());

        // Cas voisin : un libellé vide envoyé exprès (client de l'API) retire le libellé hors catalogue.
        var other = await Json(await u.PostAsJsonAsync("/api/incidents", new { title = "Fiche", impact = "Faible", urgency = "Faible", affectedService = label }));
        var cleared = await Json(await u.PutAsJsonAsync($"/api/incidents/{Id(other)}", new { title = "Fiche", impact = "Faible", urgency = "Faible", affectedService = "" }));
        Assert.Equal("", cleared.GetProperty("affectedService").GetString());
    }

    [Fact]
    public async Task SLM_15_un_service_retire_ne_se_choisit_plus()
    {
        var m = api.Client("Manager", "slm15.manager");
        var svc = await Service(m, "Service SLM-15");
        var inc = await Json(await m.PostAsJsonAsync("/api/incidents", new { title = "Avant retrait", impact = "Faible", urgency = "Faible", serviceId = svc }));
        // SLM-16 : l'incident est résolu avant le retrait du service.
        await Json(await m.PutAsJsonAsync($"/api/incidents/{Id(inc)}", new { title = "Avant retrait", impact = "Faible", urgency = "Faible", serviceId = svc,
            status = "Résolu", resolution = "Rétabli", resolutionCode = "Correctif appliqué" }));
        await Json(await m.PutAsJsonAsync($"/api/services/{svc}", new
        {
            title = "Service SLM-15", status = "Retiré", criticality = "Moyenne", ownerType = "User", ownerId = "svc.owner", ownerDisplayName = "Owner"
        }));

        Assert.Equal(HttpStatusCode.BadRequest, (await m.PostAsJsonAsync("/api/incidents", new { title = "Après", impact = "Faible", urgency = "Faible", serviceId = svc })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await m.PostAsJsonAsync("/api/agreements", Sla(svc, "SLM-15", null))).StatusCode);
        // L'incident qui le portait déjà se modifie toujours.
        await Json(await m.PutAsJsonAsync($"/api/incidents/{Id(inc)}", new { title = "Avant retrait (modifié)", impact = "Faible", urgency = "Faible", serviceId = svc,
            resolution = "Rétabli", resolutionCode = "Correctif appliqué" }));
    }

    [Fact]
    public async Task SLM_16_un_service_ne_se_retire_pas_sous_SLA_en_vigueur_ni_avec_un_enregistrement_ouvert()
    {
        var m = api.Client("Manager", "slm16.manager");
        var svc = await Service(m, "Service SLM-16");
        var url = $"/api/services/{svc}";
        object Retire() => new
        {
            title = "Service SLM-16", status = "Retiré", criticality = "Moyenne", ownerType = "User", ownerId = "svc.owner", ownerDisplayName = "Owner"
        };
        var sla = await Json(await m.PostAsJsonAsync("/api/agreements", Sla(svc, "SLM-16", null)));
        await Json(await m.PutAsJsonAsync($"/api/agreements/{Id(sla)}", Sla(svc, "SLM-16", "En vigueur")));
        var inc = await Json(await m.PostAsJsonAsync("/api/incidents", new { title = "SLM-16", impact = "Faible", urgency = "Faible", serviceId = svc }));
        var req = await Json(await m.PostAsJsonAsync("/api/requests", new { title = "SLM-16", requestedItem = "Accès", serviceId = svc }));

        async Task<string> Refused()
        {
            var res = await m.PutAsJsonAsync(url, Retire());
            var body = await res.Content.ReadAsStringAsync();
            Assert.True(res.StatusCode == HttpStatusCode.BadRequest, $"HTTP {(int)res.StatusCode} : {body}");
            return body;
        }
        Assert.Contains("SLA", await Refused());
        await Json(await m.PutAsJsonAsync($"/api/agreements/{Id(sla)}", Sla(svc, "SLM-16", "Expiré")));
        Assert.Contains(inc.GetProperty("reference").GetString()!, await Refused());
        await Json(await m.PutAsJsonAsync($"/api/incidents/{Id(inc)}", new { title = "SLM-16", impact = "Faible", urgency = "Faible", serviceId = svc,
            status = "Résolu", resolution = "Rétabli", resolutionCode = "Correctif appliqué" }));
        Assert.Contains(req.GetProperty("reference").GetString()!, await Refused());
        await Json(await m.PutAsJsonAsync($"/api/requests/{Id(req)}", new { title = "SLM-16", requestedItem = "Accès", serviceId = svc, status = "Annulée" }));

        var retired = await Json(await m.PutAsJsonAsync(url, Retire()));
        Assert.Equal("Retiré", retired.GetProperty("status").GetString());
    }

    [Fact]
    public async Task SLM_14_heures_de_service_controlees_et_appliquees_a_l_echeance()
    {
        var m = api.Client("Manager", "slm14.manager");
        object Svc(string days, string? open, string? close, string? zone = null) => new
        {
            title = "Service SLM-14", status = "En service", criticality = "Moyenne", ownerType = "User", ownerId = "svc.owner",
            ownerDisplayName = "Owner", workingDays = days, openingTime = open, closingTime = close, holidays = "2026-12-25", timeZone = zone
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await m.PostAsJsonAsync("/api/services", Svc("1,2,3,4,5", "18:00", "08:00"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await m.PostAsJsonAsync("/api/services", Svc("1,2,3,4,5", "08:00", "18:00", "Nulle/Part"))).StatusCode);
        var svc = Id(await Json(await m.PostAsJsonAsync("/api/services", Svc("1,2,3,4,5", "08:00", "18:00", "Europe/Paris"))));
        var sla = await Json(await m.PostAsJsonAsync("/api/agreements", Sla(svc, "SLM-14", null)));
        await Json(await m.PutAsJsonAsync($"/api/agreements/{Id(sla)}", Sla(svc, "SLM-14", "En vigueur")));

        var inc = await Json(await m.PostAsJsonAsync("/api/incidents", new { title = "SLM-14", impact = "Faible", urgency = "Faible", serviceId = svc }));
        var created = inc.GetProperty("createdAt").GetDateTime().ToUniversalTime();
        var (calendar, _) = SAloha.Api.Core.Itil.ServiceCalendar.Parse("1,2,3,4,5", "08:00", "18:00", "2026-12-25", "Europe/Paris");
        Assert.Equal(calendar!.Add(created, TimeSpan.FromHours(48)), inc.GetProperty("resolutionDueAt").GetDateTime().ToUniversalTime(), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task INC_07_22_attente_motivee_et_horloge_suspendue()
    {
        var m = api.Client("Manager", "inc22.manager");
        var svc = await Service(m, "Service INC-22");   // sans calendrier : 24 h/24
        var sla = await Json(await m.PostAsJsonAsync("/api/agreements", Sla(svc, "INC-22", null)));
        await Json(await m.PutAsJsonAsync($"/api/agreements/{Id(sla)}", Sla(svc, "INC-22", "En vigueur")));
        object Inc(string? status, string? onHoldReason = null) => new
        {
            title = "INC-22", impact = "Faible", urgency = "Faible", serviceId = svc, status, onHoldReason,
            ownerType = "User", ownerId = "inc22.tech", ownerDisplayName = "Tech"
        };
        var inc = await Json(await m.PostAsJsonAsync("/api/incidents", Inc(null)));
        var url = $"/api/incidents/{Id(inc)}";
        var due = inc.GetProperty("resolutionDueAt").GetDateTime();
        await Json(await m.PutAsJsonAsync(url, Inc("En cours")));

        // INC-07 : sans motif, ou hors liste : 400.
        Assert.Equal(HttpStatusCode.BadRequest, (await m.PutAsJsonAsync(url, Inc("En attente"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await m.PutAsJsonAsync(url, Inc("En attente", "Pause café"))).StatusCode);
        var held = await Json(await m.PutAsJsonAsync(url, Inc("En attente", "Attente fournisseur")));
        Assert.Equal("Attente fournisseur", held.GetProperty("onHoldReason").GetString());

        // Deux heures d'attente (horloge reculée en base) : à la reprise, l'échéance recule d'autant.
        await using (var db = api.Database())
        {
            var i = await db.Incidents.FirstAsync(x => x.Id == Id(inc));
            i.OnHoldSince = DateTime.UtcNow.AddHours(-2);
            await db.SaveChangesAsync();
        }
        var resumed = await Json(await m.PutAsJsonAsync(url, Inc("En cours")));
        Assert.Equal(JsonValueKind.Null, resumed.GetProperty("onHoldReason").ValueKind);
        Assert.InRange(resumed.GetProperty("heldMinutes").GetInt32(), 119, 121);
        Assert.Equal(due.AddMinutes(resumed.GetProperty("heldMinutes").GetInt32()), resumed.GetProperty("resolutionDueAt").GetDateTime(), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task SLM_20_23_respect_mesure_alerte_et_amelioration_proposee()
    {
        var m = api.Client("Manager", "slm20.manager");
        var svc = await Service(m, "Service SLM-20");
        var sla = await Json(await m.PostAsJsonAsync("/api/agreements", Sla(svc, "SLM-20", null)));
        object Target(string? status, decimal? target) => new
        {
            title = "SLA SLM-20", status, serviceId = svc, customer = "SLM-20", resolutionHoursP1 = 4, resolutionHoursP2 = 8,
            resolutionHoursP3 = 24, resolutionHoursP4 = 48, validFrom = DateTime.UtcNow.Date.AddMonths(-6), complianceTarget = target
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await m.PutAsJsonAsync($"/api/agreements/{Id(sla)}", Target(null, 120))).StatusCode);
        await Json(await m.PutAsJsonAsync($"/api/agreements/{Id(sla)}", Target("En vigueur", 90)));

        // Le mois dernier : un incident résolu à temps, un résolu en retard, un encore ouvert dont l'échéance est passée.
        var now = DateTime.UtcNow;
        var month = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-1);
        var ids = new List<int>();
        foreach (var t in new[] { "à temps", "en retard", "ouvert" })
            ids.Add(Id(await Json(await m.PostAsJsonAsync("/api/incidents", new { title = $"SLM-20 {t}", impact = "Faible", urgency = "Faible", serviceId = svc }))));
        await using (var db = api.Database())
        {
            var list = await db.Incidents.Where(i => ids.Contains(i.Id)).OrderBy(i => i.Id).ToListAsync();
            foreach (var i in list) { i.CreatedAt = month.AddDays(2); i.ResolutionDueAt = month.AddDays(4); }
            list[0].Status = "Résolu"; list[0].ResolvedAt = month.AddDays(3);
            list[1].Status = "Résolu"; list[1].ResolvedAt = month.AddDays(5);
            await db.SaveChangesAsync();
        }

        var c = await Json(await api.Client("User", "slm20.user").GetAsync($"/api/agreements/{Id(sla)}/compliance?from={month:yyyy-MM}&to={month:yyyy-MM}"));
        var measured = Assert.Single(c.GetProperty("months").EnumerateArray());
        Assert.Equal(3, measured.GetProperty("total").GetInt32());
        Assert.Equal(1, measured.GetProperty("onTime").GetInt32());
        Assert.Equal(33.3, measured.GetProperty("percent").GetDouble());
        Assert.False(measured.GetProperty("met").GetBoolean());
        Assert.Equal("P4", measured.GetProperty("byPriority")[0].GetProperty("priority").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await m.GetAsync($"/api/agreements/{Id(sla)}/compliance?from=2026-13")).StatusCode);

        // SLM-23 : alerte aux gestionnaires et amélioration Proposée reliée au SLA, une seule fois.
        await Json(await api.Client().PostAsync("/api/notifications/run", null));
        await Json(await api.Client().PostAsync("/api/notifications/run", null));
        var events = (await Json(await api.Client("Manager", "slm20.autre").GetAsync("/api/notifications"))).GetProperty("items").EnumerateArray()
            .Where(n => n.GetProperty("reference").GetString() == sla.GetProperty("reference").GetString())
            .Select(n => n.GetProperty("event").GetString()!).ToList();
        Assert.Single(events, e => e.StartsWith($"SLA sous la cible ({month:yyyy-MM}"));
        var links = (await Json(await m.GetAsync($"/api/links?type=agreement&id={Id(sla)}"))).EnumerateArray()
            .Where(l => l.GetProperty("type").GetString() == "improvement").ToList();
        var improvement = await Json(await m.GetAsync($"/api/improvements/{Assert.Single(links).GetProperty("id").GetInt32()}"));
        Assert.Equal("Proposée", improvement.GetProperty("status").GetString());
        Assert.Equal("SLA", improvement.GetProperty("source").GetString());
    }

    [Fact]
    public async Task SLM_24_SLA_expirant_dans_30_jours_dans_les_relances()
    {
        var m = api.Client("Manager", "slm24.manager");
        var svc = await Service(m, "Service SLM-24");
        async Task<JsonElement> Active(string customer, DateTime? validTo)
        {
            var sla = await Json(await m.PostAsJsonAsync("/api/agreements", Sla(svc, customer, null)));
            return await Json(await m.PutAsJsonAsync($"/api/agreements/{Id(sla)}", Sla(svc, customer, "En vigueur", validTo: validTo)));
        }
        var soon = await Active("SLM-24 proche", DateTime.UtcNow.Date.AddDays(20));
        var later = await Active("SLM-24 lointain", DateTime.UtcNow.Date.AddDays(60));
        var open = await Active("SLM-24 sans fin", null);

        var lines = (await Json(await m.GetAsync("/api/console"))).GetProperty("management").GetProperty("expiringAgreements")
            .EnumerateArray().ToList();
        var line = Assert.Single(lines, l => Id(l) == Id(soon));
        Assert.Equal("agreement", line.GetProperty("type").GetString());
        Assert.Equal($"Expire le {DateTime.UtcNow.Date.AddDays(20):dd/MM/yyyy}", line.GetProperty("status").GetString());
        Assert.DoesNotContain(lines, l => Id(l) == Id(later) || Id(l) == Id(open));

        // Un User n'a pas de bloc de pilotage (PER-09).
        var user = await Json(await api.Client("User", "slm24.user").GetAsync("/api/console"));
        Assert.Equal(JsonValueKind.Null, user.GetProperty("management").ValueKind);
    }
}
