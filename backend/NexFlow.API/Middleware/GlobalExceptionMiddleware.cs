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
            // Dejamos que la petición continúe su camino normal
            await _next(context);
        }
        catch (Exception ex)
        {
            // Si explota en CUALQUIER capa (Dominio, BD, API), lo atrapamos aquí
            _logger.LogError(ex, "Excepción no controlada atrapada por el escudo global en {Path}", context.Request.Path);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        // Forzamos la respuesta como un JSON de error estándar (ProblemDetails)
        context.Response.ContentType = "application/problem+json";

        // Mapeamos el tipo de excepción a un código HTTP
        var statusCode = exception switch
        {
            DomainException => (int)HttpStatusCode.BadRequest, // Errores de negocio (Ej: "La fecha debe ser mayor")
            UnauthorizedAccessException => (int)HttpStatusCode.Unauthorized, // Problemas de acceso
            _ => (int)HttpStatusCode.InternalServerError // Cualquier otra explosión (500)
        };

        context.Response.StatusCode = statusCode;

        // Ocultamos los detalles técnicos si el error es 500
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
                : exception.Message // Si es de Dominio, sí mostramos el mensaje porque es seguro ("La licencia está inactiva")
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(response));
    }
}