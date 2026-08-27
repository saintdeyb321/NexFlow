using NexFlow.Domain.Entities;
namespace NexFlow.Application.Abstractions.Repositories;

public interface ITemplateRepository
{
    void Add(Template template);
    Task<Template?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<Template?> GetByNameAsync(string name, CancellationToken cancellationToken);
    Task<Template?> GetByCodeAsync(string templateCode, CancellationToken cancellationToken);
    Task<IEnumerable<Module>> GetActiveModulesForTemplateAsync(Guid templateId, CancellationToken cancellationToken);
    Task<IEnumerable<Template>> GetAllAsync(CancellationToken cancellationToken);
}