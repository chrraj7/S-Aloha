using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace SAloha.Api.Tests;

/// <summary>Incidents, lot 3 : incidents enfants (INC-16), relance après 5 jours ouvrés En attente (INC-26).</summary>
[Collection("api")]
public class IncidentLot3Tests(ApiFixture api)
{
    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode} : {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static int Id(JsonElement e) => e.GetProperty("id").GetInt32();
    private static string? Str(JsonElement e, string p) => e.GetProperty(p).GetString();

    private static object Incident(string title, string? status = null, string owner = "tech.inc16", string? onHold = null,
        string? resolution = null, string? code = null) => new
    {
        title, description = "d", impact = "Moyen", urgency = "Moyenne", category = "", affectedService = "", status, isMajor = false,
        ownerType = "User", ownerId = owner, ownerDisplayName = owner, onHoldReason = onHold, resolution, resolutionCode = code
    };

    [Fact]
    public async Task INC_16_resoudre_le_parent_resout_ses_enfants_avec_la_meme_resolution()
    {
        var agent = api.Client("User", "tech.inc16");
        var parent = await Json(await agent.PostAsJsonAsync("/api/incidents", Incident("Coupure du réseau du bâtiment B")));
        var child = await Json(await agent.PostAsJsonAsync("/api/incidents", Incident("Pas d'accès à la messagerie (bâtiment B)")));
        var held = await Json(await agent.PostAsJsonAsync("/api/incidents", Incident("Imprimante injoignable (bâtiment B)")));
        var other = await Json(await agent.PostAsJsonAsync("/api/incidents", Incident("Écran cassé")));
        async Task<HttpResponseMessage> Attach(JsonElement i, string? reference) =>
            await agent.PutAsJsonAsync($"/api/incidents/{Id(i)}/parent", new { parentReference = reference });

        Assert.Equal(HttpStatusCode.BadRequest, (await Attach(parent, Str(parent, "reference"))).StatusCode);   // soi-même
        Assert.Equal(HttpStatusCode.BadRequest, (await Attach(child, "INC-1900-0001")).StatusCode);            // introuvable
        Assert.Equal(Id(parent), (await Json(await Attach(child, Str(parent, "reference")))).GetProperty("parentId").GetInt32());
        await Json(await Attach(held, Str(parent, "reference")));
        // Un seul niveau : ni petit-enfant, ni parent rattaché à son tour.
        Assert.Equal(HttpStatusCode.BadRequest, (await Attach(other, Str(child, "reference"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Attach(parent, Str(other, "reference"))).StatusCode);
        await Json(await Attach(other, Str(parent, "reference")));
        Assert.Equal(JsonValueKind.Null, (await Json(await Attach(other, ""))).GetProperty("parentId").ValueKind);   // détaché

        var family = await Json(await agent.GetAsync($"/api/incidents/{Id(parent)}/family"));
        Assert.Equal(2, family.GetProperty("children").GetArrayLength());
        Assert.Equal(Str(parent, "reference"), Str((await Json(await agent.GetAsync($"/api/incidents/{Id(child)}/family"))).GetProperty("parent"), "reference"));

        await Json(await agent.PutAsJsonAsync($"/api/incidents/{Id(held)}", Incident("Imprimante injoignable (bâtiment B)", "En cours")));
        await Json(await agent.PutAsJsonAsync($"/api/incidents/{Id(held)}", Incident("Imprimante injoignable (bâtiment B)", "En attente", onHold: "Attente fournisseur")));
        await Json(await agent.PutAsJsonAsync($"/api/incidents/{Id(parent)}",
            Incident("Coupure du réseau du bâtiment B", "Résolu", resolution: "Commutateur remplacé", code: "Correctif appliqué")));
        foreach (var c in new[] { child, held })
        {
            var r = await Json(await agent.GetAsync($"/api/incidents/{Id(c)}"));
            Assert.Equal(("Résolu", "Commutateur remplacé", "Correctif appliqué"), (Str(r, "status"), Str(r, "resolution"), Str(r, "resolutionCode")));
            Assert.Equal(JsonValueKind.Null, r.GetProperty("onHoldSince").ValueKind);
        }
        Assert.Equal("Nouveau", Str(await Json(await agent.GetAsync($"/api/incidents/{Id(other)}")), "status"));   // détaché : inchangé
        Assert.Equal(HttpStatusCode.BadRequest, (await Attach(child, null)).StatusCode);   // résolu : rattachement figé
    }

    [Fact]
    public async Task INC_15_INC_16_la_cascade_saute_les_enfants_majeurs_sans_probleme()
    {
        // Décision du 2026-10-26 (#200) : INC-15 sans exception ; le majeur sans problème reste ouvert.
        var m = api.Client("Manager", "chef.inc16m");
        object Major(string title) => new
        {
            title, description = "d", impact = "Élevé", urgency = "Élevée", category = "", affectedService = "", isMajor = true,
            ownerType = "User", ownerId = "chef.inc16m", ownerDisplayName = "chef.inc16m"
        };
        var parent = await Json(await m.PostAsJsonAsync("/api/incidents", Incident("Panne du stockage partagé", owner: "chef.inc16m")));
        var alone = await Json(await m.PostAsJsonAsync("/api/incidents", Major("Paie inaccessible (stockage)")));
        var linked = await Json(await m.PostAsJsonAsync("/api/incidents", Major("Messagerie inaccessible (stockage)")));
        var plain = await Json(await m.PostAsJsonAsync("/api/incidents", Incident("Partage personnel absent", owner: "chef.inc16m")));
        foreach (var c in new[] { alone, linked, plain })
            await Json(await m.PutAsJsonAsync($"/api/incidents/{Id(c)}/parent", new { parentReference = Str(parent, "reference") }));
        var p = await Json(await m.PostAsJsonAsync("/api/problems", new
            { title = "Cause de la panne du stockage", description = "d", impact = "Moyen", urgency = "Moyenne", category = "", affectedService = "", source = "Proactif" }));
        await Json(await m.PostAsJsonAsync("/api/links", new { fromType = "incident", fromId = Id(linked), toReference = Str(p, "reference") }));

        var resolved = await Json(await m.PutAsJsonAsync($"/api/incidents/{Id(parent)}",
            Incident("Panne du stockage partagé", "Résolu", owner: "chef.inc16m", resolution: "Contrôleur remplacé", code: "Correctif appliqué")));
        Assert.Equal("Résolu", Str(resolved, "status"));
        var left = resolved.GetProperty("unresolvedChildren").EnumerateArray().ToList();
        Assert.Equal(Str(alone, "reference"), Str(Assert.Single(left), "reference"));
        Assert.Contains("problème", Str(left[0], "reason"));
        Assert.Equal("Nouveau", Str(await Json(await m.GetAsync($"/api/incidents/{Id(alone)}")), "status"));
        foreach (var c in new[] { linked, plain })
            Assert.Equal("Résolu", Str(await Json(await m.GetAsync($"/api/incidents/{Id(c)}")), "status"));
    }

    [Fact]
    public async Task INC_26_en_attente_depuis_plus_de_5_jours_ouvres_remonte_au_responsable()
    {
        var owner = api.Client("User", "tech.inc26");
        var i = await Json(await owner.PostAsJsonAsync("/api/incidents", Incident("Licence en attente du fournisseur", owner: "tech.inc26")));
        var url = $"/api/incidents/{Id(i)}";
        await Json(await owner.PutAsJsonAsync(url, Incident("Licence en attente du fournisseur", "En cours", owner: "tech.inc26")));
        await Json(await owner.PutAsJsonAsync(url, Incident("Licence en attente du fournisseur", "En attente", owner: "tech.inc26", onHold: "Attente fournisseur")));
        Assert.Equal(JsonValueKind.Null, (await Json(await owner.GetAsync(url))).GetProperty("holdFollowUp").ValueKind);
        await using (var db = api.Database())
        {
            var e = await db.Incidents.SingleAsync(x => x.Id == Id(i));
            e.OnHoldSince = DateTime.UtcNow.AddDays(-10);
            await db.SaveChangesAsync();
        }
        Assert.Contains("Attente fournisseur", Str(await Json(await owner.GetAsync(url)), "holdFollowUp"));
        var console = await Json(await owner.GetAsync("/api/console"));
        Assert.Contains(console.GetProperty("myIncidentFollowUps").EnumerateArray(), x => x.GetProperty("id").GetInt32() == Id(i));
        var managers = await Json(await api.Client("Manager", "chef.inc26").GetAsync("/api/console"));
        Assert.Contains(managers.GetProperty("management").GetProperty("incidentFollowUps").EnumerateArray(), x => x.GetProperty("id").GetInt32() == Id(i));

        await Json(await api.Client("Admin", "admin.inc26").PostAsync("/api/notifications/run", null));
        await using var check = api.Database();
        var sent = await check.Notifications.Where(n => n.TargetType == "incident" && n.TargetId == Id(i) && n.RecipientId == "tech.inc26"
                                                        && n.Event.StartsWith("En attente depuis")).CountAsync();
        Assert.Equal(1, sent);
        await Json(await api.Client("Admin", "admin.inc26").PostAsync("/api/notifications/run", null));
        Assert.Equal(1, await check.Notifications.CountAsync(n => n.TargetType == "incident" && n.TargetId == Id(i) && n.Event.StartsWith("En attente depuis")));
    }
}
