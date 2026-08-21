namespace NexFlow.Application.Features.SuperAdmin.ProvisionClient;

public record ProvisionClientCommand(
    string Email,
    string FirstName,
    string LastName,
    string WorkspaceName,
    string? TemplateName, // <-- Cambiado a string para recibir "SECRETARY"
    DateTime ExpiresAt,
    List<string>? CustomModules
);  