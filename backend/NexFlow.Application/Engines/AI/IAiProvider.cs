using System.Text.Json.Nodes;

namespace NexFlow.Application.Engines.AI;

// 🔥 SPRINT 10: Enum estricto para identificación de proveedores
public enum AiProviderType
{
    Groq,
    Gemini,
    OpenAI,
    DeepSeek // Preparado para el futuro
}

public record AiMessage(string Role, string Text);
public record AiTool(string Name, string Description, JsonObject ParametersSchema);
public record AiToolCall(string Name, JsonObject Arguments);
public record AiResponse(string? Text, AiToolCall? ToolCall);

public interface IAiProvider
{
    // 🔥 SPRINT 10: Cada proveedor DEBE declarar quién es
    AiProviderType ProviderType { get; }

    Task<string> GenerateTextAsync(string systemPrompt, string userMessage, bool useJsonMode = false, CancellationToken cancellationToken = default);

    Task<AiResponse> GenerateChatResponseAsync(
        string systemPrompt,
        List<AiMessage> history,
        List<AiTool>? tools = null,
        CancellationToken cancellationToken = default);
}