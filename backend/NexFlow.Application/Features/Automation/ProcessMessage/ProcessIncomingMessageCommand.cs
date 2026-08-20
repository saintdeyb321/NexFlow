using System;

namespace NexFlow.Application.Features.Automation.ProcessMessage;

public record ProcessIncomingMessageCommand(
    Guid WorkspaceId,
    string CustomerPhone,
    string CustomerName,
    string MessageText
);