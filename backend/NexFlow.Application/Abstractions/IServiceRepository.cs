using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NexFlow.Application.Features.Business;
using NexFlow.Application.Features.Reservations;

namespace NexFlow.Application.Abstractions;

public interface IServiceRepository
{
    Task<IEnumerable<ServiceDto>> GetServicesAsync(Guid workspaceId, CancellationToken cancellationToken);
    Task<ServiceDto> SaveServiceAsync(Guid workspaceId, ServiceDto service, CancellationToken cancellationToken);
    Task DeleteServiceAsync(Guid workspaceId, string serviceId, CancellationToken cancellationToken);
}