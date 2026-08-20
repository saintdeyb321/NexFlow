using NexFlow.Domain.Entities;

namespace NexFlow.Application.Abstractions.Repositories;

public interface IAuditLogRepository
{
    void Add(AuditLog auditLog);
}