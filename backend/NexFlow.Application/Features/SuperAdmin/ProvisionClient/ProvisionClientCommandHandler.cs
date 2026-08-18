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
    private readonly ICurrentUser _currentUser; // El SuperAdmin ejecutando esto

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
        // 1. Validar Email
        var email = new Email(request.Email);
        var existingUser = await _userRepository.GetByEmailAsync(email, cancellationToken);
        if (existingUser != null)
        {
            return Result<Guid>.Failure(new Error("User.Exists", "El correo ya está registrado en el sistema."));
        }

        // 2. Obtener Plantilla y sus módulos
        var template = await _templateRepository.GetByIdAsync(request.TemplateId, cancellationToken);
        if (template == null)
        {
            return Result<Guid>.Failure(new Error("Template.NotFound", "La plantilla especificada no existe."));
        }

        var templateModules = await _templateRepository.GetTemplateModulesAsync(template.Id, cancellationToken);

        // 3. Orquestar el Dominio
        var now = _clock.UtcNow;

        // a) Crear Usuario
        var user = User.Create(email, request.FirstName, request.LastName);
        _userRepository.Add(user);

        // b) Crear Workspace
        var workspace = Workspace.Create(request.WorkspaceName);
        _workspaceRepository.Add(workspace);

        // c) Vincular Usuario como Dueño (OWNER)
        var membership = Membership.Create(user.Id, workspace.Id, MembershipRole.Owner);
        _membershipRepository.Add(membership);

        // d) Crear Licencia basada en la Plantilla
        var startDate = now;
        var endDate = startDate.AddMonths(request.DurationInMonths);
        var license = License.CreateTemplateLicense(workspace.Id, template.Id, startDate, endDate, now);

        // e) Copiar Módulos efectivos de la Plantilla a la Licencia (Regla arquitectónica B)
        foreach (var tm in templateModules)
        {
            license.AddModule(tm.ModuleId);
        }
        _licenseRepository.Add(license);

        // f) Auditoría (El SuperAdmin es el Actor, apuntando al nuevo Workspace)
        var audit = AuditLog.Create(
            workspaceId: workspace.Id,
            userId: _currentUser.UserId,
            action: AuditAction.WorkspaceCreated,
            details: $"Workspace '{workspace.Name}' creado con plantilla '{template.Name}' por {_currentUser.Email}."
        );
        _auditLogRepository.Add(audit);

        // 4. Persistir la Transacción Completa
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // 5. Retornar el ID del nuevo Workspace
        return Result<Guid>.Success(workspace.Id);
    }
}