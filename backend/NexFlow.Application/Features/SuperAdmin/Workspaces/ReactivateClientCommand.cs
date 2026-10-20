using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Common;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NexFlow.Application.Features.SuperAdmin.Workspaces;

public record ReactivateClientCommand(Guid WorkspaceId);

public class ReactivateClientCommandHandler
{
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILicenseRepository _licenseRepository;
    private readonly IEntitlementService _entitlements;

    public ReactivateClientCommandHandler(IWorkspaceRepository workspaceRepository, IUnitOfWork unitOfWork,
        ILicenseRepository licenseRepository, IEntitlementService entitlements)
    {
        _workspaceRepository = workspaceRepository;
        _unitOfWork = unitOfWork;
        _licenseRepository = licenseRepository;
        _entitlements = entitlements;
    }

    public async Task<Result> Handle(ReactivateClientCommand request, CancellationToken cancellationToken)
    {
        // 🛡️ CORRECCIÓN BLINDADA: Usamos la ruta exclusiva del SuperAdmin
        var workspace = await _workspaceRepository.GetByIdForSuperAdminAsync(request.WorkspaceId, cancellationToken);

        if (workspace == null) return Result.Failure(new Error("Workspace.NotFound", "El negocio no existe."));

        // Cambiamos el estado
        workspace.Activate();
        var license = await _licenseRepository.GetByWorkspaceIdAsync(request.WorkspaceId, cancellationToken);
        license?.Resume();

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _entitlements.InvalidateWorkspaceCache(request.WorkspaceId);
        return Result.Success();
    }
}
