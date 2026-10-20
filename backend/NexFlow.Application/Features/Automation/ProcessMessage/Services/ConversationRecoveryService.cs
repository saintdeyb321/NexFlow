using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Business.Offerings;
using NexFlow.Application.Features.Reservations;
using NexFlow.Domain.Enums;
using NexFlow.Application.Features.Automation.Dialogue.Understanding;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services;

public sealed record RecoveryDecision(string? Response = null, AiInterpretation? Interpretation = null);
public interface IConversationRecoveryService
{
    Task<RecoveryDecision?> RecoverAsync(Guid workspace, string phone, string conversation, ProcessIncomingMessageCommand request, ConversationContextDto context, HashSet<string> modules, CancellationToken ct);
    Task<RecoveryDecision> ClarifyAsync(Guid workspace, string phone, string source, ConversationContextDto context, HashSet<string> modules, string introduction, CancellationToken ct);
    Task<string> DecorateAsync(Guid workspace, string phone, ConversationContextDto context, HashSet<string> modules, string response, CancellationToken ct);
    Task<bool> AcquireAiAsync(Guid workspace, string phone, string source, CancellationToken ct);
}

public sealed class ConversationRecoveryService(IContextRecoveryService contexts, IConversationStateRepository states,
    IOfferingService offerings, ILocationRepository locations, IBusinessHoursRepository hours, IReservationEngine reservations,
    IClock clock, IOptions<AssistantRecoveryOptions> options, IBusinessProfileRepository profiles) : IConversationRecoveryService
{
    private AssistantRecoveryOptions Limits => options.Value;
    public static string Normalize(string text) => string.Join(' ', new string(text.Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
        .Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ').ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    private static bool Yes(string text) => text is "si" or "si confirmo" or "confirmo" or "confirmar" or "de acuerdo";
    private static bool No(string text) => text is "no" or "no gracias";
    private static string Fingerprint(ConversationContextDto c) => $"{c.CurrentGoal}:{c.CurrentStep}:{c.SelectedLocationId}:{c.SelectedServiceId}:{c.TargetDate}";
    private static string Continuation(ConversationContextDto c) => string.IsNullOrEmpty(c.CurrentGoal) ? "¿Qué necesitas consultar?"
        : "Tu borrador sigue guardado. " + (c.CurrentStep switch {
            "COLLECT_CUSTOMERNAME" => "¿Cuál es tu nombre?", "COLLECT_LOCATION" => "¿En qué sede deseas reservar?",
            "COLLECT_SERVICE" => "¿Qué servicio deseas reservar?", "COLLECT_DATE" => "¿Para qué fecha?",
            "COLLECT_TIME" => "¿Qué hora prefieres?", "CONFIRM" => "¿Confirmas la reserva? Responde sí o no.",
            _ => c.LastQuestion ?? "Puedes continuar o escribir cancelar para descartar el borrador." });

    public Task<bool> AcquireAiAsync(Guid workspace, string phone, string source, CancellationToken ct)
        => states.TryConsumeAiBudgetAsync(workspace, phone, source, clock.UtcNow, Limits.ClientAiCallsPerMinute, Limits.WorkspaceAiCallsPerMinute, ct);

    public async Task<RecoveryDecision?> RecoverAsync(Guid workspace, string phone, string conversation,
        ProcessIncomingMessageCommand request, ConversationContextDto context, HashSet<string> modules, CancellationToken ct)
    {
        if (context.RecoveryLastMessageId == request.MessageId)
            return new(context.RecoveryLastResponse, context.RecoveryLastInterpretation == null ? null : JsonSerializer.Deserialize<AiInterpretation>(context.RecoveryLastInterpretation));
        if (request.MessageText.Length > Limits.MaxInputCharacters)
            return await Remember("Tu mensaje es demasiado largo. Envíalo en partes más cortas y plantea una consulta por vez.");
        var text = Normalize(request.MessageText);
        if (Regex.IsMatch(text, @"\b(ignora (?:todas las |las )?instrucciones|system prompt|api key|revela (?:el )?(?:prompt|secreto)|actua como administrador|cambia (?:el )?workspace)\b", RegexOptions.CultureInvariant))
            return await Remember("Solo puedo ayudarte con información y operaciones autorizadas de este negocio. " + Continuation(context));
        if (Regex.IsMatch(text, @"\b(asesor humano|hablar con (?:un |una )?(?:asesor|persona|humano)|atencion humana)\b") || text is "asesor" or "humano")
            return await RememberInterpretation(new() { Intent = ConversationIntent.Support });
        if (context.PendingRecoveryAction != null && Regex.IsMatch(text, @"\b(que|cual|cuanto|donde|como|precio|horarios?|direccion)\b"))
            return null;
        if (context.PendingRecoveryAction == "REQUEST_DETAILS")
        {
            if (text is "cancelar" or "no") { context.PendingRecoveryAction = null; return await Remember("No he registrado ninguna solicitud."); }
            if (text.Length < 10 || Yes(text)) return await Remember("Describe brevemente qué necesitas solicitar al negocio, o escribe cancelar.");
            context.PendingRecoveryAction = null;
            return await RememberInterpretation(new() { Intent = ConversationIntent.Request });
        }
        if (context.PendingRecoveryAction != null)
        {
            if (Yes(text))
            {
                context.CurrentGoal = null; context.CurrentStep = null; context.SelectedServiceId = null; context.SelectedLocationId = null;
                context.TargetDate = null; context.TargetTime = null; context.OrderDraftItems.Clear(); context.MissingFields.Clear(); context.LastQuestion = null;
                context.ReservationCommitSourceMessageId = null; context.RecoveryOptionsOffset = 0; context.RecoveryOptionsContext = null;
                context.PendingCustomerName = null; context.PendingServiceIds.Clear(); context.PendingDialogueSlots.Clear(); context.PreferredTime = null;
                context.PendingRecoveryAction = null; context.RecoveryOptions.Clear(); context.ClarificationAttempts = 0;
                return await Remember("He descartado únicamente el borrador. No se ha cancelado ninguna reserva ni pedido registrado.");
            }
            if (No(text)) { context.PendingRecoveryAction = null; return await Remember(Continuation(context)); }
            return await Remember("Confirma si deseas descartar el borrador: responde sí o no. Para hablar con una persona escribe asesor.");
        }
        if (!string.IsNullOrWhiteSpace(context.CurrentGoal) && context.LastUpdated < clock.UtcNow.AddHours(-Limits.ContextExpiryHours))
        {
            context.PendingRecoveryAction = "EXPIRE";
            return await Remember("El borrador lleva tiempo sin actualizarse. ¿Quieres descartarlo para empezar de nuevo? Responde sí o no; no realizaré operaciones con esos datos mientras confirmas.");
        }
        if (text is "cancelar" or "cancela" or "cancelar reserva" or "cancelar pedido" or "cancelar borrador" or "cancela la reserva" or "cancela el pedido")
        {
            if (string.IsNullOrEmpty(context.CurrentGoal)) return await Remember("No hay un borrador activo. Para revisar una reserva o pedido registrado escribe asesor.");
            context.PendingRecoveryAction = "CANCEL";
            return await Remember("¿Quieres descartar el borrador actual? Responde sí o no. Esto no cancela una reserva o pedido ya registrado.");
        }
        if (context.CurrentStep == "CONFIRM" && context.CurrentGoal is "BOOKING" or "RESERVATION" && Yes(text))
            return await RememberInterpretation(new() { Intent = ConversationIntent.Reservation, Confirmed = true });
        if (No(text) && context.CurrentStep == "CONFIRM") return await Remember("No he registrado la reserva. Puedes corregir la sede, servicio, fecha u hora, o escribir cancelar para descartar el borrador.");
        if (Regex.IsMatch(text, @"^(hola+|holi|buenas|buenos dias|buenas tardes|buenas noches|hey|ola)$"))
            return string.IsNullOrEmpty(context.CurrentGoal) ? await Menu("¡Hola! Puedo ayudarte con estas opciones:") : await Remember("¡Hola! " + Continuation(context));
        if (text is "adios" or "chau" or "hasta luego" or "gracias" or "muchas gracias")
            return await Remember("¡Gracias por escribirnos! " + (string.IsNullOrEmpty(context.CurrentGoal) ? "Aquí estaremos cuando necesites información del negocio." : Continuation(context)));
        if (text is "quiero informacion" or "informacion" or "ayudame" or "ayuda" or "que ofrecen" or "menu" or "opciones")
        { context.RecoveryOptionsOffset = 0; return await Menu("Estas son las opciones disponibles:"); }
        if (text is "ver mas" or "mas opciones" or "otros servicios")
        {
            if (context.RecoveryOptionsContext == Fingerprint(context) && context.RecoveryHasMore) context.RecoveryOptionsOffset += 5;
            return await Menu("Estas son las opciones disponibles:");
        }
        var index = SelectionIndex(text);
        if (index == 0 && context.RecoveryOptionsContext == Fingerprint(context))
        {
            var matches = context.RecoveryOptions.Select((o, i) => (Option: o, Index: i + 1)).Where(o => Normalize(o.Option.Label) == text).ToArray();
            index = matches.Length > 1 ? -1 : matches.Length == 1 ? matches[0].Index : 0;
        }
        if (text is "esa opcion" or "esa" || Yes(text)) index = context.RecoveryOptions.Count == 1 ? 1 : -1;
        if (text is "la otra" or "otra" or "esa otra") index = -1;
        if (index != 0)
        {
            if (context.LastUpdated < clock.UtcNow.AddHours(-Limits.ContextExpiryHours))
                return await Menu("La lista anterior ha caducado. Elige una opción vigente:");
            if (context.RecoveryOptionsContext != Fingerprint(context) || index < 1 || index > context.RecoveryOptions.Count)
                return await ClarifyAsync(workspace, phone, request.MessageId, context, modules,
                    context.RecoveryOptions.Count == 0 ? "Necesito una respuesta concreta para el paso actual." : "Necesito que elijas una opción de la lista actual.", ct);
            var selected = context.RecoveryOptions[index - 1];
            var available = await BuildOptions(workspace, context, modules, ct);
            if (!available.Any(o => o.Key == selected.Key && o.Kind == selected.Kind)) return await Menu("Esa opción ya no está disponible. Elige una de estas:");
            context.RecoveryOptions.Clear(); context.ClarificationAttempts = 0;
            var interpretation = new AiInterpretation { Intent = selected.Kind == "Action" ? Enum.Parse<ConversationIntent>(selected.Key) : ConversationIntent.Reservation,
                QueryKind = OfferingQueryKind.Broad, Location = selected.Kind == "Location" ? selected.Key : null,
                Service = selected.Kind == "Service" ? selected.Key : null, Time = selected.Kind == "Time" ? selected.Key : null };
            if (!string.IsNullOrEmpty(context.CurrentGoal) && selected.Kind == "Action" && interpretation.Intent is ConversationIntent.Order or ConversationIntent.Reservation)
                return await Remember("Ya tienes un borrador activo. " + Continuation(context));
            if (selected.Kind == "Action" && interpretation.Intent == ConversationIntent.Request)
            { context.PendingRecoveryAction = "REQUEST_DETAILS"; return await Remember("¿Qué necesitas solicitar al negocio? Describe tu solicitud antes de registrarla."); }
            return await RememberInterpretation(interpretation);
        }
        if (No(text)) return await Remember(Continuation(context));
        if (text.Contains("horario") && (text.Contains("que") || text.Contains("cual") || text == "horarios")) return await RememberInterpretation(new() { Intent = ConversationIntent.BusinessHours });
        if (text is "ubicacion" or "direccion" || text.Contains("donde estan") || text.Contains("donde atienden")) return await RememberInterpretation(new() { Intent = ConversationIntent.Location });
        return null;

        Task<RecoveryDecision> Remember(string response) => SaveAsync(workspace, phone, request.MessageId, context, new(response), ct);
        Task<RecoveryDecision> RememberInterpretation(AiInterpretation interpretation) => SaveAsync(workspace, phone, request.MessageId, context, new(Interpretation: interpretation), ct);
        async Task<RecoveryDecision> Menu(string intro)
        {
            var response = await PrepareOptionsAsync(workspace, context, modules, intro, ct);
            return await Remember(response);
        }
    }

    private static int SelectionIndex(string text)
    {
        // Match the whole utterance. Quantities, identifiers, dates, times and
        // mixed requests must never be extracted as a menu index.
        var match = Regex.Match(text,
            @"^(?:(?:escojo|elijo|selecciono) (?:la )?(?:(?:opcion|numero) )?|quiero la opcion |(?:la )?(?:(?:opcion|numero) )?)([0-9]{1,2}|uno|dos|tres|cuatro|cinco|primera|segunda|tercera|cuarta|quinta)$",
            RegexOptions.CultureInvariant);
        if (!match.Success) return 0;
        var index = match.Groups[1].Value switch
        {
            "uno" or "primera" => 1, "dos" or "segunda" => 2, "tres" or "tercera" => 3,
            "cuatro" or "cuarta" => 4, "cinco" or "quinta" => 5,
            var number => int.Parse(number, CultureInfo.InvariantCulture)
        };
        return index == 0 ? -1 : index;
    }

    public async Task<RecoveryDecision> ClarifyAsync(Guid workspace, string phone, string source, ConversationContextDto context, HashSet<string> modules, string introduction, CancellationToken ct)
    {
        if (++context.ClarificationAttempts >= Limits.MaxClarifications)
            return await SaveAsync(workspace, phone, source, context, new(Interpretation: new() { Intent = ConversationIntent.Support }), ct);
        return await SaveAsync(workspace, phone, source, context, new(await PrepareOptionsAsync(workspace, context, modules, introduction, ct)), ct);
    }

    public async Task<string> DecorateAsync(Guid workspace, string phone, ConversationContextDto context, HashSet<string> modules, string response, CancellationToken ct)
    {
        var decorated = await PrepareOptionsAsync(workspace, context, modules, response, ct, replacePicker: true);
        await contexts.SaveContextAsync(workspace, phone, context, ct);
        return decorated;
    }

    private async Task<string> PrepareOptionsAsync(Guid workspace, ConversationContextDto context, HashSet<string> modules, string response, CancellationToken ct, bool replacePicker = false)
    {
        if (context.RecoveryOptionsContext != Fingerprint(context)) context.RecoveryOptionsOffset = 0;
        var available = await BuildOptions(workspace, context, modules, ct, paginate: false);
        context.RecoveryOptions = available.Skip(context.RecoveryOptionsOffset).Take(5).ToList();
        context.RecoveryOptionsContext = Fingerprint(context);
        var noOptions = context.CurrentStep is "COLLECT_CUSTOMERNAME" or "COLLECT_DATE" or "CONFIRM" ? Continuation(context)
            : "No hay opciones vigentes para este paso. Puedes indicar otra sede o fecha, o escribir asesor para solicitar ayuda humana.";
        if (context.RecoveryOptions.Count == 0) return response + "\n" + noOptions;
        var menu = string.Join("\n", context.RecoveryOptions.Select((o, i) => $"{i + 1}. {o.Label}"))
                + (context.RecoveryHasMore ? "\nHay más opciones disponibles: escribe ver más. Para solicitar ayuda humana escribe asesor." : "")
                + (context.RecoveryOptionsOffset > 0 ? "\nPara volver al inicio escribe menú." : "");
        return replacePicker ? ComposePickerResponse(response, available, menu)
            : response + "\n" + menu + "\n¿Cuál opción eliges? Escribe su número.";
    }

    private static string ComposePickerResponse(string response, List<ConversationMenuOption> available, string menu)
    {
        // Only replace exact, authoritative option lines. Other lists, explanations
        // and questions belong to the response and must survive the composition.
        var pickerLines = available.Select(o => "• " + o.Label).ToHashSet(StringComparer.Ordinal);
        var lines = response.Replace("\r\n", "\n").Split('\n');
        var finalLine = Array.FindLastIndex(lines, line => !string.IsNullOrWhiteSpace(line));
        var hasQuestion = finalLine >= 0 && lines[finalLine].TrimEnd().EndsWith('?');
        var insertion = Array.FindIndex(lines, line => pickerLines.Contains(line.Trim()));
        if (insertion < 0) insertion = hasQuestion ? finalLine : lines.Length;
        var parts = new List<string>();
        for (var i = 0; i <= lines.Length; i++)
        {
            if (i == insertion) parts.Add(menu);
            if (i < lines.Length && !pickerLines.Contains(lines[i].Trim())) parts.Add(lines[i]);
        }
        if (!hasQuestion) parts.Add("¿Cuál opción eliges? Escribe su número.");
        return string.Join("\n", parts);
    }

    private async Task<List<ConversationMenuOption>> BuildOptions(Guid workspace, ConversationContextDto context, HashSet<string> modules, CancellationToken ct, bool paginate = true)
    {
        var result = new List<ConversationMenuOption>();
        if (context.CurrentGoal is "RESERVATION" or "BOOKING" && modules.Contains("RESERVATIONS") && modules.Contains("SERVICES"))
        {
            if (context.CurrentStep == "COLLECT_LOCATION") result.AddRange((await locations.GetLocationsAsync(workspace, ct)).Where(l => l.Id != null).OrderBy(l => l.Name).ThenBy(l => l.Id).Select(l => new ConversationMenuOption(l.Id!, l.Name, "Location")));
            else if (context.CurrentStep == "COLLECT_SERVICE" && context.SelectedLocationId != null)
                result.AddRange((await offerings.GetServicesAsync(workspace, context.SelectedLocationId, null, ct)).Where(s => s.IsActive && s.RequiresReservation && s.DurationInMinutes >= 5).OrderBy(s => s.Name).ThenBy(s => s.Id).Select(s => new ConversationMenuOption(s.Id, s.Name, "Service")));
            else if (context.CurrentStep == "COLLECT_TIME" && context.SelectedLocationId != null && context.SelectedServiceId != null && DateTime.TryParseExact(context.TargetDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                var profile = await profiles.GetProfileAsync(workspace, ct);
                TimeZoneInfo zone;
                try { zone = TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(profile?.TimeZone) ? "America/Lima" : profile.TimeZone); }
                catch (TimeZoneNotFoundException) { zone = TimeZoneInfo.FindSystemTimeZoneById("America/Lima"); }
                catch (InvalidTimeZoneException) { zone = TimeZoneInfo.FindSystemTimeZoneById("America/Lima"); }
                var available = (await reservations.GetAvailabilityAsync(workspace, context.SelectedLocationId, context.SelectedServiceId, date, ct)).Where(s => s.IsAvailable);
                if (context.PreferredTime != null) available = new TimePreferenceResolver().FilterAvailableSlots(available, DateOnly.FromDateTime(date), zone, context.PreferredTime);
                result.AddRange(available.OrderBy(s => s.StartTime).Select(s => TimeZoneInfo.ConvertTimeFromUtc(s.StartTime, zone).ToString("HH:mm", CultureInfo.InvariantCulture)).Distinct().Select(t => new ConversationMenuOption(t, t, "Time")));
            }
            else return result;
        }
        else
        {
            var products = modules.Contains("CATALOG") && (await offerings.GetProductsAsync(workspace, null, null, ct)).Any(p => p.IsActive);
            var services = modules.Contains("SERVICES") ? (await offerings.GetServicesAsync(workspace, null, null, ct)).Where(s => s.IsActive).ToList() : new();
            if (products) result.Add(new("ProductQuery", "Consultar productos", "Action"));
            if (services.Count != 0) result.Add(new("ServiceQuery", "Consultar servicios", "Action"));
            if (modules.Contains("RESERVATIONS") && services.Any(s => s.RequiresReservation && s.DurationInMinutes >= 5)) result.Add(new("Reservation", "Reservar un servicio", "Action"));
            if (modules.Contains("ORDERS") && products) result.Add(new("Order", "Preparar un pedido", "Action"));
            if (modules.Contains("LOCATIONS") && (await locations.GetLocationsAsync(workspace, ct)).Any()) result.Add(new("Location", "Consultar ubicaciones", "Action"));
            if (modules.Contains("BUSINESS_HOURS") && (await hours.GetBusinessHoursAsync(workspace, null, ct)).Any()) result.Add(new("BusinessHours", "Consultar horarios", "Action"));
            if (modules.Contains("REQUESTS")) result.Add(new("Request", "Registrar una solicitud", "Action"));
            result.Add(new("Support", "Hablar con un asesor", "Action"));
        }
        context.RecoveryOptionsOffset = Math.Clamp(context.RecoveryOptionsOffset, 0, result.Count == 0 ? 0 : (result.Count - 1) / 5 * 5);
        context.RecoveryHasMore = context.RecoveryOptionsOffset + 5 < result.Count;
        return paginate ? result.Skip(context.RecoveryOptionsOffset).Take(5).ToList() : result;
    }

    private async Task<RecoveryDecision> SaveAsync(Guid workspace, string phone, string source, ConversationContextDto context, RecoveryDecision decision, CancellationToken ct)
    {
        context.RecoveryLastMessageId = source; context.RecoveryLastResponse = decision.Response;
        context.RecoveryLastInterpretation = decision.Interpretation == null ? null : JsonSerializer.Serialize(decision.Interpretation);
        await contexts.SaveContextAsync(workspace, phone, context, ct); return decision;
    }
}
