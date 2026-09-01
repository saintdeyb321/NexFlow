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

        var timeout = int.TryParse(configuration["Evolution:TimeoutSeconds"], out var t) ? t : 10;
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

        // 🔥 BLINDAJE 1: Si la IA devolvió un texto vacío, enviamos un fallback para que Evolution no colapse
        var safeMessage = string.IsNullOrWhiteSpace(message)
            ? "Lo siento, tuve un pequeño problema procesando la respuesta. ¿Puedes repetir?"
            : message;

        // 🔥 BLINDAJE 2: Compatibilidad multiplataforma. Enviamos tanto "text" (para v1) como "textMessage" (para v2)
        var payload = new
        {
            number = customerIdentifier,
            text = safeMessage,
            textMessage = new { text = safeMessage },
            options = new { delay = 1200, presence = "composing" }
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync(url, payload, cancellationToken);

            // 🔥 RADAR DE DEPURACIÓN: Si Evolution lo rechaza, imprimiremos el porqué exacto
            if (!response.IsSuccessStatusCode)
            {
                var errorDetails = await response.Content.ReadAsStringAsync(cancellationToken);
                Console.WriteLine($"\n🚨 EVOLUTION API RECHAZÓ EL MENSAJE (400) 🚨");
                Console.WriteLine($"URL: {url}");
                Console.WriteLine($"Detalle del Error: {errorDetails}");
                Console.WriteLine("--------------------------------------------------\n");

                response.EnsureSuccessStatusCode();
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
            _logger.LogError("Timeout: Evolution API no respondió al enviar mensaje a {Customer}", customerIdentifier);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo crítico en Evolution. Workspace: {WorkspaceId}, Instancia: {InstanceName}", workspaceId, instanceName);
            throw;
        }
    }
}