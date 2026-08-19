using NexFlow.Domain.Entities;

namespace NexFlow.Application.Abstractions;

public interface IAuditLogRepository
{
    void Add(AuditLog auditLog);
}