using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;

public class SystemAdministratorRepository : ISystemAdministratorRepository
{
    private readonly NexFlowDbContext _context;

    public SystemAdministratorRepository(NexFlowDbContext context)
    {
        _context = context;
    }

    public async Task<bool> IsUserSuperAdminAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _context.Set<Domain.Entities.SystemAdministrator>()
                             .AnyAsync(sa => sa.UserId == userId, cancellationToken);
    }
}