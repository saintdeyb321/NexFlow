namespace NexFlow.Application.Features.SuperAdmin.ProvisionClient;

public record ProvisionClientCommand(
    string Email,
    string FirstName,
    string LastName,
    string WorkspaceName,
    string? TemplateCode, 
    DateTime ExpiresAt,
    List<string>? CustomModules
);  