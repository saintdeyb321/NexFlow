using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace NexFlow.API.Services;

public class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _multiplexer;

    public RedisHealthCheck(IConnectionMultiplexer multiplexer)
    {
        _multiplexer = multiplexer;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (_multiplexer.IsConnected)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Redis conectado."));
        }

        return Task.FromResult(HealthCheckResult.Degraded("Redis no conectado o en modo degradado."));
    }
}