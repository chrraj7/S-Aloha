using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Audit;
using SAloha.Api.Core.Data;
using SAloha.Api.Core.Itil;
using SAloha.Api.Modules.IncidentManagement;
using SAloha.Api.Modules.ServiceConfigurationManagement;

namespace SAloha.Api.Core.Features;

/// <summary>
/// Proposition d'une fonctionnalité optionnelle (OPT-14) : la seule écriture permise au conteneur.
/// Elle n'est appliquée que si un utilisateur qui a le droit de modifier sa cible l'accepte (OPT-15).
/// </summary>
public class Proposal
{
    public const string Open = "Ouverte", Accepted = "Acceptée", Corrected = "Corrigée", Rejected = "Rejetée", Expired = "Expirée";

    public int Id { get; set; }
    [MaxLength(40)] public string Feature { get; set; } = "";
    [MaxLength(20)] public string TargetType { get; set; } = "";
    public int TargetId { get; set; }
    public int PerimeterId { get; set; }
    [MaxLength(40)] public string Field { get; set; } = "";
    public string? Before { get; set; }
    [MaxLength(2000)] public string After { get; set; } = "";
    [MaxLength(1000)] public string Reasons { get; set; } = "";
    /// <summary>Enregistrements cités par les motifs, en JSON : <c>[{"type":"incident","id":12}]</c>.</summary>
    public string CitedJson { get; set; } = "[]";
    /// <summary>Indicative, jamais utilisée pour appliquer (OPT-14).</summary>
    public double? Confidence { get; set; }
    [MaxLength(20)] public string Status { get; set; } = Open;
    public string? FinalValue { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DecidedAt { get; set; }
    [MaxLength(200)] public string? DecidedBy { get; set; }
}

/// <summary>OPT-17 : comptes mensuels gardés après la purge des propositions.</summary>
public class ProposalCount
{
    [MaxLength(7)] public string Month { get; set; } = "";   // AAAA-MM
    [MaxLength(40)] public string Feature { get; set; } = "";
    [MaxLength(40)] public string Field { get; set; } = "";
    public int Accepted { get; set; }
    public int Corrected { get; set; }
    public int Rejected { get; set; }
    public int Expired { get; set; }
}

public record CitedRef(string Type, int Id);
public record ProposalInput(string Feature, string TargetType, int TargetId, string Field, string After, string? Reasons,
    List<CitedRef>? Cited, double? Confidence);
public record CitedView(string Type, int Id, string Reference, string Title);
public record ProposalView(int Id, string Feature, string Field, string? Before, string After, string Reasons, List<CitedView> Cited,
    double? Confidence, DateTime CreatedAt, bool Stale);

/// <summary>
/// Champs qu'une fonctionnalité peut proposer, avec leur lecture et leur liste fermée (OPT-14).
/// Un champ absent d'ici ne se propose pas. Le nom est celui de l'API (camelCase).
/// </summary>
public static class ProposalFields
{
    /// <param name="Validate">Contrôle de la valeur proposée, l'enregistrement visé en main : message (400) ou null.</param>
    /// <param name="Other">
    /// La valeur désigne un second enregistrement du même type (fusion, CMA-05 ; relation, CMA-02) : son identifiant, lu
    /// dans la valeur. La proposition porte alors sur la paire ; elle se décide par sa route propre, jamais par la
    /// modification de la pratique, et plusieurs restent ouvertes sur un même enregistrement.
    /// </param>
    /// <param name="Mirror">Pour une paire sans sens (fusion) : la même proposition vue de l'autre enregistrement.</param>
    public record Field(string TargetType, string Name, string Feature, Func<object, string?> Read,
        Func<AppDbContext, object, string, Task<string?>> Validate, Func<string, int?>? Other = null, Func<int, string>? Mirror = null)
    {
        public bool Pair => Other is not null;
    }

    public const string Merge = "merge", Relation = "relation";

    private static int? Id(string v) => int.TryParse(v, out var id) ? id : null;

    private static Func<AppDbContext, object, string, Task<string?>> OneOf(string label, string[] values) =>
        (_, _, v) => Task.FromResult(values.Contains(v) ? null : $"{label} inconnu(e) : « {v} ». Valeurs : {string.Join(", ", values)}.");

    private static Func<AppDbContext, object, string, Task<string?>> Text(string label, int max) =>
        (_, _, v) => Task.FromResult(string.IsNullOrWhiteSpace(v) ? $"{label} vide." : v.Length > max ? $"{label} : {max} caractères au plus." : null);

    public static readonly Field[] All =
    [
        // Tri assisté des tickets (TRI-03, TRI-04).
        new("incident", "impact", FeatureCatalog.AssistedTriage, e => ((Incident)e).Impact, OneOf("Impact", Priority.Impacts)),
        new("incident", "category", FeatureCatalog.AssistedTriage, e => ((Incident)e).Category,
            async (db, _, v) => (await Category.CheckAsync(db, "incident", v, null)).Error ?? (string.IsNullOrWhiteSpace(v) ? "Catégorie vide." : null)),
        new("incident", "serviceId", FeatureCatalog.AssistedTriage, e => ((Incident)e).ServiceId?.ToString(),
            async (db, _, v) => int.TryParse(v, out var id) && await db.Services.AnyAsync(s => s.Id == id && s.Status == "En service")
                ? null : $"Service inconnu ou hors service : « {v} »."),
        new("incident", "owner", FeatureCatalog.AssistedTriage, e => ((Incident)e) is { OwnerType: "Group" } i ? i.OwnerId : null,
            Text("Groupe responsable", 200)),
        // CMDB assistée (CMA-02).
        new("ci", "title", FeatureCatalog.AssistedCmdb, e => ((ConfigurationItem)e).Title, Text("Nom", 200)),
        new("ci", "ciType", FeatureCatalog.AssistedCmdb, e => ((ConfigurationItem)e).CiType, OneOf("Type de CI", ConfigurationItem.Types)),
        new("ci", "criticality", FeatureCatalog.AssistedCmdb, e => ((ConfigurationItem)e).Criticality, OneOf("Criticité", ConfigurationItem.Criticalities)),
        new("ci", "operatingSystem", FeatureCatalog.AssistedCmdb, e => ((ConfigurationItem)e).OperatingSystem, Text("Système", 150)),
        new("ci", "vendor", FeatureCatalog.AssistedCmdb, e => ((ConfigurationItem)e).Vendor, Text("Constructeur", 100)),
        new("ci", "model", FeatureCatalog.AssistedCmdb, e => ((ConfigurationItem)e).Model, Text("Modèle", 100)),
        // CMA-05 : fusionner le CI visé dans l'autre (son identifiant), que la proposition suggère de conserver.
        new("ci", Merge, FeatureCatalog.AssistedCmdb, _ => null, CiMerge.ValidateAsync, Id, target => target.ToString()),
        // CMA-02 : relier le CI visé à un autre, « <identifiant>|<type de relation> » ; le sens compte.
        new("ci", Relation, FeatureCatalog.AssistedCmdb, _ => null, CiRelate.ValidateAsync, v => CiRelate.Parse(v)?.Target),
    ];

    public static Field? Of(string targetType, string name) => All.FirstOrDefault(f => f.TargetType == targetType && f.Name == name);

    public static async Task<object?> LoadAsync(AppDbContext db, string targetType, int id) => targetType switch
    {
        "incident" => await db.Incidents.FindAsync(id),
        "ci" => await db.ConfigurationItems.FindAsync(id),
        _ => null
    };

    /// <summary>Origine du journal d'audit quand une proposition est appliquée (OPT-08).</summary>
    public static string OriginOf(string feature) => feature switch
    {
        FeatureCatalog.AssistedTriage => AuditEntry.AssistedTriage,
        FeatureCatalog.AssistedCmdb => AuditEntry.AssistedCmdb,
        _ => AuditEntry.Saisie
    };
}

public static class ProposalDecisions
{
    /// <summary>En-tête d'une modification qui applique une proposition (OPT-15).</summary>
    public const string Header = "X-S-Aloha-Proposal";

    /// <summary>
    /// TRI-09 : en-tête d'une création qui porte des suggestions appliquées à la saisie : JSON encodé pour l'URL,
    /// <c>[{ "feature", "field", "value" }]</c>, 10 au plus.
    /// </summary>
    public const string SuggestionsHeader = "X-S-Aloha-Suggestions";

    public record AppliedSuggestion(string? Feature, string? Field, string? Value);

    /// <summary>Champ du journal d'audit où une modification de ce champ s'écrit (les identifiants n'y figurent pas).</summary>
    private static readonly Dictionary<string, string> AuditField = new() { ["serviceId"] = "affectedService", ["owner"] = "ownerDisplayName" };

    /// <summary>
    /// TRI-09, TRI-10, OPT-08 : à la création, chaque suggestion appliquée à la saisie et encore portée par
    /// l'enregistrement devient une proposition acceptée (comptée dans le mois) et une ligne du journal d'audit
    /// d'origine « Tri assisté des tickets ». Une fonctionnalité coupée, un champ qu'elle ne propose pas, une
    /// valeur que l'agent a changée ensuite, ou un en-tête illisible : ignorés, sans erreur.
    /// </summary>
    public static async Task RecordAppliedAsync(AppDbContext db, IFeatureGate gate, string targetType, Records.Record e, string? header,
        string by, string byDisplay)
    {
        if (string.IsNullOrWhiteSpace(header)) return;
        List<AppliedSuggestion> applied;
        try { applied = JsonSerializer.Deserialize<List<AppliedSuggestion>>(Uri.UnescapeDataString(header), JsonSerializerOptions.Web) ?? []; }
        catch (Exception ex) when (ex is JsonException or UriFormatException) { return; }
        var enabled = await gate.EnabledAsync();
        var now = DateTime.UtcNow;
        foreach (var a in applied.Where(a => a.Feature is not null && a.Field is not null && a.Value is not null)
                     .DistinctBy(a => (a.Feature, a.Field)).Take(10))
        {
            var field = ProposalFields.Of(targetType, a.Field!);
            if (field is null || field.Feature != a.Feature || !enabled.Contains(field.Feature)) continue;
            var value = field.Read(e);
            if (!string.Equals(value, a.Value, StringComparison.OrdinalIgnoreCase)) continue;
            var p = new Proposal
            {
                Feature = field.Feature, TargetType = targetType, TargetId = e.Id, PerimeterId = e.PerimeterId, Field = field.Name,
                Before = null, After = value!, Reasons = "Appliquée à la saisie", CitedJson = "[]", CreatedAt = now,
                Status = Proposal.Accepted, FinalValue = value, DecidedAt = now, DecidedBy = by,
            };
            db.Proposals.Add(p);
            await CountAsync(db, p, Proposal.Accepted);
            var shown = field.Name switch
            {
                "serviceId" when int.TryParse(value, out var sid) => await db.Services.Where(x => x.Id == sid).Select(x => x.Title).FirstOrDefaultAsync() ?? value,
                _ => value,
            };
            db.AuditEntries.Add(new Audit.AuditEntry
            {
                EntityType = targetType, EntityId = e.Id, Reference = e.Reference, Action = Audit.AuditEntry.Modification,
                Field = AuditField.GetValueOrDefault(field.Name, field.Name), OldValue = null, NewValue = shown,
                Author = by, AuthorDisplayName = byDisplay, At = now, Origin = OriginOf(field.Feature),
            });
        }
        await db.SaveChangesAsync();
    }

    private static string OriginOf(string feature) =>
        feature == FeatureCatalog.AssistedCmdb ? Audit.AuditEntry.AssistedCmdb : Audit.AuditEntry.AssistedTriage;

    /// <summary>OPT-15, OPT-17 : décision enregistrée, et comptée dans le mois.</summary>
    public static async Task DecideAsync(AppDbContext db, Proposal p, string status, string? finalValue, string by)
    {
        (p.Status, p.FinalValue, p.DecidedAt, p.DecidedBy) = (status, finalValue, DateTime.UtcNow, by);
        await CountAsync(db, p, status);
        await db.SaveChangesAsync();
    }

    public static async Task CountAsync(AppDbContext db, Proposal p, string status)
    {
        var month = DateTime.UtcNow.ToString("yyyy-MM");
        var row = await db.ProposalCounts.FindAsync(month, p.Feature, p.Field);
        if (row is null) db.ProposalCounts.Add(row = new ProposalCount { Month = month, Feature = p.Feature, Field = p.Field });
        switch (status)
        {
            case Proposal.Accepted: row.Accepted++; break;
            case Proposal.Corrected: row.Corrected++; break;
            case Proposal.Rejected: row.Rejected++; break;
            case Proposal.Expired: row.Expired++; break;
        }
    }

    public static List<CitedRef> Cited(Proposal p)
    {
        try { return JsonSerializer.Deserialize<List<CitedRef>>(p.CitedJson, JsonSerializerOptions.Web) ?? []; }
        catch (JsonException) { return []; }
    }
}

/// <summary>
/// OPT-17 : une proposition ouverte expire à 30 jours ; une proposition décidée est purgée à
/// 13 mois ; les comptes mensuels restent. Passe quotidienne.
/// </summary>
public class ProposalsRetention(IServiceScopeFactory scopes, ILogger<ProposalsRetention> logger) : BackgroundService
{
    public static readonly TimeSpan OpenFor = TimeSpan.FromDays(30);
    public const int KeepMonths = 13;

    public static async Task<(int Expired, int Purged)> RunAsync(AppDbContext db, DateTime now)
    {
        var stale = await db.Proposals.Where(p => p.Status == Proposal.Open && p.CreatedAt < now - OpenFor).ToListAsync();
        foreach (var p in stale)
        {
            (p.Status, p.DecidedAt, p.DecidedBy) = (Proposal.Expired, now, "Système");
            await ProposalDecisions.CountAsync(db, p, Proposal.Expired);
            await db.SaveChangesAsync();
        }
        var limit = now.AddMonths(-KeepMonths);
        var purged = await db.Proposals.Where(p => p.Status != Proposal.Open && p.DecidedAt < limit).ExecuteDeleteAsync();
        // TRI-14 : une panne probable fermée se garde comme une proposition décidée, 13 mois ; ses membres partent avec.
        purged += await db.ProbableOutages.Where(o => o.ClosedAt != null && o.ClosedAt < limit).ExecuteDeleteAsync();
        return (stale.Count, purged);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var (expired, purged) = await RunAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), DateTime.UtcNow);
                if (expired + purged > 0) logger.LogInformation("Propositions : {Expired} expirées, {Purged} purgées.", expired, purged);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Propositions : passe de conservation en échec.");
            }
            await Task.Delay(TimeSpan.FromDays(1), ct);
        }
    }
}
