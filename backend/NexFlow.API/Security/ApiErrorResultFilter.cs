using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace NexFlow.API.Security;

public sealed class ApiErrorResultFilter : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var status = context.Result switch { ObjectResult r => r.StatusCode, StatusCodeResult r => r.StatusCode, _ => null };
        if (status is >= 400)
        {
            var value = (context.Result as ObjectResult)?.Value;
            var json = JsonSerializer.SerializeToElement(value);
            var code = json.ValueKind == JsonValueKind.Object && json.TryGetProperty("code", out var c) ? c.GetString() : null;
            var message = value is string text ? text : json.ValueKind == JsonValueKind.Object && json.TryGetProperty("message", out var m) ? m.GetString() : null;
            context.Result = new ObjectResult(new {
                code = code ?? Code(status.Value),
                message = status >= 500 ? "No se pudo completar la operación." : message ?? "La operación no está disponible.",
                correlationId = context.HttpContext.Items["CorrelationId"]?.ToString() ?? context.HttpContext.TraceIdentifier
            }) { StatusCode = status };
        }
        await next();
    }

    public static string Code(int status) => status switch { 400 => "Validation.Invalid", 401 => "Security.Unauthorized", 403 => "Security.Forbidden", 404 => "Resource.NotFound", 409 => "Resource.Conflict", 429 => "RateLimit.Exceeded", 503 => "Dependency.Unavailable", _ => "System.InternalError" };
}
