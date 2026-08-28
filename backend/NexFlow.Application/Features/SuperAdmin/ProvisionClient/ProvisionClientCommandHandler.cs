using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Common;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Enums;
using NexFlow.Domain.ValueObjects;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NexFlow.Application.Features.SuperAdmin.ProvisionClient;

public class ProvisionClientCommandHandler
{
    private readonly IUserRepository _userRepository;
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly IMembershipRepository _membershipRepository;
    private readonly ILicenseRepository _licenseRepository;
    private readonly ITemplateRepository _templateRepository;
    private readonly IModuleRepository _moduleRepository;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public ProvisionClientCommandHandler(
        IUserRepository userRepository,
        IWorkspaceRepository workspaceRepository,
        IMembershipRepository membershipRepository,
        ILicenseRepository licenseRepository,
        ITemplateRepository templateRepository,
        IModuleRepository moduleRepository,
        IAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _userRepository = userRepository;
        _workspaceRepository = workspaceRepository;
        _membershipRepository = membershipRepository;
        _licenseRepository = licenseRepository;
        _templateRepository = templateRepository;
        _moduleRepository = moduleRepository;
        _auditLogRepository = auditLogRepository;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<Guid>> Handle(ProvisionClientCommand request, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        if (request.ExpiresAt <= now)
            return Result<Guid>.Failure(new Error("License.Invalid", "Fecha de expiración inválida."));

        // 🔥 SPRINT 1: Validar MaxLocations desde el Backend (Auditoría #37)
        if (request.MaxLocations < 1)
            return Result<Guid>.Failure(new Error("Provision.InvalidLocations", "El negocio debe permitir al menos 1 sede operativa."));

        bool hasTemplate = !string.IsNullOrEmpty(request.TemplateCode);
        bool hasCustomModules = request.CustomModules != null && request.CustomModules.Any();

        if (hasTemplate && hasCustomModules)
            return Result<Guid>.Failure(new Error("Provision.Conflict", "No puede especificar una Plantilla y Módulos Personalizados al mismo tiempo. Elija solo uno."));

        if (!hasTemplate && !hasCustomModules)
            return Result<Guid>.Failure(new Error("Provision.Invalid", "Debe proporcionar obligatoriamente un TemplateCode o una lista de CustomModules."));

        var email = new Email(request.Email);
        var user = await _userRepository.GetByEmailAsync(request.Email, cancellationToken);

        if (user == null)
        {
            // Solo colocamos "Usuario" si de verdad es nuevo y no enviaron nombre.
            user = User.Create(email, request.FirstName ?? "Usuario", request.LastName ?? "");
            _userRepository.Add(user);
        }
        else
        {
            // 🔥 SPRINT 1: Bloqueamos creación de múltiples workspaces para el mismo correo (Auditoría #36)
            var existingMemberships = await _membershipRepository.GetMembershipsByUserIdAsync(user.Id, cancellationToken);
            if (existingMemberships.Any())
            {
                return Result<Guid>.Failure(new Error("Provision.UserAlreadyHasWorkspace", "El usuario ya tiene un negocio asignado. El sistema solo permite un negocio por correo."));
            }
        }

        var workspace = Workspace.Create(request.WorkspaceName);
        _workspaceRepository.Add(workspace);

        var membership = Membership.Create(user.Id, workspace.Id, MembershipRole.Owner);
        _membershipRepository.Add(membership);

        License license;

        if (hasTemplate)
        {
            var template = await _templateRepository.GetByCodeAsync(request.TemplateCode!, cancellationToken);

            if (template == null || template.Status != TemplateStatus.Active)
                return Result<Guid>.Failure(new Error("Template.Invalid", "Plantilla inactiva o no encontrada."));

            var activeModules = await _templateRepository.GetActiveModulesForTemplateAsync(template.Id, cancellationToken);
            if (!activeModules.Any())
                return Result<Guid>.Failure(new Error("Template.NoModules", "La plantilla seleccionada no tiene módulos configurados."));

            license = License.CreateTemplateLicense(workspace.Id, template.Id, now, request.ExpiresAt, request.MaxLocations);
            foreach (var module in activeModules) license.AddTemplateModule(module.Id);
        }
        else
        {
            license = License.CreateCustomLicense(workspace.Id, now, request.ExpiresAt, request.MaxLocations);

            var systemModules = await _moduleRepository.GetByCodesAsync(request.CustomModules!, cancellationToken);

            if (systemModules.Count != request.CustomModules!.Count)
                return Result<Guid>.Failure(new Error("Modules.Invalid", "Uno o más módulos personalizados enviados no existen en el sistema."));

            foreach (var module in systemModules) license.AddCustomModule(module.Id);
        }

        _licenseRepository.Add(license);

        var audit = AuditLog.Create(workspace.Id, user.Id, AuditAction.WorkspaceCreated, "Provisioned");
        _auditLogRepository.Add(audit);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Guid>.Success(workspace.Id);
    }
}