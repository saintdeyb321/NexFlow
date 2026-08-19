using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using NexFlow.Application.Engines.AI;

namespace NexFlow.Infrastructure.Engines.AI;

public class GeminiAiProvider : IAiProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public GeminiAiProvider(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        // La API Key de Gemini vivirá en tu appsettings.json o variables de entorno
        _apiKey = configuration["Gemini:ApiKey"] ?? throw new ArgumentNullException("Falta la API Key de Gemini");
    }

    public async Task<string> GenerateTextAsync(string systemPrompt, string userMessage, CancellationToken cancellationToken)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={_apiKey}";

        // Estructura JSON exacta que pide Google Gemini
        var payload = new
        {
            system_instruction = new { parts = new[] { new { text = systemPrompt } } },
            contents = new[] { new { parts = new[] { new { text = userMessage } } } }
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(url, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
        using var jsonDocument = JsonDocument.Parse(responseString);

        // Navegar por el JSON de respuesta para extraer solo el texto generado
        var text = jsonDocument.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

        return text ?? string.Empty;
    }
}