using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
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
    private readonly IModuleRepository _moduleRepository; // ¡NUEVO!
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
            return Result<Guid>.Failure(new Error("License.Invalid", "Fecha inválida."));
        // 1. CORRECCIÓN SAAS: Reutilizar usuario si existe, crearlo si no[cite: 1]
        var email = new Email(request.Email);
        var user = await _userRepository.GetByEmailAsync(request.Email, cancellationToken);

        if (user == null)
        {
            user = User.Create(email, request.FirstName, request.LastName);
            _userRepository.Add(user);
        }
        // 2. Crear Entorno y Membresía
        var workspace = Workspace.Create(request.WorkspaceName);
        _workspaceRepository.Add(workspace);

        var membership = Membership.Create(user.Id, workspace.Id, MembershipRole.Owner);
        _membershipRepository.Add(membership);
        // 3. ESTRATEGIA DE LICENCIAMIENTO (Template vs Custom)
        License license;

        // ⚠️ NOTA: Asegúrate de cambiar 'TemplateName' por 'TemplateCode' en tu archivo ProvisionClientCommand.cs
        if (!string.IsNullOrEmpty(request.TemplateCode))
        {
            // FLUJO A: Plantilla (Busca por Código inmutable, NO por nombre)
            var template = await _templateRepository.GetByCodeAsync(request.TemplateCode, cancellationToken);

            if (template == null || template.Status != TemplateStatus.Active)
                return Result<Guid>.Failure(new Error("Template.Invalid", "Plantilla inactiva o no encontrada."));

            var activeModules = await _templateRepository.GetActiveModulesForTemplateAsync(template.Id, cancellationToken);
            if (!activeModules.Any())
                return Result<Guid>.Failure(new Error("Template.NoModules", "La plantilla no tiene módulos configurados."));

            license = License.CreateTemplateLicense(workspace.Id, template.Id, now, request.ExpiresAt);
            foreach (var module in activeModules) license.AddTemplateModule(module.Id);
        }
        else if (request.CustomModules != null && request.CustomModules.Any())
        {
            // FLUJO B: Custom (A la carta)
            license = License.CreateCustomLicense(workspace.Id, now, request.ExpiresAt);

            var systemModules = await _moduleRepository.GetByCodesAsync(request.CustomModules, cancellationToken);

            if (systemModules.Count != request.CustomModules.Count)
                return Result<Guid>.Failure(new Error("Modules.Invalid", "Uno o más módulos personalizados enviados no existen."));

            foreach (var module in systemModules) license.AddCustomModule(module.Id);
        }
        else
        {
            return Result<Guid>.Failure(new Error("Provision.Invalid", "Debe proporcionar un TemplateCode o una lista de CustomModules."));
        }

        _licenseRepository.Add(license);

        // 4. Auditoría y Guardado
        var audit = AuditLog.Create(workspace.Id, user.Id, AuditAction.WorkspaceCreated, "Provisioned");
        _auditLogRepository.Add(audit);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(workspace.Id);
    }
}