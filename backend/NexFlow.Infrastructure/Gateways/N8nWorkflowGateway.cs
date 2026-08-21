using System;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions.Integrations;

namespace NexFlow.Infrastructure.Gateways;

public class N8nWorkflowGateway : IWorkflowGateway
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly ILogger<N8nWorkflowGateway> _logger;

    public N8nWorkflowGateway(HttpClient httpClient, IConfiguration configuration, ILogger<N8nWorkflowGateway> logger)
    {
        _httpClient = httpClient;
        _baseUrl = configuration["N8n:BaseUrl"] ?? "http://localhost:5678";
        _logger = logger;
    }

    public async Task TriggerWorkflowAsync<T>(string workflowId, N8nEventPayload<T> payload, CancellationToken cancellationToken)
    {
        var url = $"{_baseUrl}/webhook/{workflowId}";

        try
        {
            // Ahora la petición viaja fuertemente tipada y blindada con multi-tenant
            var response = await _httpClient.PostAsJsonAsync(url, payload, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            // Usamos Structured Logging para atrapar el CorrelationId
            _logger.LogError(ex, "Fallo al disparar flujo {WorkflowId} en n8n para Workspace {WorkspaceId}. Correlation: {CorrelationId}",
                workflowId, payload.WorkspaceId, payload.CorrelationId);
            throw;
        }
    }
}