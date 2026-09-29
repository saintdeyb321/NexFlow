using Google.Cloud.Firestore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace NexFlow.API.Services;

public sealed class FirestoreHealthCheck(FirestoreDb db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await db.Collection("workspaces").Limit(1).Select(Array.Empty<string>()).GetSnapshotAsync(timeout.Token);
            return HealthCheckResult.Healthy();
        }
        catch (Exception) { return HealthCheckResult.Unhealthy("Firestore no disponible."); }
    }
}
