using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Domain.Entities;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;

public class AuditLogRepository : IAuditLogRepository
{
    private readonly NexFlowDbContext _context;

    public AuditLogRepository(NexFlowDbContext context) => _context = context;

    public void Add(AuditLog auditLog) => _context.AuditLogs.Add(auditLog);
}