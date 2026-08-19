using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using NexFlow.Application.Abstractions;
using NexFlow.Domain.ValueObjects;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context; // <-- Para leer la nueva tabla
using Microsoft.EntityFrameworkCore;

namespace NexFlow.API.Security;

public class SuperAdminRequirement : IAuthorizationRequirement { }

public class SuperAdminHandler : AuthorizationHandler<SuperAdminRequirement>
{
    private readonly IUserRepository _userRepository;
    private readonly NexFlowDbContext _context; // Llamamos directo al contexto para esta validación global

    public SuperAdminHandler(IUserRepository userRepository, NexFlowDbContext context)
    {
        _userRepository = userRepository;
        _context = context;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, SuperAdminRequirement requirement)
    {
        var emailClaim = context.User.FindFirst("email")?.Value;
        if (string.IsNullOrEmpty(emailClaim)) return;

        var user = await _userRepository.GetByEmailAsync(new Email(emailClaim), System.Threading.CancellationToken.None);
        if (user == null) return;

        // Validamos estrictamente contra la tabla aislada
        bool isGod = await _context.Set<NexFlow.Domain.Entities.SystemAdministrator>()
                                   .AnyAsync(sa => sa.UserId == user.Id);

        if (isGod)
        {
            context.Succeed(requirement);
        }
    }
}