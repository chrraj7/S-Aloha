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
    public record Field(string TargetType, string Name, string Feature, Func<object, string?> Read, Func<AppDbContext, string, Task<string?>> Validate);

    private static Func<AppDbContext, string, Task<string?>> OneOf(string label, string[] values) =>
        (_, v) => Task.FromResult(values.Contains(v) ? null : $"{label} inconnu(e) : « {v} ». Valeurs : {string.Join(", ", values)}.");

    private static Func<AppDbContext, string, Task<string?>> Text(string label, int max) =>
        (_, v) => Task.FromResult(string.IsNullOrWhiteSpace(v) ? $"{label} vide." : v.Length > max ? $"{label} : {max} caractères au plus." : null);

    public static readonly Field[] All =
    [
        // Tri assisté des tickets (TRI-03, TRI-04).
        new("incident", "impact", FeatureCatalog.AssistedTriage, e => ((Incident)e).Impact, OneOf("Impact", Priority.Impacts)),
        new("incident", "category", FeatureCatalog.AssistedTriage, e => ((Incident)e).Category,
            async (db, v) => (await Category.CheckAsync(db, "incident", v, null)).Error ?? (string.IsNullOrWhiteSpace(v) ? "Catégorie vide." : null)),
        new("incident", "serviceId", FeatureCatalog.AssistedTriage, e => ((Incident)e).ServiceId?.ToString(),
            async (db, v) => int.TryParse(v, out var id) && await db.Services.AnyAsync(s => s.Id == id && s.Status == "En service")
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
