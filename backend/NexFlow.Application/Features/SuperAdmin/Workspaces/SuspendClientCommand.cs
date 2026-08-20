using NexFlow.Application.Common;
using NexFlow.Application.Abstractions;
using NexFlow.Domain.Enums;
using NexFlow.Domain.Entities;
using NexFlow.Application.Abstractions.Repositories;

namespace NexFlow.Application.Features.SuperAdmin.Workspaces;

public record SuspendClientCommand(Guid WorkspaceId, string Reason);

public class SuspendClientCommandHandler
{
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly ILicenseRepository _licenseRepository;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;

    public SuspendClientCommandHandler(
        IWorkspaceRepository workspaceRepository,
        ILicenseRepository licenseRepository,
        IAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser)
    {
        _workspaceRepository = workspaceRepository;
        _licenseRepository = licenseRepository;
        _auditLogRepository = auditLogRepository;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(SuspendClientCommand request, CancellationToken cancellationToken)
    {
        var workspace = await _workspaceRepository.GetByIdAsync(request.WorkspaceId, cancellationToken);
        if (workspace == null) return Result.Failure(new Error("Workspace.NotFound", "Workspace no encontrado."));

        var license = await _licenseRepository.GetByWorkspaceIdAsync(request.WorkspaceId, cancellationToken);
        if (license == null) return Result.Failure(new Error("License.NotFound", "Licencia no encontrada."));

        // Se apaga el negocio completo
        workspace.Suspend();
        license.Suspend();

        var audit = AuditLog.Create(
            workspaceId: request.WorkspaceId,
            userId: _currentUser.UserId,
            action: AuditAction.WorkspaceSuspended,
            details: $"Cliente suspendido. Razón: {request.Reason}. Ejecutado por {_currentUser.Email}."
        );
        _auditLogRepository.Add(audit);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}