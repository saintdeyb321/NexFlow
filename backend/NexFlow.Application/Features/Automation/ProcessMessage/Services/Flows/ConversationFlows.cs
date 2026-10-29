using System.Globalization;
using NexFlow.Application.Features.Knowledge;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Application.Features.Business.Locations;
using NexFlow.Application.Features.Business.Offerings;
using NexFlow.Application.Features.Requests;
using NexFlow.Application.Features.Reservations;
using NexFlow.Domain.Entities.Catalog;
using NexFlow.Domain.Enums;
using NexFlow.Domain.Exceptions;
using NexFlow.Application.Features.Services.DTOs;
using NexFlow.Application.Features.Shared.DTOs;
using NexFlow.Application.Features.Business;
using NexFlow.Application.Features.Notifications;
using NexFlow.Application.Features.Automation.Dialogue.Policy;
using NexFlow.Application.Features.Automation.Dialogue.Understanding;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Automation.Dialogue.Composition;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services.Flows;

public interface IBookingFlow { Task<string?> TryResumeAsync(Guid workspaceId, string phone, string sourceMessageId, ConversationContextDto context, CancellationToken ct); Task<string> ProcessAsync(Guid workspaceId, string phone, string conversationId, ConversationContextDto context, AiInterpretation interpretation, string fallbackName, string sourceMessageId, CancellationToken ct); Task<ResponsePlan> ProcessPlannedAsync(Guid workspaceId, string phone, string conversationId, ConversationContextDto context, AiInterpretation interpretation, string fallbackName, string sourceMessageId, DialogueAct act, CancellationToken ct); Task<string> GetContinuationAsync(Guid workspaceId, ConversationContextDto context, CancellationToken ct, bool concise = false); }
public interface IRequestFlow { Task<string> ProcessAsync(Guid workspaceId, string phone, string messageText, string conversationId, string sourceMessageId, CancellationToken ct); }
public interface ISupportFlow { Task<string> ProcessAsync(Guid workspaceId, string conversationId, CancellationToken ct); }
public record ChatResponse(string Text, string? MediaUrl = null, string? FileName = null, string? CatalogScope = null)
{
    public VerifiedResponseLayout? Realization { get; init; }
    public IReadOnlyList<VerifiedFact> Facts { get; init; } = [];
    public IReadOnlyList<ConversationMenuOption>? Choices { get; init; }
    public KnowledgeResult? Information { get; init; }
    public OperationStatus Operation { get; init; } = OperationStatus.Informational;
}
public interface IChatFlow { Task<ChatResponse> ProcessAsync(Guid workspaceId, string text, AiInterpretation interpretation, string? factualLocationId, CancellationToken ct); }

public class BookingFlow : IBookingFlow
{
    // Evidence belongs to this invocation only; the returned plan takes defensive copies.
    private sealed class TurnEvidence
    {
        public List<VerifiedFact> Facts { get; } = [];
        public IReadOnlyList<ConversationMenuOption> Choices { get; set; } = [];
        public string? Question { get; set; }
        public string? Currency { get; set; }
        public string? Zone { get; set; }
        public OperationStatus Operation { get; set; } = OperationStatus.Collecting;
        public void Set(IEnumerable<VerifiedFact> facts, string? question, IReadOnlyList<ConversationMenuOption> choices)
        { Facts.AddRange(facts); Question = question; Choices = choices; }
    }
    private readonly IEntitlementService _entitlementService;
    private readonly IOfferingService _offeringService;
    private readonly ILocationResolverService _locationResolver;
    private readonly IReservationEngine _reservationEngine;
    private readonly IReservationRepository _reservationRepository;
    private readonly ILocationRepository _locationRepo;
    private readonly IBusinessHoursRepository _hoursRepo;
    private readonly IBusinessProfileRepository _profileRepo;
    private readonly IKnowledgeService _knowledgeService;
    private readonly IContextRecoveryService _contextStore;

    public BookingFlow(
        IOfferingService offeringService, ILocationResolverService locationResolver,
        IReservationEngine reservationEngine, ILocationRepository locationRepo,
        IBusinessHoursRepository hoursRepo, IEntitlementService entitlementService,
        IContextRecoveryService contextStore, IReservationRepository reservationRepository,
        IBusinessProfileRepository profileRepo, IKnowledgeService knowledgeService)
    {
        _offeringService = offeringService; _locationResolver = locationResolver;
        _reservationEngine = reservationEngine; _locationRepo = locationRepo;
        _hoursRepo = hoursRepo; _profileRepo = profileRepo; _knowledgeService = knowledgeService;
        _contextStore = contextStore; _reservationRepository = reservationRepository;
        _entitlementService = entitlementService;
    }

    public async Task<string?> TryResumeAsync(Guid workspaceId, string phone, string sourceMessageId, ConversationContextDto context, CancellationToken ct)
    {
        var existing = await _reservationRepository.GetBySourceMessageIdAsync(workspaceId, sourceMessageId, ct);
        if (existing == null) return null;
        if (existing.CustomerIdentifier != phone) throw new InvalidOperationException("Reservation inbound recipient mismatch.");
        if (context.CurrentGoal is "BOOKING" or "RESERVATION" && context.ReservationCommitSourceMessageId == sourceMessageId)
        {
            ClearDraft(context);
            await _contextStore.SaveContextAsync(workspaceId, phone, context, ct);
        }
        return $"✅ Tu reserva ya está registrada con el código {existing.Id}.";
    }

    private static void ClearDraft(ConversationContextDto context)
    {
        context.CurrentGoal = null; context.CurrentStep = null;
        context.SelectedLocationId = null; context.SelectedServiceId = null;
        context.ReservationServiceOffset = 0;
        context.ReservationCommitSourceMessageId = null;
        context.TargetDate = null; context.TargetTime = null;
        context.MissingFields.Clear(); context.LastQuestion = null;
        context.PendingCustomerName = null; context.PendingServiceIds.Clear(); context.PendingDialogueSlots.Clear(); context.PreferredTime = null;
    }

    private static void UpdateStep(ConversationContextDto context)
    {
        context.MissingFields.Clear();
        if (string.IsNullOrWhiteSpace(context.RealCustomerName)) context.MissingFields.Add("CustomerName");
        if (string.IsNullOrWhiteSpace(context.SelectedLocationId)) context.MissingFields.Add("Location");
        if (string.IsNullOrWhiteSpace(context.SelectedServiceId)) context.MissingFields.Add("Service");
        if (string.IsNullOrWhiteSpace(context.TargetDate)) context.MissingFields.Add("Date");
        if (string.IsNullOrWhiteSpace(context.TargetTime)) context.MissingFields.Add("Time");
        context.CurrentStep = DialoguePolicy.NextBookingStep(context);
    }

    public async Task<string> ProcessAsync(Guid workspaceId, string phone, string conversationId, ConversationContextDto context, AiInterpretation interpretation, string fallbackName, string sourceMessageId, CancellationToken ct)
        => (await ProcessPlannedAsync(workspaceId, phone, conversationId, context, interpretation, fallbackName, sourceMessageId, DialogueAct.RequestedData, ct)).FallbackText;

    public async Task<ResponsePlan> ProcessPlannedAsync(Guid workspaceId, string phone, string conversationId, ConversationContextDto context, AiInterpretation interpretation, string fallbackName, string sourceMessageId, DialogueAct act, CancellationToken ct)
    {
        var evidence = new TurnEvidence();
        ResponsePlan Plan(string text, bool creative = false) => new(workspaceId, sourceMessageId, context.StateVersion,
            ConversationIntent.Reservation, act, evidence.Operation, evidence.Facts, evidence.Choices, context.MissingFields,
            evidence.Zone, evidence.Currency, evidence.Question is { } question && question.LastIndexOf('¿') >= 0 ? question[question.LastIndexOf('¿')..] : evidence.Question, text, creative);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceMessageId);
        var replay = await TryResumeAsync(workspaceId, phone, sourceMessageId, context, ct);
        if (replay != null) { evidence.Operation = OperationStatus.Succeeded; return Plan(replay); }
        if (context.RecoveryLastMessageId == sourceMessageId && context.RecoveryLastResponse != null) return Plan(context.RecoveryLastResponse);
        var previousService = context.SelectedServiceId;
        var proposal = interpretation.BookingProposal;
        if (proposal != null)
        {
            var modules = (await _entitlementService.GetAvailableModuleCodesAsync(workspaceId, ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var denied = AiIntentAccess.GetDeniedResponse(ConversationIntent.Reservation, modules);
            if (denied != null) { evidence.Operation = OperationStatus.Denied; return Plan(denied); }
            if (proposal.ReadOnly)
            {
                if (proposal.WorkspaceId != workspaceId || proposal.SourceMessageId != sourceMessageId || proposal.ExpectedStateVersion != context.StateVersion
                    || proposal.Understanding.Context.WorkspaceId != workspaceId || proposal.Understanding.Context.SourceMessageId != sourceMessageId)
                    throw new ConcurrencyException("Availability question no longer matches this turn.");
                evidence.Operation = OperationStatus.Informational;
                var answer = await AnswerAvailabilityAsync(workspaceId, context, proposal.Understanding, evidence, ct);
                context.Information = new() { Topic = "Availability", Clarification = "Availability",
                    DraftFingerprint = InformationalRequest.DraftFingerprint(context), UpdatedAt = proposal.Understanding.Context.LocalBusinessNow.UtcDateTime };
                if (evidence.Facts.Any(f => f.Key == "Availability" && f.Value == "Available")
                    && DateTime.TryParseExact(evidence.Facts.FirstOrDefault(f => f.Key == "RequestedLocalTime")?.Value,
                        "dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var availableAt))
                    context.Information.AvailabilityReference = new(workspaceId,
                        proposal.Understanding.Location.TryGetValue(out var site) ? site.Id : context.SelectedLocationId!,
                        proposal.Understanding.Service.TryGetValue(out var service) ? service.Id : context.SelectedServiceId!,
                        availableAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), availableAt.ToString("HH:mm", CultureInfo.InvariantCulture),
                        proposal.Understanding.Context.LocalBusinessNow.UtcDateTime);
                await _contextStore.SaveContextAsync(workspaceId, phone, context, ct);
                return Plan(answer, creative: true);
            }
            await BookingDraftMerger.ApplyAsync(workspaceId, sourceMessageId, context, proposal, _offeringService, ct);
            if (context.Information?.Topic == "Availability") context.Information = null;
            interpretation = new() { Intent = ConversationIntent.Reservation, Confirmed = interpretation.Confirmed };
        }
        var response = await ProcessTurnAsync(workspaceId, phone, context, interpretation, sourceMessageId, evidence, ct);
        if (proposal != null && evidence.Operation == OperationStatus.Collecting)
        {
            var resolved = proposal.Understanding;
            evidence.Zone = resolved.Context.TimeZoneId;
            if (resolved.Service.TryGetValue(out var selected) && (selected.Id == context.SelectedServiceId || context.PendingServiceIds.Contains(selected.Id)))
                evidence.Facts.Add(new("Service", selected.Name, VerifiedFactSource.BusinessRepository, selected.Id));
            if (resolved.Location.TryGetValue(out var location) && location.Id == context.SelectedLocationId)
                evidence.Facts.Add(new("Location", location.Name, VerifiedFactSource.BusinessRepository, location.Id));
            if (resolved.Date.TryGetValue(out var date) && date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) == context.TargetDate)
                evidence.Facts.Add(new("RequestedDate", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), VerifiedFactSource.ValidatedConversation));
        }
        if (proposal != null && evidence.Operation == OperationStatus.Collecting
            && act is DialogueAct.StartOperation or DialogueAct.MultipleData or DialogueAct.Correction
            && context.CurrentStep is "COLLECT_LOCATION" or "COLLECT_SERVICE")
        {
            var serviceFact = proposal.Understanding.Service.TryGetValue(out var requestedService)
                ? evidence.Facts.FirstOrDefault(f => f.Key == "Service" && f.EntityId == requestedService.Id) : null;
            var dateFact = evidence.Facts.FirstOrDefault(f => f.Key == "RequestedDate");
            if (serviceFact != null)
            {
                var dateText = DateOnly.TryParseExact(dateFact?.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var knownDate)
                    ? " para el " + knownDate.ToString("dddd d 'de' MMMM 'de' yyyy", CultureInfo.GetCultureInfo("es-PE")) : "";
                response = $"Te ayudo con {serviceFact.Value}{dateText}.\n\n" + response;
            }
        }
        var unresolved = proposal?.UnresolvedSlots.FirstOrDefault();
        if (proposal?.Clarification != null && unresolved != null
            && context.CurrentStep == "COLLECT_" + (unresolved == "Name" ? "CUSTOMERNAME" : unresolved.ToUpperInvariant()))
            response = proposal.Clarification;
        if (act == DialogueAct.Correction && evidence.Operation == OperationStatus.Collecting && previousService != null
            && proposal?.Understanding.Service.TryGetValue(out var replacement) == true
            && context.SelectedServiceId == replacement.Id && previousService != replacement.Id)
        {
            evidence.Facts.Add(new("Service", replacement.Name, VerifiedFactSource.BusinessRepository, replacement.Id));
            response = "✏️ *Borrador actualizado*\n\nServicio: " + replacement.Name + "\n\n" + response;
        }
        if (proposal != null)
        {
            context.RecoveryLastMessageId = sourceMessageId; context.RecoveryLastResponse = response; context.RecoveryLastInterpretation = null;
            context.LastQuestion = response;
        }
        if (proposal?.Understanding.SupplementalInformation == ConversationIntent.BusinessHours)
        {
            var modules = (await _entitlementService.GetAvailableModuleCodesAsync(workspaceId, ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var answer = AiIntentAccess.GetDeniedResponse(ConversationIntent.BusinessHours, modules);
            if (answer == null && context.SelectedLocationId is { } selectedSite)
            {
                var hours = (await _hoursRepo.GetBusinessHoursAsync(workspaceId, selectedSite, ct)).ToArray();
                var siteName = proposal.Understanding.AuthorizedLocations.SingleOrDefault(l => l.Id == selectedSite)?.Name ?? "la sede seleccionada";
                if (proposal.Understanding.SupplementalDate is { } informationDate)
                {
                    var day = hours.SingleOrDefault(h => h.DayOfWeek == (int)informationDate.DayOfWeek);
                    answer = day is { IsClosed: true } ? $"El {informationDate:dd/MM/yyyy}, {siteName} está cerrada."
                        : BusinessHoursFormatter.IsOpen(day) ? $"El {informationDate:dd/MM/yyyy}, {siteName} atiende de {day!.OpenTime} a {day.CloseTime}. El horario de atención no garantiza una cita disponible."
                        : $"No tengo un horario de atención registrado para el {informationDate:dd/MM/yyyy} en {siteName}.";
                }
                else answer = $"Horarios de atención registrados para {siteName}:\n" + BusinessHoursFormatter.Format(hours);
            }
            response = (answer ?? "Para consultar la atención de hoy necesito saber la sede.") + "\n\n" + response;
            context.RecoveryLastResponse = response;
        }
        await _contextStore.SaveContextAsync(workspaceId, phone, context, ct);
        return Plan(response, evidence.Operation == OperationStatus.Collecting);
    }

    private async Task<string> ProcessTurnAsync(Guid workspaceId, string phone, ConversationContextDto context, AiInterpretation interpretation, string sourceMessageId, TurnEvidence evidence, CancellationToken ct)
    {
        var modules = (await _entitlementService.GetAvailableModuleCodesAsync(workspaceId, ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!modules.Contains("SERVICES") || !modules.Contains("RESERVATIONS")) { evidence.Operation = OperationStatus.Denied; return AiIntentAccess.GetDeniedResponse(ConversationIntent.Reservation, modules)!; }

        // Informational turns are handled before applying extracted entities.
        // Neither a question's location nor its date/time can overwrite the draft.
        if (context.CurrentGoal is "BOOKING" or "RESERVATION")
        {
            if (interpretation.Directive == ConversationDirective.ExplainServiceScope)
            {
                context.Information = null;
                evidence.Operation = OperationStatus.Informational;
                return await ExplainServiceScopeAsync(workspaceId, context, ct);
            }
            if (interpretation.Directive == ConversationDirective.CancelCurrentFlow)
            {
                evidence.Operation = OperationStatus.Cancelled;
                ClearDraft(context);
                return "He cancelado el borrador de tu reserva. ¿En qué más puedo ayudarte?";
            }
            var editSlot = interpretation.Directive switch {
                ConversationDirective.ChangeLocation when string.IsNullOrWhiteSpace(interpretation.Location) => "Location",
                ConversationDirective.ChangeService when string.IsNullOrWhiteSpace(interpretation.Service) => "Service",
                ConversationDirective.ChangeDate when string.IsNullOrWhiteSpace(interpretation.Date) => "Date",
                ConversationDirective.ChangeTime when string.IsNullOrWhiteSpace(interpretation.Time) => "Time",
                ConversationDirective.ChangeCustomerName when string.IsNullOrWhiteSpace(interpretation.CustomerName) => "Name",
                _ => null
            };
            if (editSlot != null)
            {
                if (!context.PendingDialogueSlots.Contains(editSlot)) context.PendingDialogueSlots.Add(editSlot);
                context.ReservationServiceOffset = 0;
                UpdateStep(context);
                return "Conservo los demás datos de tu reserva; comprobaré que sigan siendo válidos con el cambio.\n\n"
                    + await PromptAsync(workspaceId, context, false, ct, evidence);
            }
            if (interpretation.Intent == ConversationIntent.ServiceQuery && interpretation.QueryKind == OfferingQueryKind.Broad)
            {
                var all = (await _offeringService.GetServicesAsync(workspaceId, null, null, ct)).Where(s => s.IsActive).ToArray();
                var query = CommerceTurnResolver.Page(workspaceId, sourceMessageId, context, "SERVICE", all.Select(VerifiedOffering.Capture).ToArray(), null, 0);
                if (query.Offerings.Count > 0)
                    new OfferingReference(workspaceId, "SERVICE", query.Offerings.Select(s => s.Id).ToArray(), null,
                        true, query.PageOffset, query.TotalCount, query.PageFingerprint).Remember(context);
                evidence.Operation = OperationStatus.Informational;
                return OfferingResponseFormatter.Format(query) + "\n\n" + await ExplainServiceScopeAsync(workspaceId, context, ct);
            }
            if (interpretation.Directive == ConversationDirective.ShowOptions)
            {
                if (context.CurrentStep == "COLLECT_TIME") return await AvailabilityOptionsAsync(workspaceId, context, false, ct, evidence);
                if (string.IsNullOrWhiteSpace(context.SelectedLocationId))
                    return await LocationOptionsAsync(workspaceId, "¿En cuál sede deseas consultar los servicios reservables?", ct);
                var services = await ReservableServicesAsync(workspaceId, context.SelectedLocationId, ct);
                var nextOffset = context.ReservationServiceOffset + 5;
                context.ReservationServiceOffset = services.Count > nextOffset ? nextOffset : 0;
                var options = OfferingServiceReservationExtensions.FormatReservationOptions(services, context.ReservationServiceOffset);
                if (context.CurrentStep == "COLLECT_SERVICE") return options;
                // Keep the current step and every collected entity.
                return RemoveFinalQuestion(options) + "\n\n" + await GetContinuationAsync(workspaceId, context, ct);
            }
            else if (interpretation.Intent is ConversationIntent.BusinessHours or ConversationIntent.Location or ConversationIntent.Faq)
            {
                var denied = AiIntentAccess.GetDeniedResponse(interpretation.Intent, modules);
                if (denied != null) return denied;
                string answer;
                if (interpretation.Intent == ConversationIntent.BusinessHours)
                {
                    if (string.IsNullOrWhiteSpace(context.SelectedLocationId))
                    {
                        var locations = await _locationRepo.GetLocationsAsync(workspaceId, ct);
                        answer = "Los horarios dependen de la sede. Primero debes elegir una de estas sedes:\n" +
                            string.Join("\n", locations.Select(l => $"• {l.Name}"));
                    }
                    else answer = await WeeklyHoursAsync(workspaceId, context.SelectedLocationId, ct);
                }
                else
                {
                    var factualLocationId = context.SelectedLocationId;
                    if (interpretation.Intent == ConversationIntent.Location && !string.IsNullOrWhiteSpace(interpretation.Location))
                        factualLocationId = await _locationResolver.ResolveLocationIdAsync(workspaceId, interpretation.Location, ct);
                    var knowledgeResult = await _knowledgeService.QueryAsync(workspaceId, new BusinessKnowledgeSnapshot { WorkspaceId = workspaceId },
                        new KnowledgeQuery { Topic = interpretation.Intent == ConversationIntent.Location ? KnowledgeTopic.Locations : KnowledgeTopic.Faqs,
                            LocationId = interpretation.Intent == ConversationIntent.Location ? factualLocationId : null,
                            SearchTerm = interpretation.SearchTerm }, ct);
                    answer = knowledgeResult.Status == KnowledgeStatus.NotFound ? "No tengo esa información registrada." : knowledgeResult.ToResponse();
                }
                return answer + "\n\nPara continuar con tu reserva:\n" + await GetContinuationAsync(workspaceId, context, ct);
            }
        }

        context.CurrentGoal = "RESERVATION";
        context.LastIntent = interpretation.Intent.ToString();
        if (!string.IsNullOrWhiteSpace(interpretation.CustomerName))
        { context.RealCustomerName = interpretation.CustomerName; context.PendingCustomerName = null; context.PendingDialogueSlots.Remove("Name"); }
        if (!string.IsNullOrWhiteSpace(interpretation.Location))
        {
            var locationId = await _locationResolver.ResolveLocationIdAsync(workspaceId, interpretation.Location, ct);
            if (locationId == null)
            {
                // An unrecognized answer does not discard an existing valid selection.
                UpdateStep(context);
                return "No pude identificar esa sede.\n\n" + await LocationOptionsAsync(workspaceId, "¿Cuál sede deseas elegir?", ct);
            }
            if (context.SelectedLocationId != locationId)
            {
                context.SelectedLocationId = locationId;
                context.ReservationServiceOffset = 0;
            }
            context.PendingDialogueSlots.Remove("Location");
        }
        if (!string.IsNullOrWhiteSpace(interpretation.Date) && context.TargetDate != interpretation.Date)
        {
            context.TargetDate = interpretation.Date;
        }
        if (!string.IsNullOrWhiteSpace(interpretation.Date)) context.PendingDialogueSlots.Remove("Date");
        if (!string.IsNullOrWhiteSpace(interpretation.Time))
        {
            context.TargetTime = interpretation.Time; context.PreferredTime = null; context.PendingDialogueSlots.Remove("Time");
            if (context.Information?.Topic == "Availability") context.Information = null;
        }

        IReadOnlyList<LocationDto> authorizedLocations = [];
        if (!string.IsNullOrWhiteSpace(context.SelectedLocationId))
        {
            authorizedLocations = (await _locationRepo.GetLocationsAsync(workspaceId, ct)).ToArray();
            if (!authorizedLocations.Any(l => l.Id == context.SelectedLocationId))
            {
                context.SelectedLocationId = null; context.SelectedServiceId = null; context.TargetTime = null;
                context.PreferredTime = null; context.PendingServiceIds.Clear();
            }
            else if (!string.IsNullOrWhiteSpace(interpretation.Service))
            {
                var resolution = await _offeringService.ResolveReservationServiceAsync(workspaceId, context.SelectedLocationId, interpretation.Service, ct);
                if (resolution.Service == null)
                {
                    UpdateStep(context);
                    return resolution.Clarification!;
                }
                context.SelectedServiceId = resolution.Service.Id;
                context.PendingServiceIds.Clear(); context.PendingDialogueSlots.Remove("Service");
            }
            else if (!string.IsNullOrWhiteSpace(context.SelectedServiceId) && !await IsSelectedServiceValidAsync(workspaceId, context, ct))
            {
                context.SelectedServiceId = null;
                UpdateStep(context);
                return "El servicio seleccionado ya no está disponible para reservar en esta sede.\n\n" +
                    await ServiceOptionsAsync(workspaceId, context.SelectedLocationId, ct);
            }
        }
        if (context.SelectedServiceId == null) await BookingDraftMerger.ResolvePendingServiceAsync(workspaceId, context, _offeringService, ct);
        UpdateStep(context);
        if (context.CurrentStep is "COLLECT_CUSTOMERNAME" or "CONFIRM")
        {
            if (!DateTime.TryParseExact(context.TargetDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var requestedDate)
                || !TimeOnly.TryParseExact(context.TargetTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                context.TargetTime = null;
                if (requestedDate == default) context.TargetDate = null;
                UpdateStep(context); return await PromptAsync(workspaceId, context, false, ct, evidence);
            }
            var profile = await _profileRepo.GetProfileAsync(workspaceId, ct);
            var zone = BookingZone(profile?.TimeZone);
            evidence.Zone = zone.Id;
            var requestedStart = DateTime.SpecifyKind(DateTime.ParseExact($"{context.TargetDate} {context.TargetTime}", "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), DateTimeKind.Unspecified);
            var available = await _reservationEngine.GetAvailabilityAtAsync(workspaceId, context.SelectedLocationId!, context.SelectedServiceId!, requestedStart, ct);
            if (!available.Any(s => s.IsAvailable && TimeZoneInfo.ConvertTimeFromUtc(s.StartTime, zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                == $"{context.TargetDate} {context.TargetTime}"))
            {
                var requestedTime = context.TargetTime;
                context.TargetTime = null; UpdateStep(context);
                return "Ese horario no está disponible.\n\n" + await AvailabilityOptionsAsync(workspaceId, context, false, ct, evidence, anchor: TimeOnly.ParseExact(requestedTime, "HH:mm", CultureInfo.InvariantCulture));
            }
        }
        if (context.CurrentStep != "CONFIRM") return await PromptAsync(workspaceId, context, false, ct, evidence);

        var service = await _offeringService.GetServiceByIdAsync(workspaceId, context.SelectedServiceId!, ct);
        if (service == null || !await IsSelectedServiceValidAsync(workspaceId, context, ct))
        {
            context.SelectedServiceId = null; context.TargetTime = null;
            UpdateStep(context);
            return "El servicio ya no está disponible en esta sede.\n\n" + await ServiceOptionsAsync(workspaceId, context.SelectedLocationId!, ct);
        }
        if (!DateTime.TryParseExact($"{context.TargetDate} {context.TargetTime}", "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var selectedTime))
        {
            context.TargetTime = null;
            if (!DateTime.TryParseExact(context.TargetDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                context.TargetDate = null;
            UpdateStep(context);
            return "No pude interpretar la fecha u hora indicada.\n\n" + await PromptAsync(workspaceId, context, false, ct, evidence);
        }
        evidence.Currency = service.Currency;
        evidence.Facts.Add(new("Service", service.Name, VerifiedFactSource.BusinessRepository, service.Id));
        evidence.Facts.Add(new("Currency", service.Currency, VerifiedFactSource.BusinessRepository, service.Id));
        evidence.Facts.Add(new("RequestedLocalTime", selectedTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), VerifiedFactSource.ValidatedConversation));
        if (!await IsCustomerNameValidAsync(workspaceId, context.RealCustomerName, authorizedLocations, service, modules, ct))
        {
            context.RealCustomerName = null; UpdateStep(context);
            return "Necesito tu nombre y apellido para el resumen. ¿Cómo te llamas?";
        }
        evidence.Facts.Add(new("CustomerName", context.RealCustomerName!, VerifiedFactSource.ValidatedConversation, Sensitive: true));
        if (!interpretation.Confirmed)
        {
            var location = authorizedLocations.Single(l => l.Id == context.SelectedLocationId);
            evidence.Operation = OperationStatus.AwaitingConfirmation;
            evidence.Set(
                [new("Service", service.Name, VerifiedFactSource.BusinessRepository, service.Id),
                 new("Location", location.Name, VerifiedFactSource.BusinessRepository, location.Id),
                 new("PriceMinorUnits", service.PriceMinorUnits.ToString(CultureInfo.InvariantCulture), VerifiedFactSource.BusinessRepository, service.Id)],
                "¿Confirmas el resumen vigente?", []);
            return $"📋 *Reserva por confirmar*\n\nSede: {location.Name}\nServicio: {service.Name}\nFecha: {selectedTime:dd/MM/yyyy}\nHora: {selectedTime:HH:mm}\nPaciente: {context.RealCustomerName}\nPrecio: {OfferingResponseFormatter.Money(service.Currency, service.PriceMinorUnits)}"
                + "\n\n¿Confirmas estos datos? Puedes responder «Confirmar» o «Sí» para registrar la cita.\nPara cambiar un dato, dime cuál o escribe «Editar». Para abandonar el borrador, escribe «Descartar».";
        }
        context.ReservationCommitSourceMessageId = sourceMessageId;
        await _contextStore.SaveContextAsync(workspaceId, phone, context, ct);
        var result = await _reservationEngine.CreateReservationAsync(workspaceId, context.SelectedLocationId!, service.Id,
            phone, context.RealCustomerName!, DateTime.SpecifyKind(selectedTime, DateTimeKind.Unspecified), ct, sourceMessageId);
        if (result.IsSuccess)
        {
            evidence.Operation = OperationStatus.Succeeded;
            evidence.Facts.Add(new("ReservationStatus", "Confirmed", VerifiedFactSource.TransactionEngine));
            var name = context.RealCustomerName;
            var location = authorizedLocations.Single(l => l.Id == context.SelectedLocationId);
            ClearDraft(context);
            return $"✅ ¡Todo listo, {name}! Tu reserva está confirmada:\n• Sede: {location.Name}\n• Servicio: {service.Name}\n• Fecha: {selectedTime:dd/MM/yyyy}\n• Hora: {selectedTime:HH:mm}\n• Referencia: {result.Value.Id}\n\n¡Te esperamos!";
        }
        context.TargetTime = null;
        if (result.Error.Code is "Reservation.Closed" or "Reservation.OutOfHours")
        {
            var reason = result.Error.Code == "Reservation.Closed" ? "Esta sede no atiende ese día." : "La hora indicada está fuera del horario de atención.";
            if (result.Error.Code == "Reservation.Closed") context.TargetDate = null;
            UpdateStep(context);
            return reason + "\n\n" + await WeeklyHoursAsync(workspaceId, context.SelectedLocationId!, ct) +
                "\n\nDime otra combinación de día y hora para verificar su disponibilidad.";
        }
        if (result.Error.Code is "Reservation.Conflict" or "Reservation.ConcurrencyConflict")
        {
            UpdateStep(context);
            return "Ese horario acaba de ser ocupado.\n\n" + await AvailabilityOptionsAsync(workspaceId, context, false, ct, evidence);
        }
        if (result.Error.Code.StartsWith("Service.", StringComparison.Ordinal))
        {
            context.SelectedServiceId = null;
            UpdateStep(context);
            return "Ese servicio ya no está disponible para reservar en esta sede.\n\n" +
                await ServiceOptionsAsync(workspaceId, context.SelectedLocationId!, ct);
        }
        if (result.Error.Code == "Location.NotFound")
        {
            context.SelectedLocationId = null; context.SelectedServiceId = null;
            UpdateStep(context);
            return "La sede ya no está disponible.\n\n" + await LocationOptionsAsync(workspaceId, "¿Cuál sede deseas elegir?", ct);
        }
        UpdateStep(context);
        return result.Error.Description + "\n\n" + await PromptAsync(workspaceId, context, false, ct, evidence);
    }

    public Task<string> GetContinuationAsync(Guid workspaceId, ConversationContextDto context, CancellationToken ct, bool concise = false)
    {
        // A catalog document/factual answer already supplied offerings. Resume
        // with one question without appending a second catalog to that answer.
        if (concise && DialoguePolicy.NextBookingStep(context) == "COLLECT_SERVICE")
            return Task.FromResult("¿Qué servicio deseas reservar en la sede seleccionada?");
        if (concise && DialoguePolicy.NextBookingStep(context) == "COLLECT_LOCATION")
            return Task.FromResult("¿En cuál sede deseas continuar con tu reserva?");
        if (concise && DialoguePolicy.NextBookingStep(context) == "CONFIRM")
            return Task.FromResult("Tu reserva sigue guardada, sin confirmar. Puedes retomar la revisión del resumen o indicar el dato que deseas cambiar.");
        return PromptAsync(workspaceId, context, true, ct);
    }

    private async Task<string> PromptAsync(Guid workspaceId, ConversationContextDto context, bool preserveDraft, CancellationToken ct, TurnEvidence? evidence = null)
    {
        if (!preserveDraft && context.CurrentStep == "COLLECT_SERVICE") context.ReservationServiceOffset = 0;
        var step = DialoguePolicy.NextBookingStep(context);
        var question = step switch
        {
            "COLLECT_CUSTOMERNAME" => SecretaryPresentation.AskName,
            "COLLECT_LOCATION" => await LocationOptionsAsync(workspaceId, "¿En cuál sede deseas atenderte?", ct, evidence),
            "COLLECT_SERVICE" when !string.IsNullOrWhiteSpace(context.SelectedLocationId)
                => await ServiceOptionsAsync(workspaceId, context.SelectedLocationId, ct, evidence, context.ReservationServiceOffset),
            "COLLECT_DATE" => SecretaryPresentation.AskDate,
            "COLLECT_TIME" => await AvailabilityOptionsAsync(workspaceId, context, preserveDraft, ct, evidence, showOptions: false),
            "CONFIRM" => $"Tu reserva para el {context.TargetDate} a las {context.TargetTime} sigue por confirmar.\n\n" + SecretaryPresentation.ConfirmReservation,
            _ => "¿Qué servicio deseas reservar?"
        };
        if (step != "COLLECT_TIME" && evidence != null) evidence.Question = question;
        if (!preserveDraft) context.LastQuestion = question;
        return question;
    }

    private async Task<string> LocationOptionsAsync(Guid workspaceId, string question, CancellationToken ct, TurnEvidence? evidence = null)
    {
        var locations = (await _locationRepo.GetLocationsAsync(workspaceId, ct)).Where(l => l.Id != null).ToList();
        evidence?.Set(locations.Select(l => new VerifiedFact("Location", l.Name, VerifiedFactSource.BusinessRepository, l.Id)), question,
            locations.Select(l => new ConversationMenuOption(l.Id!, l.Name, "Location")).ToArray());
        if (locations.Count == 0) return "No hay sedes registradas disponibles para reservar.";
        return SecretaryPresentation.Locations(locations.Select(l => l.Name), question);
    }

    private async Task<bool> IsCustomerNameValidAsync(Guid workspace, string? name, IReadOnlyList<LocationDto> locations,
        ServiceDto selectedService, HashSet<string> modules, CancellationToken ct)
    {
        if (!new NameSlotResolver().ResolveEvidence(name ?? "").TryGetValue(out _)) return false;
        var normalized = SpanishTextNormalizer.Normalize(name!);
        bool SameName(string label) => SpanishTextNormalizer.Normalize(label) == normalized;
        bool MatchesOffering(BusinessOfferingDto value)
            => SameName(value.Name) || VerifiedOffering.Capture(value).Aliases.Any(SameName);
        // Exact normalized labels only: shared words, partial names and fuzzy
        // similarities do not disqualify a person. Never reuse another tenant's data.
        if (locations.Any(l => l.Id != null && SameName(l.Name)) || MatchesOffering(selectedService)) return false;
        if ((await _offeringService.GetServicesAsync(workspace, null, null, ct))
            .Any(s => s.IsActive && s.Type == "SERVICE" && MatchesOffering(s))) return false;
        return !modules.Contains("CATALOG") || !(await _offeringService.GetProductsAsync(workspace, null, null, ct))
            .Any(p => p.IsActive && p.Type == "PRODUCT" && MatchesOffering(p));
    }

    private async Task<string> AnswerAvailabilityAsync(Guid workspace, ConversationContextDto context, TurnUnderstanding query, TurnEvidence evidence, CancellationToken ct)
    {
        var location = query.Location.TryGetValue(out var site) ? site.Id : context.SelectedLocationId;
        var serviceId = query.Service.TryGetValue(out var offering) ? offering.Id : context.SelectedServiceId;
        if (query.Location.Status is ResolutionStatus.Ambiguous or ResolutionStatus.Invalid || query.Service.Status is ResolutionStatus.Ambiguous or ResolutionStatus.Invalid)
            return "Necesito precisar la sede o el servicio de esta consulta. Conservamos tu reserva en preparación.";
        if (location == null || serviceId == null) return "Para comprobar esa hora necesito la sede y el servicio. ¿Cuál falta elegir?";
        if (query.Date.Status is ResolutionStatus.Invalid or ResolutionStatus.Ambiguous) return "Necesito precisar la fecha de esta consulta. ¿Qué día y mes deseas comprobar?";
        var date = query.Date.TryGetValue(out var requestedDate) ? requestedDate
            : DateOnly.TryParseExact(context.TargetDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var previousDate) ? previousDate : (DateOnly?)null;
        if (date == null) return "¿Para qué fecha deseas comprobar ese horario? Tu borrador sigue guardado.";
        try
        {
            var service = await _offeringService.GetServiceByIdAsync(workspace, serviceId, ct);
            if (service is not { IsActive: true, RequiresReservation: true, DurationInMinutes: >= 5 }
                || !await _offeringService.IsServiceAvailableAtLocationAsync(workspace, serviceId, location, ct))
                return "El servicio no está disponible para reservar en esa sede. Conservamos tu borrador.";
            var schedule = (await _hoursRepo.GetBusinessHoursAsync(workspace, location, ct)).ToArray();
            var day = schedule.SingleOrDefault(h => h.DayOfWeek == (int)date.Value.DayOfWeek);
            if (day == null) return "No hay horario configurado para ese día; no puedo comprobar disponibilidad.";
            if (day.IsClosed) return "La sede está cerrada ese día. ¿Qué otra fecha deseas comprobar?";
            if (!TimeOnly.TryParseExact(day.OpenTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var open)
                || !TimeOnly.TryParseExact(day.CloseTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var close))
                return "No puedo comprobar el horario de atención en este momento.";
            var profile = await _profileRepo.GetProfileAsync(workspace, ct); var zone = BookingZone(profile?.TimeZone);
            evidence.Zone = zone.Id;
            if (query.Time.Status == ResolutionStatus.Invalid) return "Necesito precisar la hora. ¿Qué hora deseas comprobar?";
            var clocks = query.Time.Candidates.Where(c => c.Value.Kind == TimePreferenceKind.Exact).Select(c => c.Value.ExactTime!.Value).Distinct().ToArray();
            if (query.Time.Status == ResolutionStatus.Ambiguous && clocks.Length == 0)
                return "Necesito precisar la franja de esta consulta. ¿Qué rango de horas deseas comprobar?";
            if (clocks.Length > 1)
            {
                var withinDay = clocks.Where(t => t >= open && t.ToTimeSpan() + TimeSpan.FromMinutes(service.DurationInMinutes.Value) <= close.ToTimeSpan()).ToArray();
                if (withinDay.Length != 1) return "¿A qué hora te refieres: " + string.Join(" o ", clocks.Select(t => t.ToString("HH:mm", CultureInfo.InvariantCulture))) + "?";
                clocks = withinDay;
            }
            var localDay = date.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            var introduction = "";
            if (clocks.Length == 1)
            {
                var at = date.Value.ToDateTime(clocks[0], DateTimeKind.Unspecified);
                var label = at.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
                evidence.Facts.Add(new("RequestedLocalTime", label, VerifiedFactSource.ValidatedConversation));
                if (!ReservationEngineTime(at, zone, out var expectedUtc)) return "Necesito una hora local válida y no ambigua. ¿Qué hora deseas comprobar?";
                var point = (await _reservationEngine.GetAvailabilityAtAsync(workspace, location, serviceId, at, ct))
                    .Where(s => s.IsAvailable && s.StartTime.Kind == DateTimeKind.Utc && s.StartTime == expectedUtc
                        && s.EndTime == expectedUtc.AddMinutes(service.DurationInMinutes.Value)).ToArray();
                if (point.Length > 0)
                {
                    evidence.Facts.Add(new("Availability", "Available", VerifiedFactSource.TransactionEngine));
                    evidence.Question = "¿Deseas elegir esa hora para tu reserva?";
                    return $"Sí, el {label} está disponible para {service.Name}.\n\n¿Deseas elegir esa hora para tu reserva? Puedes responder «Sí» o indicar otra. Todavía no he confirmado la cita.";
                }
                introduction = clocks[0] < open || clocks[0].ToTimeSpan() + TimeSpan.FromMinutes(service.DurationInMinutes.Value) > close.ToTimeSpan()
                    ? $"El {label} queda fuera de la jornada de atención."
                    : $"El {label} no está disponible para {service.Name}.";
                evidence.Facts.Add(new("Availability", "Unavailable", VerifiedFactSource.TransactionEngine));
            }
            var slots = (await _reservationEngine.GetAvailabilityAsync(workspace, location, serviceId, localDay, ct)).Where(s => s.IsAvailable);
            if (query.Time.TryGetValue(out var preference) && preference.Kind != TimePreferenceKind.Exact)
                slots = new TimePreferenceResolver().FilterAvailableSlots(slots, date.Value, zone, preference);
            TimeSpan? anchor = clocks.Length == 1 ? clocks[0].ToTimeSpan() : null;
            if (anchor == null && query.Time.TryGetValue(out var preferred))
                anchor = preferred.Kind switch {
                    TimePreferenceKind.After => preferred.From?.ToTimeSpan(),
                    TimePreferenceKind.Before => preferred.Until?.ToTimeSpan(),
                    TimePreferenceKind.Period => TimeSpan.FromTicks(((preferred.From?.ToTimeSpan().Ticks ?? 0)
                        + (preferred.Until?.ToTimeSpan().Ticks ?? TimeSpan.TicksPerDay)) / 2),
                    _ => null
                };
            var alternatives = slots.DistinctBy(s => s.StartTime)
                .Where(s => clocks.Length != 1 || Math.Abs((TimeZoneInfo.ConvertTimeFromUtc(s.StartTime, zone).TimeOfDay - clocks[0].ToTimeSpan()).Ticks) <= TimeSpan.FromHours(2).Ticks)
                .OrderBy(s => anchor.HasValue ? Math.Abs((TimeZoneInfo.ConvertTimeFromUtc(s.StartTime, zone).TimeOfDay - anchor.Value).Ticks) : 0)
                .ThenBy(s => s.StartTime).Take(3)
                .Select(s => TimeZoneInfo.ConvertTimeFromUtc(s.StartTime, zone).ToString("HH:mm", CultureInfo.InvariantCulture)).ToArray();
            return introduction + (alternatives.Length == 0 ? " No encontré horarios disponibles que coincidan. ¿Qué otra fecha u hora deseas comprobar?"
                : " Horarios disponibles: " + string.Join(", ", alternatives) + ". Son algunas sugerencias cercanas; puedes indicar una de ellas u otra hora para comprobarla. No he modificado el borrador.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return "No pude comprobar la disponibilidad en este momento. Conservamos tu borrador; puedes consultar de nuevo o solicitar un asesor."; }
    }

    private static bool ReservationEngineTime(DateTime local, TimeZoneInfo zone, out DateTime utc)
    {
        utc = default;
        if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local)) return false;
        utc = TimeZoneInfo.ConvertTimeToUtc(local, zone);
        return true;
    }

    private async Task<string> ServiceOptionsAsync(Guid workspaceId, string locationId, CancellationToken ct, TurnEvidence? evidence = null, int offset = 0)
    {
        var services = await ReservableServicesAsync(workspaceId, locationId, ct);
        evidence?.Set(services.Skip(offset).Take(5).Select(s => new VerifiedFact("Service", s.Name, VerifiedFactSource.BusinessRepository, s.Id)),
            "¿Qué servicio deseas reservar?", services.Skip(offset).Take(5).Select(s => new ConversationMenuOption(s.Id, s.Name, "Service")).ToArray());
        return OfferingServiceReservationExtensions.FormatReservationOptions(services, offset);
    }

    private async Task<List<ServiceDto>> ReservableServicesAsync(Guid workspaceId, string locationId, CancellationToken ct)
    {
        return (await _offeringService.GetServicesAsync(workspaceId, locationId, null, ct))
            .Where(s => s.IsActive && s.RequiresReservation && s.DurationInMinutes >= 5
                && (string.Equals(s.LocationScope, "ALL", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s.LocationScope, "SPECIFIC", StringComparison.OrdinalIgnoreCase) && s.LocationIds.Contains(locationId)))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Id, StringComparer.Ordinal).ToList();
    }

    private async Task<bool> IsSelectedServiceValidAsync(Guid workspaceId, ConversationContextDto context, CancellationToken ct)
    {
        var service = await _offeringService.GetServiceByIdAsync(workspaceId, context.SelectedServiceId!, ct);
        return service is { IsActive: true, RequiresReservation: true, DurationInMinutes: >= 5 } &&
            await _offeringService.IsServiceAvailableAtLocationAsync(workspaceId, service.Id, context.SelectedLocationId!, ct);
    }

    private async Task<string> ExplainServiceScopeAsync(Guid workspace, ConversationContextDto context, CancellationToken ct)
    {
        var sites = await _locationRepo.GetLocationsAsync(workspace, ct);
        var all = await _offeringService.GetServicesAsync(workspace, null, null, ct);
        var explanation = OfferingResponseFormatter.ServiceScope(all, sites, context.SelectedLocationId)
            ?? "El catálogo general incluye los servicios del negocio. Para reservar, comprobaré cuáles se ofrecen en la sede que elijas.";
        return explanation + "\n\n" + await GetContinuationAsync(workspace, context, ct, concise: true);
    }

    private async Task<string> WeeklyHoursAsync(Guid workspaceId, string locationId, CancellationToken ct)
    {
        var locations = await _locationRepo.GetLocationsAsync(workspaceId, ct);
        var location = locations.FirstOrDefault(l => l.Id == locationId);
        if (location == null) return "La sede seleccionada ya no está disponible.";
        var hours = await _hoursRepo.GetBusinessHoursAsync(workspaceId, locationId, ct);
        return $"Horario de atención de {location.Name}:\n" + BusinessHoursFormatter.Format(hours);
    }

    private async Task<string> AvailabilityOptionsAsync(Guid workspaceId, ConversationContextDto context, bool preserveDraft, CancellationToken ct, TurnEvidence? evidence = null, bool showOptions = true, TimeOnly? anchor = null)
    {
        if (!DateTime.TryParseExact(context.TargetDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            if (!preserveDraft) { context.TargetDate = null; UpdateStep(context); }
            return "¿Para qué fecha deseas reservar? Indica un día válido.";
        }
        if (string.IsNullOrWhiteSpace(context.SelectedLocationId) || string.IsNullOrWhiteSpace(context.SelectedServiceId))
            return "Indica primero la sede y el servicio que deseas reservar.";
        if (!await IsSelectedServiceValidAsync(workspaceId, context, ct))
        {
            if (!preserveDraft) { context.SelectedServiceId = null; context.TargetTime = null; UpdateStep(context); }
            return "Ese servicio ya no está disponible para reservar.\n\n" + await ServiceOptionsAsync(workspaceId, context.SelectedLocationId, ct);
        }
        var hours = (await _hoursRepo.GetBusinessHoursAsync(workspaceId, context.SelectedLocationId, ct)).ToList();
        var dayHours = hours.FirstOrDefault(h => h.DayOfWeek == (int)date.DayOfWeek);
        if (!BusinessHoursFormatter.IsOpen(dayHours))
        {
            if (!preserveDraft) { context.TargetTime = null; UpdateStep(context); }
            return "Esta sede no atiende ese día.\n\n" + BusinessHoursFormatter.Format(hours) +
                "\n\nDime otra combinación de día y hora para verificar su disponibilidad.";
        }
        if (!showOptions)
        {
            if (!preserveDraft) { context.RecoveryOptions.Clear(); context.RecoveryOptionsContext = null; }
            var question = SecretaryPresentation.AskTime(context.PreferredTime != null);
            if (evidence != null) evidence.Question = question;
            return $"Revisemos un horario para el {date:dd/MM/yyyy}.\n\n{question}";
        }
        var slots = (await _reservationEngine.GetAvailabilityAsync(workspaceId, context.SelectedLocationId, context.SelectedServiceId, date, ct))
            .Where(s => s.IsAvailable).OrderBy(s => s.StartTime).ToList();
        if (slots.Count == 0)
        {
            if (!preserveDraft) { context.TargetTime = null; UpdateStep(context); }
            return $"El negocio atiende el {date:dd/MM/yyyy}, pero ya no quedan horarios disponibles. "
                + await VerifiedAlternativesAsync(workspaceId, context, date, ct, evidence);
        }
        var profile = await _profileRepo.GetProfileAsync(workspaceId, ct);
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(profile?.TimeZone) ? "America/Lima" : profile.TimeZone); }
        catch (TimeZoneNotFoundException) { zone = TimeZoneInfo.FindSystemTimeZoneById("America/Lima"); }
        catch (InvalidTimeZoneException) { zone = TimeZoneInfo.FindSystemTimeZoneById("America/Lima"); }
        if (context.PreferredTime != null)
        {
            slots = new TimePreferenceResolver().FilterAvailableSlots(slots, DateOnly.FromDateTime(date), zone, context.PreferredTime).ToList();
            if (slots.Count == 0) return "Hay horarios disponibles, pero ninguno coincide con tu preferencia. ¿Qué otra franja u hora prefieres?";
        }
        // Suggestions are a bounded view of real engine results, not the full
        // set of permitted starts. Keep the customer's requested time relevant.
        if (anchor.HasValue)
            slots = slots.Where(s => Math.Abs((TimeZoneInfo.ConvertTimeFromUtc(s.StartTime, zone).TimeOfDay - anchor.Value.ToTimeSpan()).Ticks) <= TimeSpan.FromHours(2).Ticks)
                .OrderBy(s => Math.Abs((TimeZoneInfo.ConvertTimeFromUtc(s.StartTime, zone).TimeOfDay - anchor.Value.ToTimeSpan()).Ticks))
                .ThenBy(s => s.StartTime).Take(3).ToList();
        else slots = slots.Take(5).ToList();
        if (slots.Count == 0) return "No encontré una alternativa cercana a esa hora. ¿Prefieres consultar otra hora o fecha?";
        if (evidence != null) { evidence.Zone = zone.Id; evidence.Set(
            slots.Select(s => new VerifiedFact("AvailableStartUtc", s.StartTime.ToString("O", CultureInfo.InvariantCulture), VerifiedFactSource.TransactionEngine)).ToArray(),
            "¿Qué horario prefieres?", slots.Select(s => {
                var label = TimeZoneInfo.ConvertTimeFromUtc(s.StartTime, zone).ToString("HH:mm", CultureInfo.InvariantCulture);
                return new ConversationMenuOption(label, label, "Time"); }).ToArray()); }
        var introduction = anchor.HasValue ? "Estas son algunas alternativas cercanas que pude verificar" : "Estos son algunos horarios que pude verificar";
        if (evidence != null) return $"{introduction} para el {date:dd/MM/yyyy}. También puedes proponer otra hora.\n\n¿Cuál horario prefieres?";
        return $"{introduction} para el {date:dd/MM/yyyy}:\n" +
            string.Join("\n", slots.Take(8).Select(s => $"• {TimeZoneInfo.ConvertTimeFromUtc(s.StartTime, zone):HH:mm}")) +
            "\nTambién puedes proponer otra hora; comprobaré su disponibilidad." +
            "\n\n¿Cuál horario prefieres?";
    }

    private static string RemoveFinalQuestion(string options)
    {
        var index = options.LastIndexOf('¿');
        return index < 0 ? options : options[..index].TrimEnd();
    }

    private static TimeZoneInfo BookingZone(string? configured)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(configured) ? "America/Lima" : configured); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("America/Lima"); }
        catch (InvalidTimeZoneException) { return TimeZoneInfo.FindSystemTimeZoneById("America/Lima"); }
    }

    private async Task<string> VerifiedAlternativesAsync(Guid workspace, ConversationContextDto context, DateTime date, CancellationToken ct, TurnEvidence? evidence = null)
    {
        var hours = (await _hoursRepo.GetBusinessHoursAsync(workspace, context.SelectedLocationId, ct)).ToArray();
        var profile = await _profileRepo.GetProfileAsync(workspace, ct);
        var zone = BookingZone(profile?.TimeZone);
        // Bounded read-only search. Never change the requested date or auto-select an alternative.
        for (var offset = 1; offset <= 3; offset++)
        {
            var next = date.AddDays(offset);
            if (!BusinessHoursFormatter.IsOpen(hours.FirstOrDefault(h => h.DayOfWeek == (int)next.DayOfWeek))) continue;
            var available = (await _reservationEngine.GetAvailabilityAsync(workspace, context.SelectedLocationId!, context.SelectedServiceId!, next, ct)).Where(s => s.IsAvailable);
            if (context.PreferredTime != null) available = new TimePreferenceResolver().FilterAvailableSlots(available, DateOnly.FromDateTime(next), zone, context.PreferredTime);
            var slots = available.Take(3).ToArray();
            if (slots.Length == 0) continue;
            if (evidence != null) { evidence.Zone = zone.Id; evidence.Set( slots.Select(s => new VerifiedFact("AvailableStartUtc", s.StartTime.ToString("O", CultureInfo.InvariantCulture), VerifiedFactSource.TransactionEngine)).ToArray(), "¿Te conviene ese día u otra fecha?", []); }
            return $"Sí hay opciones el {next:dd/MM/yyyy}: " + string.Join(", ", slots.Select(s => TimeZoneInfo.ConvertTimeFromUtc(s.StartTime, zone).ToString("HH:mm", CultureInfo.InvariantCulture))) + ". ¿Te conviene ese día u otra fecha?";
        }
        return "¿Qué otra fecha te conviene?";
    }
}

public class RequestFlow : IRequestFlow
{
    private readonly IRequestService _requestService;
    private readonly IHumanHandoffService _handoffService;

    public RequestFlow(IRequestService requestService, IHumanHandoffService handoffService)
    {
        _requestService = requestService; _handoffService = handoffService;
    }

    public async Task<string> ProcessAsync(Guid workspaceId, string phone, string messageText, string conversationId, string sourceMessageId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceMessageId);
        var type = RequestType.Support;
        var title = "Solicitud de Atención";
        var lowerText = messageText.ToLowerInvariant();
        if (lowerText.Contains("comprar") || lowerText.Contains("cotizar") || lowerText.Contains("precio") || lowerText.Contains("mayor"))
        {
            type = RequestType.CommercialInquiry; title = "Consulta Comercial / Cotización";
        }
        else if (lowerText.Contains("tramite") || lowerText.Contains("afiliar") || lowerText.Contains("documento"))
        {
            type = RequestType.Tramite; title = "Trámite Administrativo";
        }
        var ticketId = await _requestService.CreateRequestAsync(workspaceId, phone, conversationId, type, title, messageText, sourceMessageId, null, ct);
        await _handoffService.EscalateToHumanAsync(workspaceId, conversationId, HandoffReason.AiEscalation, ct);
        return $"He registrado tu solicitud con el código {ticketId}. Un asesor la revisará y se pondrá en contacto contigo a la brevedad.";
    }
}

public class SupportFlow : ISupportFlow
{
    private readonly IHumanHandoffService _handoffService;
    public SupportFlow(IHumanHandoffService handoffService) => _handoffService = handoffService;

    public async Task<string> ProcessAsync(Guid workspaceId, string conversationId, CancellationToken ct)
    {
        await _handoffService.EscalateToHumanAsync(workspaceId, conversationId, HandoffReason.AiEscalation, ct);
        return "Entiendo. Te estoy transfiriendo con un asesor humano en este momento. Por favor espera un instante.";
    }
}

public class ChatFlow : IChatFlow
{
    private readonly IBusinessReadTools _readTools;
    private readonly ILocationResolverService _locationResolver;
    private readonly ILogger<ChatFlow> _logger;
    private readonly IEntitlementService _entitlementService;
    private readonly ICatalogGenerationService _catalogGeneration;
    private readonly INotificationService _notifications;
    private readonly IOfferingService? _offerings;
    private readonly ILocationRepository? _locations;

    public ChatFlow(IKnowledgeService knowledgeService, IEntitlementService entitlementService,
        ILocationResolverService locationResolver, ILogger<ChatFlow> logger,
        ICatalogGenerationService catalogGeneration, INotificationService notifications, IBusinessReadTools? readTools = null,
        IOfferingService? offerings = null, ILocationRepository? locations = null)
    {
        _readTools = readTools ?? new BusinessReadTools(knowledgeService, entitlementService);
        _locationResolver = locationResolver;
        _logger = logger;
        _entitlementService = entitlementService;
        _catalogGeneration = catalogGeneration;
        _notifications = notifications;
        _offerings = offerings;
        _locations = locations;
    }

    public async Task<ChatResponse> ProcessAsync(Guid workspaceId, string text, AiInterpretation interpretation, string? factualLocationId, CancellationToken ct)
    {
        var readTurn = _readTools.Open(workspaceId);
        var modules = (await _entitlementService.GetAvailableModuleCodesAsync(workspaceId, ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var denied = interpretation.DeniedResponse ?? AiIntentAccess.GetDeniedResponse(interpretation.Intent, modules);
        if (denied != null) return new ChatResponse(denied) { Operation = OperationStatus.Denied };

        if (interpretation.OfferingQuery == null && interpretation.QueryKind == OfferingQueryKind.Broad
            && modules.Contains("CATALOG") && modules.Contains("SERVICES")
            && NexFlow.Application.Features.Automation.Dialogue.Understanding.InformationalRequest.UnscopedCatalog(text))
            return new ChatResponse(InformationalRequest.CatalogScopeQuestion) { Choices = InformationalRequest.CatalogScopeChoices };

        if (interpretation.OfferingQuery is { } query)
        {
            if (query.WorkspaceId != workspaceId || query.Type != (interpretation.Intent == ConversationIntent.ProductQuery ? "PRODUCT" : "SERVICE"))
                throw new InvalidOperationException("Offering query workspace/type mismatch.");
            var body = OfferingResponseFormatter.Format(query);
            if (query.Type == "SERVICE" && query.Broad && factualLocationId != null && modules.Contains("RESERVATIONS")
                && _offerings != null && _locations != null)
            {
                var all = await _offerings.GetServicesAsync(workspaceId, null, null, ct);
                var sites = await _locations.GetLocationsAsync(workspaceId, ct);
                if (OfferingResponseFormatter.ServiceScope(all, sites, query.LocationId ?? factualLocationId) is { } explanation)
                    body += "\n\n" + explanation;
            }
            var facts = query.Offerings.SelectMany(p => new[] { new VerifiedFact("Offering",p.Name,VerifiedFactSource.BusinessRepository,p.Id),
                new VerifiedFact("PriceMinorUnits",p.PriceMinorUnits.ToString(CultureInfo.InvariantCulture),VerifiedFactSource.BusinessRepository,p.Id),
                new VerifiedFact("Currency",p.Currency,VerifiedFactSource.BusinessRepository,p.Id) }).ToArray();
            if (query.Broad && query.PageOffset == 0)
            {
                var artifact = await _catalogGeneration.GetArtifactAsync(workspaceId,query.Type,ct);
                if (artifact != null && (artifact.WorkspaceId != workspaceId || artifact.Scope != query.Type)) throw new InvalidOperationException("Catalog artifact workspace/scope mismatch.");
                if (artifact?.Status == CatalogArtifactStatus.Current && Uri.TryCreate(artifact.PdfUrl,UriKind.Absolute,out var uri) && uri.Scheme is "https" or "http")
                {
                    if (query.LocationId != null || query.Question == OfferingQuestion.Availability)
                        body += "\n\nEl PDF adjunto corresponde al catálogo general del negocio.";
                    return new(body,artifact.PdfUrl,query.Type == "PRODUCT" ? "catalogo-productos.pdf" : "catalogo-servicios.pdf",query.Type) { Facts=Array.AsReadOnly(facts), Operation=OperationStatus.MediaPending };
                }
                if (artifact?.Status != CatalogArtifactStatus.Generating) await _notifications.NotifyCatalogUnavailableAsync(workspaceId,query.Type,ct);
            }
            return new(body) { Facts=Array.AsReadOnly(facts), Realization=OfferingResponseFormatter.Realization(query),
                Operation=query.Offerings.Count == 0 ? OperationStatus.NotAvailable : OperationStatus.Informational };
        }

        KnowledgeTopic? topic = interpretation.Intent switch
        {
            ConversationIntent.ProductQuery => KnowledgeTopic.Products,
            ConversationIntent.ServiceQuery => KnowledgeTopic.Services,
            ConversationIntent.Faq => KnowledgeTopic.Faqs,
            ConversationIntent.Location => KnowledgeTopic.Locations,
            ConversationIntent.BusinessHours => KnowledgeTopic.BusinessHours,
            // A general informational introduction uses the public profile. An
            // incidental FAQ text match must not replace the business identity.
            // Specific FAQ requests keep their explicit Faq route above.
            ConversationIntent.General when interpretation.Act == DialogueAct.Information => KnowledgeTopic.Profile,
            ConversationIntent.General => modules.Contains("FAQ") ? KnowledgeTopic.Faqs : KnowledgeTopic.Profile,
            ConversationIntent.Profile => KnowledgeTopic.Profile,
            _ => null
        };
        if (!topic.HasValue) return new ChatResponse("No puedo resolver esa consulta con la información disponible.");

        // An unscoped broad query is the business catalog, even during booking.
        string? locationId = interpretation.QueryKind == OfferingQueryKind.Broad
            && interpretation.Intent is ConversationIntent.ProductQuery or ConversationIntent.ServiceQuery ? null : factualLocationId;
        if (topic != KnowledgeTopic.Faqs && !string.IsNullOrWhiteSpace(interpretation.Location))
        {
            try
            {
                locationId = await _locationResolver.ResolveLocationIdAsync(workspaceId, interpretation.Location, ct);
                if (string.IsNullOrWhiteSpace(locationId))
                    return new ChatResponse(new KnowledgeResult { Status = KnowledgeStatus.NotFound, Source = topic.Value }.ToResponse());
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Knowledge location resolution failed for workspace {WorkspaceId}.", workspaceId);
                return new ChatResponse(new KnowledgeResult { Status = KnowledgeStatus.Unavailable, Source = topic.Value }.ToResponse());
            }
        }

        string? fallbackIntroduction = null;
        var broad = (interpretation.Intent is ConversationIntent.ProductQuery or ConversationIntent.ServiceQuery)
            && interpretation.QueryKind == OfferingQueryKind.Broad && string.IsNullOrWhiteSpace(interpretation.SearchTerm);
        if (broad)
        {
            var scope = interpretation.Intent == ConversationIntent.ProductQuery ? "PRODUCT" : "SERVICE";
            var artifact = await _catalogGeneration.GetArtifactAsync(workspaceId, scope, ct);
            if (artifact != null && (artifact.WorkspaceId != workspaceId || artifact.Scope != scope))
                throw new InvalidOperationException("Catalog artifact workspace/scope mismatch.");
            if (artifact?.Status == CatalogArtifactStatus.Current && Uri.TryCreate(artifact.PdfUrl, UriKind.Absolute, out var pdfUri)
                && pdfUri.Scheme is "https" or "http")
            {
                var caption = scope == "PRODUCT"
                    ? "Te comparto nuestro catálogo actualizado de productos."
                    : "Te comparto nuestro catálogo actualizado de servicios.";
                var current = await Read(new KnowledgeQuery { Topic=topic.Value, LocationId=locationId });
                caption = (current.Found ? current.Facts + "\n" : "") + caption;
                return new ChatResponse(caption, artifact.PdfUrl, scope == "PRODUCT" ? "catalogo-productos.pdf" : "catalogo-servicios.pdf", scope) {
                    Information = current,
                    Facts=current.Found ? Array.AsReadOnly(new[]{new VerifiedFact(topic.Value.ToString(),current.Facts,VerifiedFactSource.BusinessRepository)}) : [],
                    Operation=OperationStatus.MediaPending
                };
            }
            if (artifact?.Status == CatalogArtifactStatus.Generating)
                fallbackIntroduction = "Nuestro catálogo se está actualizando. Mientras tanto, estas son algunas opciones disponibles:";
            else
            {
                await _notifications.NotifyCatalogUnavailableAsync(workspaceId, scope, ct);
                fallbackIntroduction = "Nuestro catálogo completo todavía no está disponible en PDF. Mientras tanto, te comparto algunas opciones:";
            }
        }
        var result = await Read(new KnowledgeQuery
        {
            Topic = topic.Value,
            LocationId = locationId,
            SearchTerm = interpretation.Intent is ConversationIntent.Faq or ConversationIntent.General
                ? interpretation.SearchTerm ?? text : interpretation.SearchTerm
        });

        if (interpretation.Intent == ConversationIntent.General && result.Status == KnowledgeStatus.NotFound)
        {
            result = await Read(new KnowledgeQuery { Topic = KnowledgeTopic.Profile });
            if (result.Found) return Factual($"Esta es la información registrada del negocio:\n{result.Facts}\nSi necesitas otro dato, indícame cuál.");
        }

        // Business facts are returned verbatim from the factual query, never
        // expanded by an unconstrained generation step.
        if (fallbackIntroduction != null)
        {
            var facts = result.Found ? result.Facts : result.Status == KnowledgeStatus.Unavailable
                ? result.ToResponse() : "No hay opciones registradas disponibles para esta consulta.";
            return Factual($"{fallbackIntroduction}\n\n{facts}");
        }
        return Factual(result.Status == KnowledgeStatus.NotFound ? "No tengo información registrada que coincida con tu consulta." : result.ToResponse());

        async Task<KnowledgeResult> Read(KnowledgeQuery query)
        {
            var read = await _readTools.ReadAsync(readTurn, new KnowledgeRead(query.Topic, query.LocationId, query.SearchTerm), ct);
            return read.Knowledge ?? new KnowledgeResult { Source = query.Topic, LocationId = query.LocationId,
                Status = read.Status == BusinessReadStatus.Denied ? KnowledgeStatus.Denied : KnowledgeStatus.Unavailable };
        }

        ChatResponse Factual(string response) => new(response) {
            Information = result.Source is KnowledgeTopic.BusinessHours or KnowledgeTopic.Locations or KnowledgeTopic.Profile or KnowledgeTopic.Products or KnowledgeTopic.Services ? result : null,
            Facts = result.Found ? Array.AsReadOnly(new[] { new VerifiedFact(result.Source.ToString(), result.Facts, VerifiedFactSource.BusinessRepository) }) : [],
            Operation = result.Found ? OperationStatus.Informational : OperationStatus.NotAvailable
        };
    }
}


