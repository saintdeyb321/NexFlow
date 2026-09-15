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
        if (string.IsNullOrEmpty(instanceName)) return $"FAILED_NO_INSTANCE_{Guid.NewGuid()}";

        var url = $"{_baseUrl}/message/sendText/{instanceName}";
        var safeMessage = string.IsNullOrWhiteSpace(message) ? "Lo siento, tuve un pequeño problema. ¿Puedes repetir?" : message;

        var payload = new
        {
            number = customerIdentifier,
            text = safeMessage,
            options = new { delay = 1200, presence = "composing" }
        };

        return await ExecutePostAsync(url, payload, workspaceId, instanceName, customerIdentifier, cancellationToken);
    }

    // 🔥 SPRINT 17: Soporte para PDFs
    public async Task<string> SendDocumentAsync(Guid workspaceId, string customerIdentifier, string documentUrl, string fileName, string caption, CancellationToken cancellationToken)
    {
        var instanceName = await _instanceResolver.GetInstanceNameAsync(workspaceId, cancellationToken);
        if (string.IsNullOrEmpty(instanceName)) return $"FAILED_NO_INSTANCE_{Guid.NewGuid()}";

        var url = $"{_baseUrl}/message/sendMedia/{instanceName}";
        var payload = new
        {
            number = customerIdentifier,
            options = new { delay = 2000, presence = "composing" },
            mediaMessage = new { mediatype = "document", fileName = fileName, caption = caption, media = documentUrl }
        };

        return await ExecutePostAsync(url, payload, workspaceId, instanceName, customerIdentifier, cancellationToken);
    }

    // 🔥 SPRINT 17: Soporte Nativo para Imágenes (WebP/JPG)
    public async Task<string> SendImageAsync(Guid workspaceId, string customerIdentifier, string imageUrl, string caption, CancellationToken cancellationToken)
    {
        var instanceName = await _instanceResolver.GetInstanceNameAsync(workspaceId, cancellationToken);
        if (string.IsNullOrEmpty(instanceName)) return $"FAILED_NO_INSTANCE_{Guid.NewGuid()}";

        var url = $"{_baseUrl}/message/sendMedia/{instanceName}";
        var payload = new
        {
            number = customerIdentifier,
            options = new { delay = 1500, presence = "composing" },
            mediaMessage = new { mediatype = "image", caption = caption, media = imageUrl }
        };

        return await ExecutePostAsync(url, payload, workspaceId, instanceName, customerIdentifier, cancellationToken);
    }

    private async Task<string> ExecutePostAsync(string url, object payload, Guid workspaceId, string instanceName, string customerIdentifier, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(url, payload, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorDetails = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Evolution rejected message. Status={StatusCode}, Error={ErrorDetails}", response.StatusCode, errorDetails);
                return $"FAILED_{(int)response.StatusCode}_{Guid.NewGuid()}";
            }
            var jsonResponse = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            if (jsonResponse.TryGetProperty("key", out var keyProp) && keyProp.TryGetProperty("id", out var idProp))
                return idProp.GetString() ?? Guid.NewGuid().ToString();

            return Guid.NewGuid().ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo crítico en conexión a Evolution.");
            return $"FAILED_ERROR_{Guid.NewGuid()}";
        }
    }
}