using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Audit;
using SAloha.Api.Core.Features;
using SAloha.Api.Modules.IncidentManagement;

namespace SAloha.Api.Tests;

/// <summary>Mesure du tri (TRI-10), suggestions appliquées à la saisie (TRI-09, OPT-08), conservation des pannes (TRI-14).</summary>
[Collection("api")]
public class TriageMetricsTests(ApiFixture api) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        // Le démarrage de l'API applique les migrations : lancé seul, ce test arrive avant elles.
        await api.Client().GetAsync("/health/live");
        await SwitchAsync(FeatureCatalog.AssistedTriage);
        // La mesure porte sur toutes les propositions du tri : chaque test part d'un compte nul.
        await using var db = api.Database();
        await db.Proposals.Where(p => p.Feature == FeatureCatalog.AssistedTriage).ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => SwitchAsync();

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
            .UseSetting("Discovery:VaultKey", "cle-du-coffre-de-test-uniquement-0123456789"));

    private static HttpClient Admin(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFixture.Token("hery.rakoto", "Admin"));
        return client;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode} : {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static HttpRequestMessage Create(string title, string impact, string? suggestions)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/incidents")
        {
            Content = JsonContent.Create(new { title, description = "d", impact, urgency = "Moyenne", isMajor = false }),
        };
        if (suggestions is not null) req.Headers.Add(ProposalDecisions.SuggestionsHeader, Uri.EscapeDataString(suggestions));
        return req;
    }

    private const string Applied = """[{"feature":"assisted-ticket-triage","field":"impact","value":"Élevé"}]""";

    [Fact]
    public async Task TRI_09_une_suggestion_appliquee_a_la_saisie_compte_acceptee_et_se_trace_avec_son_origine()
    {
        using var factory = Factory();
        var admin = Admin(factory);
        var created = await Json(await admin.SendAsync(Create("Messagerie coupée (TRI-09)", "Élevé", Applied)));
        var id = created.GetProperty("id").GetInt32();
        await using (var db = api.Database())
        {
            var p = await db.Proposals.AsNoTracking().SingleAsync(x => x.TargetType == "incident" && x.TargetId == id);
            Assert.Equal((Proposal.Accepted, "impact", "Élevé", "Élevé"), (p.Status, p.Field, p.After, p.FinalValue));
            var line = await db.AuditEntries.AsNoTracking().SingleAsync(a => a.EntityType == "incident" && a.EntityId == id && a.Field == "impact");
            Assert.Equal((AuditEntry.AssistedTriage, "Élevé"), (line.Origin, line.NewValue));
        }

        // La valeur changée avant l'enregistrement, un en-tête illisible : rien n'est compté, la création passe.
        var changed = await Json(await admin.SendAsync(Create("Messagerie lente (TRI-09)", "Faible", Applied)));
        var broken = await Json(await admin.SendAsync(Create("Messagerie muette (TRI-09)", "Élevé", "{pas du JSON")));
        await using (var db = api.Database())
        {
            var ids = new[] { changed.GetProperty("id").GetInt32(), broken.GetProperty("id").GetInt32() };
            Assert.False(await db.Proposals.AnyAsync(x => x.TargetType == "incident" && ids.Contains(x.TargetId)));
        }

        // Tri coupé : l'en-tête est ignoré.
        await SwitchAsync();
        using var off = Factory();
        var ignored = await Json(await Admin(off).SendAsync(Create("Messagerie hors tri (TRI-09)", "Élevé", Applied)));
        await using (var db = api.Database())
            Assert.False(await db.Proposals.AnyAsync(x => x.TargetType == "incident" && x.TargetId == ignored.GetProperty("id").GetInt32()));
    }

    [Fact]
    public async Task TRI_10_la_mesure_compte_par_champ_les_acceptees_corrigees_rejetees_et_laissees_sur_30_et_90_jours()
    {
        using var factory = Factory();
        var admin = Admin(factory);
        async Task<int> Incident(string title, string? status = null)
        {
            var id = (await Json(await admin.SendAsync(Create(title, "Moyen", null)))).GetProperty("id").GetInt32();
            if (status is not null)
                await using (var db = api.Database())
                    await db.Incidents.Where(i => i.Id == id).ExecuteUpdateAsync(i => i.SetProperty(x => x.Status, status));
            return id;
        }
        var fresh = await Incident("Nouveau (TRI-10)");
        var taken = await Incident("En cours (TRI-10)", "En cours");
        var now = DateTime.UtcNow;
        Proposal P(string field, string status, int target, int daysAgo) => new()
        {
            Feature = FeatureCatalog.AssistedTriage, TargetType = "incident", TargetId = target, PerimeterId = 1, Field = field,
            After = "x", Reasons = "r", CitedJson = "[]", Status = status, CreatedAt = now.AddDays(-daysAgo),
            DecidedAt = status == Proposal.Open ? null : now.AddDays(-daysAgo),
        };
        await using (var db = api.Database())
        {
            db.Proposals.AddRange(
                P("category", Proposal.Accepted, taken, 2), P("category", Proposal.Accepted, taken, 3), P("category", Proposal.Corrected, taken, 4),
                P("category", Proposal.Rejected, taken, 5),
                P("category", Proposal.Open, taken, 1),    // laissée : l'incident a quitté « Nouveau »
                P("category", Proposal.Expired, taken, 6), // laissée : expirée
                P("category", Proposal.Open, fresh, 1),    // en attente : l'incident est encore Nouveau
                P("category", Proposal.Accepted, taken, 60),  // seulement sur 90 jours
                P("category", Proposal.Accepted, taken, 120), // hors des deux fenêtres
                P("owner", Proposal.Corrected, taken, 2));
            await db.SaveChangesAsync();
        }

        var windows = (await Json(await admin.GetAsync("/api/admin/triage/metrics"))).GetProperty("windows").EnumerateArray().ToList();
        JsonElement Field(int window, string field) => windows[window].GetProperty("fields").EnumerateArray().Single(f => f.GetProperty("field").GetString() == field);
        var month = Field(0, "category");
        Assert.Equal(30, windows[0].GetProperty("days").GetInt32());
        Assert.Equal((6, 2, 1, 1, 2, 1), (month.GetProperty("decided").GetInt32(), month.GetProperty("accepted").GetInt32(),
            month.GetProperty("corrected").GetInt32(), month.GetProperty("rejected").GetInt32(), month.GetProperty("left").GetInt32(),
            month.GetProperty("pending").GetInt32()));
        Assert.Equal((33, 17, 17, 33), (month.GetProperty("acceptedShare").GetInt32(), month.GetProperty("correctedShare").GetInt32(),
            month.GetProperty("rejectedShare").GetInt32(), month.GetProperty("leftShare").GetInt32()));
        Assert.Equal(3, Field(1, "category").GetProperty("accepted").GetInt32());   // 90 jours : une de plus
        Assert.Equal(100, Field(0, "owner").GetProperty("correctedShare").GetInt32());
        Assert.Equal(0, Field(0, "impact").GetProperty("decided").GetInt32());

        // Admin seul ; tri coupé, la route n'existe pas (OPT-04).
        var manager = factory.CreateClient();
        manager.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFixture.Token("lova.rabe", "Manager"));
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/admin/triage/metrics")).StatusCode);
        await SwitchAsync();
        using var off = Factory();
        Assert.Equal(HttpStatusCode.NotFound, (await Admin(off).GetAsync("/api/admin/triage/metrics")).StatusCode);
    }

    [Fact]
    public async Task TRI_14_une_panne_probable_fermee_se_purge_au_bout_de_13_mois()
    {
        using var factory = Factory();
        var admin = Admin(factory);
        var a = (await Json(await admin.SendAsync(Create("Panne ancienne (TRI-14)", "Moyen", null)))).GetProperty("id").GetInt32();
        var now = DateTime.UtcNow;
        int old, recent, open;
        await using (var db = api.Database())
        {
            var perimeter = await db.Incidents.Where(i => i.Id == a).Select(i => i.PerimeterId).SingleAsync();
            ProbableOutage O(DateTime? closed) => new() { PerimeterId = perimeter, Basis = "service", ClosedAt = closed, Members = [new() { IncidentId = a }] };
            var rows = new[] { O(now.AddMonths(-14)), O(now.AddMonths(-2)), O(null) };
            db.ProbableOutages.AddRange(rows);
            await db.SaveChangesAsync();
            (old, recent, open) = (rows[0].Id, rows[1].Id, rows[2].Id);
            await ProposalsRetention.RunAsync(db, now);
        }
        await using (var db = api.Database())
        {
            var left = await db.ProbableOutages.AsNoTracking().Select(o => o.Id).ToListAsync();
            Assert.DoesNotContain(old, left);
            Assert.Contains(recent, left);
            Assert.Contains(open, left);
            Assert.False(await db.Set<ProbableOutageMember>().AnyAsync(m => m.OutageId == old));
        }
    }
}
