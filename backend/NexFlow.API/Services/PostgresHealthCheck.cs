using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;

namespace NexFlow.API.Services;

public sealed class PostgresHealthCheck(IServiceScopeFactory scopes) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexFlowDbContext>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT 1", timeout.Token);
            return HealthCheckResult.Healthy();
        }
        catch (Exception) { return HealthCheckResult.Unhealthy("PostgreSQL no disponible."); }
    }
}
