using System.Threading.Channels;

namespace SAloha.Features.Host;

/// <summary>Une fonctionnalité qui lit le fil d'événements de l'API (OPT-12) : les types qu'elle traite.</summary>
public interface IEventHandler
{
    string Feature { get; }
    IReadOnlyCollection<string> Types { get; }
    Task HandleAsync(FeatureEvent e, CancellationToken ct);
    /// <summary>Le curseur précédait les événements purgés (<c>gap</c>) : repartir d'un état complet (OPT-06).</summary>
    Task ResetAsync(CancellationToken ct) => Task.CompletedTask;
}

/// <summary>
/// Lecture du fil d'événements de l'API (OPT-12), à son rythme : <c>GET /internal/events?after=</c>, page
/// après page, puis une pause quand il est à jour. Le curseur vit en mémoire : au démarrage, le conteneur
/// relit le fil gardé par l'API (7 jours), et rattrape ainsi ce qu'il a manqué arrêté ; une proposition
/// redéposée à l'identique n'est pas recréée (OPT-14). Chaque fonctionnalité a sa file et son exécutant
/// (OPT-28) : un traitement long de l'une ne retarde pas l'autre, et un échec n'arrête que l'événement.
/// </summary>
public class EventLoop(ApiClient api, IEnumerable<IEventHandler> handlers, IConfiguration config, ILogger<EventLoop> log) : BackgroundService
{
    public const int PageSize = 500;

    private TimeSpan Poll => TimeSpan.FromMilliseconds(config.GetValue("Events:PollMilliseconds", 5000));
    /// <summary>Aucune fonctionnalité activée (403) ou API injoignable : on revient plus tard.</summary>
    private TimeSpan Idle => TimeSpan.FromMilliseconds(config.GetValue("Events:IdleMilliseconds", 30000));

    /// <summary>Dernier événement lu ; 0 au démarrage.</summary>
    public long Cursor { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var all = handlers.ToList();
        if (!api.Configured || all.Count == 0)
        {
            log.LogWarning("Api:Url absente ou aucune fonctionnalité inscrite : le fil d'événements n'est pas lu.");
            return;
        }
        var queues = all.ToDictionary(h => h, _ => Channel.CreateUnbounded<FeatureEvent>(new UnboundedChannelOptions { SingleReader = true }));
        var workers = queues.Select(q => Task.Run(() => WorkAsync(q.Key, q.Value.Reader, stop), stop)).ToList();
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var wait = await ReadPageAsync(all, queues, stop);
                if (wait > TimeSpan.Zero) await Task.Delay(wait, stop);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally
        {
            foreach (var q in queues.Values) q.Writer.TryComplete();
        }
        await Task.WhenAll(workers.Select(w => w.ContinueWith(_ => { }, TaskScheduler.Default)));
    }

    /// <summary>Lit une page et la répartit ; rend la pause à observer avant la suivante.</summary>
    private async Task<TimeSpan> ReadPageAsync(List<IEventHandler> all, Dictionary<IEventHandler, Channel<FeatureEvent>> queues, CancellationToken stop)
    {
        EventsPage? page;
        try
        {
            page = await api.EventsAsync(Cursor, PageSize, stop);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stop.IsCancellationRequested)
        {
            log.LogWarning("Fil d'événements illisible ({Error}) : nouvel essai dans {Seconds} s.", ex.Message.Split('\n')[0], Idle.TotalSeconds);
            return Idle;
        }
        if (page is null) return Idle;   // 403 : aucune fonctionnalité activée ne lit le fil
        if (page.Gap)
        {
            log.LogWarning("Fil d'événements : des événements ont été purgés depuis le curseur {Cursor} ; état complet.", Cursor);
            foreach (var h in all) await h.ResetAsync(stop);
        }
        foreach (var e in page.Events)
            foreach (var h in all.Where(h => h.Types.Contains(e.Type)))
                await queues[h].Writer.WriteAsync(e, stop);
        Cursor = Math.Max(Cursor, page.Next);
        return page.Events.Count >= PageSize ? TimeSpan.Zero : Poll;
    }

    private async Task WorkAsync(IEventHandler handler, ChannelReader<FeatureEvent> reader, CancellationToken stop)
    {
        await foreach (var e in reader.ReadAllAsync(stop))
        {
            try
            {
                await handler.HandleAsync(e, stop);
            }
            catch (Exception ex) when (!stop.IsCancellationRequested)
            {
                log.LogWarning("{Feature} : événement {Id} ({Type} {EntityType} {EntityId}) non traité : {Error}",
                    handler.Feature, e.Id, e.Type, e.EntityType, e.EntityId, ex.Message.Split('\n')[0]);
            }
        }
    }
}
