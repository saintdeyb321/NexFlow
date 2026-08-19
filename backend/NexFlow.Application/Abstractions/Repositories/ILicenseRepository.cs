using System;
using System.Threading;
using System.Threading.Tasks;
using NexFlow.Domain.Entities;

namespace NexFlow.Application.Abstractions;

public interface ILicenseRepository
{
    Task<License?> GetByWorkspaceIdAsync(Guid workspaceId, CancellationToken cancellationToken);
    void Add(License license);
}