using System;

namespace NexFlow.Application.Features.SuperAdmin.ProvisionClient;

public record ProvisionClientCommand(
    string Email,
    string FirstName,
    string LastName,
    string WorkspaceName,
    Guid TemplateId,
    DateTime ExpiresAt
);