using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NexFlow.Domain.Entities;

namespace NexFlow.Application.Abstractions.Repositories;

public interface IModuleRepository
{
    Task<Module?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IEnumerable<Module>> GetActiveModulesAsync(IEnumerable<Guid> moduleIds, CancellationToken cancellationToken);
}