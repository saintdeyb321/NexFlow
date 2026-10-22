using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.Knowledge;
using NexFlow.Application.Features.Automation.Dialogue.Policy;
using NexFlow.Application.Features.Reservations;

namespace NexFlow.Application.Features.Automation.Dialogue.Understanding;

// Recognizes request structure only. Data, options, permissions and delivery stay server-owned.
internal static class InformationalRequest
{
    internal static string DraftFingerprint(ConversationContextDto context)
        => $"{context.CurrentGoal}:{context.CurrentStep}:{context.SelectedLocationId}:{context.SelectedServiceId}:{context.TargetDate}:{context.TargetTime}";

    internal static KnowledgeTopic? PublicTopic(string text)
    {
        var normalized = SpanishTextNormalizer.Normalize(text);
        var words = normalized.Split(' ');
        if (words.Contains("y") && words.Any(w => w is "producto" or "productos" or "servicio" or "servicios" or "pdf" or "catalogo")) return null;
        var hours = words.Any(w => w is "horario" or "horarios")
            && (words.Any(w => w is "que" or "cual" or "cuales" or "tienen" or "tiene" or "pregunte" or "dije" or "informacion")
                || normalized.StartsWith("horario", StringComparison.Ordinal));
        if (hours || normalized is "dias de atencion" or "cuando abren" or "cuando cierran" or "hasta que hora") return KnowledgeTopic.BusinessHours;
        if (words.Contains("informacion") && !words.Any(w => w is "producto" or "productos" or "servicio" or "servicios" or "pedido" or "pedidos" or "horario" or "horarios")
            && (!words.Any(w => w.StartsWith("reserv", StringComparison.Ordinal) || w == "cita") || normalized.Contains("no quiero", StringComparison.Ordinal)))
            return KnowledgeTopic.Profile;
        if (normalized is "quienes son" or "a que se dedican") return KnowledgeTopic.Profile;
        return null;
    }

    internal static bool ExistingReservation(string text, bool draft)
        => ReservationOperationClassifier.Classify(text, draft) is ReservationOperationKind.ViewExistingReservation
            or ReservationOperationKind.RescheduleExistingReservation or ReservationOperationKind.CancelExistingReservation;

    internal static bool IsRepair(string text)
    {
        var words = SpanishTextNormalizer.Normalize(text).Split(' ');
        return words.Any(w => w is "pregunte" or "pedi" or "solicite" or "refiero")
            || words.Contains("no") && words.Contains("eso") && words.Any(w => w is "era" or "es" or "fue" or "dije")
            || words.Contains("hablo") && words.Contains("no")
            || ScopeCorrection(text) || words.Contains("eso") && words.Contains("ya") && words.Contains("dije");
    }

    // Resolve asserted/rejected subjects by clause, not by the first keyword in a correction.
    internal static KnowledgeTopic? RepairTopic(string text, bool rejected = false)
    {
        var subjects = new HashSet<KnowledgeTopic>();
        foreach (var clause in text.Replace(";", ",").Replace(".", ",").Split(','))
        foreach (var part in SpanishTextNormalizer.Normalize(clause).Split(" sino ", StringSplitOptions.None))
        {
            var words = part.Split(' ');
            if (words.Contains("no") != rejected) continue;
            if (words.Any(w => w is "horario" or "horarios" or "abren" or "cierran")) subjects.Add(KnowledgeTopic.BusinessHours);
            if (words.Any(w => w is "direccion" or "direcciones" or "ubicacion" or "ubicaciones")) subjects.Add(KnowledgeTopic.Locations);
            if (words.Any(w => w is "servicio" or "servicios")) subjects.Add(KnowledgeTopic.Services);
            if (words.Any(w => w is "producto" or "productos")) subjects.Add(KnowledgeTopic.Products);
            if (words.Any(w => w is "perfil" or "empresa" or "clinica")) subjects.Add(KnowledgeTopic.Profile);
        }
        return subjects.Count == 1 ? subjects.Single() : null;
    }

    internal static bool DraftServicePrice(string text)
    {
        var words = SpanishTextNormalizer.Normalize(text).Split(' ');
        return words.Any(w => w is "precio" or "cuesta")
            && words.All(w => w is "antes" or "dime" or "me" or "puedes" or "decir" or "cuanto" or "cuesta" or "cual" or "es"
                or "el" or "precio" or "que" or "tiene" or "ese" or "este" or "servicio" or "por" or "favor");
    }

    internal static bool ScopeCorrection(string text)
    {
        var words = SpanishTextNormalizer.Normalize(text).Split(' ');
        return words.Contains("sede") && words.Any(w => w is "preguntaste" or "preguntaron" or "cual");
    }

    internal static void Remember(ConversationContextDto context, KnowledgeTopic topic, KnowledgeResult result, DateTime now)
        => context.Information = new() {
            Topic = topic.ToString(), LocationId = result.LocationId,
            Clarification = result.LocationChoices.Count > 0 ? "Location" : null,
            Locations = result.LocationChoices.Take(5).Select(l => new ConversationMenuOption(l.Id, l.Name, "InformationLocation")).ToList(),
            DraftFingerprint = DraftFingerprint(context), UpdatedAt = now
        };

    internal const string CatalogScopeQuestion = "¿Deseas el catálogo de productos o el de servicios?";
    internal static IReadOnlyList<NexFlow.Application.Abstractions.Cache.ConversationMenuOption> CatalogScopeChoices =>
        [new("ProductQuery", "Productos", "Action"), new("ServiceQuery", "Servicios", "Action")];
    internal static bool UnscopedCatalog(string text) => Catalog(text)
        && !OrderMentionResolver.Words(text).Any(w => w is "producto" or "productos" or "servicio" or "servicios");
    private static readonly HashSet<string> MenuWords = new(StringComparer.Ordinal) {
        "menu", "opciones", "disponibles", "disponible", "muestrame", "muestras", "mostrar", "muestra",
        "ver", "quiero", "quisiera", "necesito", "me", "que", "cuales", "son", "las", "el", "los", "la",
        "tengo", "tenemos", "puedo", "puede", "puedes", "hacer", "por", "aqui", "cosas", "ayudar",
        "a", "gestionar", "usar", "ayudas", "con", "favor", "porfa", "gracias"
    };

    internal static bool Menu(string text)
    {
        var words = OrderMentionResolver.Words(text);
        return words.Length > 0 && words.All(MenuWords.Contains)
            && (words.Any(w => w is "menu" or "opciones")
                || words.Contains("que") && (words.Contains("hacer") && words.Contains("puedo")
                    || words.Contains("ayudar") && words.Contains("gestionar")));
    }

    internal static bool Catalog(string text)
    {
        var words = OrderMentionResolver.Words(text);
        if (words.Contains("no") || OrderMentionResolver.IsHypothetical(text)) return false;
        var document = words.Any(w => w is "catalogo" or "folleto" or "pdf")
            || words.Contains("lista") && words.Contains("precios");
        var request = words.Any(w => w is "mandame" or "enviame" or "compartes" or "comparteme" or "compartir"
            or "pasar" or "pasas" or "pasame" or "ver" or "muestrame" or "muestras" or "tienes" or "tienen");
        var factual = words.Any(w => w is "cuanto" or "cuesta" or "cuestan" or "dura" or "duracion" or "diferencia");
        return document && request && !factual;
    }
}
