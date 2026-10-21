using System.Globalization;
using Microsoft.Extensions.Options;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Automation.Dialogue.Policy;
using NexFlow.Application.Features.Automation.Dialogue.Understanding;
using NexFlow.Application.Features.Business.Offerings;
using NexFlow.Application.Features.Reservations;
using NexFlow.Domain.Enums;
using NexFlow.Domain.Exceptions;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services.Flows;

public interface IExistingReservationFlow
{
    Task<RecoveryDecision?> TryHandleAsync(Guid workspace, string contact, ProcessIncomingMessageCommand request,
        ConversationContextDto context, HashSet<string> modules, CancellationToken ct);
}

public sealed class ExistingReservationFlow(IReservationRepository reservations, IReservationEngine engine,
    IOutboxRepository outbox, IContextRecoveryService contexts, IEntitlementService entitlements,
    IBusinessProfileRepository profiles, ILocationRepository locations, IOfferingService offerings, IClock clock,
    IBusinessHoursRepository hours, IOptions<AssistantRecoveryOptions> options) : IExistingReservationFlow
{
    public async Task<RecoveryDecision?> TryHandleAsync(Guid workspace, string contact, ProcessIncomingMessageCommand request,
        ConversationContextDto context, HashSet<string> modules, CancellationToken ct)
    {
        var text = SpanishTextNormalizer.Normalize(request.MessageText);
        var operation = ReservationOperationClassifier.Classify(text, context.ReservationManagement == null && context.CurrentGoal is "BOOKING" or "RESERVATION");
        var management = context.ReservationManagement;
        var requested = operation is ReservationOperationKind.ViewExistingReservation or ReservationOperationKind.RescheduleExistingReservation or ReservationOperationKind.CancelExistingReservation;
        if (context.Mode == "Human") throw new ConcurrencyException("Human control superseded reservation management.");
        if (management != null)
        {
            // Legacy selections/summary text cannot establish authorization or leak through an informational continuation.
            // Keep independent draft slots and SQL receipts; abandon only the unverified management proposal.
            if (context.CurrentGoal == "RESERVATION_MANAGEMENT")
            { context.CurrentGoal = management.SuspendedGoal; context.CurrentStep = management.SuspendedStep; }
            if (management.SuspendedDraftExpired && context.CurrentGoal != null) context.PendingRecoveryAction = "EXPIRE";
            context.ReservationManagement = null;
            await contexts.SaveContextAsync(workspace, contact, context, ct);
        }
        // Keep recovery's human/repair/information priority and the existing transactional draft.
        if (text is "asesor" or "humano" || text.Contains("asesor humano", StringComparison.Ordinal)
            || text.Contains("hablar con", StringComparison.Ordinal) || InformationalRequest.IsRepair(text)
            || !requested && (InformationalRequest.PublicTopic(text) != null
                || text.Split(' ').Any(w => w is "precio" or "cuesta" or "cuanto" or "duracion" or "dura" or "productos" or "servicios" or "catalogo" or "pdf" or "direccion" or "ubicacion" or "donde"))) return null;
        if (management == null && !requested && !ConversationConfirmation.IsAffirmative(request.MessageText)) return null;
        var denied = AiIntentAccess.GetDeniedResponse(ConversationIntent.Reservation, modules);
        if (denied != null) return requested || management != null ? new(Response: denied) : null;
        ReservationContact.Validate(workspace, contact);
        if (context.Mode == "Human") throw new ConcurrencyException("Human control superseded reservation management.");

        // No independent owner attestation exists in the current channel/consumer contracts.
        // A phone, matching name or saved selection is not authorization. Gate BEFORE private SQL reads.
        // Keep the management implementation below for a future reviewed authorization contract;
        // do not enable it with a client/context boolean or a configuration bypass.
        if (requested || management != null)
            return new(Response: "Para proteger los datos de tu cita, un asesor debe verificar tu titularidad. Te transfiero para continuar; no he creado, cambiado ni cancelado una reserva.",
                Interpretation: new() { Intent = ConversationIntent.Support }, Act: DialogueAct.HumanAssistance, Operation: OperationStatus.Human);

        var response = await HandleOperationAsync(workspace, contact, request, context, ct);
        return response == null ? null : new(Response: response);
    }

    private async Task<string?> HandleOperationAsync(Guid workspace, string contact, ProcessIncomingMessageCommand request,
        ConversationContextDto context, CancellationToken ct)
    {
        var text = SpanishTextNormalizer.Normalize(request.MessageText);
        var management = context.ReservationManagement;
        var operation = ReservationOperationClassifier.Classify(text, management == null && context.CurrentGoal is "BOOKING" or "RESERVATION");
        var requested = operation is ReservationOperationKind.ViewExistingReservation or ReservationOperationKind.RescheduleExistingReservation or ReservationOperationKind.CancelExistingReservation;
        var receiptRow = await outbox.GetByIdAsync(workspace, ReservationOperationCommand.KeyFor(workspace, contact, request.MessageId), ct);
        if (receiptRow != null)
        {
            var receipt = ReservationOperationReceipt.Read(receiptRow, workspace, contact);
            if (receipt.ConfirmationHash != ReservationOperationCommand.Hash(text)) return "La identidad del mensaje ya fue utilizada con otro contenido. No realicé otro cambio.";
            // A replay of an older operation must not remove a newer proposal.
            if (management?.PendingCommand?.SourceMessageId == request.MessageId) { FinishManagement(); await Save(); }
            return "Esta solicitud ya fue registrada. No volví a modificar la cita ni a crear otro evento de cambio. La entrega de la notificación puede seguir pendiente.";
        }
        if (management == null && !requested) return null;
        if (ReservationOperationClassifier.RequiresHumanVerification(text))
            return await Stop("Para proteger los datos de la cita necesitamos una verificación adicional. Escribe asesor para recibir atención humana.");
        if (management != null && (management.WorkspaceId != workspace || management.ContactHash != ReservationOperationCommand.Hash(contact)
            || management.ExpiresAt <= clock.UtcNow)) return await Stop("La selección anterior ya no es válida. Solicita nuevamente consultar tu cita; no se ha modificado ninguna reserva.");
        if (management != null && text is "cancelar" or "dejar cambio" or "no quiero continuar")
            return await Stop("He descartado únicamente esta propuesta. Tu reserva registrada sigue sin cambios.");
        if (management != null && operation is ReservationOperationKind.CreateReservation or ReservationOperationKind.AmbiguousReservationOperation)
            return "Estamos revisando una cita ya registrada. ¿Deseas continuar con ella? Escribe cancelar para dejar esta propuesta antes de iniciar otra operación.";
        if (requested && (management == null || management.Operation != operation))
        {
            management = new() { WorkspaceId = workspace, ContactHash = ReservationOperationCommand.Hash(contact), Operation = operation, ExpiresAt = clock.UtcNow.AddMinutes(10),
                SuspendedGoal = management != null ? management.SuspendedGoal : context.CurrentGoal,
                SuspendedStep = management != null ? management.SuspendedStep : context.CurrentStep,
                SuspendedDraftExpired = management?.SuspendedDraftExpired == true || context.CurrentGoal != null && context.LastUpdated < clock.UtcNow.AddHours(-options.Value.ContextExpiryHours) };
            context.ReservationManagement = management;
            // Preserve the draft slots, but never expose its old CONFIRM step as the active operation.
            context.CurrentGoal = "RESERVATION_MANAGEMENT"; context.CurrentStep = null;
            return await Page(null);
        }
        if (management == null) return null;
        if (management.Stage == "Select")
        {
            if (text == "ver mas" && management.HasMore) return await Page(management.Cursor);
            var index = ConversationRecoveryService.SelectionIndex(text);
            if (index < 1 || index > management.Options.Count) return "Indica el número de una cita de la lista vigente o escribe asesor.";
            return await Select(management.Options[index - 1]);
        }
        var selected = management.Selected!;
        var fresh = await reservations.GetOwnedFutureAsync(workspace, contact, selected.Id, ct);
        if (fresh == null || fresh.RowVersion != selected.RowVersion || fresh.StartTime != selected.StartTime || fresh.EndTime != selected.EndTime)
            return await Stop("La cita cambió o ya no está vigente. Vuelve a consultarla antes de realizar cambios.");
        if (!await reservations.HasUnambiguousFutureOwnerNameAsync(workspace, contact, ct)) return await Stop("Necesitamos verificar quién solicita la gestión de este contacto. Escribe asesor; no mostraré más datos de citas.");
        var profile = await profiles.GetProfileAsync(workspace, ct);
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(profile?.TimeZone) ? "America/Lima" : profile.TimeZone); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return "No puedo verificar la zona horaria del negocio. Solicita un asesor; no realicé cambios."; }
        if (management.ZoneId != zone.Id) return await Stop("La zona horaria del negocio cambió. Consulta nuevamente la cita antes de confirmar.");
        if (management.Stage == "View")
        {
            if (text.Split(' ').Any(w => w is "cambiar" or "reagendar" or "reprogramar" or "editar"))
            { management.Operation = ReservationOperationKind.RescheduleExistingReservation; management.Stage = "Change"; return await Ask("¿Qué fecha u hora deseas cambiar? Conservamos la sede y el servicio de tu cita."); }
            return "Tu cita sigue registrada sin cambios. Si deseas reagendarla, escribe cambiar horario; para atención humana, escribe asesor.";
        }
        if (management.Stage == "Confirm")
        {
            if (ConversationConfirmation.IsNegative(request.MessageText))
            {
                if (management.Operation == ReservationOperationKind.CancelExistingReservation) return await Stop("No he cancelado tu cita. La reserva original sigue vigente.");
                management.Stage = "Change"; management.PendingCommand = null; return await Ask("No he realizado cambios. Indica qué deseas corregir o escribe cancelar para dejar la propuesta.");
            }
            if (ConversationConfirmation.IsAffirmative(request.MessageText))
            {
                var currentModules = (await entitlements.GetAvailableModuleCodesAsync(workspace, ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var currentDenied = AiIntentAccess.GetDeniedResponse(ConversationIntent.Reservation, currentModules);
                if (currentDenied != null) return currentDenied;
                var proposed = management.Operation == ReservationOperationKind.CancelExistingReservation ? (DateTime?)null : ResolveUtc(management.ProposedDate!, management.ProposedTime!, zone);
                if (proposed == null && management.Operation != ReservationOperationKind.CancelExistingReservation) return "La hora indicada no es válida o es ambigua. Indica otra hora; no realicé cambios.";
                management.PendingCommand ??= new(workspace, selected.Id, contact, request.MessageId, management.Operation,
                    selected.RowVersion, selected.StartTime, proposed, ReservationOperationCommand.Hash(text));
                // Persist the exact command before SQL. Commit recovery never derives a new payload/key.
                await Save();
                var latest = await contexts.GetOrRecoverContextAsync(workspace, contact, ct);
                if (latest.Mode == "Human" || latest.StateVersion != context.StateVersion) throw new ConcurrencyException("Conversation changed before reservation confirmation.");
                var result = await engine.ApplyExistingOperationAsync(management.PendingCommand, ct);
                if (result.IsFailure)
                {
                    management.PendingCommand = null; management.Stage = "Change"; await Save();
                    if (result.Error.Code is "Reservation.Conflict" or "Reservation.ConcurrencyConflict")
                        return result.Error.Description + "\n" + await VerifyProposal(zone, null);
                    return result.Error.Description + " La reserva no se ha modificado por esta solicitud.";
                }
                var value = result.Value;
                FinishManagement(); await Save();
                return management.Operation == ReservationOperationKind.CancelExistingReservation ? "La cancelación de tu cita ha sido confirmada."
                    : $"Tu cita fue reagendada para {Local(value.DateTime, zone)}. Se conservaron la sede y el servicio.";
            }
            if (ConversationConfirmation.NeedsClarification(request.MessageText)) return "No he confirmado cambios. Para aceptar el resumen vigente responde sí confirmo; si deseas corregirlo, indica el cambio.";
        }
        if (management.Operation == ReservationOperationKind.CancelExistingReservation) return "Para cancelar la cita del resumen responde sí confirmo, o escribe cancelar para dejar la propuesta.";
        var chosenSlot = ConversationRecoveryService.SelectionIndex(text);
        if (management.Slots.Count > 0 && chosenSlot > 0 && chosenSlot <= management.Slots.Count)
        {
            var slot = TimeZoneInfo.ConvertTimeFromUtc(management.Slots[chosenSlot - 1].StartTime, zone);
            management.ProposedDate = slot.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); management.ProposedTime = slot.ToString("HH:mm", CultureInfo.InvariantCulture);
        }
        else
        {
            var now = TimeZoneInfo.ConvertTime(new DateTimeOffset(clock.UtcNow), zone);
            var date = new BusinessDateResolver().ResolveReservation(request.MessageText, now, dateWasRequested: true,
                contextDate: DateOnly.ParseExact(management.ProposedDate!, "yyyy-MM-dd", CultureInfo.InvariantCulture), previousQuestion: management.NextQuestion);
            if (date.Status is ResolutionStatus.Ambiguous or ResolutionStatus.Invalid) return "Indica una fecha completa e inequívoca para tu cita; no realicé cambios.";
            if (date.TryGetValue(out var day)) management.ProposedDate = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var preference = new TimePreferenceResolver().Resolve(request.MessageText, timeWasRequested: true);
            if (preference.Status is ResolutionStatus.Ambiguous or ResolutionStatus.Invalid) return "Indica una hora concreta (por ejemplo, 10:30) o una preferencia clara. No realicé cambios.";
            if (preference.TryGetValue(out var time))
            {
                management.ProposedTime = time.Kind == TimePreferenceKind.Exact ? time.ExactTime!.Value.ToString("HH:mm", CultureInfo.InvariantCulture) : null;
                return await VerifyProposal(zone, time.Kind == TimePreferenceKind.Exact ? null : time);
            }
            if (!date.TryGetValue(out _)) return await Ask("¿Qué nueva fecha u hora deseas? No necesitas volver a indicar sede ni servicio.");
        }
        return await VerifyProposal(zone, null);

        async Task<string> Page(ReservationCursor? after)
        {
            var page = await reservations.GetFutureForContactAsync(workspace, contact, after, 5, ct);
            if (page.Items.Count == 0) return await Stop("No identifiqué citas futuras confirmadas para el contacto verificado. Puedes solicitar un asesor.");
            if (!await reservations.HasUnambiguousFutureOwnerNameAsync(workspace, contact, ct)) return await Stop("Este contacto requiere verificación adicional antes de mostrar citas. Escribe asesor.");
            management!.Options = page.Items.ToList(); management.HasMore = page.HasMore;
            management.Cursor = new(page.Items[^1].StartTime, page.Items[^1].Id);
            if (page.Items.Count == 1 && after == null) return await Select(page.Items[0]);
            var profile = await profiles.GetProfileAsync(workspace, ct);
            var zone = TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(profile?.TimeZone) ? "America/Lima" : profile.TimeZone);
            return await Ask("Estas son tus citas futuras. ¿Cuál deseas revisar?\n" + string.Join("\n", page.Items.Select((r, index) => $"{index + 1}. {Local(r.StartTime, zone)}"))
                + (page.HasMore ? "\nEscribe ver más para consultar la siguiente página." : ""));
        }
        async Task<string> Select(ReservationCandidate candidate)
        {
            var owned = await reservations.GetOwnedFutureAsync(workspace, contact, candidate.Id, ct);
            if (owned == null || owned.RowVersion != candidate.RowVersion || owned.StartTime != candidate.StartTime)
                return await Stop("La selección ya no está vigente. Solicita nuevamente consultar tus citas.");
            if (!await reservations.HasUnambiguousFutureOwnerNameAsync(workspace, contact, ct))
                return await Stop("Este contacto requiere verificación adicional antes de mostrar citas. Escribe asesor.");
            management!.Selected = owned; management.Options.Clear(); management.Slots.Clear();
            var profile = await profiles.GetProfileAsync(workspace, ct);
            var zone = TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(profile?.TimeZone) ? "America/Lima" : profile.TimeZone);
            management.ZoneId = zone.Id;
            var local = TimeZoneInfo.ConvertTimeFromUtc(owned.StartTime, zone);
            management.ProposedDate = local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); management.ProposedTime = local.ToString("HH:mm", CultureInfo.InvariantCulture);
            var location = (await locations.GetLocationsAsync(workspace, ct)).SingleOrDefault(l => l.Id == owned.LocationId);
            var service = await offerings.GetServiceByIdAsync(workspace, owned.ServiceId, ct);
            if (location == null || service == null) return await Stop("No puedo verificar los datos comerciales de la cita. Escribe asesor; no realicé cambios.");
            var current = $"Cita actual: {Local(owned.StartTime, zone)}, {location.Name}, {service.Name}.";
            management.Stage = management.Operation == ReservationOperationKind.CancelExistingReservation ? "Confirm"
                : management.Operation == ReservationOperationKind.ViewExistingReservation ? "View" : "Change";
            return await Ask(current + (management.Stage == "Confirm" ? " ¿Confirmas cancelar esta cita? Responde sí confirmo o escribe cancelar para dejar la propuesta."
                : management.Stage == "View" ? " Puedes pedir cambiar horario o solicitar un asesor." : " ¿Qué fecha u hora deseas cambiar? Conservamos la sede y el servicio."));
        }
        async Task<string> VerifyProposal(TimeZoneInfo zone, TimePreference? preference)
        {
            var selected = management!.Selected!;
            var target = management.ProposedTime == null ? (DateTime?)null : ResolveUtc(management.ProposedDate!, management.ProposedTime, zone);
            if (management.ProposedTime != null && target == null) return await Ask("La hora local es inválida o ambigua. Indica otra hora; la cita original sigue sin cambios.");
            if (target <= clock.UtcNow) return await Ask("La fecha u hora propuesta ya pasó. Indica una fecha y hora futuras.");
            if (target == selected.StartTime) return await Ask("La fecha y hora coinciden con tu cita actual. Indica el cambio que deseas realizar.");
            var date = DateTime.ParseExact(management.ProposedDate!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var schedule = (await hours.GetBusinessHoursAsync(workspace, selected.LocationId, ct)).FirstOrDefault(h => h.DayOfWeek == (int)date.DayOfWeek);
            if (schedule == null) return await Ask("No hay un horario comercial configurado para ese día en la sede de tu cita. Indica otra fecha o solicita un asesor.");
            if (schedule.IsClosed) return await Ask("La sede no atiende ese día. Indica otra fecha; tu cita original sigue sin cambios.");
            var slots = (await engine.GetAvailabilityForExistingAsync(workspace, contact, selected.Id, date, ct)).Where(s => s.IsAvailable).OrderBy(s => s.StartTime).ToArray();
            if (preference != null) slots = new TimePreferenceResolver().FilterAvailableSlots(slots, DateOnly.FromDateTime(date), zone, preference).ToArray();
            if (target != null && slots.Any(s => s.StartTime == target))
            {
                management.Stage = "Confirm"; management.Slots.Clear(); management.PendingCommand = null;
                return await Ask($"Cita actual: {Local(selected.StartTime, zone)}. Propuesta: {Local(target.Value, zone)}. Se conserva la sede y el servicio. ¿Confirmas el cambio? Responde sí confirmo.");
            }
            management.Stage = "Change"; management.PendingCommand = null; management.Slots = slots.Take(5).ToList();
            return await Ask(management.Slots.Count == 0 ? "No encontré horarios libres para esa fecha y preferencia. Indica otra fecha; la cita original sigue sin cambios."
                : "Estas son alternativas realmente disponibles. ¿Cuál prefieres?\n" + string.Join("\n", management.Slots.Select((s, index) => $"{index + 1}. {Local(s.StartTime, zone)}")));
        }
        async Task<string> Ask(string question)
        {
            if (context.Information != null) { context.Information.Clarification = null; context.Information.Locations.Clear(); }
            management!.NextQuestion = question; await Save(); return question;
        }
        async Task<string> Stop(string response) { FinishManagement(); await Save(); return response; }
        void FinishManagement()
        {
            if (management != null && context.CurrentGoal == "RESERVATION_MANAGEMENT")
            { context.CurrentGoal = management.SuspendedGoal; context.CurrentStep = management.SuspendedStep; }
            if (management?.SuspendedDraftExpired == true && context.CurrentGoal != null) context.PendingRecoveryAction = "EXPIRE";
            context.ReservationManagement = null;
        }
        Task Save() => contexts.SaveContextAsync(workspace, contact, context, ct);
    }

    private static string Local(DateTime utc, TimeZoneInfo zone) => TimeZoneInfo.ConvertTimeFromUtc(utc, zone).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
    private static DateTime? ResolveUtc(string date, string time, TimeZoneInfo zone)
    {
        var local = DateTime.ParseExact(date + " " + time, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None);
        return zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local) ? null : TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone);
    }
}
