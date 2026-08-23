using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Common;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NexFlow.Application.Features.SuperAdmin.Licenses;

public record CreateCustomLicenseCommand(Guid WorkspaceId, int DurationInMonths, List<Guid> ModuleIds);

public class CreateCustomLicenseCommandHandler
{
    private readonly ILicenseRepository _licenseRepository;
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;

    public CreateCustomLicenseCommandHandler(
        ILicenseRepository licenseRepository,
        IWorkspaceRepository workspaceRepository,
        IAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork,
        IClock clock,
        ICurrentUser currentUser)
    {
        _licenseRepository = licenseRepository;
        _workspaceRepository = workspaceRepository;
        _auditLogRepository = auditLogRepository;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(CreateCustomLicenseCommand request, CancellationToken cancellationToken)
    {
        var workspace = await _workspaceRepository.GetByIdAsync(request.WorkspaceId, cancellationToken);
        if (workspace == null) return Result.Failure(new Error("Workspace.NotFound", "Cliente no encontrado."));

        var existingLicense = await _licenseRepository.GetByWorkspaceIdAsync(request.WorkspaceId, cancellationToken);
        if (existingLicense != null) return Result.Failure(new Error("License.Exists", "El cliente ya tiene una licencia activa."));

        var now = _clock.UtcNow;
        var expiration = now.AddMonths(request.DurationInMonths);

        var customLicense = License.CreateCustomLicense(request.WorkspaceId, now, expiration);

        foreach (var moduleId in request.ModuleIds)
        {
            // 🐛 BUG SOLUCIONADO: Ahora llama correctamente al método de licencias a la carta
            customLicense.AddCustomModule(moduleId);
        }

        _licenseRepository.Add(customLicense);

        var audit = AuditLog.Create(
            workspaceId: request.WorkspaceId,
            userId: _currentUser.UserId,
            action: AuditAction.LicenseCreated,
            details: $"Licencia Custom ({request.DurationInMonths} meses) creada por {_currentUser.Email} con {request.ModuleIds.Count} módulos."
        );
        _auditLogRepository.Add(audit);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}