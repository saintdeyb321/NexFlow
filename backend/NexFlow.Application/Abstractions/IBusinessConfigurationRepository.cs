using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NexFlow.Application.Features.Business;
using NexFlow.Application.Features.Knowledge;
using NexFlow.Application.Features.Reservations;

namespace NexFlow.Application.Abstractions;

public interface IBusinessConfigurationRepository
{
    Task<BusinessProfileDto?> GetProfileAsync(Guid workspaceId, CancellationToken cancellationToken);
    Task SaveProfileAsync(Guid workspaceId, BusinessProfileDto profile, CancellationToken cancellationToken);

    Task<IEnumerable<LocationDto>> GetLocationsAsync(Guid workspaceId, CancellationToken cancellationToken);
    Task SaveLocationAsync(Guid workspaceId, LocationDto location, CancellationToken cancellationToken);
    Task DeleteLocationAsync(Guid workspaceId, string locationId, CancellationToken cancellationToken);

    Task<IEnumerable<BusinessHoursDto>> GetBusinessHoursAsync(Guid workspaceId, string? locationId, CancellationToken cancellationToken);
    Task SaveBusinessHoursAsync(Guid workspaceId, string? locationId, IEnumerable<BusinessHoursDto> hours, CancellationToken cancellationToken);

    // NUEVO: Servicios (Para el motor de reservas reales)
    Task<IEnumerable<ServiceDto>> GetServicesAsync(Guid workspaceId, CancellationToken cancellationToken);
    Task SaveServiceAsync(Guid workspaceId, ServiceDto service, CancellationToken cancellationToken);
    Task DeleteServiceAsync(Guid workspaceId, string serviceId, CancellationToken cancellationToken);

    // NUEVO: FAQs (Para la base de conocimiento de la IA)
    Task<IEnumerable<FaqDto>> GetFaqsAsync(Guid workspaceId, CancellationToken cancellationToken);
    Task SaveFaqAsync(Guid workspaceId, FaqDto faq, CancellationToken cancellationToken);
    Task DeleteFaqAsync(Guid workspaceId, string faqId, CancellationToken cancellationToken);
}