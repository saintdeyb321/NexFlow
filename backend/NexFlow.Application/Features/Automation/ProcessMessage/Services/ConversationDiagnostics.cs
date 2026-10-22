using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services;

// Standard application logging only. Callers supply fixed phase/operation names,
// enum outcomes and provider categories, never customer text or entity labels.
internal static class ConversationDiagnostics
{
    internal static async Task<T> MeasureAsync<T>(ILogger? logger, string phase, Func<Task<T>> action,
        Func<T, string>? outcome = null, string? provider = null, string? reason = null)
    {
        var started = Stopwatch.GetTimestamp();
        var status = "Error";
        try
        {
            var result = await action();
            status = outcome?.Invoke(result) ?? "Success";
            return result;
        }
        catch (OperationCanceledException) { status = "Cancelled"; throw; }
        finally
        {
            try
            {
                logger?.LogInformation("ConversationPhase Phase={Phase} Outcome={Outcome} ElapsedMs={ElapsedMs} Provider={Provider} Reason={Reason} AiInvocations={AiInvocations}",
                    phase, status, Stopwatch.GetElapsedTime(started).TotalMilliseconds, provider, reason, provider == null ? 0 : 1);
            }
            catch { /* Diagnostics must not alter business outcomes. */ }
        }
    }

    internal static void QueriesAvoided(ILogger? logger, string operation, int count)
    {
        try { logger?.LogInformation("ConversationReuse Operation={Operation} QueriesAvoided={QueriesAvoided}", operation, count); }
        catch { /* Diagnostics must not alter business outcomes. */ }
    }
}
