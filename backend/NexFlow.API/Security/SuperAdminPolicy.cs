using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using NexFlow.Application.Abstractions.Repositories;

namespace NexFlow.API.Security;

public class SuperAdminRequirement : IAuthorizationRequirement { }

public class SuperAdminHandler : AuthorizationHandler<SuperAdminRequirement>
{
    private readonly IUserRepository _userRepository;
    private readonly ISystemAdministratorRepository _sysAdminRepository;

    public SuperAdminHandler(IUserRepository userRepository, ISystemAdministratorRepository sysAdminRepository)
    {
        _userRepository = userRepository;
        _sysAdminRepository = sysAdminRepository;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, SuperAdminRequirement requirement)
    {
        var emailClaim = context.User.FindFirst("email")?.Value
                      ?? context.User.FindFirst(ClaimTypes.Email)?.Value;

        if (string.IsNullOrEmpty(emailClaim)) return;

        var user = await _userRepository.GetByEmailAsync(emailClaim, System.Threading.CancellationToken.None);
        if (user == null) return;

        bool isGod = await _sysAdminRepository.IsUserSuperAdminAsync(user.Id, System.Threading.CancellationToken.None);

        if (isGod)
        {
            context.Succeed(requirement);
        }
    }
}