using System;
using System.Threading;
using System.Threading.Tasks;
using NexFlow.Domain.Entities;

namespace NexFlow.Application.Abstractions.Repositories;

public interface IWorkspaceRepository
{
    Task<Workspace?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    void Add(Workspace workspace);
}