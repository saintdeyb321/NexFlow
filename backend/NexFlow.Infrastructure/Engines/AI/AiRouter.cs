using System.Text.Json;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Engines.Dispatcher;

namespace NexFlow.Infrastructure.Engines.AI;

public class AiRouter : IAiRouter
{
    private readonly IAiProvider _aiProvider;

    public AiRouter(IAiProvider aiProvider)
    {
        _aiProvider = aiProvider;
    }

    public async Task<string> GenerateResponseAsync(Guid workspaceId, ModuleExecutionResult systemContext, CancellationToken cancellationToken)
    {
        // 🔥 SPRINT 3.3: Adaptación a JSON estructurado y MissingParameters
        var systemInstruction = @"
Eres la voz de atención al cliente de un negocio por WhatsApp. Tu misión es comunicar el 'RESULTADO ESTRUCTURADO DEL SISTEMA' de forma natural y cálida.

MANDAMIENTOS:
1. TRADUCTOR ESTRICTO: Basate 100% en el nodo 'Data'.
2. CERO ALUCINACIONES: Si un dato (precio, horario, sede) no está en el JSON, dile al cliente que no tienes esa información. No inventes.
3. EXTRACCIÓN DE DATOS: Si el JSON contiene 'MissingParameters', DEBES preguntarle al cliente por ese dato específico (ej. ¿En qué sede deseas la reserva?).
4. LENGUAJE HUMANO: NUNCA menciones la palabra 'JSON', 'MissingParameters' o 'Sistema'.
5. HANDOFF: Si el JSON dice 'RequiresHuman: true', despídete y dile que un asesor continuará la charla.";

        var jsonContext = JsonSerializer.Serialize(systemContext, new JsonSerializerOptions { WriteIndented = true });
        var userPrompt = $"RESULTADO ESTRUCTURADO DEL SISTEMA:\n{jsonContext}";

        return await _aiProvider.GenerateTextAsync(systemInstruction, userPrompt, useJsonMode: false, cancellationToken);
    }
}