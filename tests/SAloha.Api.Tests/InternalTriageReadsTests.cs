using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SAloha.Api.Core.Features;
using SAloha.Api.Core.Search;

namespace SAloha.Api.Tests;

/// <summary>Lectures internes du tri assisté (OPT-13) : vues de l'API publique, jeton, fonctionnalité activée, périmètres.</summary>
[Collection("api")]
public class InternalTriageReadsTests(ApiFixture api) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        // Le démarrage de l'API applique les migrations : lancé seul, ce test arrive avant elles.
        await api.Client().GetAsync("/health/live");
        await SwitchAsync();
    }

    public Task DisposeAsync() => SwitchAsync();

    /// <summary>Fonctionnalités activées en base (aucune par défaut) ; chaque fabrique relit la base.</summary>
    private async Task SwitchAsync(params string[] enabled)
    {
        await using var db = api.Database();
        await db.OptionalFeatures.ExecuteDeleteAsync();
        foreach (var key in enabled) db.OptionalFeatures.Add(new OptionalFeature { Key = key, Enabled = true, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    private WebApplicationFactory<Program> Factory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Default", api.Database().Database.GetConnectionString())
            .UseSetting("Jwt:Key", ApiFixture.JwtKey)
            .UseSetting("Jwt:Issuer", "S-Aloha")
            .UseSetting("Discovery:IntervalMinutes", "0")
            .UseSetting("Discovery:VaultKey", "cle-du-coffre-de-test-uniquement-0123456789")
            .UseSetting("Features:Token", FakeFeaturesContainer.Token)
            .UseSetting("Features:InternalPort", "8081")
            .ConfigureTestServices(s =>
            {
                s.RemoveAll<IEmbeddingClient>();
                s.AddSingleton<IEmbeddingClient, FakeEmbeddingClient>();
            }));

    /// <summary>Requête reçue sur le port interne (le serveur de test n'ouvre pas de socket : le port est posé sur la connexion).</summary>
    private static async Task<(int Status, JsonElement? Body)> Internal(WebApplicationFactory<Program> factory, string method, string path,
        object? body = null, string? bearer = FakeFeaturesContainer.Token)
    {
        var uri = new Uri("http://api" + path);
        var json = body is null ? null : Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body, JsonSerializerOptions.Web));
        var context = await factory.Server.SendAsync(c =>
        {
            c.Request.Method = method;
            c.Request.Path = uri.AbsolutePath;
            c.Request.QueryString = new QueryString(uri.Query);
            c.Connection.LocalPort = 8081;
            if (json is not null)
            {
                c.Request.ContentType = "application/json";
                c.Request.ContentLength = json.Length;
                c.Request.Body = new MemoryStream(json);
            }
            if (bearer is not null) c.Request.Headers.Authorization = $"Bearer {bearer}";
        });
        var text = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return (context.Response.StatusCode, text.Length > 0 && text[0] is '{' or '[' ? JsonDocument.Parse(text).RootElement : null);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode} : {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static int Id(JsonElement e) => e.GetProperty("id").GetInt32();

    private static object Incident(string title, int perimeter, string status = "Nouveau", int? serviceId = null) => new
    {
        title, description = "Les courriels n'arrivent plus dans la boîte de réception", status, impact = "Moyen", urgency = "Moyenne",
        perimeterId = perimeter, serviceId, isMajor = false,
        resolution = status == "Résolu" ? "Redémarrage du connecteur" : null, resolutionCode = status == "Résolu" ? "Correctif appliqué" : null,
    };

    /// <summary>Un incident résolu : créé, puis résolu (il n'entre dans l'index qu'à ce moment, RAG-03).</summary>
    private async Task<JsonElement> ResolvedAsync(string title, int perimeter)
    {
        var admin = api.Client();
        var created = await Json(await admin.PostAsJsonAsync("/api/incidents", Incident(title, perimeter)));
        return await Json(await admin.PutAsJsonAsync($"/api/incidents/{Id(created)}", Incident(title, perimeter, "Résolu")));
    }

    private static List<JsonElement> Neighbours(JsonElement body) => body.GetProperty("neighbours").EnumerateArray().ToList();

    [Fact]
    public async Task OPT_13_les_lectures_du_tri_exigent_le_jeton_et_une_fonctionnalite_qui_s_en_sert()
    {
        var similar = new { title = "Messagerie", perimeters = new[] { 1 } };
        (string Method, string Path, object? Body, bool Cmdb)[] routes =
        [
            ("GET", "/internal/incidents/1", null, false),
            ("POST", "/internal/search/similar", similar, false),
            ("GET", "/internal/categories?practice=incident", null, false),
            ("GET", "/internal/configuration-items?ids=1", null, true),
            ("GET", "/internal/services", null, true),
        ];
        using (var off = Factory())
            foreach (var (method, path, body, _) in routes)
            {
                Assert.Equal(401, (await Internal(off, method, path, body, bearer: null)).Status);
                Assert.Equal(401, (await Internal(off, method, path, body, bearer: ApiFixture.Token("hery.rakoto", "Admin"))).Status);
                Assert.Equal(403, (await Internal(off, method, path, body)).Status);   // aucune fonctionnalité activée
            }

        // CMDB assistée seule : les CI et les services lui servent aussi, pas les lectures des incidents.
        await SwitchAsync(FeatureCatalog.AssistedCmdb);
        using (var cmdb = Factory())
            foreach (var (method, path, body, shared) in routes)
                Assert.Equal(shared ? 200 : 403, (await Internal(cmdb, method, path, body)).Status);

        await SwitchAsync(FeatureCatalog.AssistedTriage);
        using var triage = Factory();
        foreach (var (method, path, body, _) in routes.Where(r => !r.Path.StartsWith("/internal/incidents/")))
            Assert.Equal(200, (await Internal(triage, method, path, body)).Status);
        Assert.Equal(404, (await Internal(triage, "GET", "/internal/incidents/999999")).Status);
        // Sur le port public, les mêmes routes n'existent pas (OPT-11).
        Assert.Equal(HttpStatusCode.NotFound, (await triage.CreateClient().GetAsync("/internal/services")).StatusCode);
    }

    [Fact]
    public async Task OPT_13_un_incident_se_lit_dans_sa_vue_publique_avec_ses_CI_relies_et_leur_criticite()
    {
        var admin = api.Client();
        var perimeter = Id(await Json(await admin.PostAsJsonAsync("/api/admin/perimeters", new { name = "Siège OPT-13 lecture" })));
        var service = await Json(await admin.PostAsJsonAsync("/api/services", new
        {
            title = "Messagerie OPT-13", status = "En service", criticality = "Élevée", serviceHours = "24/7", perimeterId = perimeter,
            ownerType = "User", ownerId = "svc.opt13", ownerDisplayName = "Responsable",
        }));
        var ci = await Json(await admin.PostAsJsonAsync("/api/configuration-items", new
        {
            title = "Exchange OPT-13", ciType = "Application", environment = "Production", status = "Planifié", perimeterId = perimeter,
            ownerType = "User", ownerId = "ci.opt13", ownerDisplayName = "Responsable",
        }));
        var incident = await Json(await admin.PostAsJsonAsync("/api/incidents", Incident("Outlook muet OPT-13", perimeter, serviceId: Id(service))));
        await Json(await admin.PostAsJsonAsync("/api/links", new { fromType = "incident", fromId = Id(incident), toReference = ci.GetProperty("reference").GetString() }));
        await using (var db = api.Database())
            await db.ConfigurationItems.Where(c => c.Id == Id(ci)).ExecuteUpdateAsync(c => c.SetProperty(x => x.Criticality, "Vitale"));

        await SwitchAsync(FeatureCatalog.AssistedTriage);
        using var factory = Factory();
        var (status, read) = await Internal(factory, "GET", $"/internal/incidents/{Id(incident)}");
        Assert.Equal(200, status);
        var view = read!.Value.GetProperty("incident");
        Assert.Equal(Id(service), view.GetProperty("serviceId").GetInt32());
        Assert.Equal("Nouveau", view.GetProperty("status").GetString());
        Assert.Equal(perimeter, view.GetProperty("perimeterId").GetInt32());
        Assert.Equal([Id(ci)], read.Value.GetProperty("configurationItemIds").EnumerateArray().Select(e => e.GetInt32()));

        var (_, cis) = await Internal(factory, "GET", $"/internal/configuration-items?ids={Id(ci)},999999");
        Assert.Equal("Vitale", Assert.Single(cis!.Value.EnumerateArray()).GetProperty("criticality").GetString());
        var (_, services) = await Internal(factory, "GET", "/internal/services");
        var listed = services!.Value.EnumerateArray().Single(s => Id(s) == Id(service));
        Assert.Equal(("Élevée", "En service"), (listed.GetProperty("criticality").GetString(), listed.GetProperty("status").GetString()));
        var (_, categories) = await Internal(factory, "GET", "/internal/categories?practice=incident");
        Assert.Equal(JsonValueKind.Array, categories!.Value.ValueKind);
        Assert.Equal(400, (await Internal(factory, "GET", "/internal/categories?practice=change")).Status);
        Assert.Equal(400, (await Internal(factory, "GET", "/internal/configuration-items?ids=" + string.Join(',', Enumerable.Range(1, 51)))).Status);
    }

    [Fact]
    public async Task TRI_03_les_voisins_sont_les_incidents_resolus_les_plus_proches_dans_les_seuls_perimetres_donnes()
    {
        var admin = api.Client();
        var siege = Id(await Json(await admin.PostAsJsonAsync("/api/admin/perimeters", new { name = "Siège TRI-03 voisins" })));
        var usine = Id(await Json(await admin.PostAsJsonAsync("/api/admin/perimeters", new { name = "Usine TRI-03 voisins" })));
        var near = await ResolvedAsync("Messagerie ZV3A bloquée", siege);
        var other = await ResolvedAsync("Messagerie ZV3A bloquée à l'usine", usine);
        var open = await Json(await admin.PostAsJsonAsync("/api/incidents", Incident("Messagerie ZV3A bloquée encore", siege)));

        await SwitchAsync(FeatureCatalog.AssistedTriage);
        using var factory = Factory();
        var draft = new { title = "Messagerie ZV3A bloquée", description = "Plus aucun courriel", perimeters = new[] { siege } };
        JsonElement body = default;
        for (var i = 0; i < 50; i++)   // indexation en tâche de fond (RAG-03)
        {
            body = (await Internal(factory, "POST", "/internal/search/similar", draft)).Body!.Value;
            if (Neighbours(body).Any(n => Id(n.GetProperty("incident")) == Id(near))) break;
            await Task.Delay(200);
        }
        var ids = Neighbours(body).Select(n => Id(n.GetProperty("incident"))).ToList();
        Assert.Contains(Id(near), ids);
        Assert.DoesNotContain(Id(other), ids);   // TRI-11 : un autre périmètre ne vote jamais
        Assert.DoesNotContain(Id(open), ids);    // RAG-01 : un incident ouvert n'est pas dans l'index
        Assert.True(body.GetProperty("semantic").GetBoolean());
        var neighbour = Neighbours(body).Single(n => Id(n.GetProperty("incident")) == Id(near));
        Assert.Equal("Résolu", neighbour.GetProperty("incident").GetProperty("status").GetString());
        Assert.True(neighbour.GetProperty("score").GetDouble() > 0);

        // Sans service d'embeddings : les mots seuls, et la réponse le dit (motif « par les mots seuls », TRI-08).
        FakeEmbeddingClient.Available = false;
        try
        {
            var (_, words) = await Internal(factory, "POST", "/internal/search/similar", draft);
            Assert.False(words!.Value.GetProperty("semantic").GetBoolean());
            Assert.Contains(Id(near), Neighbours(words.Value).Select(n => Id(n.GetProperty("incident"))));
        }
        finally { FakeEmbeddingClient.Available = true; }

        // L'incident traité s'exclut ; sans périmètre ni titre, rien ne se cherche.
        var (_, excluded) = await Internal(factory, "POST", "/internal/search/similar", new { draft.title, draft.description, draft.perimeters, excludeId = Id(near) });
        Assert.DoesNotContain(Id(near), Neighbours(excluded!.Value).Select(n => Id(n.GetProperty("incident"))));
        Assert.Equal(400, (await Internal(factory, "POST", "/internal/search/similar", new { draft.title })).Status);
        Assert.Equal(400, (await Internal(factory, "POST", "/internal/search/similar", new { title = " ", draft.perimeters })).Status);
        var (_, none) = await Internal(factory, "POST", "/internal/search/similar", new { draft.title, perimeters = Array.Empty<int>() });
        Assert.Empty(Neighbours(none!.Value));
    }
}
