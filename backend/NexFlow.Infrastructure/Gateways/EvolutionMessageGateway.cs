using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;

namespace NexFlow.Infrastructure.Gateways;

public interface IMessageGateway
{
    Task SendWhatsAppTextAsync(string phone, string message, CancellationToken cancellationToken);
}

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

    public async Task SendWhatsAppTextAsync(string phone, string message, CancellationToken cancellationToken)
    {
        var url = $"{_baseUrl}/message/sendText/default"; // Asumiendo instancia 'default'

        var payload = new
        {
            number = phone,
            textMessage = new { text = message },
            options = new { delay = 1200, presence = "composing" }
        };

        var response = await _httpClient.PostAsJsonAsync(url, payload, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}