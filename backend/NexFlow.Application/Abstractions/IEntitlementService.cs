namespace NexFlow.Application.Abstractions;

public interface IEntitlementService
{
    // ¿El workspace actual tiene una licencia válida y contiene este módulo?
    Task<bool> HasModuleAccessAsync(Guid workspaceId, string moduleCode, CancellationToken cancellationToken);

    // Devuelve todos los códigos de módulos a los que el workspace tiene acceso (para el frontend)
    Task<IEnumerable<string>> GetAvailableModulesAsync(Guid workspaceId, CancellationToken cancellationToken);

    // ¿La licencia en general es válida hoy?
    Task<bool> IsLicenseValidAsync(Guid workspaceId, CancellationToken cancellationToken);
}