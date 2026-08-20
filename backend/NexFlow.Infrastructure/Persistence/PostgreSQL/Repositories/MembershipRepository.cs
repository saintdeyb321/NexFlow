using Microsoft.EntityFrameworkCore;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Domain.Entities;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;

public class MembershipRepository : IMembershipRepository
{
    private readonly NexFlowDbContext _context;

    public MembershipRepository(NexFlowDbContext context) => _context = context;

    public void Add(Membership membership) => _context.Memberships.Add(membership);

    public async Task<Membership?> GetUserMembershipAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken)
    {
        return await _context.Memberships
            .FirstOrDefaultAsync(m => m.UserId == userId && m.WorkspaceId == workspaceId, cancellationToken);
    }
}