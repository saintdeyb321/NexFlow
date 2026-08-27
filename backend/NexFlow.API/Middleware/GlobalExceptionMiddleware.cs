using System.Net;
using System.Text.Json;
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
            _logger.LogError(ex, "Excepción no controlada atrapada por el escudo global en {Path}", context.Request.Path);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json"; // 🛡️ Simplificado a JSON estándar

        var statusCode = exception switch
        {
            DomainException => (int)HttpStatusCode.BadRequest,
            UnauthorizedAccessException => (int)HttpStatusCode.Unauthorized,
            _ => (int)HttpStatusCode.InternalServerError
        };

        context.Response.StatusCode = statusCode;
        var correlationId = context.Items["CorrelationId"]?.ToString() ?? "N/A";

        // 🛡️ CONTRATO UNIFICADO: Solo code, message y correlationId
        var response = new
        {
            code = exception switch
            {
                DomainException => "Domain.RuleViolation",
                UnauthorizedAccessException => "Security.AccessDenied",
                _ => "System.InternalError"
            },
            message = statusCode == 500
                ? "Ha ocurrido un error inesperado en NexFlow. Nuestro equipo ha sido notificado."
                : exception.Message,
            correlationId = correlationId
        };

        // Forzamos formato CamelCase para que JS lo lea nativo
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        return context.Response.WriteAsync(JsonSerializer.Serialize(response, options));
    }
}