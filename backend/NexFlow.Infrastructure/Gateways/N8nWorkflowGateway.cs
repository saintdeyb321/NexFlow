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
        // Asume el puerto de n8n por defecto si no está en appsettings
        _baseUrl = configuration["N8n:BaseUrl"] ?? "http://localhost:5678";
        _logger = logger;
    }

    public async Task TriggerWorkflowAsync(Guid workspaceId, string workflowId, object payload, CancellationToken cancellationToken)
    {
        // Los Webhooks de n8n siguen este formato
        var url = $"{_baseUrl}/webhook/{workflowId}";

        try
        {
            // Envolvemos tu payload con el ID del negocio para asegurar el multi-tenant también en n8n
            var n8nPayload = new { WorkspaceId = workspaceId, Data = payload };

            var response = await _httpClient.PostAsJsonAsync(url, n8nPayload, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo al disparar el flujo {WorkflowId} en n8n para el Workspace {WorkspaceId}", workflowId, workspaceId);
            throw;
        }
    }
}