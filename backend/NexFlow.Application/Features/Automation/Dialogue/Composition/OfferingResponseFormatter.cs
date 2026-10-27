using NexFlow.Application.Features.Automation.Dialogue.Contracts;

namespace NexFlow.Application.Features.Automation.Dialogue.Composition;

public static class OfferingResponseFormatter
{
    public static VerifiedResponseLayout? Realization(OfferingQueryPlan query)
        => query.Question == OfferingQuestion.Comparison && !query.Broad && query.Clarification == null
            && query.Offerings.Count is >= 2 and <= 5 && query.Offerings.All(p => string.IsNullOrWhiteSpace(p.Description))
                ? new("Estas son sus características registradas:", query.Offerings.Select((p, i) =>
                    new VerifiedResponseBlock("f" + i, Line(p, query.Broad))), "No hay otras diferencias verificadas que pueda afirmar.") : null;

    private static string Line(VerifiedOffering p, bool broad)
    {
        var line = p.Type == "SERVICE" ? $"• *{p.Name}*\n  {Money(p.Currency, p.PriceMinorUnits)}"
            : $"• {p.Name}: {p.Currency} {p.PriceMinorUnits / 100m:0.00}";
        if (p.DurationMinutes.HasValue) line += $" · {p.DurationMinutes.Value} min";
        if (!string.IsNullOrWhiteSpace(p.Description) && !broad) line += ". " + p.Description;
        return line;
    }

    public static string Format(OfferingQueryPlan query)
    {
        if (query.Clarification != null) return query.Clarification;
        if (query.Offerings.Count == 0) return "No tengo una opción activa registrada que coincida con tu consulta.";
        if (Realization(query) is { } verifiedLayout) return verifiedLayout.CanonicalText;
        var facts = query.Offerings.Take(5).Select(p => Line(p, query.Broad));
        var text = "*" + (query.Type == "PRODUCT" ? "Productos del catálogo" : "Servicios del negocio") + "*\n\n" + string.Join("\n\n", facts);
        if (query.PageRestarted) text = "El catálogo cambió. Esta es la primera página actualizada.\n\n" + text;
        if (query.Broad && query.PageOffset + query.Offerings.Count < query.TotalCount)
            text += "\n\nEstas son algunas opciones. Puedes pedir «Ver más» para continuar.";
        if (query.Type == "SERVICE" && query.Broad)
            text += query.LocationId == null ? "\n\nEl catálogo general puede incluir servicios de distintas sedes. Para reservar, comprobaré el servicio y la hora en la sede que elijas."
                : "\n\nEstos servicios corresponden a la sede consultada. Una reserva requiere comprobar disponibilidad para la fecha y hora elegidas.";
        if (query.Question is OfferingQuestion.Stock || query.Question == OfferingQuestion.Availability && query.Type == "PRODUCT")
            text += "\n\nEsto confirma su presencia en el catálogo, no existencias de stock ni una entrega garantizada; el negocio debe confirmarlas.";
        if (query.Question == OfferingQuestion.Availability && query.Type == "SERVICE")
            text += query.LocationId == null ? "\nPuedo verificar en qué sede se ofrece; un horario de reserva requiere consultar la agenda."
                : "\nEl servicio figura para la sede indicada. Esto no confirma una cita ni un horario disponible.";
        if (query.Question == OfferingQuestion.Recommendation)
            text = "Puedo compartir los servicios registrados; no tengo una recomendación profesional validada.\n" + text;
        if (query.Question == OfferingQuestion.Comparison)
            text = "Estas son sus características registradas:\n" + text + "\nNo hay otras diferencias verificadas que pueda afirmar.";
        return text;
    }
    public static string Money(string currency, long minorUnits) => $"{(currency == "PEN" ? "S/" : currency)} {(minorUnits / 100m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}";
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
