using Microsoft.Extensions.Logging;
using System.Text;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Engines.Dispatcher;

namespace NexFlow.Infrastructure.Engines.AI;

public class AiRouter : IAiRouter
{
    private readonly IAiProvider _aiProvider;
    private readonly ILogger<AiRouter> _logger;

    public AiRouter(IAiProvider aiProvider, ILogger<AiRouter> logger)
    {
        _aiProvider = aiProvider;
        _logger = logger;
    }

    public async Task<string> GenerateResponseAsync(Guid workspaceId, ModuleExecutionResult systemContext, ConversationContextDto conversationContext, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Construyendo AI Context Builder. WorkspaceId: {WorkspaceId} | Módulo: {ModuleCode}", workspaceId, systemContext.ModuleCode);

        // 🔥 SPRINT 2.1: Reglas de titanio para evitar alucinaciones
        var systemInstruction = $@"Eres el asistente virtual corporativo de este negocio.
Tu misión es interpretar el contexto de la conversación y el resultado extraído de la base de datos, para redactar una respuesta natural y cálida para WhatsApp.

REGLAS ESTRICTAS (MANDAMIENTOS):
1. RESPONDE ÚNICAMENTE SOBRE EL MÓDULO: '{systemContext.ModuleCode}'.
2. Utiliza SOLAMENTE la información listada bajo 'Datos Extraídos (Verdad Absoluta)'. NUNCA inventes precios, sedes, fechas, servicios o reglas comerciales.
3. Si el contexto indica 'Parámetros Faltantes', tu ÚNICO OBJETIVO en este mensaje es formular una pregunta amable para obtener ese dato específico del usuario (ej. si falta 'locationId', pregunta en qué sede; si falta 'date', pregunta para cuándo).
4. NUNCA menciones palabras técnicas como 'JSON', 'Sistema', 'Módulo', 'Intent', 'Capability' o 'MissingParameters' al cliente.
5. BREVEDAD EXTREMA: Lenguaje directo y dinámico para WhatsApp (máximo 1 o 2 párrafos cortos).";

        // 🔥 SPRINT 2.1: El "Context Builder" Quirúrgico
        var contextBuilder = new StringBuilder();

        contextBuilder.AppendLine("--- CONTEXTO CONVERSACIONAL ---");
        contextBuilder.AppendLine($"Intención Actual: {conversationContext.CurrentIntent ?? "Ninguna"}");
        contextBuilder.AppendLine($"Acción Pendiente: {conversationContext.PendingAction ?? "Ninguna"}");

        if (!string.IsNullOrEmpty(conversationContext.SelectedLocationId))
            contextBuilder.AppendLine($"Sede Seleccionada (ID): {conversationContext.SelectedLocationId}");

        if (!string.IsNullOrEmpty(conversationContext.SelectedServiceId))
            contextBuilder.AppendLine($"Servicio Seleccionado (ID): {conversationContext.SelectedServiceId}");

        contextBuilder.AppendLine("\n--- RESULTADO ESTRUCTURADO DEL SISTEMA ---");
        contextBuilder.AppendLine($"Módulo Ejecutado: {systemContext.ModuleCode}");
        contextBuilder.AppendLine($"Operación: {systemContext.Capability}");
        contextBuilder.AppendLine($"Éxito de la operación: {systemContext.Success}");

        if (systemContext.MissingParameters != null && systemContext.MissingParameters.Any())
        {
            contextBuilder.AppendLine($"Parámetros Faltantes (¡DEBES PREGUNTAR ESTO AL USUARIO!): {string.Join(", ", systemContext.MissingParameters)}");
        }

        contextBuilder.AppendLine("\nDatos Extraídos (Verdad Absoluta):");
        contextBuilder.AppendLine(string.IsNullOrWhiteSpace(systemContext.Data) ? "Ningún dato encontrado." : systemContext.Data);

        var userPrompt = contextBuilder.ToString();

        return await _aiProvider.GenerateTextAsync(systemInstruction, userPrompt, useJsonMode: false, cancellationToken);
    }
}