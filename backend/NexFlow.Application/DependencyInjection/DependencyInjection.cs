using Microsoft.Extensions.DependencyInjection;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Reservations;
using NexFlow.Application.Features.SuperAdmin.ProvisionClient;
using NexFlow.Application.Services;

namespace NexFlow.Application.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Aquí registraremos todos los Handlers de los Casos de Uso
        services.AddScoped<ProvisionClientCommandHandler>();
        services.AddScoped<IReservationEngine, NexFlow.Application.Engines.Reservation.ReservationEngine>();
        services.AddScoped<IEntitlementService, EntitlementService>();

        return services;
    }
}