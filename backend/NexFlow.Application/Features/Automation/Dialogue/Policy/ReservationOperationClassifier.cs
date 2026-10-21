using NexFlow.Application.Features.Automation.Dialogue.Understanding;
using NexFlow.Application.Features.Reservations;

namespace NexFlow.Application.Features.Automation.Dialogue.Policy;

// Classifies operation scope only. It cannot choose a reservation, authorize a contact or execute a mutation.
public static class ReservationOperationClassifier
{
    public static bool RequiresHumanVerification(string text)
    {
        var words = SpanishTextNormalizer.Normalize(text).Split(' ');
        return words.Any(w => w is "compartido" or "compartimos" or "ajena" or "madre" or "padre" or "esposa" or "esposo" or "hijo" or "hija")
            || SpanishTextNormalizer.Normalize(text).Contains("otra persona", StringComparison.Ordinal);
    }

    public static ReservationOperationKind Classify(string text, bool activeDraft)
    {
        var normalized = SpanishTextNormalizer.Normalize(text);
        var words = normalized.Split(' ');
        var noun = words.Any(w => w is "reserva" or "reservacion" or "cita" or "reservas" or "citas");
        var create = words.Any(w => w is "reservar" or "agendar")
            || noun && words.Any(w => w is "nueva" or "nuevo" or "crear");
        var change = words.Any(w => w is "editar" or "modificar" or "cambiar" or "reagendar" or "reprogramar");
        var cancel = words.Any(w => w is "cancelar" or "cancela" or "anular");
        var view = words.Any(w => w is "consultar" or "revisar" or "ver" or "cuando" or "hora" or "estado");
        if (!noun && !(activeDraft && change)) return create && !words.Contains("no") ? ReservationOperationKind.CreateReservation : ReservationOperationKind.None;
        if (words.Contains("no")) return ReservationOperationKind.None;
        if (create && (change || cancel || words.Any(w => w is "consultar" or "revisar" or "ver"))) return ReservationOperationKind.AmbiguousReservationOperation;
        var registered = words.Any(w => w is "registrada" or "confirmada" or "existente" or "hecha")
            || normalized.Contains("ya reserve", StringComparison.Ordinal);
        var draft = words.Any(w => w is "borrador" or "preparacion");
        if (draft && registered) return ReservationOperationKind.AmbiguousReservationOperation;
        if (activeDraft && !registered && (change || cancel)) return ReservationOperationKind.EditActiveDraft;
        if (cancel && noun) return ReservationOperationKind.CancelExistingReservation;
        if (change && noun) return ReservationOperationKind.RescheduleExistingReservation;
        if (view && noun && (registered || words.Any(w => w is "mi" or "mis"))) return ReservationOperationKind.ViewExistingReservation;
        if (noun && words.Any(w => w is "mi" or "mis") && !create) return ReservationOperationKind.AmbiguousReservationOperation;
        return create ? ReservationOperationKind.CreateReservation : ReservationOperationKind.None;
    }
}
