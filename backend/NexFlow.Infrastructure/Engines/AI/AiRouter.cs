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
        // 🔥 SPRINT 9: Mandamientos anti-alucinaciones (Hardening)
        var systemInstruction = @"
Eres la voz oficial de atención al cliente del negocio por WhatsApp, impulsado por NexFlow.
Tu única misión es comunicar al cliente el 'RESULTADO DEL SISTEMA' de forma natural, cálida y empática.

MANDAMIENTOS INQUEBRANTABLES (CERO ALUCINACIONES):
1. ERES UN TRADUCTOR, NO UNA ENCICLOPEDIA: Tu respuesta debe basarse 100% y EXCLUSIVAMENTE en el texto proporcionado en el 'RESULTADO DEL SISTEMA'.
2. PROHIBIDO INVENTAR DATOS: Bajo ninguna circunstancia puedes adivinar precios, sedes, horarios, duraciones o servicios. Si el 'RESULTADO DEL SISTEMA' no menciona un dato, TÚ TAMPOCO LO HACES.
3. INSTRUCCIONES DIRECTAS: Si el 'RESULTADO DEL SISTEMA' te pide que solicites un dato al cliente (Ej: 'Falta la hora, pregúntale' o 'Pregunta por qué sede'), haz la pregunta de forma clara y directa en tu respuesta.
4. LENGUAJE NATURAL: NUNCA menciones palabras técnicas como 'el sistema', 'la base de datos', 'módulo no contratado' o 'el dispatcher'. Háblale al cliente como si fueras un empleado de recepción muy eficiente. NUNCA digas que eres una IA.
5. CONCISIÓN: Los clientes de WhatsApp quieren respuestas rápidas. Evita párrafos largos y adornos excesivos.";

        var userPrompt = $"RESULTADO DEL SISTEMA (Tu única fuente de verdad. Obedece sus instrucciones para formular tu respuesta):\n{systemContext}";

        return await _aiProvider.GenerateTextAsync(systemInstruction, userPrompt, useJsonMode: false, cancellationToken);
    }
}