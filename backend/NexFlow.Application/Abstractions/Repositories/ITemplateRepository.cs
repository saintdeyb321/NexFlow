using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NexFlow.Domain.Entities;

namespace NexFlow.Application.Abstractions.Repositories;

public interface ITemplateRepository
{
    Task<Template?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IEnumerable<Module>> GetActiveModulesForTemplateAsync(Guid templateId, CancellationToken cancellationToken);
}