using System;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NexFlow.Application.Abstractions.Integrations; // <-- Contrato correcto

namespace NexFlow.Infrastructure.Gateways;

public class EvolutionMessageGateway : IMessageGateway
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _apiKey;

    public EvolutionMessageGateway(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _baseUrl = configuration["Evolution:BaseUrl"] ?? "http://localhost:8080";
        _apiKey = configuration["Evolution:ApiKey"] ?? string.Empty;

        _httpClient.DefaultRequestHeaders.Add("apikey", _apiKey);
    }

    // Firma exacta del contrato de Application
    public async Task SendTextAsync(Guid workspaceId, string customerIdentifier, string message, CancellationToken cancellationToken)
    {
        var url = $"{_baseUrl}/message/sendText/{workspaceId}"; // Asumimos que la instancia de Evolution se llama igual que el WorkspaceId

        var payload = new
        {
            number = customerIdentifier, // El número de teléfono
            textMessage = new { text = message },
            options = new { delay = 1200, presence = "composing" }
        };

        var response = await _httpClient.PostAsJsonAsync(url, payload, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}