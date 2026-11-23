using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Data;

namespace SAloha.Api.Core.Features;

/// <summary>Fonctionnalités activées par l'Admin (OPT-04), lues en base et gardées 30 secondes au plus.</summary>
public interface IFeatureGate
{
    Task<IReadOnlySet<string>> EnabledAsync(CancellationToken ct = default);
    Task<bool> IsEnabledAsync(string key, CancellationToken ct = default);
    /// <summary>Après une activation ou une coupure : la prochaine lecture repart de la base.</summary>
    void Invalidate();
}

public class FeatureGate(IServiceScopeFactory scopes) : IFeatureGate
{
    /// <summary>OPT-04 : une coupure prend effet en moins de 30 secondes, même pour une autre instance de l'API.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(30);
    private sealed record Snapshot(IReadOnlySet<string> Keys, DateTime At);
    // Une référence se lit et s'écrit d'un bloc : deux requêtes simultanées relisent au pire la base deux fois.
    private volatile Snapshot? cache;

    public async Task<IReadOnlySet<string>> EnabledAsync(CancellationToken ct = default)
    {
        if (cache is { } c && DateTime.UtcNow - c.At < MaxAge) return c.Keys;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var known = FeatureCatalog.All.Select(f => f.Key).ToList();
        IReadOnlySet<string> keys = (await db.OptionalFeatures.AsNoTracking().Where(f => f.Enabled && known.Contains(f.Key))
            .Select(f => f.Key).ToListAsync(ct)).ToHashSet();
        cache = new Snapshot(keys, DateTime.UtcNow);
        return keys;
    }

    public async Task<bool> IsEnabledAsync(string key, CancellationToken ct = default) => (await EnabledAsync(ct)).Contains(key);

    public void Invalidate() => cache = null;
}

/// <summary>
/// OPT-04 : une route d'une fonctionnalité coupée n'existe pas (404), quel que soit le rôle.
/// Posée sur le contrôleur ou l'action : <c>[RequiresFeature(FeatureCatalog.AssistedTriage)]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequiresFeatureAttribute(string key) : Attribute, IAsyncActionFilter
{
    public string Key { get; } = FeatureCatalog.ByKey(key)?.Key ?? throw new ArgumentException($"Fonctionnalité inconnue : {key}", nameof(key));

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var gate = context.HttpContext.RequestServices.GetRequiredService<IFeatureGate>();
        if (!await gate.IsEnabledAsync(Key, context.HttpContext.RequestAborted))
        {
            context.Result = new NotFoundResult();
            return;
        }
        await next();
    }
}
