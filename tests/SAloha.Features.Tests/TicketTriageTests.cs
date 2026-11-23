using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace SAloha.Features.Tests;

/// <summary>
/// Port interne factice de l'API : fil d'événements, lectures du tri (OPT-13) et dépôt des propositions
/// (OPT-14), tels que les sert l'API ; chaque appel reçu est gardé pour les vérifications.
/// </summary>
public sealed class FakeInternalApi : IAsyncLifetime
{
    public const string Token = "jeton-du-conteneur-de-test-0123456789";
    private WebApplication app = null!;
    public string Url { get; private set; } = "";
    public volatile bool EventsForbidden;
    public readonly ConcurrentQueue<object> Events = new();
    public readonly ConcurrentDictionary<int, object> Incidents = new();
    public object Similar = new { semantic = true, neighbours = Array.Empty<object>() };
    public readonly ConcurrentQueue<JsonElement> SimilarQueries = new();
    public readonly ConcurrentQueue<JsonElement> Proposals = new();
    public readonly ConcurrentQueue<JsonElement> Outages = new();
    /// <summary>Réglages du tri servis par l'API (TRI-13) ; null : route absente (404), valeurs par défaut.</summary>
    public volatile object? Settings;
    public readonly ConcurrentQueue<string> Authorizations = new();

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0));
        app = builder.Build();
        // Le jeton du conteneur ne vaut que pour le port interne de l'API, pas pour le service d'embeddings.
        app.Use(async (c, next) =>
        {
            if (c.Request.Path.StartsWithSegments("/internal")) Authorizations.Enqueue(c.Request.Headers.Authorization.ToString());
            await next();
        });
        app.MapGet("/internal/events", (long after) =>
        {
            if (EventsForbidden) return Results.StatusCode(403);
            var list = Events.Select(e => JsonSerializer.SerializeToElement(e, JsonSerializerOptions.Web))
                .Where(e => e.GetProperty("id").GetInt64() > after).ToList();
            return Results.Json(new { events = list, next = list.Count > 0 ? list[^1].GetProperty("id").GetInt64() : after, gap = false });
        });
        app.MapGet("/internal/incidents/{id:int}", (int id) => Incidents.TryGetValue(id, out var i) ? Results.Json(i) : Results.NotFound());
        app.MapGet("/internal/incidents/open", (int after) => Results.Json(new
        {
            items = Incidents.OrderBy(i => i.Key).Where(i => i.Key > after).Select(i => JsonSerializer.SerializeToElement(i.Value, JsonSerializerOptions.Web))
                .Where(i => i.GetProperty("incident").GetProperty("status").GetString() is "Nouveau" or "En cours" or "En attente").ToList(),
            next = (int?)null,
        }));
        app.MapGet("/internal/features/assisted-ticket-triage/settings", () => Settings is { } s ? Results.Json(s) : Results.NotFound());
        app.MapPost("/internal/outages", async (HttpContext c) =>
        {
            Outages.Enqueue(await JsonSerializer.DeserializeAsync<JsonElement>(c.Request.Body));
            return Results.Json(new { id = Outages.Count, created = true }, statusCode: 201);
        });
        // Service d'embeddings factice : une dimension par mot clé, les autres textes à part.
        app.MapPost("/api/embed", async (HttpContext c) =>
        {
            var body = await JsonSerializer.DeserializeAsync<JsonElement>(c.Request.Body);
            var vectors = body.GetProperty("input").EnumerateArray().Select(t =>
            {
                var text = t.GetString()!.ToLowerInvariant();
                return new[] { text.Contains("vpn") ? 1f : 0f, text.Contains("courriel") ? 1f : 0f, text.Contains("vpn") || text.Contains("courriel") ? 0f : 1f };
            }).ToArray();
            return Results.Json(new { embeddings = vectors });
        });
        app.MapPost("/internal/search/similar", async (HttpContext c) =>
        {
            SimilarQueries.Enqueue(await JsonSerializer.DeserializeAsync<JsonElement>(c.Request.Body));
            return Results.Json(Similar);
        });
        app.MapGet("/internal/services", () => Results.Json(new[]
        {
            new { id = 1, reference = "SVC-1", title = "Messagerie", status = "En service", criticality = "Moyenne" },
        }));
        app.MapGet("/internal/categories", () => Results.Json(new[] { new { id = 1, practice = "incident", name = "Messagerie", active = true, usage = 7 } }));
        app.MapGet("/internal/configuration-items", (string ids) => Results.Json(ids.Split(',').Select(int.Parse)
            .Select(id => new { id, reference = $"CI-{id}", title = "Exchange", criticality = "Vitale" })));
        app.MapPost("/internal/proposals", async (HttpContext c) =>
        {
            Proposals.Enqueue(await JsonSerializer.DeserializeAsync<JsonElement>(c.Request.Body));
            return Results.Json(new { id = Proposals.Count, created = true }, statusCode: 201);
        });
        await app.StartAsync();
        Url = app.Urls.Single();
    }

    public async Task DisposeAsync() => await app.DisposeAsync();
}

/// <summary>Tri assisté de bout en bout dans le conteneur : fil d'événements, lectures, propositions (TRI-01 à TRI-04, TRI-11, OPT-12).</summary>
public class TicketTriageTests(FakeInternalApi api) : IClassFixture<FakeInternalApi>
{
    private WebApplicationFactory<Program> Container() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Features:Token", FakeInternalApi.Token);
            b.UseSetting("Api:Url", api.Url);
            b.UseSetting("Embeddings:Url", api.Url);
            b.UseSetting("Events:PollMilliseconds", "50");
            b.UseSetting("Events:IdleMilliseconds", "100");
        });

    private static object Incident(int id, string status, int perimeter = 7, int? service = 1, int minutesAgo = 0, string title = "Outlook ne reçoit plus les courriels",
        int[]? cis = null) => new
    {
        incident = new
        {
            id, perimeterId = perimeter, reference = $"INC-{id}", title, description = "Depuis ce matin",
            status, category = "", serviceId = service, impact = "Moyen", ownerType = "User", ownerId = "hery.rakoto", ownerDisplayName = "Hery Rakoto",
            createdAt = DateTime.UtcNow.AddMinutes(-minutesAgo),
        },
        configurationItemIds = cis ?? [5],
    };

    private static long eventId = 1000;
    private static void Event(FakeInternalApi api, string type, int incident, int perimeter) =>
        api.Events.Enqueue(new { id = Interlocked.Increment(ref eventId), type, entityType = "incident", entityId = incident, perimeterId = perimeter, at = DateTime.UtcNow });

    private static async Task<List<JsonElement>> OutagesWith(FakeInternalApi api, int member, Func<JsonElement, bool> match)
    {
        for (var i = 0; i < 100; i++)
        {
            var found = api.Outages.Where(o => o.GetProperty("members").EnumerateArray().Any(m => m.GetInt32() == member) && match(o)).ToList();
            if (found.Count > 0) return found;
            await Task.Delay(50);
        }
        return [];
    }

    private static bool Closed(JsonElement o) => o.TryGetProperty("closed", out var c) && c.GetBoolean();

    private static object Resolved(int id, string category) => new
    {
        score = 0.5, lexical = true, semantic = true,
        incident = new { id, perimeterId = 7, reference = $"INC-{id}", title = "Messagerie", status = "Résolu", category, serviceId = 1 },
    };

    private static List<JsonElement> ProposalsFor(FakeInternalApi api, int id) =>
        api.Proposals.Where(p => p.GetProperty("targetId").GetInt32() == id).ToList();

    private static async Task<List<JsonElement>> WaitFor(FakeInternalApi api, int id, int count)
    {
        for (var i = 0; i < 100 && ProposalsFor(api, id).Count < count; i++) await Task.Delay(50);
        return ProposalsFor(api, id);
    }

    [Fact]
    public async Task TRI_02_apres_un_enregistrement_le_conteneur_depose_ses_propositions_dans_le_perimetre_de_l_incident()
    {
        api.Incidents[1] = Incident(1, "Nouveau");
        api.Incidents[2] = Incident(2, "Résolu");
        api.Similar = new { semantic = true, neighbours = new[] { Resolved(11, "Messagerie"), Resolved(12, "Messagerie"), Resolved(13, "Messagerie") } };
        api.Events.Enqueue(new { id = 1, type = "incident.created", entityType = "incident", entityId = 1, perimeterId = 7, at = DateTime.UtcNow });
        api.Events.Enqueue(new { id = 2, type = "incident.updated", entityType = "incident", entityId = 2, perimeterId = 7, at = DateTime.UtcNow });
        api.Events.Enqueue(new { id = 3, type = "ci.created", entityType = "ci", entityId = 5, perimeterId = 7, at = DateTime.UtcNow });

        using var container = Container();
        _ = container.Services;   // démarre l'hôte, donc la boucle d'événements
        var proposals = await WaitFor(api, 1, 2);

        var category = proposals.Single(p => p.GetProperty("field").GetString() == "category");
        Assert.Equal(("assisted-ticket-triage", "incident", "Messagerie"),
            (category.GetProperty("feature").GetString(), category.GetProperty("targetType").GetString(), category.GetProperty("after").GetString()));
        Assert.Equal("3 des 3 incidents résolus les plus proches portent la catégorie Messagerie", category.GetProperty("reasons").GetString());
        // TRI-04 : le CI relié Vital l'emporte sur le service de criticité Moyenne.
        var impact = proposals.Single(p => p.GetProperty("field").GetString() == "impact");
        Assert.Equal("Élevé", impact.GetProperty("after").GetString());

        // TRI-11 : en tâche de fond, les voisins se cherchent dans le seul périmètre de l'incident, lui exclu.
        var query = api.SimilarQueries.First(q => q.GetProperty("excludeId").GetInt32() == 1);
        Assert.Equal([7], query.GetProperty("perimeters").EnumerateArray().Select(e => e.GetInt32()));
        Assert.Equal(1, query.GetProperty("excludeId").GetInt32());
        // TRI-01 : un incident résolu n'est pas trié ; un événement d'un autre type n'est pas lu par le tri.
        await Task.Delay(300);
        Assert.Empty(ProposalsFor(api, 2));
        Assert.DoesNotContain(api.SimilarQueries, q => q.GetProperty("excludeId").GetInt32() is 2 or 5);
        // OPT-10 : chaque appel porte le jeton du conteneur.
        Assert.All(api.Authorizations, a => Assert.Equal($"Bearer {FakeInternalApi.Token}", a));
    }

    [Fact]
    public async Task OPT_12_sans_fonctionnalite_activee_le_conteneur_attend_puis_rattrape_le_fil()
    {
        api.EventsForbidden = true;
        api.Incidents[21] = Incident(21, "En cours");
        api.Similar = new { semantic = true, neighbours = Array.Empty<object>() };
        api.Events.Enqueue(new { id = 21, type = "incident.created", entityType = "incident", entityId = 21, perimeterId = 7, at = DateTime.UtcNow });

        using var container = Container();
        _ = container.Services;
        await Task.Delay(300);
        Assert.Empty(ProposalsFor(api, 21));   // 403 : rien n'est lu, rien n'est proposé

        // Le tri est activé : le conteneur reprend le fil où il en était et traite l'événement en attente.
        api.EventsForbidden = false;
        var proposals = await WaitFor(api, 21, 1);
        Assert.Equal("impact", Assert.Single(proposals).GetProperty("field").GetString());
    }

    [Fact]
    public async Task TRI_05_trois_incidents_d_un_service_forment_une_panne_signalee_une_fois_avec_l_impact_eleve()
    {
        // Scénario de TRI-05 : trois incidents ouverts sur Messagerie en 20 minutes au Siège ; un quatrième à l'Usine.
        api.Incidents[31] = Incident(31, "Nouveau", perimeter: 9, minutesAgo: 20, cis: []);
        api.Incidents[32] = Incident(32, "En cours", perimeter: 9, minutesAgo: 10, cis: []);
        api.Incidents[33] = Incident(33, "Nouveau", perimeter: 9, minutesAgo: 0, cis: []);
        api.Incidents[34] = Incident(34, "Nouveau", perimeter: 10, minutesAgo: 5, cis: []);
        foreach (var (id, p) in new[] { (31, 9), (32, 9), (33, 9), (34, 10) }) Event(api, "incident.created", id, p);

        using var container = Container();
        _ = container.Services;
        var signal = Assert.Single(await OutagesWith(api, 31, o => !Closed(o)));
        Assert.Equal([31, 32, 33], signal.GetProperty("members").EnumerateArray().Select(m => m.GetInt32()));
        Assert.Equal(("service", 1, "3 incidents ouverts sur le service Messagerie"),
            (signal.GetProperty("basis").GetString(), signal.GetProperty("serviceId").GetInt32(), signal.GetProperty("reasons").GetString()));
        Assert.DoesNotContain(api.Outages, o => o.GetProperty("members").EnumerateArray().Any(m => m.GetInt32() == 34));   // TRI-11

        // TRI-04 : chaque incident de la panne reçoit l'impact Élevé proposé, motivé par elle.
        foreach (var id in new[] { 31, 32, 33 })
        {
            var impact = (await WaitFor(api, id, 1)).First(p => p.GetProperty("field").GetString() == "impact");
            Assert.Equal(("Élevé", "Panne probable : 3 incidents ouverts sur le service Messagerie"),
                (impact.GetProperty("after").GetString(), impact.GetProperty("reasons").GetString()));
        }
        await Task.Delay(300);
        Assert.Single(api.Outages, o => o.GetProperty("members").EnumerateArray().Any(m => m.GetInt32() == 31));   // une seule fois
    }

    [Fact]
    public async Task TRI_06_par_le_sens_puis_un_incident_resolu_sort_de_l_index_et_la_panne_se_ferme()
    {
        // Sans service commun ni CI : seul le sens les lie.
        api.Incidents[41] = Incident(41, "Nouveau", perimeter: 11, service: null, minutesAgo: 30, title: "VPN injoignable depuis le site de Lyon", cis: []);
        api.Incidents[42] = Incident(42, "Nouveau", perimeter: 11, service: null, minutesAgo: 20, title: "Impossible de me connecter au VPN", cis: []);
        api.Incidents[43] = Incident(43, "Nouveau", perimeter: 11, service: null, minutesAgo: 10, title: "Le VPN coupe sans cesse", cis: []);
        api.Incidents[44] = Incident(44, "Nouveau", perimeter: 11, service: null, minutesAgo: 5, title: "Imprimante bloquée", cis: []);
        foreach (var id in new[] { 41, 42, 43, 44 }) Event(api, "incident.created", id, 11);

        using var container = Container();
        _ = container.Services;
        var formed = Assert.Single(await OutagesWith(api, 41, o => !Closed(o)));
        Assert.Equal([41, 42, 43], formed.GetProperty("members").EnumerateArray().Select(m => m.GetInt32()));
        Assert.Equal(("sens", "3 incidents ouverts aux descriptions proches"), (formed.GetProperty("basis").GetString(), formed.GetProperty("reasons").GetString()));

        // L'un est résolu : il sort de l'index, il n'en reste que deux, la panne est signalée fermée.
        api.Incidents[42] = Incident(42, "Résolu", perimeter: 11, service: null, minutesAgo: 20, title: "Impossible de me connecter au VPN", cis: []);
        Event(api, "incident.resolved", 42, 11);
        var closed = Assert.Single(await OutagesWith(api, 41, Closed));
        Assert.Equal([41, 42, 43], closed.GetProperty("members").EnumerateArray().Select(m => m.GetInt32()));
    }

    [Fact]
    public async Task TRI_02_a_la_saisie_le_conteneur_suggere_dans_les_perimetres_donnes_avec_les_incidents_ouverts_proches()
    {
        // Deux incidents ouverts sur un service : l'index les connaît dès le premier événement.
        api.Incidents[51] = Incident(51, "Nouveau", perimeter: 12, service: 2, minutesAgo: 15, cis: []);
        api.Incidents[52] = Incident(52, "En cours", perimeter: 12, service: 2, minutesAgo: 5, cis: []);
        Event(api, "incident.created", 52, 12);
        api.Similar = new { semantic = true, neighbours = new[] { Resolved(61, "Messagerie"), Resolved(62, "Messagerie"), Resolved(63, "Messagerie") } };

        using var container = Container();
        var triage = container.Services.GetRequiredService<SAloha.Features.TicketTriage.TicketTriage>();
        for (var i = 0; i < 100 && !triage.Index.Built; i++) await Task.Delay(50);
        Assert.True(triage.Index.Built);

        var client = container.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", FakeInternalApi.Token);
        var before = api.SimilarQueries.Count;
        var res = await client.PostAsJsonAsync("/triage/suggest", new
        {
            title = "Outlook ne reçoit plus les courriels", description = "Depuis ce matin", serviceId = 2, configurationItemIds = Array.Empty<int>(),
            impact = "Moyen", perimeters = new[] { 12, 13 },
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

        // TRI-11 : les voisins se cherchent dans les périmètres de l'agent, sans incident à exclure.
        var query = api.SimilarQueries.Skip(before).Single();
        Assert.Equal([12, 13], query.GetProperty("perimeters").EnumerateArray().Select(e => e.GetInt32()));
        Assert.Equal(JsonValueKind.Null, query.GetProperty("excludeId").ValueKind);
        var suggestions = body.GetProperty("suggestions").EnumerateArray().ToList();
        Assert.Equal("Messagerie", suggestions.Single(x => x.GetProperty("field").GetString() == "category").GetProperty("value").GetString());
        // Les deux incidents ouverts du service sont proches ; avec le brouillon, ils font une panne probable (TRI-04).
        Assert.Equal([52, 51], body.GetProperty("related").EnumerateArray().Select(e => e.GetInt32()));
        var impact = suggestions.Single(x => x.GetProperty("field").GetString() == "impact");
        Assert.Equal(("Élevé", "Panne probable : 3 incidents ouverts liés, celui-ci compris"),
            (impact.GetProperty("value").GetString(), impact.GetProperty("reasons").GetString()));

        // Un titre trop court ne cherche rien ; sans le jeton, rien ne répond (OPT-10).
        var empty = JsonDocument.Parse(await (await client.PostAsJsonAsync("/triage/suggest", new { title = "Out", perimeters = new[] { 12 } }))
            .Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(0, empty.GetProperty("suggestions").GetArrayLength());
        Assert.Equal(HttpStatusCode.Unauthorized, (await container.CreateClient().PostAsJsonAsync("/triage/suggest", new { title = "Outlook muet" })).StatusCode);
    }

    [Fact]
    public async Task TRI_13_les_reglages_de_l_Admin_s_appliquent_au_conteneur()
    {
        // Cinq voisins, catégorie et panne probable coupées.
        api.Settings = new
        {
            neighbours = 5, minVoters = 3, minSharePercent = 50, correlationWindowMinutes = 60, outageThreshold = 3, correlationMinSimilarity = 0.6,
            suggestCategory = false, suggestService = true, suggestGroup = true, suggestImpact = true, suggestOutage = false,
        };
        try
        {
            api.Incidents[71] = Incident(71, "Nouveau", perimeter: 14, minutesAgo: 20, cis: []);
            api.Incidents[72] = Incident(72, "Nouveau", perimeter: 14, minutesAgo: 10, cis: []);
            api.Incidents[73] = Incident(73, "Nouveau", perimeter: 14, minutesAgo: 0, cis: [5]);
            api.Similar = new { semantic = true, neighbours = new[] { Resolved(81, "Messagerie"), Resolved(82, "Messagerie"), Resolved(83, "Messagerie") } };
            foreach (var id in new[] { 71, 72, 73 }) Event(api, "incident.created", id, 14);

            using var container = Container();
            _ = container.Services;
            var proposals = await WaitFor(api, 73, 1);
            await Task.Delay(300);
            // Le nombre de voisins réglé part dans la recherche ; la catégorie coupée n'est pas proposée.
            Assert.Contains(api.SimilarQueries, q => q.GetProperty("excludeId").ValueKind == JsonValueKind.Number
                && q.GetProperty("excludeId").GetInt32() == 73 && q.GetProperty("limit").GetInt32() == 5);
            Assert.DoesNotContain(ProposalsFor(api, 73), p => p.GetProperty("field").GetString() == "category");
            // Panne probable coupée : trois incidents d'un service, aucun signalement ; l'impact vient du CI (Vital).
            Assert.DoesNotContain(api.Outages, o => o.GetProperty("members").EnumerateArray().Any(m => m.GetInt32() == 71));
            Assert.Equal("CI relié de criticité Vitale", proposals.Single(p => p.GetProperty("field").GetString() == "impact").GetProperty("reasons").GetString());
        }
        finally { api.Settings = null; }
    }
}
