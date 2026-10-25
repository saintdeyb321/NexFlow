using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services;

// Standard application logging only. Callers supply fixed phase/operation names,
// enum outcomes and provider categories, never customer text or entity labels.
internal static class ConversationDiagnostics
{
    private static readonly Meter Meter = new("NexFlow.Conversation", "1.0");
    private static readonly Counter<long> Calls = Meter.CreateCounter<long>("nexflow.conversation.events");
    private static readonly Histogram<double> Durations = Meter.CreateHistogram<double>("nexflow.conversation.phase.duration", "ms");
    internal static void Count(string phase, string outcome)
    {
        try { Calls.Add(1, new TagList { { "phase", phase }, { "outcome", outcome } }); }
        catch { /* Observers are not business dependencies. */ }
    }
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
                var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                Count(phase, status);
                Durations.Record(elapsed, new TagList { { "phase", phase }, { "outcome", status } });
                logger?.LogInformation("ConversationPhase Phase={Phase} Outcome={Outcome} ElapsedMs={ElapsedMs} Provider={Provider} Reason={Reason} ProviderInvocations={ProviderInvocations}",
                    phase, status, elapsed, provider, reason, provider == null ? 0 : 1);
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
