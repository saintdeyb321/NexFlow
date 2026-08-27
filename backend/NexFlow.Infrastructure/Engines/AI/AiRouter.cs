using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Engines.Intent.AI;
using NexFlow.Application.Engines.Dispatcher;

namespace NexFlow.Infrastructure.Engines.AI;

public class AiRouter : IAiRouter
{
    private readonly IAiProvider _aiProvider;

    public AiRouter(IAiProvider aiProvider)
    {
        _aiProvider = aiProvider;
    }

    // 🔥 SPRINT 7: Ahora recibimos el ModuleExecutionResult estructurado
    public async Task<string> GenerateResponseAsync(Guid workspaceId, IntentResultDto intent, ModuleExecutionResult systemContext, CancellationToken cancellationToken)
    {
        var systemInstruction = @"
Eres la voz oficial de atención al cliente del negocio por WhatsApp, impulsado por NexFlow.
Tu única misión es comunicar al cliente el 'RESULTADO DEL SISTEMA' (Que viene en formato JSON) de forma natural, cálida y empática.

MANDAMIENTOS INQUEBRANTABLES (CERO ALUCINACIONES):
1. ERES UN TRADUCTOR, NO UNA ENCICLOPEDIA: Tu respuesta debe basarse 100% y EXCLUSIVAMENTE en el nodo 'Data' del JSON proporcionado.
2. PROHIBIDO INVENTAR DATOS: Bajo ninguna circunstancia puedes adivinar precios, sedes, horarios, duraciones o servicios. Si el JSON no menciona un dato, TÚ TAMPOCO LO HACES.
3. INSTRUCCIONES DIRECTAS: Si el JSON contiene 'MissingParameters' (Ej: locationId o date), DEBES preguntarle al cliente específicamente por esos datos faltantes.
4. LENGUAJE NATURAL: NUNCA menciones palabras técnicas como 'el sistema', 'JSON', 'MissingParameters' o 'el dispatcher'. Háblale al cliente como si fueras un empleado humano de recepción. NUNCA digas que eres una IA.
5. HANDOFF: Si el JSON indica 'RequiresHuman: true', despídete y dile que un humano tomará el control del chat en breve.";

        // Convertimos el DTO en un JSON legible para que Gemini lo entienda a la perfección
        var jsonContext = JsonSerializer.Serialize(systemContext, new JsonSerializerOptions { WriteIndented = true });

        var userPrompt = $"RESULTADO DEL SISTEMA (Formato JSON Estructurado. Tu única fuente de verdad):\n{jsonContext}";

        return await _aiProvider.GenerateTextAsync(systemInstruction, userPrompt, useJsonMode: false, cancellationToken);
    }
}