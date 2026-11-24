using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SAloha.Api.Core.Attachments;
using SAloha.Api.Core.Data;

namespace SAloha.Api.Core.Search;

/// <summary>
/// File d'indexation : l'enregistrement d'un article, d'un problème ou d'un
/// incident n'attend pas le calcul des vecteurs (plusieurs secondes) ; il est
/// réindexé en tâche de fond, un à la fois (RAG-03).
/// </summary>
public class IndexQueue
{
    public const string All = "*";
    private readonly Channel<(string Type, int Id)> _channel = Channel.CreateUnbounded<(string, int)>();
    public void Enqueue(string type, int id) => _channel.Writer.TryWrite((type, id));
    public void EnqueueAll() => _channel.Writer.TryWrite((All, 0));
    public ChannelReader<(string Type, int Id)> Reader => _channel.Reader;
}

/// <summary>Consommateur de la file ; au démarrage, construit l'index s'il est vide ou sans vecteurs.</summary>
public class IndexWorker(IServiceScopeFactory scopes, IndexQueue queue, ILogger<IndexWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var embeddings = scope.ServiceProvider.GetRequiredService<IEmbeddingClient>();
            var empty = !await db.SearchPassages.AnyAsync(ct);
            // Vecteurs manquants alors que le service est configuré : il était
            // absent lors d'une indexation précédente, on rattrape.
            var missing = embeddings.Enabled && await db.SearchPassages.AnyAsync(p => p.Embedding == null, ct);
            if (empty || missing) queue.EnqueueAll();
        }

        await foreach (var (type, id) in queue.Reader.ReadAllAsync(ct))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var indexer = scope.ServiceProvider.GetRequiredService<SearchIndexer>();
                if (type == IndexQueue.All) await indexer.ReindexAllAsync(ct);
                else await indexer.ReindexAsync(type, id, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // L'index est dérivé : un échec ne doit jamais bloquer le processus métier.
                logger.LogError(ex, "Indexation de {Type} {Id} en échec.", type, id);
            }
        }
    }
}

/// <summary>
/// Repère, à chaque enregistrement EF, les articles, problèmes et incidents
/// ajoutés, modifiés ou supprimés, et les met en file une fois l'enregistrement
/// réussi (l'identifiant d'un ajout n'est connu qu'après). Une pièce jointe
/// déposée ou retirée remet en file son enregistrement (RAG-12).
/// </summary>
public class SearchIndexInterceptor(IndexQueue queue) : SaveChangesInterceptor
{
    private readonly ConditionalWeakTable<DbContext, List<object>> _pending = new();

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

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData e, int result, CancellationToken ct = default)
    {
        Flush(e.Context);
        return base.SavedChangesAsync(e, result, ct);
    }

    public override int SavedChanges(SaveChangesCompletedEventData e, int result)
    {
        Flush(e.Context);
        return base.SavedChanges(e, result);
    }

    private void Collect(DbContext? context)
    {
        if (context is null) return;
        var changed = context.ChangeTracker.Entries()
            .Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                        && (SearchSources.TypeOf(x.Entity) is not null
                            || x.Entity is Attachment a && SearchSources.Types.Contains(a.TargetType)))
            .Select(x => x.Entity).ToList();
        if (changed.Count > 0) _pending.AddOrUpdate(context, changed);
    }

    private void Flush(DbContext? context)
    {
        if (context is null || !_pending.TryGetValue(context, out var entities)) return;
        _pending.Remove(context);
        foreach (var entity in entities)
        {
            if (entity is Attachment a) { queue.Enqueue(a.TargetType, a.TargetId); continue; }
            var id = (int)context.Entry(entity).Property("Id").CurrentValue!;
            queue.Enqueue(SearchSources.TypeOf(entity)!, id);
        }
    }
}
