using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Audit;
using SAloha.Api.Core.Data;
using SAloha.Api.Modules.ServiceConfigurationManagement;

namespace SAloha.Api.Core.Features;

/// <summary>
/// OPT-02 : réglages propres d'une fonctionnalité, gardés en JSON sur sa ligne : leurs bornes (400 hors d'elles) et
/// les champs tracés au journal d'audit, libellé et valeur lisible (OPT-07).
/// </summary>
public interface IFeatureSettings
{
    string? Error();
    IEnumerable<(string Label, string Value)> Traced();
}

/// <summary>
/// TRI-13 : réglages du tri assisté des tickets, par l'Admin. Les bornes sont celles de la règle ; une suggestion
/// coupée (catégorie, service, groupe, impact, panne probable) n'est plus proposée, ni à la saisie ni en tâche de fond.
/// </summary>
public record TriageSettings(int Neighbours = 10, int MinVoters = 3, int MinSharePercent = 50, int CorrelationWindowMinutes = 60,
    int OutageThreshold = 3, double CorrelationMinSimilarity = 0.60, bool SuggestCategory = true, bool SuggestService = true,
    bool SuggestGroup = true, bool SuggestImpact = true, bool SuggestOutage = true) : IFeatureSettings
{
    private static string N(int v) => v.ToString(CultureInfo.InvariantCulture);
    private static string Yes(bool v) => v ? "oui" : "non";

    /// <summary>Champs tracés au journal d'audit (OPT-07) : libellé et valeur lisible.</summary>
    public static readonly (string Label, Func<TriageSettings, string> Read)[] Fields =
    [
        ("Voisins", s => N(s.Neighbours)), ("Votants minimaux", s => N(s.MinVoters)), ("Part minimale (%)", s => N(s.MinSharePercent)),
        ("Fenêtre de corrélation (min)", s => N(s.CorrelationWindowMinutes)), ("Seuil de panne probable", s => N(s.OutageThreshold)),
        ("Seuil de similarité", s => s.CorrelationMinSimilarity.ToString("0.00", CultureInfo.GetCultureInfo("fr-FR"))),
        ("Suggérer la catégorie", s => Yes(s.SuggestCategory)), ("Suggérer le service", s => Yes(s.SuggestService)),
        ("Suggérer le groupe", s => Yes(s.SuggestGroup)), ("Suggérer l'impact", s => Yes(s.SuggestImpact)),
        ("Signaler les pannes probables", s => Yes(s.SuggestOutage)),
    ];

    /// <summary>TRI-13 : message (400) si un réglage sort de ses bornes, null sinon.</summary>
    public string? Error()
    {
        static string? Range(string label, double v, double min, double max) =>
            v < min || v > max ? $"{label} : de {min.ToString(CultureInfo.GetCultureInfo("fr-FR"))} à {max.ToString(CultureInfo.GetCultureInfo("fr-FR"))}." : null;
        return Range("Voisins", Neighbours, 5, 20) ?? Range("Votants minimaux", MinVoters, 2, 10)
            ?? Range("Part minimale (%)", MinSharePercent, 50, 100) ?? Range("Fenêtre de corrélation (min)", CorrelationWindowMinutes, 15, 240)
            ?? Range("Seuil de panne probable", OutageThreshold, 2, 20) ?? Range("Seuil de similarité", CorrelationMinSimilarity, 0.40, 0.95)
            ?? (MinVoters > Neighbours ? "Votants minimaux : pas plus que de voisins." : null);
    }

    public IEnumerable<(string Label, string Value)> Traced() => Fields.Select(f => (f.Label, f.Read(this)));

    public static TriageSettings Of(OptionalFeature? row) => FeatureSettings.Read(row, () => new TriageSettings());
}

/// <summary>Les fonctionnalités qui ont des réglages propres : leur type et leur lecture sur la ligne de la fonctionnalité.</summary>
public static class FeatureSettings
{
    private static readonly Dictionary<string, (Type Type, Func<OptionalFeature?, IFeatureSettings> Of)> Known = new()
    {
        [FeatureCatalog.AssistedTriage] = (typeof(TriageSettings), TriageSettings.Of),
        [FeatureCatalog.AssistedCmdb] = (typeof(AssistedCmdbSettings), AssistedCmdbSettings.Of),
    };

    public static bool Has(string key) => Known.ContainsKey(key);

    public static IFeatureSettings Of(string key, OptionalFeature? row) => Known[key].Of(row);

    /// <summary>Une saisie lue selon le type des réglages de la fonctionnalité ; null si elle n'en est pas un.</summary>
    public static IFeatureSettings? Parse(string key, JsonElement body)
    {
        try { return body.Deserialize(Known[key].Type, JsonSerializerOptions.Web) as IFeatureSettings; }
        catch (JsonException) { return null; }
    }

    /// <summary>Réglages enregistrés ; absents ou illisibles : ceux par défaut.</summary>
    public static T Read<T>(OptionalFeature? row, Func<T> defaults) where T : class
    {
        if (string.IsNullOrEmpty(row?.SettingsJson)) return defaults();
        try { return JsonSerializer.Deserialize<T>(row.SettingsJson, JsonSerializerOptions.Web) ?? defaults(); }
        catch (JsonException) { return defaults(); }
    }

    public static async Task<OptionalFeature?> RowAsync(AppDbContext db, string key, CancellationToken ct = default) =>
        await db.OptionalFeatures.AsNoTracking().FirstOrDefaultAsync(f => f.Key == key, ct);
}

/// <summary>
/// TRI-13, CMA-12, OPT-02, OPT-07 : réglages propres d'une fonctionnalité, lus et modifiés par l'Admin ; chaque réglage
/// changé est tracé au journal d'audit, une demande qui ne change rien n'écrit rien. Le tri assisté et la CMDB assistée en
/// ont ; une autre clé : 404.
/// </summary>
[ApiController]
[Route("api/admin/features/{key}/settings")]
[Authorize(Policy = "Admin")]
public class FeatureSettingsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(string key) =>
        !FeatureSettings.Has(key) ? NotFound() : Ok(FeatureSettings.Of(key, await FeatureSettings.RowAsync(db, key, HttpContext.RequestAborted)));

    [HttpPut]
    public async Task<IActionResult> Put(string key, JsonElement body)
    {
        var feature = FeatureCatalog.ByKey(key);
        if (feature is null || !FeatureSettings.Has(key)) return NotFound();
        if (FeatureSettings.Parse(key, body) is not { } settings) return BadRequest(new { message = "Réglages illisibles." });
        if (settings.Error() is { } error) return BadRequest(new { message = error });
        if (settings is AssistedCmdbSettings cmdb) settings = cmdb.Normalized();
        var ct = HttpContext.RequestAborted;
        var row = await db.OptionalFeatures.FirstOrDefaultAsync(f => f.Key == key, ct);
        var before = FeatureSettings.Of(key, row).Traced().ToDictionary(f => f.Label, f => f.Value);
        var changed = settings.Traced().Where(f => before.GetValueOrDefault(f.Label) != f.Value).ToList();
        if (changed.Count == 0) return Ok(settings);

        if (row is null) db.OptionalFeatures.Add(row = new OptionalFeature { Key = key });
        var me = User.Identity?.Name ?? "";
        var display = User.FindFirst("displayName")?.Value ?? me;
        foreach (var (label, value) in changed)
            db.AuditEntries.Add(new AuditEntry
            {
                EntityType = FeaturesController.AuditType, EntityId = Array.IndexOf(FeatureCatalog.All, feature) + 1,
                Reference = FeaturesController.AuditReference, Action = AuditEntry.Modification, Field = $"{feature.Name} — {label}",
                OldValue = before.GetValueOrDefault(label), NewValue = value, Author = me, AuthorDisplayName = display, At = DateTime.UtcNow,
            });
        row.SettingsJson = JsonSerializer.Serialize(settings, settings.GetType(), JsonSerializerOptions.Web);
        (row.UpdatedAt, row.UpdatedBy) = (DateTime.UtcNow, display);
        await db.SaveChangesAsync(ct);
        return Ok(settings);
    }
}

/// <summary>TRI-13, CMA-12 : le conteneur lit les réglages d'une fonctionnalité activée (403 sinon, OPT-04) sur le port interne.</summary>
[ApiController]
[Route("internal/features/{key}/settings")]
[Authorize(Policy = ContainerTokenHandler.Policy)]
public class InternalFeatureSettingsController(AppDbContext db, IFeatureGate gate) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(string key)
    {
        if (FeatureCatalog.ByKey(key) is null || !await gate.IsEnabledAsync(key, HttpContext.RequestAborted)) return StatusCode(StatusCodes.Status403Forbidden);
        if (!FeatureSettings.Has(key)) return NotFound();
        return Ok(FeatureSettings.Of(key, await FeatureSettings.RowAsync(db, key, HttpContext.RequestAborted)));
    }
}
