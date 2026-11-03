using System.Net;
using NexFlow.Application.Engines.AI;

namespace NexFlow.Application.Features.AI.Router;

public sealed class AiQuotaOptions
{
    // No invented provider quotas. Enable only with verified account/project settings.
    public bool Enabled { get; set; }
    public List<AiQuotaPool> Pools { get; set; } = [];
}
public sealed class AiQuotaPool
{
    public AiProviderType Provider { get; set; }
    public string Key { get; set; } = ""; // Non-secret logical account/project pool; models may share it.
    public string[] Models { get; set; } = [];
    public string ResetTimeZone { get; set; } = "";
    public int? RequestsPerMinute { get; set; }
    public int? RequestsPerDay { get; set; }
    public long? TokensPerMinute { get; set; }
    public long? TokensPerDay { get; set; }
    public double SafetyMargin { get; set; } = .30;
    public double WorkspaceShare { get; set; } = .10;
    public int TokenReservationPerAttempt { get; set; } // Conservative upper envelope, never a tokenizer estimate.
}
public interface IAiRequestAdmission
{
    Task AdmitAsync(AiProviderType provider, string model, int payloadBytes, int? maxOutputTokens, CancellationToken ct);
    Task ObserveAsync(AiProviderType provider, string model, HttpResponseMessage response, CancellationToken ct);
}
public sealed class AiBudgetDeniedException(string reason) : HttpRequestException("AI request admission denied: " + reason, null,
    reason is "GlobalQuota" or "WorkspaceShare" or "ProviderCooldown" ? HttpStatusCode.TooManyRequests : HttpStatusCode.ServiceUnavailable);
