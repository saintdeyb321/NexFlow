using NexFlow.Application.Features.Automation.Dialogue.Contracts;

namespace NexFlow.Application.Features.Automation.Dialogue.Composition;

public static class OfferingResponseFormatter
{
    public static string Format(OfferingQueryPlan query)
    {
        if (query.Clarification != null) return query.Clarification;
        if (query.Offerings.Count == 0) return "No tengo una opción activa registrada que coincida con tu consulta.";
        var facts = query.Offerings.Take(5).Select(p => {
            var line = $"• {p.Name}: {p.Currency} {p.PriceMinorUnits / 100m:0.00}";
            if (p.DurationMinutes.HasValue) line += $"; duración: {p.DurationMinutes.Value} min";
            if (!string.IsNullOrWhiteSpace(p.Description) && !query.Broad) line += ". " + p.Description;
            return line;
        });
        var text = string.Join("\n", facts);
        if (query.Question is OfferingQuestion.Stock || query.Question == OfferingQuestion.Availability && query.Type == "PRODUCT")
            text += "\nEsto confirma su presencia en el catálogo, no existencias de stock ni una entrega garantizada; el negocio debe confirmarlas.";
        if (query.Question == OfferingQuestion.Availability && query.Type == "SERVICE")
            text += query.LocationId == null ? "\nPuedo verificar en qué sede se ofrece; un horario de reserva requiere consultar la agenda."
                : "\nEl servicio figura para la sede indicada. Esto no confirma una cita ni un horario disponible.";
        if (query.Question == OfferingQuestion.Recommendation)
            text = "Puedo compartir los servicios registrados; no tengo una recomendación profesional validada.\n" + text;
        if (query.Question == OfferingQuestion.Comparison)
            text = "Estas son sus características registradas:\n" + text + "\nNo hay otras diferencias verificadas que pueda afirmar.";
        return text;
    }
    public static string Capabilities(IReadOnlySet<string> modules)
    {
        var options = new List<string>();
        if (modules.Contains("CATALOG") && modules.Contains("ORDERS")) options.Add("preparar una solicitud de pedido");
        if (modules.Contains("SERVICES") && modules.Contains("RESERVATIONS")) options.Add("preparar una reserva");
        if (modules.Contains("LOCATIONS")) options.Add("consultar ubicaciones");
        if (modules.Contains("BUSINESS_HOURS")) options.Add("consultar horarios de atención");
        return options.Count == 0 ? "Puedes preguntarme por una opción concreta o solicitar un asesor." : "También puedo ayudarte a " + string.Join(", ",options) + ".";
    }
}
