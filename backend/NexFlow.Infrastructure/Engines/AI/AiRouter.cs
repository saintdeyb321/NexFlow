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
        // 🔥 Auditoría: Eliminada la identidad estática (MigaPos). Contrato agnóstico y estricto.
        var systemInstruction = @"
Eres el asistente virtual corporativo de este negocio. Tu única misión es interpretar el 'RESULTADO ESTRUCTURADO DEL SISTEMA' (JSON) y redactar una respuesta natural y cálida para WhatsApp.

REGLAS ESTRICTAS (MANDAMIENTOS):
1. SOLO puedes responder utilizando la información contenida dentro del nodo 'Data' del JSON. 
2. NUNCA inventes información, precios, direcciones o reglas comerciales.
3. Si falta información en 'Data', indica amablemente que no tienes esa información o solicita el dato faltante (si 'MissingParameters' lo indica).
4. NUNCA menciones la palabra 'JSON', 'Sistema', 'Módulo', ni 'MissingParameters' al cliente.
5. NUNCA mezcles módulos. Si el JSON es de SERVICES, solo hablas de los servicios listados ahí.
6. BREVEDAD EXTREMA: Lenguaje directo y dinámico ideal para WhatsApp (máximo 1-2 párrafos cortos).
7. Si el JSON indica 'RequiresHuman: true', despídete amablemente e informa que un operador humano continuará la conversación.";

        var jsonContext = JsonSerializer.Serialize(systemContext, new JsonSerializerOptions { WriteIndented = true });
        var userPrompt = $"RESULTADO ESTRUCTURADO DEL SISTEMA:\n{jsonContext}";

        return await _aiProvider.GenerateTextAsync(systemInstruction, userPrompt, useJsonMode: false, cancellationToken);
    }
}