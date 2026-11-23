using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;

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
    public readonly ConcurrentQueue<string> Authorizations = new();

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0));
        app = builder.Build();
        app.Use(async (c, next) => { Authorizations.Enqueue(c.Request.Headers.Authorization.ToString()); await next(); });
        app.MapGet("/internal/events", (long after) =>
        {
            if (EventsForbidden) return Results.StatusCode(403);
            var list = Events.Select(e => JsonSerializer.SerializeToElement(e, JsonSerializerOptions.Web))
                .Where(e => e.GetProperty("id").GetInt64() > after).ToList();
            return Results.Json(new { events = list, next = list.Count > 0 ? list[^1].GetProperty("id").GetInt64() : after, gap = false });
        });
        app.MapGet("/internal/incidents/{id:int}", (int id) => Incidents.TryGetValue(id, out var i) ? Results.Json(i) : Results.NotFound());
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
            b.UseSetting("Events:PollMilliseconds", "50");
            b.UseSetting("Events:IdleMilliseconds", "100");
        });

    private static object Incident(int id, string status, int perimeter = 7) => new
    {
        incident = new
        {
            id, perimeterId = perimeter, reference = $"INC-{id}", title = "Outlook ne reçoit plus les courriels", description = "Depuis ce matin",
            status, category = "", serviceId = 1, impact = "Moyen", ownerType = "User", ownerId = "hery.rakoto", ownerDisplayName = "Hery Rakoto",
        },
        configurationItemIds = new[] { 5 },
    };

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
}
