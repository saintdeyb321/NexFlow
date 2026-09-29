using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using NexFlow.Application.Abstractions.Integrations;

namespace NexFlow.Infrastructure.Gateways;

public class N8nWorkflowGateway(HttpClient httpClient, IConfiguration configuration) : IWorkflowGateway
{
    public async Task TriggerWorkflowAsync<T>(string workflowId, N8nEventPayload<T> payload, CancellationToken ct)
    {
        var baseUrl = configuration["N8n:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl)) throw new InvalidOperationException("N8n URL is not configured.");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/webhook/{workflowId}")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", payload.IdempotencyKey);
        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
}
