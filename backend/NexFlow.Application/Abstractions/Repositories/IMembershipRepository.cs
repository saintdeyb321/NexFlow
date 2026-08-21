using System;
using System.Threading;
using System.Threading.Tasks;
using NexFlow.Domain.Entities;

namespace NexFlow.Application.Abstractions.Repositories;

public interface IMembershipRepository
{
    Task<Membership?> GetUserMembershipAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken);
    void Add(Membership membership);
    Task<IEnumerable<Membership>> GetMembershipsByUserIdAsync(Guid userId, CancellationToken cancellationToken);
}