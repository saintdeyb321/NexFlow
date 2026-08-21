using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Common;

namespace NexFlow.Application.Features.Identity.GetMe;

public record MeDto(UserDto User, WorkspaceDto? Workspace, LicenseDto? License, string[] Entitlements);
public record UserDto(Guid Id, string Email, string FirstName, string LastName);
public record WorkspaceDto(Guid Id, string Name, string Status);
public record LicenseDto(string Type, string Status, DateTime? ExpiresAt);

public class GetMeQueryHandler
{
    private readonly ICurrentUser _currentUser;
    private readonly IUserRepository _userRepository;
    private readonly IMembershipRepository _membershipRepository;
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly ILicenseRepository _licenseRepository;
    private readonly IEntitlementService _entitlementService;

    public GetMeQueryHandler(
        ICurrentUser currentUser, IUserRepository userRepository, IMembershipRepository membershipRepository,
        IWorkspaceRepository workspaceRepository, ILicenseRepository licenseRepository, IEntitlementService entitlementService)
    {
        _currentUser = currentUser; _userRepository = userRepository; _membershipRepository = membershipRepository;
        _workspaceRepository = workspaceRepository; _licenseRepository = licenseRepository; _entitlementService = entitlementService;
    }

    public async Task<Result<MeDto>> Handle(CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(_currentUser.UserId, cancellationToken);
        if (user == null) return Result<MeDto>.Failure(new Error("User.NotFound", "Usuario no encontrado."));

        var userDto = new UserDto(user.Id, user.Email.Value, user.FirstName, user.LastName);

        var memberships = await _membershipRepository.GetMembershipsByUserIdAsync(user.Id, cancellationToken);
        var activeMembership = memberships.FirstOrDefault();

        if (activeMembership == null)
            return Result<MeDto>.Success(new MeDto(userDto, null, null, Array.Empty<string>()));

        // BLINDAJE NULL: Verificamos que el workspace realmente exista
        var workspace = await _workspaceRepository.GetByIdAsync(activeMembership.WorkspaceId, cancellationToken);
        if (workspace == null)
            return Result<MeDto>.Success(new MeDto(userDto, null, null, Array.Empty<string>()));

        var license = await _licenseRepository.GetByWorkspaceIdAsync(workspace.Id, cancellationToken);
        var entitlements = await _entitlementService.GetAvailableModuleCodesAsync(workspace.Id, cancellationToken);

        var workspaceDto = new WorkspaceDto(workspace.Id, workspace.Name, workspace.Status.ToString());

        // CORRECCIÓN: Accedemos al ValueObject ValidityPeriod.End
        var licenseDto = license != null
            ? new LicenseDto(license.Type.ToString(), license.Status.ToString(), license.ValidityPeriod.End)
            : null;

        return Result<MeDto>.Success(new MeDto(userDto, workspaceDto, licenseDto, entitlements.ToArray()));
    }
}