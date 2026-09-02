using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace NexFlow.API.Middleware;

public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private const string CorrelationIdHeader = "X-Correlation-ID";

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ILogger<CorrelationIdMiddleware> logger)
    {
        string correlationId = context.Request.Headers.TryGetValue(CorrelationIdHeader, out StringValues values)
            ? values.FirstOrDefault() ?? Guid.NewGuid().ToString()
            : Guid.NewGuid().ToString();

        context.Response.Headers.Append(CorrelationIdHeader, correlationId);
        context.Items["CorrelationId"] = correlationId;

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                await _next(context);
            }
            finally
            {
                stopwatch.Stop();

                // Evitamos saturar logs con las comprobaciones periódicas de salud
                if (!context.Request.Path.StartsWithSegments("/health"))
                {
                    logger.LogInformation(
                        "HTTP {Method} {Path} respondido con Status {StatusCode} en {ElapsedMs}ms",
                        context.Request.Method,
                        context.Request.Path,
                        context.Response.StatusCode,
                        stopwatch.ElapsedMilliseconds);
                }
            }
        }
    }
}