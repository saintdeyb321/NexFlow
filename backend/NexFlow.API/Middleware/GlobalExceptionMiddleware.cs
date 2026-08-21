using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NexFlow.Domain.Exceptions;

namespace NexFlow.API.Middleware;

public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            // El ILogger ya tiene el CorrelationId gracias al Middleware anterior
            _logger.LogError(ex, "Excepción no controlada atrapada por el escudo global en {Path}", context.Request.Path);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/problem+json";

        var statusCode = exception switch
        {
            DomainException => (int)HttpStatusCode.BadRequest,
            UnauthorizedAccessException => (int)HttpStatusCode.Unauthorized,
            _ => (int)HttpStatusCode.InternalServerError
        };

        context.Response.StatusCode = statusCode;

        // Extraemos el Correlation ID del HttpContext
        var correlationId = context.Items["CorrelationId"]?.ToString() ?? "N/A";

        var response = new
        {
            status = statusCode,
            title = exception switch
            {
                DomainException => "Violación de regla de negocio",
                UnauthorizedAccessException => "Acceso denegado",
                _ => "Error interno del servidor"
            },
            detail = statusCode == 500
                ? "Ha ocurrido un error inesperado en NexFlow. Nuestro equipo ha sido notificado."
                : exception.Message,
            correlationId = correlationId // <-- Blindaje Enterprise: Trazabilidad total
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(response));
    }
}