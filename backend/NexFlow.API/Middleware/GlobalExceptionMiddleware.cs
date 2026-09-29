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
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Excepción no controlada atrapada por el escudo global en {Path}", context.Request.Path);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var statusCode = exception switch
        {
            DomainException or ArgumentException => 400,
            UnauthorizedAccessException => context.User.Identity?.IsAuthenticated == true ? 403 : 401,
            KeyNotFoundException => 404,
            ConcurrencyException or Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException => 409,
            Npgsql.PostgresException pg when pg.SqlState is "23505" or "40001" or "23P01" => 409,
            Grpc.Core.RpcException rpc when rpc.StatusCode == Grpc.Core.StatusCode.NotFound => 404,
            Grpc.Core.RpcException rpc when rpc.StatusCode is Grpc.Core.StatusCode.AlreadyExists or Grpc.Core.StatusCode.Aborted => 409,
            Grpc.Core.RpcException rpc when rpc.StatusCode == Grpc.Core.StatusCode.InvalidArgument => 400,
            Grpc.Core.RpcException or Npgsql.NpgsqlException or HttpRequestException or TimeoutException or OperationCanceledException or StackExchange.Redis.RedisException => 503,
            AggregateException aggregate when aggregate.Flatten().InnerExceptions.Any(e => e is HttpRequestException or Grpc.Core.RpcException or Npgsql.NpgsqlException or TimeoutException) => 503,
            Microsoft.EntityFrameworkCore.DbUpdateException db when db.InnerException is Npgsql.PostgresException pg && pg.SqlState is "23505" or "40001" or "23P01" => 409,
            Microsoft.EntityFrameworkCore.DbUpdateException db when db.InnerException is Npgsql.NpgsqlException => 503,
            _ => 500
        };
        context.Response.StatusCode = statusCode;
        var code = statusCode switch { 400 => "Validation.Invalid", 401 => "Security.Unauthorized", 403 => "Security.Forbidden", 404 => "Resource.NotFound", 409 => "Resource.Conflict", 503 => "Dependency.Unavailable", _ => "System.InternalError" };
        var message = statusCode switch
        {
            400 when exception is DomainException => exception.Message,
            400 => "La entrada no es válida.", 401 => "Autenticación requerida.", 403 => "Acceso denegado.",
            404 => "Recurso no encontrado.", 409 => "El recurso cambió o la operación entra en conflicto.",
            503 => "Una dependencia no está disponible. Inténtalo nuevamente.", _ => "Ha ocurrido un error inesperado."
        };
        return context.Response.WriteAsJsonAsync(new { code, message, correlationId = context.Items["CorrelationId"]?.ToString() ?? context.TraceIdentifier });
    }
}
