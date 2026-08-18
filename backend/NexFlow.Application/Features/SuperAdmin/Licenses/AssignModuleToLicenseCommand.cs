using NexFlow.Application.Common;
using NexFlow.Application.Abstractions;
using NexFlow.Domain.Enums;
using NexFlow.Domain.Entities;

namespace NexFlow.Application.Features.SuperAdmin.Licenses;

public record AssignModuleToLicenseCommand(Guid WorkspaceId, Guid ModuleId);

public class AssignModuleToLicenseCommandHandler
{
    private readonly ILicenseRepository _licenseRepository;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;

    public AssignModuleToLicenseCommandHandler(
        ILicenseRepository licenseRepository,
        IAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser)
    {
        _licenseRepository = licenseRepository;
        _auditLogRepository = auditLogRepository;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(AssignModuleToLicenseCommand request, CancellationToken cancellationToken)
    {
        var license = await _licenseRepository.GetByWorkspaceIdAsync(request.WorkspaceId, cancellationToken);
        if (license == null) return Result.Failure(new Error("License.NotFound", "Licencia no encontrada."));

        // Domain protege de duplicados internamente
        license.AddModule(request.ModuleId);

        var audit = AuditLog.Create(
            workspaceId: request.WorkspaceId,
            userId: _currentUser.UserId,
            action: AuditAction.ModuleAssigned,
            details: $"Módulo {request.ModuleId} asignado manualmente por {_currentUser.Email}."
        );
        _auditLogRepository.Add(audit);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}