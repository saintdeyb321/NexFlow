using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Features.Requests;
using NexFlow.Application.Features.Reservations;
using NexFlow.Domain.Enums;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using CacheContextDto = NexFlow.Application.Abstractions.Cache.ConversationContextDto;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services;

public interface ICapabilityExecutor
{
    Task<(string SystemPrompt, List<AiTool> Tools)> GetContextAndToolsAsync(Guid workspaceId, HashSet<string> activeModules, string? selectedLocationId, CancellationToken ct);
    Task<string> ExecuteToolAsync(AiToolCall toolCall, HashSet<string> activeModules, Guid workspaceId, string phone, string conversationId, CacheContextDto context, CancellationToken ct);
}

public sealed class CapabilityExecutor : ICapabilityExecutor
{
    private readonly IReservationEngine _reservationEngine;
    private readonly ILocationRepository _locationRepo;
    private readonly ICatalogRepository _catalogRepo;
    private readonly IBusinessHoursRepository _hoursRepo;
    private readonly IFaqRepository _faqRepo;
    private readonly IBusinessProfileRepository _profileRepo;
    private readonly IRequestRepository _requestRepo;
    private readonly IConversationRepository _conversationRepo;
    private readonly ILogger<CapabilityExecutor> _logger;

    public CapabilityExecutor(
        IReservationEngine reservationEngine, ILocationRepository locationRepo, ICatalogRepository catalogRepo,
        IBusinessHoursRepository hoursRepo, IFaqRepository faqRepo, IBusinessProfileRepository profileRepo,
        IRequestRepository requestRepo, IConversationRepository conversationRepo, ILogger<CapabilityExecutor> logger)
    {
        _reservationEngine = reservationEngine; _locationRepo = locationRepo; _catalogRepo = catalogRepo;
        _hoursRepo = hoursRepo; _faqRepo = faqRepo; _profileRepo = profileRepo;
        _requestRepo = requestRepo; _conversationRepo = conversationRepo; _logger = logger;
    }

    public async Task<(string SystemPrompt, List<AiTool> Tools)> GetContextAndToolsAsync(Guid workspaceId, HashSet<string> activeModules, string? selectedLocationId, CancellationToken ct)
    {
        var locations = await _locationRepo.GetLocationsAsync(workspaceId, ct);
        // 🔥 CORRECCIÓN 1: Exponer la dirección física (Address) al System Prompt
        var locationsInfo = string.Join(" | ", locations.Select(l =>
            $"ID: '{l.Id}', Nombre: '{l.Name}', Dirección: '{(string.IsNullOrWhiteSpace(l.Address) ? "No especificada" : l.Address)}'"));

        var rules = new List<string>
        {
            "- NUNCA inventes información no verificable ni datos fuera del catálogo.",
            "- Responde siempre en español, de forma concisa, cálida y profesional."
        };

        var tools = new List<AiTool>
        {
            new("request_human", "Transfiere a humano.", JsonNode.Parse(@"{ ""type"": ""object"", ""properties"": {} }")!.AsObject())
        };

        if (activeModules.Contains("RESERVATIONS"))
        {
            rules.Add("- Para reservar, RECOLECTA: sede, servicio y fecha. Luego verifica con 'check_availability'.");
            rules.Add("- Para confirmar una cita, invoca 'create_reservation' (sede, servicio, fecha, hora, nombre).");
            tools.Add(new("check_availability", "Consulta horarios libres.", JsonNode.Parse(@"{ ""type"": ""object"", ""properties"": { ""locationId"": { ""type"": ""string"" }, ""serviceId"": { ""type"": ""string"" }, ""date"": { ""type"": ""string"" } }, ""required"": [""locationId"", ""serviceId"", ""date""] }")!.AsObject()));
            tools.Add(new("create_reservation", "Registra reserva.", JsonNode.Parse(@"{ ""type"": ""object"", ""properties"": { ""locationId"": { ""type"": ""string"" }, ""serviceId"": { ""type"": ""string"" }, ""date"": { ""type"": ""string"" }, ""time"": { ""type"": ""string"" }, ""customerName"": { ""type"": ""string"" } }, ""required"": [""locationId"", ""serviceId"", ""date"", ""time"", ""customerName""] }")!.AsObject()));
            tools.Add(new("cancel_active_reservation", "Cancela cita activa.", JsonNode.Parse(@"{ ""type"": ""object"", ""properties"": {} }")!.AsObject()));
        }

        if (activeModules.Contains("CATALOG") || activeModules.Contains("SERVICES"))
        {
            rules.Add("- Para productos o servicios NUNCA inventes precios ni tiempos. Utiliza siempre 'search_catalog'.");
            tools.Add(new("search_catalog", "Busca productos y servicios.", JsonNode.Parse(@"{ ""type"": ""object"", ""properties"": { ""query"": { ""type"": ""string"" }, ""type"": { ""type"": ""string"" } } }")!.AsObject()));
        }

        if (activeModules.Contains("REQUESTS"))
        {
            rules.Add("- Para reclamos o trámites, utiliza 'create_request'.");
            tools.Add(new("create_request", "Crea ticket de atención.", JsonNode.Parse(@"{ ""type"": ""object"", ""properties"": { ""title"": { ""type"": ""string"" }, ""description"": { ""type"": ""string"" } }, ""required"": [""title"", ""description""] }")!.AsObject()));
            tools.Add(new("check_request_status", "Revisa último trámite.", JsonNode.Parse(@"{ ""type"": ""object"", ""properties"": {} }")!.AsObject()));
        }

        if (activeModules.Contains("BUSINESS_HOURS"))
            tools.Add(new("get_business_hours", "Obtiene horarios de atención.", JsonNode.Parse(@"{ ""type"": ""object"", ""properties"": { ""locationId"": { ""type"": ""string"" } }, ""required"": [""locationId""] }")!.AsObject()));

        if (activeModules.Contains("FAQ"))
            tools.Add(new("get_faqs", "Obtiene preguntas frecuentes.", JsonNode.Parse(@"{ ""type"": ""object"", ""properties"": {} }")!.AsObject()));

        if (activeModules.Contains("BUSINESS_PROFILE"))
            tools.Add(new("get_profile", "Obtiene información del negocio.", JsonNode.Parse(@"{ ""type"": ""object"", ""properties"": {} }")!.AsObject()));

        var systemPrompt = $@"Eres el asistente virtual corporativo del negocio.
FECHA ACTUAL: {DateTime.Now:yyyy-MM-dd}
SEDES: {locationsInfo}
SEDE PREFERIDA DEL CLIENTE: {(string.IsNullOrWhiteSpace(selectedLocationId) ? "No definida" : selectedLocationId)}

REGLAS ESTRICTAS:
{string.Join("\n", rules)}";

        return (systemPrompt, tools);
    }

    public async Task<string> ExecuteToolAsync(AiToolCall toolCall, HashSet<string> activeModules, Guid workspaceId, string phone, string conversationId, CacheContextDto context, CancellationToken ct)
    {
        var args = toolCall.Arguments ?? new JsonObject();
        var toolName = toolCall.Name ?? "unknown";

        try
        {
            return toolName switch
            {
                "check_availability" when activeModules.Contains("RESERVATIONS") => await CheckAvailabilityAsync(workspaceId, args, context, ct),
                "create_reservation" when activeModules.Contains("RESERVATIONS") => await CreateReservationAsync(workspaceId, phone, args, ct),
                "cancel_active_reservation" when activeModules.Contains("RESERVATIONS") => await CancelReservationAsync(workspaceId, phone, ct),
                "search_catalog" when activeModules.Contains("CATALOG") || activeModules.Contains("SERVICES") => await SearchCatalogAsync(workspaceId, args, ct),
                "create_request" when activeModules.Contains("REQUESTS") => await CreateRequestAsync(workspaceId, phone, args, ct),
                "check_request_status" when activeModules.Contains("REQUESTS") => await CheckRequestStatusAsync(workspaceId, phone, ct),
                "get_business_hours" when activeModules.Contains("BUSINESS_HOURS") => JsonSerializer.Serialize(await _hoursRepo.GetBusinessHoursAsync(workspaceId, GetStringArg(args, "locationId", context.SelectedLocationId ?? ""), ct)),
                "get_faqs" when activeModules.Contains("FAQ") => JsonSerializer.Serialize(await _faqRepo.GetFaqsAsync(workspaceId, ct)),
                "get_profile" when activeModules.Contains("BUSINESS_PROFILE") => JsonSerializer.Serialize(await _profileRepo.GetProfileAsync(workspaceId, ct)),
                "request_human" => await RequestHumanAsync(workspaceId, conversationId, ct),
                _ => "La funcionalidad solicitada no está disponible o no se encuentra licenciada."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ejecutando Tool {ToolName}", toolName);
            return "Ocurrió un error técnico interno al procesar la consulta de datos.";
        }
    }

    private async Task<string> CheckAvailabilityAsync(Guid workspaceId, JsonObject args, CacheContextDto context, CancellationToken ct)
    {
        var dateStr = GetStringArg(args, "date");
        var locId = GetStringArg(args, "locationId");
        if (DateTime.TryParse(dateStr, out var date))
        {
            context.SelectedLocationId = locId;
            var slots = await _reservationEngine.GetAvailabilityAsync(workspaceId, locId, GetStringArg(args, "serviceId"), date, ct);
            return slots.Any() ? $"Horarios libres: {string.Join(", ", slots.Select(s => s.StartTime.ToString("HH:mm")))}" : "No hay horarios libres para la fecha solicitada.";
        }
        return "Formato de fecha inválido. Usa YYYY-MM-DD.";
    }

    private async Task<string> CreateReservationAsync(Guid workspaceId, string phone, JsonObject args, CancellationToken ct)
    {
        var rawDateTime = $"{GetStringArg(args, "date")} {GetStringArg(args, "time")}".Trim();
        if (DateTime.TryParse(rawDateTime, out var exactDateTime))
        {
            var result = await _reservationEngine.CreateReservationAsync(workspaceId, GetStringArg(args, "locationId"), GetStringArg(args, "serviceId"), phone, GetStringArg(args, "customerName", "Cliente"), exactDateTime, ct);
            return result.IsSuccess ? "Reserva confirmada exitosamente." : $"Fallo: {result.Error.Description}";
        }
        return "Fecha u hora inválida.";
    }

    private async Task<string> CancelReservationAsync(Guid workspaceId, string phone, CancellationToken ct)
    {
        var result = await _reservationEngine.CancelActiveReservationAsync(workspaceId, phone, ct);
        return result.IsSuccess ? "Reserva cancelada satisfactoriamente." : "No tienes reservas activas.";
    }

    private async Task<string> SearchCatalogAsync(Guid workspaceId, JsonObject args, CancellationToken ct)
    {
        var query = GetStringArg(args, "query");
        var type = GetStringArg(args, "type", "ALL").ToUpperInvariant();
        var items = await _catalogRepo.GetActiveItemsAsync(workspaceId, ct);

        if (type is "PRODUCT" or "SERVICE") items = items.Where(i => string.Equals(i.Type, type, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(query)) items = items.Where(i => i.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || (i.Description != null && i.Description.Contains(query, StringComparison.OrdinalIgnoreCase)));

        // 🔥 CORRECCIÓN 2: Exponer la duración, descripción y precio formateado a la IA
        var results = items.Take(10).Select(i => new {
            i.Id,
            i.Name,
            i.Description,
            i.Type,
            PrecioSoles = i.PriceMinorUnits / 100.0m, // Formato humano para evitar errores
            DuracionMinutos = i.DurationInMinutes,
            i.LocationScope,
            i.LocationIds
        }).ToList();

        return results.Any() ? JsonSerializer.Serialize(results) : "No hay coincidencias en el catálogo.";
    }

    private async Task<string> CreateRequestAsync(Guid workspaceId, string phone, JsonObject args, CancellationToken ct)
    {
        var req = new RequestRecord { Id = Guid.NewGuid().ToString(), ConsumerPhone = phone, Title = GetStringArg(args, "title", "Solicitud"), Description = GetStringArg(args, "description", "-"), Status = RequestStatus.Pending, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        await _requestRepo.CreateRequestAsync(workspaceId, req, ct);
        return $"Solicitud registrada con ID {req.Id}.";
    }

    private async Task<string> CheckRequestStatusAsync(Guid workspaceId, string phone, CancellationToken ct)
    {
        var req = await _requestRepo.GetLatestRequestByPhoneAsync(workspaceId, phone, ct);
        return req != null ? $"Trámite '{req.Title}' estado: {req.Status}." : "No hay trámites previos.";
    }

    private async Task<string> RequestHumanAsync(Guid workspaceId, string conversationId, CancellationToken ct)
    {
        await _conversationRepo.UpdateConversationModeAsync(workspaceId, conversationId, ConversationMode.Human, HandoffReason.AiEscalation, ct);
        return "Transferido a humano exitosamente.";
    }

    private static string GetStringArg(JsonObject args, string key, string defaultValue = "")
    {
        var cleanKey = key.Replace("_", string.Empty);
        foreach (var property in args)
        {
            if (string.Equals(property.Key.Replace("_", string.Empty), cleanKey, StringComparison.OrdinalIgnoreCase) && property.Value != null)
                return property.Value.ToString().Trim('\"');
        }
        return defaultValue;
    }
}