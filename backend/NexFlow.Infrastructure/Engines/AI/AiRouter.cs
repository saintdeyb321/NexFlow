using Microsoft.Extensions.Logging;
using System.Text.Json;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Engines.Dispatcher;

namespace NexFlow.Infrastructure.Engines.AI;

public class AiRouter : IAiRouter
{
    private readonly IAiProvider _aiProvider;
    private readonly ILogger<AiRouter> _logger; // 🔥 Auditoría (Sprint 4.2): Logger inyectado

    public AiRouter(IAiProvider aiProvider, ILogger<AiRouter> logger)
    {
        _aiProvider = aiProvider;
        _logger = logger;
    }

    public async Task<string> GenerateResponseAsync(Guid workspaceId, ModuleExecutionResult systemContext, CancellationToken cancellationToken)
    {
        // 🔥 Auditoría (Sprint 4.2): Trazabilidad utilizando el WorkspaceId
        _logger.LogInformation("Enrutando solicitud a IA. WorkspaceId: {WorkspaceId} | Módulo: {ModuleCode} | Capacidad: {Capability}",
            workspaceId, systemContext.ModuleCode, systemContext.Capability);

        var systemInstruction = $@"
Eres el asistente virtual corporativo de este negocio. Tu única misión es interpretar el 'RESULTADO ESTRUCTURADO DEL SISTEMA' (JSON) y redactar una respuesta natural y cálida para WhatsApp.

REGLAS ESTRICTAS (MANDAMIENTOS):
1. ESTÁS AUTORIZADO ÚNICAMENTE A RESPONDER SOBRE EL MÓDULO: '{systemContext.ModuleCode}'. Si el resultado contiene datos fuera de este ámbito o el usuario pregunta por otra cosa, recházalo amablemente.
2. SOLO puedes responder utilizando la información contenida dentro del nodo 'Data' del JSON. 
3. NUNCA inventes información, precios, direcciones o reglas comerciales.
4. Si falta información en 'Data', indica amablemente que no tienes esa información o solicita el dato faltante.
5. NUNCA menciones la palabra 'JSON', 'Sistema', 'Módulo', ni 'MissingParameters' al cliente.
6. BREVEDAD EXTREMA: Lenguaje directo y dinámico ideal para WhatsApp (máximo 1-2 párrafos cortos).
7. Si el JSON indica 'RequiresHuman: true', despídete amablemente e informa que un operador humano continuará la conversación.";

        var jsonContext = JsonSerializer.Serialize(systemContext, new JsonSerializerOptions { WriteIndented = true });
        var userPrompt = $"RESULTADO ESTRUCTURADO DEL SISTEMA:\n{jsonContext}";

        return await _aiProvider.GenerateTextAsync(systemInstruction, userPrompt, useJsonMode: false, cancellationToken);
    }
}