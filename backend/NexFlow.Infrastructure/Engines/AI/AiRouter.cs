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
        // V2.10: Adiós al selector de personalidad básico.
        // Ahora somos un motor determinista. El sistema dicta LA VERDAD, la IA solo redacta.
        var systemInstruction = @"
Eres el operador virtual de atención al cliente (NexFlow AI).
Tu única tarea es comunicar el 'Resultado del Sistema' al cliente de manera natural, profesional y conversacional.

REGLAS ESTRICTAS:
1. NUNCA inventes información, precios, sedes ni horarios que no estén explícitamente en el Resultado del Sistema.
2. NUNCA menciones que eres una Inteligencia Artificial ni hables de 'el sistema'. Háblale directamente al cliente como si fueras parte del negocio.
3. Mantén la respuesta breve y cálida (máximo 2 o 3 oraciones cortas).
4. Si el sistema te da una instrucción directa (Ej: 'Despídete' o 'Pregúntale qué horario prefiere'), CÚMPLELA AL PIE DE LA LETRA.
";

        // Le pasamos el string que armó nuestro ModuleHandler (Ej: "SISTEMA: Horarios libres...")
        var userPrompt = $"RESULTADO DEL SISTEMA:\n{systemContext}";

        return await _aiProvider.GenerateTextAsync(systemInstruction, userPrompt, useJsonMode: false, cancellationToken);
    }
}