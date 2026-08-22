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
    private const int MaxRetries = 3;

    public N8nWorkflowGateway(HttpClient httpClient, IConfiguration configuration, ILogger<N8nWorkflowGateway> logger)
    {
        _httpClient = httpClient;

        // CRÍTICO: Eliminamos el fallback a localhost. 
        // Si no hay configuración en producción, la aplicación debe fallar fuerte y claro.
        _baseUrl = configuration["N8n:BaseUrl"]
                   ?? throw new InvalidOperationException("CRÍTICO: La URL de n8n no está configurada en las variables de entorno.");

        _logger = logger;
    }

    public async Task TriggerWorkflowAsync<T>(string workflowId, N8nEventPayload<T> payload, CancellationToken cancellationToken)
    {
        var url = $"{_baseUrl}/webhook/{workflowId}";

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                // La petición viaja fuertemente tipada
                var response = await _httpClient.PostAsJsonAsync(url, payload, cancellationToken);
                response.EnsureSuccessStatusCode();

                _logger.LogInformation("Evento {EventType} enviado a n8n exitosamente. CorrelationId: {CorrelationId}",
                    payload.EventType, payload.CorrelationId);

                return; // Éxito, salimos del bucle de reintentos
            }
            catch (Exception ex) when (attempt < MaxRetries)
            {
                // Respaldo exponencial: Espera 2s, luego 4s...
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));

                _logger.LogWarning(ex, "Intento {Attempt} fallido al enviar evento {EventType} a n8n. Reintentando en {Delay}s. CorrelationId: {CorrelationId}",
                    attempt, payload.EventType, delay.TotalSeconds, payload.CorrelationId);

                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fallo definitivo tras {MaxRetries} intentos al disparar flujo {WorkflowId} en n8n para Workspace {WorkspaceId}. CorrelationId: {CorrelationId}",
                    MaxRetries, workflowId, payload.WorkspaceId, payload.CorrelationId);
                throw;
            }
        }
    }
}