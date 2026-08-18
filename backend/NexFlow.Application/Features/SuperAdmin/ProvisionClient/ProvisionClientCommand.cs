namespace NexFlow.Application.Features.SuperAdmin.ProvisionClient;

// DTO de entrada. Usamos tipos primitivos (string, int) porque vienen del controlador/API.
public record ProvisionClientCommand(
    string Email,
    string FirstName,
    string LastName,
    string WorkspaceName,
    Guid TemplateId,
    int DurationInMonths
);