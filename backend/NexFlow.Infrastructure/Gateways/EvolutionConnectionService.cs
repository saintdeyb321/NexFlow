using System.Net.Http.Json;
using System.Text.Json;
using System.Net;
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

        _webhookUrl = configuration["Evolution:WebhookUrl"] ?? string.Empty;
        _webhookKey = configuration["Evolution:WebhookKey"] ?? string.Empty;

        // 🔥 SPRINT 20: CORRECCIÓN DE HEADERS. 
        // Solo inyectamos "apikey" una vez. .NET concatena si lo agregas dos veces, arruinando la clave.
        if (!string.IsNullOrEmpty(_apiKey))
        {
            _httpClient.DefaultRequestHeaders.Remove("apikey");
            _httpClient.DefaultRequestHeaders.Add("apikey", _apiKey);
        }
    }

    // 🔥 BLINDAJE: Evolution explota si hay guiones, espacios o mayúsculas en el nombre.
    private string SanitizeInstanceName(string? rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return string.Empty;
        return rawName.Replace("-", "").Replace(" ", "").ToLowerInvariant();
    }

    public async Task<string> GetConnectionStatusAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var rawName = await _instanceResolver.GetInstanceNameAsync(workspaceId, cancellationToken);
        var instanceName = SanitizeInstanceName(rawName);
        if (string.IsNullOrEmpty(instanceName)) return "DISCONNECTED";

        var url = $"{_baseUrl}/instance/connectionState/{instanceName}";

        try
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound) return "DISCONNECTED";
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("instance", out var instanceNode)
                && instanceNode.ValueKind == JsonValueKind.Object && instanceNode.TryGetProperty("state", out var stateNode)
                && stateNode.ValueKind == JsonValueKind.String)
            {
                var state = stateNode.GetString()?.ToUpperInvariant();
                if (state == "OPEN") return "CONNECTED";
                if (state == "CONNECTING") return "QR_AVAILABLE";
                return "DISCONNECTED";
            }
            throw new HttpRequestException("Evolution devolvió una respuesta de estado inválida.");
        }
        catch (JsonException ex)
        {
            throw new HttpRequestException("Evolution devolvió una respuesta de estado inválida.", ex);
        }
    }

    public async Task<string?> ConnectAndGetQrAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var rawName = await _instanceResolver.GetInstanceNameAsync(workspaceId, cancellationToken);
        var instanceName = SanitizeInstanceName(rawName);

        if (string.IsNullOrEmpty(instanceName))
        {
            _logger.LogWarning("ConnectAndGetQrAsync: No se pudo resolver un nombre de instancia válido.");
            return null;
        }

        var currentStatus = await GetConnectionStatusAsync(workspaceId, cancellationToken);
        if (currentStatus == "CONNECTED")
        {
            return "ALREADY_CONNECTED";
        }

        try
        {
            // 1. Intentamos conectar si la instancia ya existe (Devuelve el QR si está desconectada)
            var connectUrl = $"{_baseUrl}/instance/connect/{instanceName}";
            using var connectResponse = await _httpClient.GetAsync(connectUrl, cancellationToken);
            if (!connectResponse.IsSuccessStatusCode && connectResponse.StatusCode != HttpStatusCode.NotFound)
                connectResponse.EnsureSuccessStatusCode();

            if (connectResponse.IsSuccessStatusCode)
            {
                var connectJson = await connectResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
                var base64 = ExtractBase64(connectJson);
                if (!string.IsNullOrEmpty(base64))
                {
                    // Si logró conectarse a una existente, aseguramos el webhook por si acaso
                    await SetWebhookSafeAsync(instanceName, cancellationToken);
                    return base64;
                }
                throw new HttpRequestException("Evolution no devolvió un código QR válido.");
            }

            // 2. Si no existe, CREAMOS la instancia con un payload súper limpio
            var createUrl = $"{_baseUrl}/instance/create";
            var createPayload = new
            {
                instanceName = instanceName,
                qrcode = true,
                token = Guid.NewGuid().ToString("N")
            };

            using var createResponse = await _httpClient.PostAsJsonAsync(createUrl, createPayload, cancellationToken);
            createResponse.EnsureSuccessStatusCode();

            // 3. SETEAMOS EL WEBHOOK en un paso separado
            await SetWebhookSafeAsync(instanceName, cancellationToken);

            // 4. Retornamos el QR
            var createJson = await createResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            var createdQr = ExtractBase64(createJson);
            if (string.IsNullOrWhiteSpace(createdQr))
                throw new HttpRequestException("Evolution no devolvió un código QR válido.");
            return createdQr;
        }
        catch (JsonException ex)
        {
            throw new HttpRequestException("Evolution devolvió una respuesta de conexión inválida.", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo crítico de red hacia Evolution API para la instancia {InstanceName}", instanceName);
            throw;
        }
    }

    private async Task SetWebhookSafeAsync(string instanceName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_webhookUrl)) return;

        try
        {
            var webhookUrlEndpoint = $"{_baseUrl}/webhook/set/{instanceName}";
            var webhookPayload = new
            {
                webhook = new
                {
                    url = _webhookUrl,
                    byEvents = false,
                    base64 = false,
                    events = new[] { "MESSAGES_UPSERT" },
                    headers = new Dictionary<string, string> { { "X-NexFlow-Webhook-Key", _webhookKey } }
                }
            };

            using var response = await _httpClient.PostAsJsonAsync(webhookUrlEndpoint, webhookPayload, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Excepción al intentar configurar el webhook para {Instance}", instanceName);
            throw;
        }
    }

    public async Task<bool> DisconnectAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var rawName = await _instanceResolver.GetInstanceNameAsync(workspaceId, cancellationToken);
        var instanceName = SanitizeInstanceName(rawName);
        if (string.IsNullOrEmpty(instanceName)) return true;

        // Logout cierra la sesión de WhatsApp pero no borra la instancia
        var url = $"{_baseUrl}/instance/logout/{instanceName}";
        using var response = await _httpClient.DeleteAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        return true;
    }

    // Extractor Universal para soportar formatos Base64 de v1 y v2 de Evolution
    private string? ExtractBase64(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object)
            throw new HttpRequestException("Evolution devolvió una respuesta de conexión inválida.");
        if (json.TryGetProperty("base64", out var b1)) return b1.GetString();

        if (json.TryGetProperty("qrcode", out var q2))
        {
            if (q2.ValueKind == JsonValueKind.String) return q2.GetString();
            if (q2.ValueKind == JsonValueKind.Object && q2.TryGetProperty("base64", out var b2)) return b2.GetString();
        }

        if (json.TryGetProperty("hash", out var hashNode) && hashNode.TryGetProperty("qrcode", out var q3))
            return q3.GetString();

        return null;
    }
}
