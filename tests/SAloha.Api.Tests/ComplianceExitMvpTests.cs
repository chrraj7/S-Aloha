using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SAloha.Api.Modules.ComplianceAssessment;

namespace SAloha.Api.Tests;

/// <summary>Conformité NIS 2 : preuves (NIS-08), comparaison (NIS-09), plan de traitement des écarts (NIS-14), revue périodique (NIS-16), export (NIS-17), Mon travail du pilote.</summary>
[Collection("api")]
public class ComplianceExitMvpTests(ApiFixture api)
{
    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode} : {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static object Dto(string title, string? status, DateTime? next = null, string owner = "nis.pilote", int? perimeterId = null) => new
    {
        title, description = "Périmètre", status, entityCategory = Assessment.Important,
        ownerType = "User", ownerId = owner, ownerDisplayName = owner, nextAssessmentDate = next, perimeterId
    };

    /// <summary>Évaluation entièrement répondue : 3 partout sauf la première exigence (1, « Inventaire partiel »).</summary>
    private async Task<(int Id, string Reference, string GapCode)> Answered(HttpClient manager, string title, int firstScore = 1, int? perimeterId = null)
    {
        var created = await Json(await manager.PostAsJsonAsync("/api/assessments", Dto(title, null, perimeterId: perimeterId)));
        var id = created.GetProperty("id").GetInt32();
        var q = (await Json(await manager.GetAsync($"/api/assessments/{id}/questionnaire"))).GetProperty("objectives").EnumerateArray()
            .SelectMany(o => o.GetProperty("themes").EnumerateArray())
            .SelectMany(t => t.GetProperty("requirements").EnumerateArray()).ToList();
        foreach (var (r, i) in q.Select((r, i) => (r, i)))
            await Json(await manager.PutAsJsonAsync($"/api/assessments/{id}/responses/{r.GetProperty("id").GetInt32()}",
                new { score = i == 0 ? firstScore : 3, notApplicable = false, justification = i == 0 ? "Inventaire partiel; à compléter" : "" }));
        return (id, created.GetProperty("reference").GetString()!, q[0].GetProperty("code").GetString()!);
    }

    [Fact]
    public async Task NIS_14_17_ecart_avec_son_action_et_export()
    {
        var m = api.Client("Manager", "nis14.manager");
        var (id, _, code) = await Answered(m, "NIS-14");

        var gap = (await Json(await m.GetAsync($"/api/assessments/{id}/score"))).GetProperty("gaps")[0];
        Assert.Equal(code, gap.GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.Null, gap.GetProperty("action").ValueKind);

        var improvement = await Json(await m.PostAsJsonAsync($"/api/assessments/{id}/improvements", new { code }));
        gap = (await Json(await m.GetAsync($"/api/assessments/{id}/score"))).GetProperty("gaps")[0];
        Assert.Equal(improvement.GetProperty("reference").GetString(), gap.GetProperty("action").GetProperty("reference").GetString());
        Assert.Equal("Proposée", gap.GetProperty("action").GetProperty("status").GetString());

        // NIS-17 : une ligne par exigence évaluée, écart et action liée compris ; lisible de tous.
        var export = await Json(await api.Client("User", "nis14.auditeur").GetAsync($"/api/assessments/{id}/export"));
        var rows = export.GetProperty("rows").EnumerateArray().ToList();
        var row = rows.Single(r => r.GetProperty("code").GetString() == code);
        Assert.Equal(1, row.GetProperty("score").GetInt32());
        Assert.Equal(2, row.GetProperty("gap").GetInt32());
        Assert.Equal("Inventaire partiel; à compléter", row.GetProperty("justification").GetString());
        Assert.Equal(improvement.GetProperty("reference").GetString(), row.GetProperty("action").GetString());
        Assert.Equal(JsonValueKind.Null, rows.First(r => r.GetProperty("code").GetString() != code).GetProperty("action").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, (await m.GetAsync("/api/assessments/999999/export")).StatusCode);
    }

    [Fact]
    public async Task NIS_16_prochaine_evaluation_et_relance()
    {
        var m = api.Client("Manager", "nis16.manager");
        var (id, reference, _) = await Answered(m, "NIS-16");
        var url = $"/api/assessments/{id}";

        // Validée sans date : 12 mois après la validation.
        var validated = await Json(await m.PutAsJsonAsync(url, Dto("NIS-16", "Validée")));
        var next = validated.GetProperty("nextAssessmentDate").GetDateTime();
        Assert.Equal(DateTime.UtcNow.Date.AddMonths(12), next.Date);

        // Figée, l'évaluation garde sa date modifiable ; le reste ne l'est pas.
        var yesterday = DateTime.UtcNow.Date.AddDays(-1);
        await Json(await m.PutAsJsonAsync(url, Dto("NIS-16", "Validée", yesterday)));
        Assert.Equal(HttpStatusCode.BadRequest, (await m.PutAsJsonAsync(url, Dto("NIS-16 renommée", "Validée", yesterday))).StatusCode);

        // Échéance atteinte : relance au pilote, une seule fois.
        await Json(await api.Client().PostAsync("/api/notifications/run", null));
        await Json(await api.Client().PostAsync("/api/notifications/run", null));
        var events = (await Json(await api.Client("User", "nis.pilote").GetAsync("/api/notifications"))).GetProperty("items").EnumerateArray()
            .Where(n => n.GetProperty("reference").GetString() == reference).Select(n => n.GetProperty("event").GetString()).ToList();
        Assert.Single(events, e => e!.StartsWith("Évaluation à renouveler"));
        // Console du gestionnaire : relance « À renouveler » (Revues échues).
        var due = (await Json(await m.GetAsync("/api/console"))).GetProperty("management").GetProperty("reviewsDue").EnumerateArray();
        Assert.Contains(due, d => d.GetProperty("reference").GetString() == reference && d.GetProperty("status").GetString() == "À renouveler");
    }

    [Fact]
    public async Task NIS_08_preuves_reliees_a_une_reponse()
    {
        var m = api.Client("Manager", "nis08.manager");
        var u = api.Client("User", "nis08.user");
        var (id, _, code) = await Answered(m, "NIS-08");
        var change = await Json(await u.PostAsJsonAsync("/api/changes", new { title = "Inventaire du SI", changeType = "Normal", risk = "Faible" }));
        var reference = change.GetProperty("reference").GetString();
        var requirement = Requirement(await Json(await u.GetAsync($"/api/assessments/{id}/questionnaire")), code);
        var url = $"/api/assessments/{id}/responses/{requirement.GetProperty("id").GetInt32()}/evidence";

        var evidence = await Json(await u.PostAsJsonAsync(url, new { reference = reference!.ToLower() }));
        Assert.Equal(reference, evidence.GetProperty("reference").GetString());
        Assert.Equal("change", evidence.GetProperty("targetType").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await u.PostAsJsonAsync(url, new { reference })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await u.PostAsJsonAsync(url, new { reference = "CHG-1999-9999" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await u.PostAsJsonAsync(url, new { reference = "n'importe quoi" })).StatusCode);

        // Le questionnaire et l'export portent la preuve.
        requirement = Requirement(await Json(await u.GetAsync($"/api/assessments/{id}/questionnaire")), code);
        Assert.Equal(reference, requirement.GetProperty("evidence")[0].GetProperty("reference").GetString());
        var row = (await Json(await u.GetAsync($"/api/assessments/{id}/export"))).GetProperty("rows").EnumerateArray()
            .Single(r => r.GetProperty("code").GetString() == code);
        Assert.Equal(reference, row.GetProperty("evidence").GetString());

        // Validée, l'évaluation est figée : ni ajout ni retrait (NIS-06).
        var evidenceId = evidence.GetProperty("id").GetInt32();
        await Json(await m.PutAsJsonAsync($"/api/assessments/{id}", Dto("NIS-08", "Validée")));
        Assert.Equal(HttpStatusCode.BadRequest, (await u.DeleteAsync($"/api/assessments/{id}/evidence/{evidenceId}")).StatusCode);
        await Json(await m.PutAsJsonAsync($"/api/assessments/{id}", Dto("NIS-08", "En cours")));
        Assert.Equal(HttpStatusCode.NoContent, (await u.DeleteAsync($"/api/assessments/{id}/evidence/{evidenceId}")).StatusCode);
        requirement = Requirement(await Json(await u.GetAsync($"/api/assessments/{id}/questionnaire")), code);
        Assert.Equal(0, requirement.GetProperty("evidence").GetArrayLength());
    }

    private static JsonElement Requirement(JsonElement questionnaire, string code) =>
        questionnaire.GetProperty("objectives").EnumerateArray()
            .SelectMany(o => o.GetProperty("themes").EnumerateArray())
            .SelectMany(t => t.GetProperty("requirements").EnumerateArray())
            .Single(r => r.GetProperty("code").GetString() == code);

    [Fact]
    public async Task NIS_09_comparaison_de_deux_evaluations_du_meme_perimetre()
    {
        var admin = api.Client();
        var (before, _, code) = await Answered(admin, "NIS-09 avant", firstScore: 1);
        var (after, _, _) = await Answered(admin, "NIS-09 après", firstScore: 3);

        var c = await Json(await api.Client("User", "nis09.user").GetAsync($"/api/assessments/{after}/compare/{before}"));
        Assert.True(c.GetProperty("global").GetProperty("delta").GetDouble() > 0);
        var changed = c.GetProperty("requirements").EnumerateArray().ToList();
        var only = Assert.Single(changed);
        Assert.Equal(code, only.GetProperty("code").GetString());
        Assert.Equal("3", only.GetProperty("current").GetString());
        Assert.Equal("1", only.GetProperty("other").GetString());
        Assert.Equal(2, only.GetProperty("delta").GetInt32());
        Assert.Contains(c.GetProperty("objectives").EnumerateArray(), o => o.GetProperty("score").GetProperty("delta").GetDouble() > 0);

        // Autre périmètre, ou elle-même : refusé.
        var usine = (await Json(await admin.PostAsJsonAsync("/api/admin/perimeters", new { name = "Usine NIS-09" }))).GetProperty("id").GetInt32();
        var (elsewhere, _, _) = await Answered(admin, "NIS-09 usine", perimeterId: usine);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"/api/assessments/{after}/compare/{elsewhere}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"/api/assessments/{after}/compare/{after}")).StatusCode);
    }

    [Fact]
    public async Task NIS_mes_evaluations_ouvertes_dans_mon_travail()
    {
        var m = api.Client("Manager", "nismt.manager");
        var open = await Json(await m.PostAsJsonAsync("/api/assessments", Dto("Mon travail ouverte", null, owner: "nismt.pilote")));
        var (validatedId, _, _) = await Answered(m, "Mon travail validée");
        await Json(await m.PutAsJsonAsync($"/api/assessments/{validatedId}", Dto("Mon travail validée", "Validée", owner: "nismt.pilote")));

        // Le pilote voit son évaluation ouverte dans Mon travail ; une évaluation validée n'y est plus.
        var mine = (await Json(await api.Client("User", "nismt.pilote").GetAsync("/api/console"))).GetProperty("myAssignments").EnumerateArray().ToList();
        Assert.Contains(mine, a => a.GetProperty("type").GetString() == "assessment" && a.GetProperty("id").GetInt32() == open.GetProperty("id").GetInt32()
                                   && a.GetProperty("status").GetString() == "Brouillon");
        Assert.DoesNotContain(mine, a => a.GetProperty("type").GetString() == "assessment" && a.GetProperty("id").GetInt32() == validatedId);

        // Un autre compte ne la voit pas dans son Mon travail.
        var other = (await Json(await api.Client("User", "nismt.autre").GetAsync("/api/console"))).GetProperty("myAssignments").EnumerateArray();
        Assert.DoesNotContain(other, a => a.GetProperty("type").GetString() == "assessment");
    }
}
