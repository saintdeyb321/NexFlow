namespace NexFlow.Application.Features.SuperAdmin.ProvisionClient;

public record ProvisionClientCommand(
    string Email,
    string FirstName,
    string LastName,
    string WorkspaceName,
    Guid? TemplateId, // ¡Ahora es anulable (opcional)!
    DateTime ExpiresAt,
    List<string>? CustomModules // Módulos a la carta (Ej: ["FAQ", "SERVICES"])
);