namespace NexFlow.Application.Features.Automation.ProcessMessage;

public record ProcessIncomingMessageCommand(
    string InstanceName,
    string CustomerPhone,
    string CustomerName,
    string MessageText,
    string MessageId
);