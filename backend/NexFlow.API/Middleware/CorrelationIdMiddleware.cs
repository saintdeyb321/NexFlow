using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
        // Extraer el ID si viene del cliente, o generar uno nuevo
        string correlationId = context.Request.Headers.TryGetValue(CorrelationIdHeader, out StringValues values)
            ? values.FirstOrDefault() ?? Guid.NewGuid().ToString()
            : Guid.NewGuid().ToString();

        // Agregar a la respuesta para que el Frontend lo vea
        context.Response.Headers.Append(CorrelationIdHeader, correlationId);

        // Guardar en el contexto de la petición
        context.Items["CorrelationId"] = correlationId;

        // Inyectar el CorrelationId en todos los logs estructurados
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await _next(context);
        }
    }
}