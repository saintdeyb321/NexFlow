using NexFlow.Application.Common;
using NexFlow.Application.Abstractions;
using NexFlow.Domain.Enums;
using NexFlow.Domain.Entities;

namespace NexFlow.Application.Features.SuperAdmin.Licenses;

public record RenewLicenseCommand(Guid WorkspaceId, DateTime NewStartDate, int DurationInMonths);

public class RenewLicenseCommandHandler
{
    private readonly ILicenseRepository _licenseRepository;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;

    public RenewLicenseCommandHandler(
        ILicenseRepository licenseRepository,
        IAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork,
        IClock clock,
        ICurrentUser currentUser)
    {
        _licenseRepository = licenseRepository;
        _auditLogRepository = auditLogRepository;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(RenewLicenseCommand request, CancellationToken cancellationToken)
    {
        var license = await _licenseRepository.GetByWorkspaceIdAsync(request.WorkspaceId, cancellationToken);
        if (license == null)
            return Result.Failure(new Error("License.NotFound", "El workspace no tiene una licencia asignada."));

        var now = _clock.UtcNow;
        var endDate = request.NewStartDate.AddMonths(request.DurationInMonths);

        // Aquí usamos la regla de negocio pura de tu Dominio
        license.Renew(request.NewStartDate, endDate, now);

        var audit = AuditLog.Create(
            workspaceId: request.WorkspaceId,
            userId: _currentUser.UserId,
            action: AuditAction.LicenseRenewed,
            details: $"Licencia renovada hasta {endDate:yyyy-MM-dd} por {_currentUser.Email}."
        );
        _auditLogRepository.Add(audit);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}