namespace NexFlow.Application.Features.Automation.Dialogue.Understanding;

// Recognizes request structure only. Data, options, permissions and delivery stay server-owned.
internal static class InformationalRequest
{
    internal const string CatalogScopeQuestion = "¿Deseas el catálogo de productos o el de servicios?";
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
