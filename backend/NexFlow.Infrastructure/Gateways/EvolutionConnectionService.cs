using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Integrations;

namespace NexFlow.Infrastructure.Gateways;

public class EvolutionConnectionService : IEvolutionConnectionService
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly string _webhookUrl;
    private readonly string _webhookKey;
    private readonly IInstanceResolver _instanceResolver;
    private readonly ILogger<EvolutionConnectionService> _logger;

    public EvolutionConnectionService(
        HttpClient httpClient,
        IConfiguration configuration,
        IInstanceResolver instanceResolver,
        ILogger<EvolutionConnectionService> logger)
    {
        _httpClient = httpClient;
        _instanceResolver = instanceResolver;
        _logger = logger;

        _baseUrl = configuration["Evolution:BaseUrl"]?.TrimEnd('/') ?? throw new ArgumentNullException("Evolution BaseUrl no configurada");
        _apiKey = configuration["Evolution:ApiKey"] ?? string.Empty;

        // 🔥 SPRINT 18: Obtenemos configuración de Webhook
        _webhookUrl = configuration["Evolution:WebhookUrl"] ?? string.Empty;
        _webhookKey = configuration["Evolution:WebhookKey"] ?? string.Empty;

        if (!string.IsNullOrEmpty(_apiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("apikey", _apiKey);
        }
    }

    public async Task<string> GetConnectionStatusAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var instanceName = await _instanceResolver.GetInstanceNameAsync(workspaceId, cancellationToken);
        if (string.IsNullOrEmpty(instanceName)) return "DISCONNECTED";

        var url = $"{_baseUrl}/instance/connectionState/{instanceName}";

        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return "DISCONNECTED";

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            if (json.TryGetProperty("instance", out var instanceNode) && instanceNode.TryGetProperty("state", out var stateNode))
            {
                var state = stateNode.GetString()?.ToUpperInvariant();
                if (state == "OPEN") return "CONNECTED";
                if (state == "CONNECTING") return "QR_AVAILABLE";
            }
            return "DISCONNECTED";
        }
        catch (Exception)
        {
            return "ERROR";
        }
    }

    public async Task<string?> ConnectAndGetQrAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var instanceName = await _instanceResolver.GetInstanceNameAsync(workspaceId, cancellationToken);
        if (string.IsNullOrEmpty(instanceName)) return null;

        // 🔥 SPRINT 18 (Regla fundamental): Bloquear una segunda conexión únicamente cuando CONNECTED
        var currentStatus = await GetConnectionStatusAsync(workspaceId, cancellationToken);
        if (currentStatus == "CONNECTED")
        {
            _logger.LogWarning("Intento de reconexión bloqueado. La instancia {Instance} ya está CONNECTED.", instanceName);
            return null; // O lanzar excepción de dominio según prefieras
        }

        var url = $"{_baseUrl}/instance/create";

        // 🔥 SPRINT 18: Inyección segura del Webhook al momento de crear
        var payload = new
        {
            instanceName = instanceName,
            token = Guid.NewGuid().ToString("N"),
            qrcode = true,
            webhook = string.IsNullOrEmpty(_webhookUrl) ? null : new
            {
                url = _webhookUrl,
                byEvents = false,
                base64 = false,
                events = new[] { "MESSAGES_UPSERT" },
                headers = new Dictionary<string, string> { { "X-NexFlow-Webhook-Key", _webhookKey } }
            }
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync(url, payload, cancellationToken);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);

            if (json.TryGetProperty("qrcode", out var qrNode) && qrNode.TryGetProperty("base64", out var base64Node))
            {
                return base64Node.GetString();
            }

            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                var connectUrl = $"{_baseUrl}/instance/connect/{instanceName}";
                var connectResponse = await _httpClient.GetAsync(connectUrl, cancellationToken);
                var connectJson = await connectResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
                if (connectJson.TryGetProperty("base64", out var fallbackBase64))
                {
                    return fallbackBase64.GetString();
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo al crear la instancia de Evolution para {InstanceName}", instanceName);
            return null;
        }
    }

    public async Task<bool> DisconnectAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var instanceName = await _instanceResolver.GetInstanceNameAsync(workspaceId, cancellationToken);
        if (string.IsNullOrEmpty(instanceName)) return true;

        var url = $"{_baseUrl}/instance/logout/{instanceName}";
        try
        {
            await _httpClient.DeleteAsync(url, cancellationToken);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}