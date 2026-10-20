using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Automation.Dialogue.Understanding;
using NexFlow.Application.Features.Business;
using NexFlow.Application.Features.Business.Offerings;
using NexFlow.Application.Features.Catalog.DTOs;
using NexFlow.Application.Features.Services.DTOs;

namespace NexFlow.Tests.Concierge;

internal sealed class ConciergeFixture
{
    internal Guid Workspace { get; } = Guid.NewGuid();
    internal DateTime Now { get; set; } = new(2026, 10, 20, 21, 0, 0, DateTimeKind.Utc);
    internal string Zone { get; set; } = "America/Lima";
    internal HashSet<string> Modules { get; } = new(["CATALOG", "SERVICES", "RESERVATIONS", "ORDERS", "LOCATIONS", "BUSINESS_HOURS", "REQUESTS"], StringComparer.OrdinalIgnoreCase);
    internal Mock<IEntitlementService> Entitlements { get; } = new(MockBehavior.Strict);
    internal Mock<IBusinessProfileRepository> Profiles { get; } = new(MockBehavior.Strict);
    internal Mock<ILocationRepository> Locations { get; } = new(MockBehavior.Strict);
    internal Mock<IOfferingService> Offerings { get; } = new(MockBehavior.Strict);
    internal WorkspaceSlotResolver Resolver { get; }
    internal ConciergeFixture(string service = "Limpieza dental", string product = "Cepillo dental", ILogger<WorkspaceSlotResolver>? logger = null)
    {
        Entitlements.Setup(e => e.GetAvailableModuleCodesAsync(Workspace, It.IsAny<CancellationToken>())).ReturnsAsync(() => Modules.ToArray());
        Profiles.Setup(p => p.GetProfileAsync(Workspace, It.IsAny<CancellationToken>())).ReturnsAsync(() => new BusinessProfileDto("Synthetic", "", "", "", "", Zone));
        Locations.Setup(l => l.GetLocationsAsync(Workspace, It.IsAny<CancellationToken>())).ReturnsAsync(new[]
        {
            new LocationDto("concepcion", "Sede Concepcion", "Synthetic", null, null, true),
            new LocationDto("san-jeronimo", "Sede san jeronimo", "Synthetic", null, null, false)
        });
        Offerings.Setup(o => o.GetServicesAsync(Workspace, It.IsAny<string?>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(new[]
        {
            new ServiceDto { Id = "service", Name = service, IsActive = true, RequiresReservation = true, DurationInMinutes = 30 },
            new ServiceDto { Id = "similar", Name = service.Split(' ')[0] + " especial", IsActive = true, RequiresReservation = true, DurationInMinutes = 45 },
            new ServiceDto { Id = "inactive", Name = "Servicio retirado", IsActive = false, RequiresReservation = true, DurationInMinutes = 30 },
            new ServiceDto { Id = "other-location", Name = "Servicio privado", IsActive = true, RequiresReservation = true, DurationInMinutes = 30, LocationScope = "SPECIFIC", LocationIds = ["san-jeronimo"] },
            new ServiceDto { Id = "not-reservable", Name = "Consulta informativa", IsActive = true, RequiresReservation = false }
        });
        Offerings.Setup(o => o.GetProductsAsync(Workspace, It.IsAny<string?>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(new[]
        {
            new ProductDto { Id = "product", Name = product, IsActive = true },
            new ProductDto { Id = "inactive-product", Name = "Producto retirado", IsActive = false }
        });
        var clock = new Mock<IClock>(); clock.SetupGet(c => c.UtcNow).Returns(() => Now);
        Resolver = new(Entitlements.Object, Profiles.Object, Locations.Object, Offerings.Object, clock.Object,
            new(), new(), new(), new(), new(), logger ?? NullLogger<WorkspaceSlotResolver>.Instance);
    }
}
