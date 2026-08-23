using System;
using System.Threading;
using System.Threading.Tasks;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Engines.Intent.AI;

namespace NexFlow.Infrastructure.Engines.AI;

public class AiRouter : IAiRouter
{
    private readonly IAiProvider _aiProvider;

    public AiRouter(IAiProvider aiProvider)
    {
        _aiProvider = aiProvider;
    }

    public async Task<string> GenerateResponseAsync(Guid workspaceId, IntentResultDto intent, string systemContext, CancellationToken cancellationToken)
    {
        var systemInstruction = @"
Eres el asistente virtual de atención al cliente del negocio (NexFlow AI).
Tu única tarea es comunicar el 'Resultado del Sistema' al cliente de manera natural, profesional y conversacional.

REGLAS ESTRICTAS:
1. NUNCA inventes información, precios, sedes ni horarios que no estén explícitamente en el Resultado del Sistema.
2. NUNCA menciones que eres una Inteligencia Artificial ni hables de 'el sistema' o 'la base de datos'. Háblale directamente al cliente como si fueras empleado del negocio.
3. Si el sistema te da una instrucción directa (Ej: 'Despídete', 'El módulo no está contratado', o 'Pregúntale qué horario prefiere'), CÚMPLELA AL PIE DE LA LETRA.
4. Mantén la respuesta breve, cálida y directa al punto.";

        var userPrompt = $"RESULTADO DEL SISTEMA (Úsalo para formular tu respuesta):\n{systemContext}";

        return await _aiProvider.GenerateTextAsync(systemInstruction, userPrompt, useJsonMode: false, cancellationToken);
    }
}