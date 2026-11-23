using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SAloha.Api.Core.Data;
using SAloha.Api.Core.Perimeters;
using SAloha.Api.Modules.IncidentManagement;
using SAloha.Api.Modules.ServiceConfigurationManagement;

namespace SAloha.Api.Core.Features;

/// <summary>
/// Un événement du fil lu par le conteneur des fonctionnalités (OPT-12) : quoi, sur quel
/// enregistrement, dans quel périmètre. Le conteneur relit ensuite l'enregistrement par les
/// routes internes ; le fil ne porte aucun contenu.
/// </summary>
public class FeatureEvent
{
    public long Id { get; set; }
    [MaxLength(40)] public string Type { get; set; } = "";
    [MaxLength(20)] public string EntityType { get; set; } = "";
    public int EntityId { get; set; }
    public int? PerimeterId { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
}

public record FeatureEventView(long Id, string Type, string EntityType, int EntityId, int? PerimeterId, DateTime At)
{
    public static FeatureEventView From(FeatureEvent e) => new(e.Id, e.Type, e.EntityType, e.EntityId, e.PerimeterId, e.At);
}

/// <summary>Ce qui devient un événement, et quelle fonctionnalité le lit (OPT-12).</summary>
public static class FeatureEventSources
{
    public const string IncidentCreated = "incident.created", IncidentUpdated = "incident.updated",
        IncidentResolved = "incident.resolved", IncidentDeleted = "incident.deleted",
        CiCreated = "ci.created", CiUpdated = "ci.updated", CiRetired = "ci.retired", CiDeleted = "ci.deleted",
        StagingCreated = "staging.created", DiscoveryFinished = "discovery.finished";

    public static readonly string[] AllTypes = [IncidentCreated, IncidentUpdated, IncidentResolved, IncidentDeleted,
        CiCreated, CiUpdated, CiRetired, CiDeleted, StagingCreated, DiscoveryFinished];

    /// <summary>Préfixes lus par chaque fonctionnalité ; le serveur MCP n'en lit aucun (ADR-0024).</summary>
    public static readonly Dictionary<string, string[]> Consumers = new()
    {
        [FeatureCatalog.AssistedTriage] = ["incident."],
        [FeatureCatalog.AssistedCmdb] = ["ci.", "staging.", "discovery."],
    };

    public static bool ConsumedBy(string type, IEnumerable<string> features) =>
        features.Any(f => Consumers.TryGetValue(f, out var prefixes) && prefixes.Any(type.StartsWith));

    /// <summary>Type d'événement d'un changement suivi, ou null.</summary>
    public static (string Type, string EntityType)? Classify(EntityEntry entry)
    {
        static bool Became(EntityEntry e, string property, params string[] values) =>
            e.State == EntityState.Modified && e.Property(property) is { IsModified: true } p
            && !Equals(p.OriginalValue, p.CurrentValue) && values.Contains(p.CurrentValue as string);

        return (entry.Entity, entry.State) switch
        {
            (Incident, EntityState.Added) => (IncidentCreated, "incident"),
            (Incident, EntityState.Deleted) => (IncidentDeleted, "incident"),
            (Incident, EntityState.Modified) => (Became(entry, "Status", "Résolu") ? IncidentResolved : IncidentUpdated, "incident"),
            (ConfigurationItem, EntityState.Added) => (CiCreated, "ci"),
            (ConfigurationItem, EntityState.Deleted) => (CiDeleted, "ci"),
            (ConfigurationItem, EntityState.Modified) => (Became(entry, "Status", "Retiré") ? CiRetired : CiUpdated, "ci"),
            (DiscoveredAsset, EntityState.Added) => (StagingCreated, "staging"),
            (DiscoveryJob, EntityState.Modified) when Became(entry, "LastStatus", DiscoveryJob.Succeeded, DiscoveryJob.Failed)
                => (DiscoveryFinished, "discovery-job"),
            _ => null
        };
    }
}

/// <summary>
/// OPT-12 : inscrit au fil, après chaque enregistrement réussi, les événements qu'une
/// fonctionnalité activée lit. Sans fonctionnalité qui le lit, rien n'est écrit : le fil ne
/// grossit pas pour rien, et le conteneur repart d'un état complet à l'activation (OPT-06).
/// Un échec d'écriture du fil ne fait jamais échouer l'enregistrement de la fiche.
/// </summary>
public class FeatureEventsInterceptor(IFeatureGate gate, ILogger<FeatureEventsInterceptor> logger) : SaveChangesInterceptor
{
    private sealed record Pending(object Entity, string Type, string EntityType, int? Id, int? PerimeterId);
    private readonly ConditionalWeakTable<DbContext, List<Pending>> _pending = new();

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e, InterceptionResult<int> result, CancellationToken ct = default)
    {
        Collect(e.Context);
        return base.SavingChangesAsync(e, result, ct);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData e, InterceptionResult<int> result)
    {
        Collect(e.Context);
        return base.SavingChanges(e, result);
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData e, int result, CancellationToken ct = default)
    {
        await FlushAsync(e.Context, ct);
        return await base.SavedChangesAsync(e, result, ct);
    }

    public override int SavedChanges(SaveChangesCompletedEventData e, int result)
    {
        FlushAsync(e.Context, CancellationToken.None).GetAwaiter().GetResult();
        return base.SavedChanges(e, result);
    }

    private void Collect(DbContext? context)
    {
        if (context is null) return;
        var found = new List<Pending>();
        foreach (var entry in context.ChangeTracker.Entries())
            if (FeatureEventSources.Classify(entry) is { } kind)
                // Un ajout n'a son identifiant qu'après l'enregistrement : relu au FlushAsync.
                found.Add(new Pending(entry.Entity, kind.Type, kind.EntityType,
                    entry.State == EntityState.Added ? null : (int)entry.Property("Id").CurrentValue!,
                    (entry.Entity as IHasPerimeter)?.PerimeterId));
        if (found.Count > 0) _pending.AddOrUpdate(context, found);
    }

    private async Task FlushAsync(DbContext? context, CancellationToken ct)
    {
        if (context is not AppDbContext db || !_pending.TryGetValue(context, out var pending)) return;
        _pending.Remove(context);
        try
        {
            var enabled = await gate.EnabledAsync(ct);
            var events = pending.Where(p => FeatureEventSources.ConsumedBy(p.Type, enabled)).Select(p => new FeatureEvent
            {
                Type = p.Type, EntityType = p.EntityType, PerimeterId = p.PerimeterId,
                EntityId = p.Id ?? (int)db.Entry(p.Entity).Property("Id").CurrentValue!,
            }).ToList();
            if (events.Count == 0) return;
            db.FeatureEvents.AddRange(events);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Fil des fonctionnalités : événements non inscrits.");
        }
    }
}

/// <summary>OPT-12 : le fil garde 7 jours (Features:EventRetentionDays), purge quotidienne.</summary>
public class FeatureEventsRetention(IServiceScopeFactory scopes, IConfiguration config, ILogger<FeatureEventsRetention> logger) : BackgroundService
{
    public static Task<int> PurgeAsync(AppDbContext db, int days, CancellationToken ct = default)
    {
        var limit = DateTime.UtcNow.AddDays(-days);
        return db.FeatureEvents.Where(e => e.At < limit).ExecuteDeleteAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var days = Math.Max(1, config.GetValue("Features:EventRetentionDays", 7));
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var purged = await PurgeAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), days, ct);
                if (purged > 0) logger.LogInformation("Fil des fonctionnalités : {Count} événements de plus de {Days} jours purgés.", purged, days);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Fil des fonctionnalités : purge en échec.");
            }
            await Task.Delay(TimeSpan.FromDays(1), ct);
        }
    }
}
