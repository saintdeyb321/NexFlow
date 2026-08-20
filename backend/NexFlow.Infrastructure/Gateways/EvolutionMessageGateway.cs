using System;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions.Integrations;

namespace NexFlow.Infrastructure.Gateways;

public class EvolutionMessageGateway : IMessageGateway
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly ILogger<EvolutionMessageGateway> _logger;

    public EvolutionMessageGateway(HttpClient httpClient, IConfiguration configuration, ILogger<EvolutionMessageGateway> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        _baseUrl = configuration["Evolution:BaseUrl"] ?? throw new ArgumentNullException("Evolution BaseUrl no configurada");
        _apiKey = configuration["Evolution:ApiKey"] ?? string.Empty;

        // Limitar a que falle rápido si Evolution está apagado o colgado (Fail-Fast)
        var timeout = int.TryParse(configuration["Evolution:TimeoutSeconds"], out var t) ? t : 10;
        _httpClient.Timeout = TimeSpan.FromSeconds(timeout);

        if (!string.IsNullOrEmpty(_apiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("apikey", _apiKey);
        }
    }

    public async Task SendTextAsync(Guid workspaceId, string customerIdentifier, string message, CancellationToken cancellationToken)
    {
        var url = $"{_baseUrl}/message/sendText/{workspaceId}";

        var payload = new
        {
            number = customerIdentifier,
            textMessage = new { text = message },
            options = new { delay = 1200, presence = "composing" }
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync(url, payload, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (TaskCanceledException)
        {
            _logger.LogError("Timeout: Evolution API no respondió en el tiempo esperado para enviar un mensaje a {Customer}", customerIdentifier);
            throw; // Propagamos para que se marque como fallo
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo crítico al enviar mensaje vía Evolution. Workspace: {WorkspaceId}, Cliente: {Customer}", workspaceId, customerIdentifier);
            throw;
        }
    }
}