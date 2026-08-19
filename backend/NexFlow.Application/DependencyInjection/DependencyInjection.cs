using Microsoft.Extensions.DependencyInjection;
using NexFlow.Application.Features.SuperAdmin.ProvisionClient;

namespace NexFlow.Application.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Aquí registraremos todos los Handlers de los Casos de Uso
        services.AddScoped<ProvisionClientCommandHandler>();
        services.AddScoped<NexFlow.Application.Engines.Reservation.IReservationEngine, NexFlow.Application.Engines.Reservation.ReservationEngine>();

        return services;
    }
}