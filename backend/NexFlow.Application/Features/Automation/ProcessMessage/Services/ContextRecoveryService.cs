using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services;

public interface IContextRecoveryService
{
    Task<ConversationContextDto> GetOrRecoverContextAsync(Guid workspaceId, string phone, CancellationToken ct);
}

public class ContextRecoveryService : IContextRecoveryService
{
    private readonly IConversationCache _cache;
    private readonly IConversationRepository _conversationRepo;

    public ContextRecoveryService(IConversationCache cache, IConversationRepository conversationRepo)
    {
        _cache = cache;
        _conversationRepo = conversationRepo;
    }

    public async Task<ConversationContextDto> GetOrRecoverContextAsync(Guid workspaceId, string phone, CancellationToken ct)
    {
        // 1. Intentamos leer de Redis (Ruta feliz y ultra-rápida)
        var context = await _cache.GetContextAsync(workspaceId, phone, ct);
        if (context != null) return context;

        // 2. Si Redis falló o expulsó la clave, iniciamos la RECUPERACIÓN (Context Builder)
        context = new ConversationContextDto();
        var conversation = await _conversationRepo.GetActiveConversationAsync(workspaceId, phone, ct);

        if (conversation != null)
        {
            // A. Recuperar el Modo de Atención (SSOT)
            context.Mode = conversation.Mode.ToString();
            context.HandoffReason = conversation.HandoffReason.ToString();
            context.HandoffAt = conversation.Mode == ConversationMode.Human ? conversation.LastMessageAt : null;

            // B. Recuperar el estado del flujo leyendo los últimos mensajes de la IA
            var recentMessages = await _conversationRepo.GetMessagesAsync(workspaceId, conversation.Id, 10, ct);
            var lastAiMessage = recentMessages
                .Where(m => m.Sender == SenderType.AI)
                .OrderByDescending(m => m.Timestamp)
                .FirstOrDefault();

            if (lastAiMessage != null)
            {
                var text = lastAiMessage.Content.ToLowerInvariant();

                // Máquina de estados inversa basada en los textos del BookingFlow
                if (text.Contains("nombre y apellido"))
                {
                    context.CurrentGoal = "RESERVATION"; context.CurrentStep = "COLLECT_CUSTOMERNAME";
                }
                else if (text.Contains("qué servicio deseas") || text.Contains("que servicio deseas"))
                {
                    context.CurrentGoal = "RESERVATION"; context.CurrentStep = "COLLECT_SERVICE";
                }
                else if (text.Contains("cuál de nuestras sedes") || text.Contains("en cuál de nuestras sedes"))
                {
                    context.CurrentGoal = "RESERVATION"; context.CurrentStep = "COLLECT_LOCATION";
                }
                else if (text.Contains("para qué fecha") || text.Contains("para que fecha"))
                {
                    context.CurrentGoal = "RESERVATION"; context.CurrentStep = "COLLECT_DATE";
                }
                else if (text.Contains("a qué hora prefieres") || text.Contains("a que hora prefieres"))
                {
                    context.CurrentGoal = "RESERVATION"; context.CurrentStep = "COLLECT_TIME";
                }
            }
        }

        // 3. Volvemos a guardar el contexto reconstruido en Redis para no tener que deducirlo en el próximo mensaje
        await _cache.SetContextAsync(workspaceId, phone, context, ct);

        return context;
    }
}
