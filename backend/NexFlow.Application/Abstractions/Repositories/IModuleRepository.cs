using NexFlow.Domain.Entities;

namespace NexFlow.Application.Abstractions.Repositories;

public interface IModuleRepository
{
    void Add(Module module);
    Task<Module?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IEnumerable<Module>> GetActiveModulesAsync(IEnumerable<Guid> moduleIds, CancellationToken cancellationToken);
    Task<List<Module>> GetByCodesAsync(IEnumerable<string> moduleCodes, CancellationToken cancellationToken);
    Task<IEnumerable<Module>> GetAllAsync(CancellationToken cancellationToken);
}