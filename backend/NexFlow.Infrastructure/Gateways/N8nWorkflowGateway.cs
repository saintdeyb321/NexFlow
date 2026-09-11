using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions.Integrations;

namespace NexFlow.Infrastructure.Gateways;

public class N8nWorkflowGateway : IWorkflowGateway
{
    private readonly HttpClient _httpClient;
    private readonly string? _baseUrl;
    private readonly string? _catalogWebhookId;
    private readonly ILogger<N8nWorkflowGateway> _logger;
    private const int MaxRetries = 3;

    public N8nWorkflowGateway(HttpClient httpClient, IConfiguration configuration, ILogger<N8nWorkflowGateway> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _baseUrl = configuration["N8n:BaseUrl"];
        _catalogWebhookId = configuration["N8n:CatalogWebhookId"] ?? "catalog-generator";
    }

    // =========================================================
    // 1. MÉTODO ORIGINAL GENÉRICO
    // =========================================================
    public async Task TriggerWorkflowAsync<T>(string workflowId, N8nEventPayload<T> payload, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_baseUrl))
        {
            _logger.LogError("Se intentó disparar el flujo {WorkflowId} pero N8n:BaseUrl no está configurado en appsettings.", workflowId);
            throw new InvalidOperationException("CRÍTICO: La URL de n8n no está configurada.");
        }

        var url = $"{_baseUrl.TrimEnd('/')}/webhook/{workflowId}";

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync(url, payload, cancellationToken);
                response.EnsureSuccessStatusCode();

                _logger.LogInformation("Evento {EventType} enviado a n8n exitosamente. CorrelationId: {CorrelationId}",
                    payload.EventType, payload.CorrelationId);

                return;
            }
            catch (Exception) when (attempt < MaxRetries) // 🔥 CORRECCIÓN: Quitamos el 'ex' sin usar
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                _logger.LogWarning("Intento {Attempt} fallido al enviar evento {EventType} a n8n. Reintentando en {Delay}s.",
                    attempt, payload.EventType, delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fallo definitivo tras {MaxRetries} intentos al disparar flujo {WorkflowId} en n8n.",
                    MaxRetries, workflowId);
                throw;
            }
        }
    }

    // =========================================================
    // 2. MÉTODO ESPECÍFICO PARA EL CATÁLOGO (SPRINT 8)
    // =========================================================
    public async Task TriggerCatalogGenerationAsync(string jsonPayload, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_baseUrl))
        {
            _logger.LogError("N8n:BaseUrl no configurado.");
            return;
        }

        var url = $"{_baseUrl.TrimEnd('/')}/webhook/{_catalogWebhookId}";
        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var response = await _httpClient.PostAsync(url, content, cancellationToken);
                response.EnsureSuccessStatusCode();
                _logger.LogInformation("JSON del catálogo enviado a n8n exitosamente.");
                return;
            }
            catch (Exception) when (attempt < MaxRetries) // 🔥 CORRECCIÓN: Quitamos el 'ex' sin usar
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fallo definitivo al disparar generador de catálogos en n8n.");
                throw;
            }
        }
    }
}