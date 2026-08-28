using System;
using System.Threading;
using System.Threading.Tasks;
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
            var phone = request.Parameters.TryGetValue("phone", out var p) ? p?.ToString() ?? "Desconocido" : "Desconocido";
            var contextDescription = request.Parameters.TryGetValue("context", out var c) ? c?.ToString() ?? "Solicitud general (Sin detalles extraídos)" : "Solicitud general (Sin detalles extraídos)";

            var record = new Features.Requests.RequestRecord
            {
                ConsumerPhone = phone,
                Title = "Nueva Solicitud / Trámite",
                Description = contextDescription
            };

            await _requestRepository.CreateRequestAsync(workspaceId, record, cancellationToken);

            return "SISTEMA: El trámite o afiliación ha sido registrado correctamente. Infórmale al cliente que hemos guardado su solicitud y que un asesor revisará su caso a la brevedad.";
        }

        // 🔥 SPRINT 3 (Auditoría #10): Implementación segura de UPDATE_STATUS
        if (request.CapabilityCode == "UPDATE_STATUS")
        {
            var phone = request.Parameters.TryGetValue("phone", out var p) ? p?.ToString() ?? "Desconocido" : "Desconocido";
            return $"SISTEMA: Dile al cliente que un agente revisará el estado del trámite asociado a su número {phone} y le responderá por este mismo medio. [RequiresHuman]";
        }

        return "SISTEMA: Capacidad no implementada en el módulo de trámites.";
    }
}