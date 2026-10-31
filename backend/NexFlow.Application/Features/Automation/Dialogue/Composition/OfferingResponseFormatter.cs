using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Services.DTOs;
using NexFlow.Application.Features.Business;

namespace NexFlow.Application.Features.Automation.Dialogue.Composition;

public static class OfferingResponseFormatter
{
    // Facts from the current workspace only; no inferred exclusivity or availability.
    public static string? ServiceScope(IEnumerable<ServiceDto> services, IEnumerable<LocationDto> locations, string? locationId)
    {
        var sites = locations.Where(l => l.Id != null).ToArray();
        var site = sites.SingleOrDefault(l => l.Id == locationId);
        if (site == null) return null;
        var all = services.Where(s => s.IsActive && s.Type == "SERVICE").ToArray();
        bool Reservable(ServiceDto s) => s.RequiresReservation && s.DurationInMinutes >= 5;
        bool At(ServiceDto s, string id) => VerifiedOffering.Capture(s).AtLocation(id);
        var here = all.Where(s => Reservable(s) && At(s, site.Id!)).ToArray();
        var text = $"El catálogo general actual tiene {all.Length} servicios; {here.Length} "
            + (here.Length == 1 ? "se puede reservar" : "se pueden reservar") + $" en {site.Name}.";
        foreach (var other in all.Except(here).Take(5))
        {
            var otherSites = Reservable(other) ? sites.Where(l => At(other, l.Id!)).Select(l => l.Name).Take(5).ToArray() : [];
            var reason = !other.RequiresReservation ? "no requiere reserva según su configuración actual."
                : other.DurationInMinutes < 5 ? "no tiene una duración válida configurada para reservar; puedes consultarlo con el negocio."
                : otherSites.Length > 0 ? "se ofrece para reservas en " + string.Join(", ", otherSites) + "."
                : "no figura como reservable en esta sede; puedes consultarlo con el negocio.";
            text += "\n\n• " + other.Name + ": " + reason;
        }
        return text;
    }

    public static VerifiedResponseLayout? Realization(OfferingQueryPlan query)
        => query.Question == OfferingQuestion.Comparison && !query.Broad && query.Clarification == null
            && query.Offerings.Count is >= 2 and <= 5 && query.Offerings.All(p => string.IsNullOrWhiteSpace(p.Description))
                ? new("Estas son sus características registradas:", query.Offerings.Select((p, i) =>
                    new VerifiedResponseBlock("f" + i, Line(p, query.Broad))), "No hay otras diferencias verificadas que pueda afirmar.") : null;

    private static string Line(VerifiedOffering p, bool broad)
    {
        var line = $"• *{p.Name}*\n  {Money(p.Currency, p.PriceMinorUnits)}";
        if (p.DurationMinutes.HasValue) line += $" · {p.DurationMinutes.Value} min";
        if (!string.IsNullOrWhiteSpace(p.Description) && !broad) line += ". " + p.Description;
        return line;
    }

    public static string Format(OfferingQueryPlan query)
    {
        if (query.Clarification != null) return query.Clarification;
        if (query.Broad && query.Type == "PRODUCT" && query.Offerings.Count == 0)
            return query.LocationId == null
                ? "En este momento no hay productos activos publicados en el catálogo del negocio."
                : "En este momento no hay productos activos publicados en el catálogo de la sede consultada.";
        if (query.Offerings.Count == 0) return "No tengo una opción activa registrada que coincida con tu consulta.";
        if (Realization(query) is { } verifiedLayout) return verifiedLayout.CanonicalText;
        var facts = query.Offerings.Take(5).Select(p => Line(p, query.Broad));
        var title = query.Type == "PRODUCT" ? "Productos del catálogo"
            : query.Question == OfferingQuestion.Availability ? "Servicios reservables" : "Servicios del negocio";
        var text = "*" + title + "*\n\n" + string.Join("\n\n", facts);
        if (query.PageRestarted) text = "El catálogo cambió. Esta es la primera página actualizada.\n\n" + text;
        if (query.Broad && query.PageOffset + query.Offerings.Count < query.TotalCount)
            text += "\n\nEstas son algunas opciones. Puedes pedir «Ver más» para continuar.";
        if (query.Type == "SERVICE" && query.Broad)
            text += query.LocationId == null ? "\n\nEl catálogo general puede incluir servicios de distintas sedes. Para reservar, comprobaré el servicio y la hora en la sede que elijas."
                : "\n\nEstos servicios corresponden a la sede consultada. Una reserva requiere comprobar disponibilidad para la fecha y hora elegidas.";
        // The catalog verifies publication, never physical inventory. A specific
        // product fact must preserve that limit even on the zero-token path.
        if (query.Question is OfferingQuestion.Stock || query.Type == "PRODUCT"
            && (query.Question == OfferingQuestion.Availability || !query.Broad && query.Question == OfferingQuestion.Facts))
            text += "\n\nEsto confirma su presencia en el catálogo, no existencias de stock ni una entrega garantizada; el negocio debe confirmarlas.";
        if (query.Question == OfferingQuestion.Availability && query.Type == "SERVICE" && !query.Broad)
            text += query.LocationId == null ? "\nPuedo verificar en qué sede se ofrece; un horario de reserva requiere consultar la agenda."
                : "\nEl servicio figura para la sede indicada. Esto no confirma una cita ni un horario disponible.";
        if (query.Question == OfferingQuestion.Recommendation)
            text = "Puedo compartir los servicios registrados; no tengo una recomendación profesional validada.\n" + text;
        if (query.Question == OfferingQuestion.Comparison)
            text = "Estas son sus características registradas:\n" + text + "\nNo hay otras diferencias verificadas que pueda afirmar.";
        return text;
    }
    public static string Money(string currency, long minorUnits, bool useSymbol = true) => $"{(useSymbol && currency == "PEN" ? "S/" : currency)} {(minorUnits / 100m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}";
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
