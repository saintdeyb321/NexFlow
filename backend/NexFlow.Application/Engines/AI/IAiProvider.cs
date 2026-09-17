using System.Text.Json.Nodes;

namespace NexFlow.Application.Engines.AI;

// Modelos para soportar Historial y Tool Calling
public record AiMessage(string Role, string Text);
public record AiTool(string Name, string Description, JsonObject ParametersSchema);
public record AiToolCall(string Name, JsonObject Arguments);
public record AiResponse(string? Text, AiToolCall? ToolCall);

public interface IAiProvider
{
    // Método legacy para cosas simples (mantener compatibilidad por ahora)
    Task<string> GenerateTextAsync(string systemPrompt, string userMessage, bool useJsonMode = false, CancellationToken cancellationToken = default);

    // 🔥 NUEVO: Método Enterprise para NexFlow 2.0 (Agentes)
    Task<AiResponse> GenerateChatResponseAsync(
        string systemPrompt,
        List<AiMessage> history,
        List<AiTool>? tools = null,
        CancellationToken cancellationToken = default);
}