using NexFlow.Application.Common;
using NexFlow.Application.Abstractions;
using NexFlow.Domain.Enums;
using NexFlow.Domain.Entities;
using NexFlow.Application.Abstractions.Repositories;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NexFlow.Application.Features.SuperAdmin.Licenses;

// 🔥 SPRINT 1: Contrato unificado basado en WorkspaceId
public record AssignModuleToLicenseCommand(Guid WorkspaceId, Guid ModuleId);

public class AssignModuleToLicenseCommandHandler
{
    private readonly ILicenseRepository _licenseRepository;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IEntitlementService _entitlementService;

    public AssignModuleToLicenseCommandHandler(
        ILicenseRepository licenseRepository,
        IAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IEntitlementService entitlementService)
    {
        _licenseRepository = licenseRepository;
        _auditLogRepository = auditLogRepository;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _entitlementService = entitlementService;
    }

    public async Task<Result> Handle(AssignModuleToLicenseCommand request, CancellationToken cancellationToken)
    {
        var license = await _licenseRepository.GetByWorkspaceIdAsync(request.WorkspaceId, cancellationToken);
        if (license == null) return Result.Failure(new Error("License.NotFound", "Licencia no encontrada."));

        // Asignación inteligente respetando las reglas de Dominio
        if (license.Type == LicenseType.Template)
        {
            license.AddTemplateModule(request.ModuleId);
        }
        else
        {
            license.AddCustomModule(request.ModuleId);
        }

        var audit = AuditLog.Create(
            workspaceId: request.WorkspaceId,
            userId: _currentUser.UserId,
            action: AuditAction.ModuleAssigned,
            details: $"Módulo {request.ModuleId} asignado manualmente por {_currentUser.Email}."
        );
        _auditLogRepository.Add(audit);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _entitlementService.InvalidateWorkspaceCache(request.WorkspaceId);

        return Result.Success();
    }
}