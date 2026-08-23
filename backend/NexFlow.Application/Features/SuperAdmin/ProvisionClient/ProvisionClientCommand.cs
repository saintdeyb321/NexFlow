namespace NexFlow.Application.Features.SuperAdmin.ProvisionClient;

public record ProvisionClientCommand(
    string Email,
    string FirstName,
    string LastName,
    string WorkspaceName,
    string? TemplateCode,
    DateTime ExpiresAt,
    List<string>? CustomModules,
    int MaxLocations = 1 // Candado puesto: Por defecto, 1 sola sede.
);