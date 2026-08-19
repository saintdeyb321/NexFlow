using NexFlow.Domain.Entities;
using NexFlow.Domain.ValueObjects;

namespace NexFlow.Application.Abstractions;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<User?> GetByEmailAsync(Email email, CancellationToken cancellationToken);
    void Add(User user);
}

public interface IWorkspaceRepository
{
    Task<Workspace?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    void Add(Workspace workspace);
}

public interface IMembershipRepository
{
    Task<Membership?> GetUserMembershipAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken);
    void Add(Membership membership);
}

public interface ILicenseRepository
{
    Task<License?> GetByWorkspaceIdAsync(Guid workspaceId, CancellationToken cancellationToken);
    void Add(License license);
}

public interface ITemplateRepository
{
    Task<Template?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IEnumerable<TemplateModule>> GetTemplateModulesAsync(Guid templateId, CancellationToken cancellationToken);
    void Add(Template template);
}

public interface IAuditLogRepository
{
    void Add(AuditLog auditLog);
}