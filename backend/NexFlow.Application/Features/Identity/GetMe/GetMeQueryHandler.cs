using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Common;

namespace NexFlow.Application.Features.Identity.GetMe;

public record MeDto(UserDto User, WorkspaceDto? Workspace, LicenseDto? License, string[] Entitlements,
    string? MembershipRole, Dictionary<string, string[]> Capabilities);
public record UserDto(Guid Id, string Email, string FirstName, string LastName, bool IsSuperAdmin);
public record WorkspaceDto(Guid Id, string Name, string Status);
public record LicenseDto(string Type, string Status, DateTime? ExpiresAt);

public class GetMeQueryHandler(ICurrentUser currentUser, IUserRepository users, IMembershipRepository memberships,
    IWorkspaceRepository workspaces, ILicenseRepository licenses, IEntitlementService entitlements,
    ISystemAdministratorRepository administrators, IWorkspaceContext workspaceContext)
{
    public async Task<Result<MeDto>> Handle(CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(currentUser.UserId, cancellationToken);
        if (user == null) return Result<MeDto>.Failure(new Error("User.NotFound", "Usuario no encontrado."));
        var isSuperAdmin = await administrators.IsUserSuperAdminAsync(user.Id, cancellationToken);
        var userDto = new UserDto(user.Id, user.Email.Value, user.FirstName, user.LastName, isSuperAdmin);
        var ownedMemberships = await memberships.GetMembershipsByUserIdAsync(user.Id, cancellationToken);
        var selectedId = workspaceContext.CurrentWorkspaceId;
        var membership = selectedId == Guid.Empty ? ownedMemberships.FirstOrDefault()
            : ownedMemberships.FirstOrDefault(m => m.WorkspaceId == selectedId);
        if (selectedId == Guid.Empty) selectedId = membership?.WorkspaceId ?? Guid.Empty;
        if (selectedId == Guid.Empty || (membership == null && !isSuperAdmin))
            return Result<MeDto>.Success(new MeDto(userDto, null, null, [], null, new()));
        var workspace = await workspaces.GetByIdAsync(selectedId, cancellationToken);
        if (workspace == null) return Result<MeDto>.Success(new MeDto(userDto, null, null, [], null, new()));
        var license = await licenses.GetByWorkspaceIdAsync(workspace.Id, cancellationToken);
        var modules = await entitlements.GetAvailableModuleCodesAsync(workspace.Id, cancellationToken);
        var capabilities = await entitlements.GetEffectiveCapabilitiesAsync(workspace.Id, cancellationToken);
        return Result<MeDto>.Success(new MeDto(userDto,
            new WorkspaceDto(workspace.Id, workspace.Name, workspace.Status.ToString()),
            license == null ? null : new LicenseDto(license.Type.ToString(), license.Status.ToString(), license.ValidityPeriod?.End),
            modules.ToArray(), membership?.Role.ToString(), capabilities));
    }
}
