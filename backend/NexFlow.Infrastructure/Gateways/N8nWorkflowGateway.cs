using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using NexFlow.Application.Abstractions.Integrations;

namespace NexFlow.Infrastructure.Gateways;

public class N8nWorkflowGateway : IWorkflowGateway
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public N8nWorkflowGateway(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        var timeout = int.TryParse(configuration["N8n:TimeoutSeconds"], out var seconds) ? seconds : 30;
        _httpClient.Timeout = TimeSpan.FromSeconds(timeout);
    }

    public async Task TriggerWorkflowAsync<T>(string workflowId, N8nEventPayload<T> payload, CancellationToken ct)
    {
        var baseUrl = _configuration["N8n:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl)) throw new InvalidOperationException("N8n URL is not configured.");
        var secret = _configuration["N8n:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(secret)) throw new InvalidOperationException("Missing configuration: N8n:WebhookSecret.");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/webhook/{Uri.EscapeDataString(workflowId)}")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", payload.IdempotencyKey);
        request.Headers.Add("X-NexFlow-Webhook-Secret", secret);
        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
}
