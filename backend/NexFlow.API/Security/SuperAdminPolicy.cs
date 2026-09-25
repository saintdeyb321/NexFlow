using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;

namespace NexFlow.API.Security;

public class SuperAdminRequirement : IAuthorizationRequirement { }

public class SuperAdminHandler : AuthorizationHandler<SuperAdminRequirement>
{
    private readonly ICurrentUser _currentUser; 
    private readonly ISystemAdministratorRepository _sysAdminRepository;

    public SuperAdminHandler(ICurrentUser currentUser, ISystemAdministratorRepository sysAdminRepository)
    {
        _currentUser = currentUser;
        _sysAdminRepository = sysAdminRepository;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, SuperAdminRequirement requirement)
    {
        var userId = _currentUser.UserId;

        if (userId == Guid.Empty) return;

        bool isGod = await _sysAdminRepository.IsUserSuperAdminAsync(userId, System.Threading.CancellationToken.None);

        if (isGod)
        {
            context.Succeed(requirement);
        }
    }
}