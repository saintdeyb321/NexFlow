using NexFlow.Application.Abstractions.Repositories;

namespace NexFlow.Application.Engines.Dispatcher.Handlers;

public class RequestModuleHandler : IModuleHandler
{
    public string ModuleCode => "REQUESTS";
    public string[] SupportedCapabilities => new[] { "CREATE", "UPDATE_STATUS" };

    private readonly IRequestRepository _requestRepository;

    public RequestModuleHandler(IRequestRepository requestRepository)
    {
        _requestRepository = requestRepository;
    }

    public async Task<string> ExecuteCapabilityAsync(Guid workspaceId, CapabilityRequest request, CancellationToken cancellationToken)
    {
        if (request.CapabilityCode == "CREATE")
        {
            var phone = request.Parameters.TryGetValue("phone", out var p) ? p : "Desconocido";
            var contextDescription = request.Parameters.TryGetValue("context", out var c) ? c : "Solicitud general (Sin detalles extraídos)";

            var record = new Features.Requests.RequestRecord
            {
                ConsumerPhone = phone,
                Title = "Nueva Solicitud / Trámite",
                Description = contextDescription
            };

            await _requestRepository.CreateRequestAsync(workspaceId, record, cancellationToken);

            // SPRINT 5: Aquí la Notificación fluye orgánicamente informándole al cliente.
            return "SISTEMA: El trámite o afiliación ha sido registrado correctamente. Infórmale al cliente que hemos guardado su solicitud y que un asesor revisará su caso a la brevedad.";
        }

        return "SISTEMA: Capacidad no implementada en el módulo de trámites.";
    }
}