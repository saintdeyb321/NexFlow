using NexFlow.Application.Common;

namespace NexFlow.Application.Features.Automation.ProcessMessage;

// Esto es lo que recibe la API desde el webhook de WhatsApp
public record ProcessIncomingMessageCommand(
    Guid WorkspaceId,
    string CustomerIdentifier, // El número de WhatsApp
    string Message
);