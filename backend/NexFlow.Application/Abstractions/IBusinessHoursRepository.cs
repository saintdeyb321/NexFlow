using NexFlow.Application.Features.Business;

namespace NexFlow.Application.Abstractions;

public interface IBusinessHoursRepository
{
    Task<IEnumerable<BusinessHoursDto>> GetBusinessHoursAsync(Guid workspaceId, string? locationId, CancellationToken cancellationToken);
    Task SaveBusinessHoursAsync(Guid workspaceId, string? locationId, IEnumerable<BusinessHoursDto> hours, CancellationToken cancellationToken);
}