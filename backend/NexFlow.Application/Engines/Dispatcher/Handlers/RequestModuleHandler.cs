using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Engines.Dispatcher;

namespace NexFlow.Application.Engines.Dispatcher.Handlers;

public class RequestModuleHandler : IModuleHandler
{
    public string ModuleCode => "REQUESTS";

    // 🔥 SPRINT 4: Agregamos READ_STATUS
    public string[] SupportedCapabilities => new[] { "CREATE", "READ_STATUS", "UPDATE_STATUS" };

    private readonly IRequestRepository _requestRepository;

    public RequestModuleHandler(IRequestRepository requestRepository)
    {
        _requestRepository = requestRepository;
    }

    public async Task<ModuleExecutionResult> ExecuteCapabilityAsync(Guid workspaceId, CapabilityRequest request, CancellationToken cancellationToken)
    {
        var phone = request.Parameters.TryGetValue("phone", out var p) ? p?.ToString() ?? "Desconocido" : "Desconocido";

        if (request.CapabilityCode == "CREATE")
        {
            var contextDescription = request.Parameters.TryGetValue("context", out var c) ? c?.ToString() ?? "Solicitud general" : "Solicitud general";

            var record = new Features.Requests.RequestRecord
            {
                ConsumerPhone = phone,
                Title = "Nueva Solicitud / Trámite",
                Description = contextDescription
            };

            await _requestRepository.CreateRequestAsync(workspaceId, record, cancellationToken);

            // 🔥 SPRINT 9 (Fase 3): Devolvemos JSON puro, sin prompts instruccionales
            var successData = JsonSerializer.Serialize(new { status = "CREATED", title = record.Title });
            return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, successData, false, Array.Empty<string>());
        }

        // 🔥 SPRINT 4 (P0): Consulta real del estado del trámite en la BD
        if (request.CapabilityCode == "READ_STATUS")
        {
            var activeRequest = await _requestRepository.GetLatestRequestByPhoneAsync(workspaceId, phone, cancellationToken);

            if (activeRequest != null)
            {
                var statusData = JsonSerializer.Serialize(new
                {
                    status = "FOUND",
                    requestStatus = activeRequest.Status.ToString(),
                    title = activeRequest.Title,
                    createdAt = activeRequest.CreatedAt.ToString("yyyy-MM-dd HH:mm")
                });
                return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, statusData, false, Array.Empty<string>());
            }

            var notFoundData = JsonSerializer.Serialize(new { status = "NOT_FOUND", message = "No se encontraron trámites activos para este número de teléfono." });
            return new ModuleExecutionResult(false, ModuleCode, request.CapabilityCode, notFoundData, false, Array.Empty<string>());
        }

        if (request.CapabilityCode == "UPDATE_STATUS")
        {
            // UPDATE_STATUS es semánticamente para administradores, no para que el usuario consulte.
            var errorData = JsonSerializer.Serialize(new { status = "ERROR", message = "Operación denegada. El usuario no puede actualizar estados directamente." });
            return new ModuleExecutionResult(false, ModuleCode, request.CapabilityCode, errorData, false, Array.Empty<string>());
        }

        return new ModuleExecutionResult(false, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new { error = "Capacidad no soportada" }), false, Array.Empty<string>());
    }
}