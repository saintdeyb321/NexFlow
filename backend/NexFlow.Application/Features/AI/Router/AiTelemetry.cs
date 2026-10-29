using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Engines.AI;

namespace NexFlow.Application.Features.AI.Router;

// Diagnostic scope only: no admission, retries, persistence or business authority.
public static class AiTelemetry
{
    public const string MeterName = "NexFlow.Ai";
    private sealed record Context(Guid? Workspace, AiTaskType Task);
    private static readonly AsyncLocal<Context?> Current = new();
    private static readonly Meter Meter = new(MeterName, "1.0");
    private static readonly Counter<long> Attempts = Meter.CreateCounter<long>("nexflow.ai.http.attempts");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("nexflow.ai.http.duration", "ms");
    private static readonly Counter<long> Tokens = Meter.CreateCounter<long>("nexflow.ai.tokens");
    private static readonly Counter<long> UsageReports = Meter.CreateCounter<long>("nexflow.ai.usage.reports");
    private static readonly Histogram<long> InputCharacters = Meter.CreateHistogram<long>("nexflow.ai.input.characters", "characters");

    public static void InterpretationInput(int contract, int context, int message, ILogger logger)
    {
        try
        {
            foreach (var part in new[] { (Kind: "Contract", Count: contract), (Kind: "Context", Count: context), (Kind: "Message", Count: message) })
                InputCharacters.Record(part.Count, new TagList { { "task", AiTaskType.IntentExtraction.ToString() }, { "kind", part.Kind }, { "version", "2" } });
            logger.LogInformation("AiInterpretationInput Version={Version} ContractCharacters={ContractCharacters} ContextCharacters={ContextCharacters} MessageCharacters={MessageCharacters} WorkspaceKey={WorkspaceKey}",
                2, contract, context, message, WorkspaceKey());
        }
        catch { /* Size diagnostics contain no content and cannot affect an operation. */ }
    }

    public static IDisposable Begin(Guid workspace, AiTaskType task) => new Scope(new(workspace, task));
    internal static IDisposable TaskScope(AiTaskType task) => new Scope(new(Current.Value?.Workspace, task));
    private sealed class Scope : IDisposable
    {
        private readonly Context? _previous = Current.Value;
        public Scope(Context value) => Current.Value = value;
        public void Dispose() => Current.Value = _previous;
    }

    public static async Task<HttpResponseMessage> SendAsync(AiProviderType provider, bool alternateModel,
        Func<Task<HttpResponseMessage>> send, ILogger logger, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp(); var outcome = "Network"; int? status = null;
        try
        {
            var response = await send(); status = (int)response.StatusCode;
            outcome = response.IsSuccessStatusCode ? "Success" : status == 429 ? "RateLimit" : status is 401 or 403 or 404 ? "Configuration" : status >= 500 ? "Unavailable" : "Rejected";
            return response;
        }
        catch (OperationCanceledException) { outcome = ct.IsCancellationRequested ? "Cancelled" : "Timeout"; throw; }
        finally
        {
            try
            {
                var task = Current.Value?.Task.ToString() ?? "Unscoped"; var role = alternateModel ? "Alternate" : "Primary";
                var tags = new TagList { { "provider", provider.ToString() }, { "task", task }, { "role", role }, { "outcome", outcome } };
                var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                Attempts.Add(1, tags); Duration.Record(elapsed, tags);
                logger.LogInformation("AiHttpAttempt Provider={Provider} Task={Task} Role={Role} Outcome={Outcome} Status={Status} ElapsedMs={ElapsedMs} WorkspaceKey={WorkspaceKey}",
                    provider, task, role, outcome, status, elapsed, WorkspaceKey());
            }
            catch { /* A listener/exporter cannot change provider or business outcomes. */ }
        }
    }

    public static void Usage(AiProviderType provider, JsonElement root, ILogger logger)
    {
        try
        {
            var groq = provider == AiProviderType.Groq;
            JsonElement usage = default;
            var hasUsage = root.ValueKind == JsonValueKind.Object && root.TryGetProperty(groq ? "usage" : "usageMetadata", out usage)
                && usage.ValueKind == JsonValueKind.Object;
            long? Value(string name) => hasUsage && usage.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.Number
                && field.TryGetInt64(out var value) && value >= 0 ? value : null;
            var input = Value(groq ? "prompt_tokens" : "promptTokenCount");
            var output = Value(groq ? "completion_tokens" : "candidatesTokenCount");
            var total = Value(groq ? "total_tokens" : "totalTokenCount");
            var task = Current.Value?.Task.ToString() ?? "Unscoped";
            var available = input.HasValue || output.HasValue || total.HasValue;
            UsageReports.Add(1, new TagList { { "provider", provider.ToString() }, { "task", task }, { "available", available } });
            foreach (var item in new[] { (Kind: "Input", Value: input), (Kind: "Output", Value: output), (Kind: "Total", Value: total) })
                if (item.Value.HasValue) Tokens.Add(item.Value.Value, new TagList { { "provider", provider.ToString() }, { "task", task }, { "kind", item.Kind } });
            logger.LogInformation("AiUsage Provider={Provider} Task={Task} Available={Available} InputTokens={InputTokens} OutputTokens={OutputTokens} TotalTokens={TotalTokens} WorkspaceKey={WorkspaceKey}",
                provider, task, available, input, output, total, WorkspaceKey());
        }
        catch { /* Missing/malformed usage must not fail an otherwise valid answer. */ }
    }

    private static string? WorkspaceKey() => Current.Value?.Workspace is { } workspace
        ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(workspace.ToString("N"))))[..16] : null;
}
