namespace NexFlow.Application.Features.Automation.ProcessMessage;

// Este comando representa un mensaje entrante (ej. de WhatsApp)
public record ProcessIncomingMessageCommand(
    string CustomerPhone,
    string CustomerName,
    string MessageText
);