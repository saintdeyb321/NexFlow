using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Integrations;

namespace NexFlow.Infrastructure.Gateways;

public class EvolutionMessageGateway : IMessageGateway
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly ILogger<EvolutionMessageGateway> _logger;
    private readonly IInstanceResolver _instanceResolver;

    public EvolutionMessageGateway(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<EvolutionMessageGateway> logger,
        IInstanceResolver instanceResolver)
    {
        _httpClient = httpClient;
        _logger = logger;
        _instanceResolver = instanceResolver;

        _baseUrl = configuration["Evolution:BaseUrl"]?.TrimEnd('/') ?? throw new ArgumentNullException("Evolution BaseUrl no configurada");
        _apiKey = configuration["Evolution:ApiKey"] ?? string.Empty;

        // 🔥 Auditoría (Sprint 1.3): Reducimos el timeout por defecto a 8 segundos para no bloquear la cola.
        var timeout = int.TryParse(configuration["Evolution:TimeoutSeconds"], out var t) ? t : 8;
        _httpClient.Timeout = TimeSpan.FromSeconds(timeout);

        if (!string.IsNullOrEmpty(_apiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("apikey", _apiKey);
        }
    }

    public async Task<string> SendTextAsync(Guid workspaceId, string customerIdentifier, string message, CancellationToken cancellationToken)
    {
        var instanceName = await _instanceResolver.GetInstanceNameAsync(workspaceId, cancellationToken);

        if (string.IsNullOrEmpty(instanceName))
        {
            _logger.LogError("El workspace {WorkspaceId} intentó enviar un mensaje pero no tiene EvolutionInstanceName configurado.", workspaceId);
            throw new InvalidOperationException($"El workspace {workspaceId} no tiene una conexión de WhatsApp asignada.");
        }

        var url = $"{_baseUrl}/message/sendText/{instanceName}";

        var safeMessage = string.IsNullOrWhiteSpace(message)
            ? "Lo siento, tuve un pequeño problema procesando la respuesta. ¿Puedes repetir?"
            : message;

        var payload = new
        {
            number = customerIdentifier,
            text = safeMessage,
            options = new { delay = 1200, presence = "composing" }
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync(url, payload, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorDetails = await response.Content.ReadAsStringAsync(cancellationToken);

                _logger.LogError(
                    "Evolution rejected outbound message. Workspace={WorkspaceId}, Instance={InstanceName}, Status={StatusCode}",
                    workspaceId,
                    instanceName,
                    response.StatusCode);

                _logger.LogDebug("Detalle completo del error: {ErrorDetails}", errorDetails);

                // 🔥 Auditoría (Sprint 1.3): Evitamos EnsureSuccessStatusCode para no crashear. Retornamos estado Failed.
                return $"FAILED_{(int)response.StatusCode}_{Guid.NewGuid()}";
            }

            var jsonResponse = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            if (jsonResponse.TryGetProperty("key", out var keyProp) && keyProp.TryGetProperty("id", out var idProp))
            {
                return idProp.GetString() ?? Guid.NewGuid().ToString();
            }

            return Guid.NewGuid().ToString();
        }
        catch (TaskCanceledException)
        {
            // 🔥 Auditoría (Sprint 1.3): Timeout controlado. No usamos throw para proteger al background worker.
            _logger.LogWarning("Timeout: Evolution API no respondió a tiempo al enviar a {Customer}. Estado: Failed.", customerIdentifier);
            return $"FAILED_TIMEOUT_{Guid.NewGuid()}";
        }
        catch (Exception ex)
        {
            // 🔥 Auditoría (Sprint 1.3): Fallo atrapado.
            _logger.LogError(ex, "Fallo crítico en conexión a Evolution. Workspace: {WorkspaceId}, Instancia: {InstanceName}", workspaceId, instanceName);
            return $"FAILED_ERROR_{Guid.NewGuid()}";
        }
    }
}