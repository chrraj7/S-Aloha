using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace SAloha.Mcp;

/// <summary>
/// MCP-05 : les six outils du serveur, tous en lecture ; aucun outil d'écriture n'est déclaré. Chacun appelle les
/// routes publiques de l'API avec le jeton de l'utilisateur (MCP-02), donc avec sa portée (MCP-06) : un enregistrement
/// hors de ses périmètres est « introuvable ». Chaque appel laisse une ligne au journal (MCP-09).
/// </summary>
[McpServerToolType]
public sealed partial class ReadTools(PublicApi api, IOptions<McpLimits> options, IHttpContextAccessor context, ILogger<ReadTools> logger)
{
    /// <summary>Clé du tri assisté des tickets dans le catalogue de l'API.</summary>
    public const string AssistedTriage = "assisted-ticket-triage";

    private sealed record Kind(string Code, string Type, string Path, string Label);

    /// <summary>Types désignés par une référence XXX-AAAA-NNNN (<c>ItemLinks.Kinds</c> de l'API) et leur registre public.</summary>
    private static readonly Kind[] Kinds =
    [
        new("INC", "incident", "incidents", "un incident"), new("REQ", "request", "requests", "une demande"),
        new("PRB", "problem", "problems", "un problème"), new("CHG", "change", "changes", "un changement"),
        new("CI", "ci", "configuration-items", "un CI"), new("SVC", "service", "services", "un service"),
        new("SLA", "agreement", "agreements", "un accord de niveau de service"), new("KB", "article", "knowledge", "un article"),
        new("AMI", "improvement", "improvements", "une amélioration"), new("EVA", "assessment", "assessments", "une évaluation"),
        new("PRJ", "project", "projects", "un projet"), new("DRG", "notification", "regulatory-notifications", "une notification réglementaire"),
    ];

    /// <summary>Sections personnelles de la console (« Mon travail ») ; le bloc de pilotage n'en fait pas partie.</summary>
    private static readonly string[] MyWork =
    [
        "myAssignments", "mySlaIncidents", "myIncidentFollowUps", "myChangeFollowUps", "myProblems", "myActions",
        "myProjectAlerts", "myUnhelpfulArticles", "myFollowUpChecks", "myCisToVerify",
    ];

    [GeneratedRegex(@"^([A-Z]{2,3})-\d{4}-\d{1,6}$")]
    private static partial Regex ReferencePattern();

    private McpLimits Limits => options.Value;

    private sealed record Answer(string Outcome, int Count, CallToolResult Result);

    [McpServerTool(Name = "search", Title = "Rechercher dans S-Aloha", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Recherche libre dans S-Aloha : articles publiés de la base de connaissance, problèmes et erreurs connues, incidents "
        + "résolus. Rend pour chaque résultat sa référence, son type, son statut et un extrait.")]
    public Task<CallToolResult> Search(
        [Description("Texte recherché, en langage courant (symptôme, message d'erreur, nom d'application…)")] string query,
        [Description("Types à chercher, séparés par des virgules, parmi article, problem, incident ; tous par défaut")] string? types = null,
        [Description("Nombre de résultats, 10 par défaut")] int limit = 10,
        CancellationToken ct = default) => RunAsync("search", async shaper =>
        {
            var text = (query ?? "").Trim();
            if (text.Length == 0) return Fail("invalide", "Texte de recherche requis.");
            if (text.Length > Limits.MaxTextLength) text = text[..Limits.MaxTextLength];
            var path = $"api/search?q={Uri.EscapeDataString(text)}&limit={Clamp(limit, 10)}"
                       + (string.IsNullOrWhiteSpace(types) ? "" : $"&types={Uri.EscapeDataString(types.Trim())}");
            var found = await api.GetAsync(path, ct);
            if (!found.Ok) return FromStatus(found, "la recherche");
            var results = shaper.Shape(found.Body) as JsonArray ?? [];
            return Done(new JsonObject { ["results"] = results }, shaper, results.Count);
        }, ct);

    [McpServerTool(Name = "get_record", Title = "Lire une fiche", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Une fiche de S-Aloha par sa référence (INC-2026-0012, REQ-…, PRB-…, CHG-…, CI-…, SVC-…, SLA-…, KB-…, AMI-…, EVA-…, "
        + "PRJ-…, DRG-…) : la vue que l'interface en montre à l'utilisateur, avec ses liens et ses commentaires.")]
    public Task<CallToolResult> GetRecord(
        [Description("Référence de la fiche, par exemple INC-2026-0012")] string reference,
        CancellationToken ct = default) => RunAsync("get_record", async shaper =>
        {
            var (kind, item, failure) = await ResolveAsync(reference, null, ct);
            if (failure is not null) return failure;
            var id = IdOf(item!);
            var record = await api.GetAsync($"api/{kind!.Path}/{id}", ct);
            if (!record.Ok) return FromStatus(record, Reference(item!));
            var links = await api.GetAsync($"api/links?type={kind.Type}&id={id}", ct);
            var comments = await api.GetAsync($"api/comments?type={kind.Type}&id={id}", ct);
            return Done(new JsonObject
            {
                ["type"] = kind.Type,
                ["record"] = shaper.Shape(record.Body),
                ["links"] = links.Ok ? shaper.Shape(links.Body) : new JsonArray(),
                ["comments"] = comments.Ok ? shaper.Shape(comments.Body?["comments"]) ?? new JsonArray() : new JsonArray(),
            }, shaper, 1);
        }, ct);

    [McpServerTool(Name = "similar_cases", Title = "Cas similaires", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Cas similaires à un incident, un problème ou un article : incidents résolus, problèmes et articles publiés les plus "
        + "proches de son titre et de sa description.")]
    public Task<CallToolResult> SimilarCases(
        [Description("Référence de l'incident, du problème ou de l'article, par exemple INC-2026-0012")] string reference,
        [Description("Nombre de résultats, 5 par défaut")] int limit = 5,
        CancellationToken ct = default) => RunAsync("similar_cases", async shaper =>
        {
            var (kind, item, failure) = await ResolveAsync(reference, ["incident", "problem", "article"], ct);
            if (failure is not null) return failure;
            var found = await api.GetAsync($"api/search/similar?type={kind!.Type}&id={IdOf(item!)}&limit={Clamp(limit, 5)}", ct);
            if (!found.Ok) return FromStatus(found, Reference(item!));
            var results = shaper.Shape(found.Body) as JsonArray ?? [];
            return Done(new JsonObject { ["reference"] = Reference(item!), ["results"] = results }, shaper, results.Count);
        }, ct);

    [McpServerTool(Name = "my_work", Title = "Mon travail", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("« Mon travail » de la console de S-Aloha pour l'utilisateur : ce qui lui est confié ou à l'un de ses groupes "
        + "(incidents, demandes, problèmes, changements, actions, projets), les échéances menacées et les relances.")]
    public Task<CallToolResult> MyWorkTool(CancellationToken ct = default) => RunAsync("my_work", async shaper =>
    {
        var console = await api.GetAsync("api/console", ct);
        if (!console.Ok) return FromStatus(console, "la console");
        var work = new JsonObject();
        var count = 0;
        foreach (var section in MyWork)
        {
            if (console.Body?[section] is not JsonArray items) continue;
            var shaped = (JsonArray)shaper.Shape(items)!;
            work[section] = shaped;
            count += shaped.Count;
        }
        return Done(work, shaper, count);
    }, ct);

    [McpServerTool(Name = "ci_impact", Title = "Impact d'un CI", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Impact d'un élément de configuration (CI) : ce qui en dépend par les relations (en aval) et ce dont il dépend "
        + "(en amont), les services touchés, les incidents et les changements ouverts qui lui sont reliés.")]
    public Task<CallToolResult> CiImpact(
        [Description("Référence du CI, par exemple CI-2026-0042")] string reference,
        CancellationToken ct = default) => RunAsync("ci_impact", async shaper =>
        {
            var (_, item, failure) = await ResolveAsync(reference, ["ci"], ct);
            if (failure is not null) return failure;
            var impact = await api.GetAsync($"api/configuration-items/{IdOf(item!)}/impact", ct);
            if (!impact.Ok) return FromStatus(impact, Reference(item!));
            var ci = new JsonObject();
            foreach (var key in new[] { "id", "reference", "title", "status", "ciType", "environment", "criticality" })
                if (item![key] is { } value) ci[key] = value.DeepClone();
            var shaped = new JsonObject { ["ci"] = shaper.Shape(ci) };
            var count = 0;
            foreach (var key in new[] { "downstream", "upstream", "services", "openIncidents", "openChanges" })
            {
                var list = shaper.Shape(impact.Body?[key]) as JsonArray ?? [];
                shaped[key] = list;
                if (key is "downstream" or "openIncidents" or "openChanges") count += list.Count;
            }
            return Done(shaped, shaper, count);
        }, ct);

    [McpServerTool(Name = "triage_suggestions", Title = "Propositions du tri assisté", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Propositions du tri assisté des tickets sur un incident (catégorie, service, groupe, impact, avec leurs motifs) et "
        + "la panne probable qu'il partage avec d'autres incidents ouverts. Rien si le tri assisté n'est pas activé.")]
    public Task<CallToolResult> TriageSuggestions(
        [Description("Référence de l'incident, par exemple INC-2026-0012")] string reference,
        CancellationToken ct = default) => RunAsync("triage_suggestions", async shaper =>
        {
            var (_, item, failure) = await ResolveAsync(reference, ["incident"], ct);
            if (failure is not null) return failure;
            var id = IdOf(item!);
            // L'incident est visible : un 404 ici veut dire que le tri assisté est coupé (OPT-04).
            var triage = await api.GetAsync($"api/incidents/{id}/triage", ct);
            if (triage.Status == HttpStatusCode.NotFound)
                return Done(new JsonObject
                {
                    ["reference"] = Reference(item!), ["enabled"] = false,
                    ["message"] = "Le tri assisté des tickets n'est pas activé dans S-Aloha.",
                }, shaper, 0);
            if (!triage.Ok) return FromStatus(triage, Reference(item!));
            var proposals = await api.GetAsync($"api/incidents/{id}/proposals", ct);
            if (!proposals.Ok) return FromStatus(proposals, Reference(item!));
            var mine = new JsonArray((proposals.Body as JsonArray ?? []).OfType<JsonObject>()
                .Where(p => Text(p["feature"]) == AssistedTriage).Select(p => (JsonNode)p.DeepClone()).ToArray());
            var shaped = shaper.Shape(mine) as JsonArray ?? [];
            return Done(new JsonObject
            {
                ["reference"] = Reference(item!), ["enabled"] = true, ["proposals"] = shaped,
                ["outage"] = shaper.Shape(triage.Body?["outage"]),
            }, shaper, shaped.Count);
        }, ct);

    /// <summary>
    /// Une fiche désignée par sa référence, cherchée dans le registre public de son type avec la portée de l'utilisateur ;
    /// hors de ses périmètres, elle n'y est pas : « introuvable » (MCP-06).
    /// </summary>
    private async Task<(Kind? Kind, JsonObject? Item, Answer? Failure)> ResolveAsync(string reference, string[]? only, CancellationToken ct)
    {
        var wanted = (reference ?? "").Trim().ToUpperInvariant();
        var match = ReferencePattern().Match(wanted);
        var kind = match.Success ? Kinds.FirstOrDefault(k => k.Code == match.Groups[1].Value) : null;
        if (kind is null)
            return (null, null, Fail("invalide", $"Référence non reconnue : « {Clip(reference)} ». Forme attendue : INC-2026-0012, CHG-2026-0003, CI-2026-0042…"));
        if (only is not null && !only.Contains(kind.Type))
            return (null, null, Fail("invalide", $"{wanted} est {kind.Label} : cet outil attend "
                + string.Join(" ou ", Kinds.Where(k => only.Contains(k.Type)).Select(k => k.Label)) + "."));
        var list = await api.GetAsync($"api/{kind.Path}?q={Uri.EscapeDataString(wanted)}", ct);
        if (!list.Ok) return (null, null, FromStatus(list, wanted));
        var item = (list.Body as JsonArray ?? []).OfType<JsonObject>()
            .FirstOrDefault(o => string.Equals(Text(o["reference"]), wanted, StringComparison.OrdinalIgnoreCase));
        return item is null ? (null, null, Fail("introuvable", $"Introuvable : {wanted}.")) : (kind, item, null);
    }

    /// <summary>MCP-09 : exécute un outil, puis écrit au journal compte, jeton, outil, résultats, durée et issue ; jamais le contenu.</summary>
    private async Task<CallToolResult> RunAsync(string tool, Func<Shaper, Task<Answer>> body, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        Answer answer;
        try { answer = await body(new Shaper(Limits)); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || ex is TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Outil MCP {Tool} : API injoignable ou réponse illisible ({Error})", tool, ex.GetType().Name);
            answer = Fail("indisponible", "S-Aloha ne répond pas : réessayer dans quelques minutes.");
        }
        var caller = McpHost.CallerOf(context.HttpContext);
        logger.LogInformation("Outil MCP {Tool} pour {Username}, jeton {TokenId} : {Outcome}, {Results} résultat(s), {DurationMs} ms",
            tool, caller?.Username, caller?.TokenId, answer.Outcome, answer.Count, watch.ElapsedMilliseconds);
        return answer.Result;
    }

    private static Answer Done(JsonObject payload, Shaper shaper, int count)
    {
        payload["truncated"] = shaper.Truncated;
        return new Answer("ok", count, new CallToolResult { Content = [new TextContentBlock { Text = payload.ToJsonString() }] });
    }

    private static Answer Fail(string outcome, string message) =>
        new(outcome, 0, new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = message }] });

    /// <summary>MCP-06 : hors de portée (404, 403) se dit « introuvable », jamais « interdit ».</summary>
    private static Answer FromStatus(ApiAnswer answer, string what) => answer.Status switch
    {
        HttpStatusCode.NotFound or HttpStatusCode.Forbidden => Fail("introuvable", $"Introuvable : {what}."),
        HttpStatusCode.Unauthorized => Fail("refuse",
            "Jeton refusé par S-Aloha (expiré, révoqué, ou compte retiré) : en créer un dans Préférences › Jetons d'accès."),
        HttpStatusCode.ServiceUnavailable => Fail("indisponible", "S-Aloha est momentanément indisponible : réessayer dans quelques minutes."),
        _ => Fail("erreur", $"S-Aloha a répondu {(int)answer.Status} pour {what}."),
    };

    private int Clamp(int limit, int fallback) => limit <= 0 ? Math.Min(fallback, Limits.MaxResults) : Math.Min(limit, Limits.MaxResults);

    private static int IdOf(JsonObject item) => item["id"]!.GetValue<int>();

    private static string Reference(JsonObject item) => Text(item["reference"]) ?? "";

    private static string? Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static string Clip(string? text) => text is null ? "" : text.Length > 40 ? text[..40] + "…" : text;
}
