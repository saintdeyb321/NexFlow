using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Engines.Dispatcher;
using NexFlow.Application.Engines.Intent.AI;
using NexFlow.Domain.Enums;
using System.Text.RegularExpressions;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services;

public interface IReservationParameterResolver
{
    Task<IntentResultDto?> ResolveAsync(Guid workspaceId, string userMessage, ConversationContextDto context, CancellationToken ct);
}

public class ReservationParameterResolver : IReservationParameterResolver
{
    private readonly IContextResolver _contextResolver;

    public ReservationParameterResolver(IContextResolver contextResolver)
    {
        _contextResolver = contextResolver;
    }

    public async Task<IntentResultDto?> ResolveAsync(Guid workspaceId, string userMessage, ConversationContextDto context, CancellationToken ct)
    {
        var msg = userMessage.Trim().ToLowerInvariant();

        // Comandos de escape globales
        if (msg == "cancelar" || msg == "salir" || msg == "detener")
            return new IntentResultDto(IntentType.CancelReservation, 1.0, new Dictionary<string, string>());

        var currentIntent = Enum.TryParse<IntentType>(context.CurrentIntent, true, out var parsed) ? parsed : IntentType.Unknown;
        if (currentIntent == IntentType.Unknown) return null;

        var state = Enum.TryParse<ReservationConversationState>(context.PendingAction, true, out var parsedState)
            ? parsedState
            : ReservationConversationState.Idle;

        var parameters = new Dictionary<string, string>();

        switch (state)
        {
            case ReservationConversationState.SelectingLocation:
                var locId = await _contextResolver.GroundLocationAsync(workspaceId, userMessage, ct);
                if (locId != null) parameters["locationId"] = locId;
                break;
            case ReservationConversationState.SelectingService:
                var srvId = await _contextResolver.GroundServiceAsync(workspaceId, userMessage, context.SelectedLocationId, ct);
                if (srvId != null) parameters["serviceId"] = srvId;
                break;
            case ReservationConversationState.SelectingDate:
                var date = await _contextResolver.GroundDateAsync(workspaceId, userMessage, ct);
                if (date != null) parameters["date"] = date;
                break;
            case ReservationConversationState.SelectingTime:
                var time = ExtractTimeFast(userMessage);
                if (time != null) parameters["time"] = time;
                break;
            case ReservationConversationState.Confirming:
                if (msg.Length > 2) parameters["name"] = userMessage.Trim(); // El nombre de quien reserva
                break;
        }

        if (parameters.Any()) return new IntentResultDto(currentIntent, 1.0, parameters);
        return null;
    }

    private string? ExtractTimeFast(string text)
    {
        var match = Regex.Match(text.ToLower(), @"(\d{1,2})(?::(\d{2}))?\s*(am|pm|de la mañana|de la tarde|de la noche)?");
        if (match.Success)
        {
            int hour = int.Parse(match.Groups[1].Value);
            int min = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0;
            string period = match.Groups[3].Value;
            if (period.Contains("pm") || period.Contains("tarde") || period.Contains("noche")) { if (hour < 12) hour += 12; }
            else if (period.Contains("am") || period.Contains("mañana")) { if (hour == 12) hour = 0; }
            return $"{hour:D2}:{min:D2}";
        }
        return null;
    }
}