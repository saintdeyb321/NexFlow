using NexFlow.Application.Abstractions;
using NexFlow.Application.Common;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Enums;
using NexFlow.Domain.ValueObjects;

namespace NexFlow.Application.Features.SuperAdmin.ProvisionClient;

public class ProvisionClientCommandHandler
{
    private readonly IUserRepository _userRepository;
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly IMembershipRepository _membershipRepository;
    private readonly ILicenseRepository _licenseRepository;
    private readonly ITemplateRepository _templateRepository;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;

    public ProvisionClientCommandHandler(
        IUserRepository userRepository,
        IWorkspaceRepository workspaceRepository,
        IMembershipRepository membershipRepository,
        ILicenseRepository licenseRepository,
        ITemplateRepository templateRepository,
        IAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork,
        IClock clock,
        ICurrentUser currentUser)
    {
        _userRepository = userRepository;
        _workspaceRepository = workspaceRepository;
        _membershipRepository = membershipRepository;
        _licenseRepository = licenseRepository;
        _templateRepository = templateRepository;
        _auditLogRepository = auditLogRepository;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(ProvisionClientCommand request, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        if (request.ExpiresAt <= now)
        {
            return Result<Guid>.Failure(new Error("License.InvalidExpiration", "La fecha de expiración debe ser en el futuro."));
        }

        if (!_currentUser.IsAuthenticated)
        {
            return Result<Guid>.Failure(new Error("Auth.Unauthorized", "Operación no autorizada."));
        }

        var email = new Email(request.Email);
        var existingUser = await _userRepository.GetByEmailAsync(email, cancellationToken);
        if (existingUser != null)
        {
            return Result<Guid>.Failure(new Error("User.Exists", "El correo ya está registrado en el sistema."));
        }

        var template = await _templateRepository.GetByIdAsync(request.TemplateId, cancellationToken);
        if (template == null)
        {
            return Result<Guid>.Failure(new Error("Template.NotFound", "La plantilla especificada no existe."));
        }
        if (template.Status != TemplateStatus.Active)
        {
            return Result<Guid>.Failure(new Error("Template.Inactive", "La plantilla está inactiva y no puede ser comercializada."));
        }

        var activeModules = await _templateRepository.GetActiveModulesForTemplateAsync(template.Id, cancellationToken);
        if (!activeModules.Any())
        {
            return Result<Guid>.Failure(new Error("Template.NoModules", "La plantilla no contiene módulos activos para asignar."));
        }

        var user = User.Create(email, request.FirstName, request.LastName);
        _userRepository.Add(user);

        var workspace = Workspace.Create(request.WorkspaceName);
        _workspaceRepository.Add(workspace);

        var membership = Membership.Create(user.Id, workspace.Id, MembershipRole.Owner);
        _membershipRepository.Add(membership);

        var license = License.CreateTemplateLicense(workspace.Id, template.Id, now, request.ExpiresAt, now);

        foreach (var module in activeModules)
        {
            license.AddModule(module.Id);
        }
        _licenseRepository.Add(license);

        var audit = AuditLog.Create(
            workspaceId: workspace.Id,
            userId: _currentUser.UserId,
            action: AuditAction.WorkspaceCreated,
            details: $"Workspace '{workspace.Name}' creado con plantilla '{template.Name}' hasta {request.ExpiresAt:yyyy-MM-dd} por SuperAdmin."
        );
        _auditLogRepository.Add(audit);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Guid>.Success(workspace.Id);
    }
}