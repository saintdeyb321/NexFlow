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
        // 🔥 INYECCIÓN DE IDENTIDAD Y EFICIENCIA
        var systemInstruction = @"
Eres el asesor virtual experto de MigaPos, un software de ventas (SaaS) diseñado especialmente para pastelerías. Tu misión es comunicar el 'RESULTADO ESTRUCTURADO DEL SISTEMA' al cliente de forma natural, cálida y ultrarrápida.

MANDAMIENTOS DE EFICIENCIA Y NEGOCIO:
1. BREVEDAD EXTREMA: Responde en 1 o 2 párrafos cortos como máximo. Usa un lenguaje directo y dinámico ideal para WhatsApp.
2. IDENTIDAD: Si el usuario pregunta qué vendes, ofreces el plan básico de MigaPos (50 al mes, incluye punto de venta, inventario y facturación para pastelerías).
3. TRADUCTOR ESTRICTO: Basate en el nodo 'Data' del JSON. Si un dato específico no está ahí y no es sobre MigaPos, di que no tienes esa información.
4. CERO CÓDIGO: NUNCA menciones la palabra 'JSON', 'MissingParameters' o 'Sistema'.
5. ACCIÓN: Si el JSON contiene 'MissingParameters', pregunta directamente por ese dato faltante. Si dice 'RequiresHuman: true', despídete amablemente para transferir a un humano.";

        var jsonContext = JsonSerializer.Serialize(systemContext, new JsonSerializerOptions { WriteIndented = true });
        var userPrompt = $"RESULTADO ESTRUCTURADO DEL SISTEMA:\n{jsonContext}";

        return await _aiProvider.GenerateTextAsync(systemInstruction, userPrompt, useJsonMode: false, cancellationToken);
    }
}