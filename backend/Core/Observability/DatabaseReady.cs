using Microsoft.Extensions.Diagnostics.HealthChecks;
using SAloha.Api.Core.Data;

namespace SAloha.Api.Core.Observability;

/// <summary>
/// O1 : disponibilité (`/health/ready`) = la base répond. Sans paquet de contrôle EF : une
/// connexion suffit, et l'échec ne renvoie aucun détail (ni hôte ni message du pilote).
/// </summary>
public sealed class DatabaseReady(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : new HealthCheckResult(context.Registration.FailureStatus);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new HealthCheckResult(context.Registration.FailureStatus);
        }
    }
}
