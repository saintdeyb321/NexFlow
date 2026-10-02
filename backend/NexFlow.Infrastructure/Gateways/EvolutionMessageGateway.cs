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

    public async Task<string> SendTextAsync(Guid workspaceId, string customerIdentifier, string message, string messageId, Func<CancellationToken, Task> transportStarting, CancellationToken cancellationToken)
    {
        var instanceName = await _instanceResolver.GetInstanceNameAsync(workspaceId, cancellationToken);
        if (string.IsNullOrEmpty(instanceName)) throw new InvalidOperationException("No se encontró instancia de Evolution.");

        var url = $"{_baseUrl}/message/sendText/{instanceName}";
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("Message content is required.", nameof(message));

        // Idempotency is enforced by the durable outbound repository, not provider options.
        var payload = new { number = customerIdentifier, text = message, options = new { delay = 1200, presence = "composing" } };
        return await ExecutePostAsync(url, payload, cancellationToken, transportStarting);
    }

    public async Task<string> SendDocumentAsync(Guid workspaceId, string customerIdentifier, string documentUrl, string fileName, string caption, string messageId, Func<CancellationToken, Task> transportStarting, CancellationToken cancellationToken)
    {
        var instanceName = await _instanceResolver.GetInstanceNameAsync(workspaceId, cancellationToken);
        if (string.IsNullOrEmpty(instanceName)) throw new InvalidOperationException("No se encontró instancia de Evolution.");

        var url = $"{_baseUrl}/message/sendMedia/{instanceName}";
        var payload = new { number = customerIdentifier, options = new { delay = 2000, presence = "composing" }, mediaMessage = new { mediatype = "document", fileName = fileName, caption = caption, media = documentUrl } };
        return await ExecutePostAsync(url, payload, cancellationToken, transportStarting);
    }

    public async Task<string> SendImageAsync(Guid workspaceId, string customerIdentifier, string imageUrl, string caption, string messageId, CancellationToken cancellationToken)
    {
        var instanceName = await _instanceResolver.GetInstanceNameAsync(workspaceId, cancellationToken);
        if (string.IsNullOrEmpty(instanceName)) throw new InvalidOperationException("No se encontró instancia de Evolution.");

        var url = $"{_baseUrl}/message/sendMedia/{instanceName}";
        var payload = new { number = customerIdentifier, options = new { delay = 1500, presence = "composing" }, mediaMessage = new { mediatype = "image", caption = caption, media = imageUrl } };
        return await ExecutePostAsync(url, payload, cancellationToken);
    }

    private async Task<string> ExecutePostAsync(string url, object payload, CancellationToken cancellationToken, Func<CancellationToken, Task>? transportStarting = null)
    {
        // Resolve, validate and serialize before claiming a durable transport attempt.
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(url, UriKind.Absolute))
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json")
        };
        cancellationToken.ThrowIfCancellationRequested();
        if (transportStarting != null) await transportStarting(cancellationToken);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Evolution API Error. Status={StatusCode}", response.StatusCode);
            throw new HttpRequestException($"Error de Evolution: {response.StatusCode}", null, response.StatusCode);
        }

        var jsonResponse = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        if (jsonResponse.ValueKind == JsonValueKind.Object &&
            jsonResponse.TryGetProperty("key", out var keyProp) && keyProp.ValueKind == JsonValueKind.Object &&
            keyProp.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(idProp.GetString()))
            return idProp.GetString()!;

        throw new InvalidOperationException("Evolution returned success without a provider message ID; delivery is unknown.");
    }
}
