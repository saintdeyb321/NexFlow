using System.Globalization;
using System.Text.RegularExpressions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;

namespace NexFlow.Application.Features.Automation.Dialogue.Understanding;

// A bounded local grammar for quantities and operations, not a source of business facts.
// Complex/unknown language remains available to the existing semantic interpreter.
public sealed class OrderMentionResolver
{
    private static readonly Dictionary<string, int> Numbers = new(StringComparer.Ordinal) {
        ["cero"]=0,["un"]=1,["una"]=1,["uno"]=1,["dos"]=2,["tres"]=3,["cuatro"]=4,["cinco"]=5,
        ["seis"]=6,["siete"]=7,["ocho"]=8,["nueve"]=9,["diez"]=10,["once"]=11,["doce"]=12,
        ["trece"]=13,["catorce"]=14,["quince"]=15,["dieciseis"]=16,["diecisiete"]=17,["dieciocho"]=18,
        ["diecinueve"]=19,["veinte"]=20,["treinta"]=30,["cuarenta"]=40,["cincuenta"]=50 };
    private static readonly HashSet<string> Noise = new(["quiero","quisiera","necesito","comprar","llevar","llevo","dame","agrega","agregar","anade","pon","poner","cambia","cambiar","mejor","quita","quitar","elimina","eliminar","saca","sacar","el","la","los","las","de","del","al","a","en","unidades","unidad","producto","productos","por","favor","me","puedes","apuntar","apuntas","vendes","pedir","pedido","si","y","esta","bien","entonces","igual","otro","otra","otros","otras"], StringComparer.Ordinal);
    public static bool IsQuestion(string text) => text.Contains('?') || PoliteRequest(text, information: true)
        || Words(text).Any(w => w is "cuanto" or "cuesta" or "cuestan" or "dura" or "tienen" or "hay" or "diferencia" or "compara" or "recomiendas");
    public static bool IsHypothetical(string text) => SpanishTextNormalizer.Normalize(text).Contains("y si ", StringComparison.Ordinal)
        || Words(text).Any(w => w is "hipoteticamente" or "supongamos" or "llevara" or "comprara");
    public static bool IsSubmit(string text)
    {
        var words = Words(text);
        return !words.Contains("no") && !IsHypothetical(text) && !IsQuestion(text)
            && (words.Any(w => w is "envia" or "enviar" or "envialo" or "finalizar" or "finaliza")
                && words.All(w => w is "esta" or "bien" or "si" or "entonces" or "por" or "favor" or "el" or "mi" or "pedido" or "envia" or "enviar" or "envialo" or "finalizar" or "finaliza" or "finalizarlo")
                || SpanishTextNormalizer.Normalize(text) == "finalizar");
    }
    public static bool IsCandidate(string text, ConversationContextDto state)
    {
        var words = Words(text);
        // Explicit independent information does not need commerce reads, even with
        // a draft/reference. Mixed questions mentioning offerings still resolve normally.
        var commercial=words.Any(w=>w is "producto" or "productos" or "servicio" or "servicios" or "catalogo" or "precio" or "cuesta" or "cuestan" or "pedido" or "stock" or "dura" or "duracion");
        var contextual=state.LastIntent?.StartsWith("OFFERING:v1:",StringComparison.Ordinal)==true
            && words.Any(w=>w is "ese" or "esa" or "eso" or "esos" or "estos" or "lo" or "anterior");
        if (!commercial && !contextual && (words.Any(w=>w is "horario" or "horarios" or "atienden" or "abren" or "cierran")
            || words.Contains("donde") && words.Any(w=>w is "sede" or "sedes" or "local" or "negocio")
            || words.Any(w=>w is "direccion" or "direcciones" or "ubicacion" or "ubicaciones"))) return false;
        if (state.CurrentGoal == "ORDER") return true;
        if (NexFlow.Application.Features.Automation.Dialogue.Policy.DialoguePolicy.HasContradictoryGoals(SpanishTextNormalizer.Normalize(text))) return false;
        if (words.Any(w => w.StartsWith("reserv", StringComparison.Ordinal) || w is "agendar" or "cita")
            && !words.Any(w=>w is "precio" or "cuesta" or "cuestan" or "dura" or "duracion" or "compara" or "diferencia")) return false;
        return IsQuestion(text) || PolitePurchase(text) || InformationalRequest.Catalog(text)
            || words.Any(w => w is "producto" or "productos" or "servicios" or "catalogo" or "pedido" or "comprar" or "agrega" or "quita" or "quiero" or "quisiera" or "necesito" or "llevo" or "dame" or "apuntas" or "apuntar" or "vendes" or "anade");
    }
    public static string[] Words(string text) => SpanishTextNormalizer.Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
    internal static bool PolitePurchase(string text) => PoliteRequest(text, information: false);
    private static bool PoliteRequest(string text, bool information)
    {
        var value = SpanishTextNormalizer.Normalize(text);
        var prefix = value.StartsWith("me das ", StringComparison.Ordinal) || value.StartsWith("agregame ", StringComparison.Ordinal);
        var asking = value.Split(' ').Any(w => w is "precio" or "precios" or "cuanto" or "cuesta" or "cuestan"
            or "informacion" or "duracion" or "horario" or "horarios" or "direccion");
        return prefix && asking == information;
    }

    public (IReadOnlyList<OrderEdit> Edits, string? Error) Resolve(string text, IReadOnlyList<VerifiedOffering> products,
        ConversationContextDto context, OfferingReference? reference, bool hypothetical = false)
    {
        text = CommercialCourtesy.Trim(text, products);
        var words = Words(text);
        if (words.Contains("no")) return ([], "No cambiaré el pedido. ¿Qué deseas consultar o corregir?");
        var kind = words.Any(w => w is "quita" or "quitar" or "elimina" or "eliminar" or "saca" or "sacar") ? OrderEditKind.Remove
            : words.Any(w => w is "mejor" or "cambia" or "cambiar" or "pon" or "poner") || hypothetical ? OrderEditKind.Set : OrderEditKind.Add;
        if (kind == OrderEditKind.Remove)
            products = products.Concat(context.OrderDraftItems.Where(i => products.All(p => p.Id != i.ProductId)).Select(i => new VerifiedOffering(i.ProductId,i.ProductName,null,0,"","PRODUCT","ALL",Array.AsReadOnly(Array.Empty<string>()),Array.AsReadOnly(Array.Empty<string>())))).ToArray();
        var parts = Split(text, products);
        var edits = new List<OrderEdit>();
        foreach (var part in parts)
        {
            var tokens = Words(part);
            // These are command prefixes only, never stop words inside a catalog name.
            if (tokens.Length >= 2 && tokens[0] == "me" && tokens[1] == "das") tokens = tokens[2..];
            else if (tokens.Length > 0 && tokens[0] == "agregame") tokens = tokens[1..];
            // Reject decimal/negative/overflow quantities instead of rounding or dropping a sign.
            if (Regex.IsMatch(part, @"-\s*\d|\d[.,]\d", RegexOptions.CultureInvariant)) return ([], "La cantidad debe ser un entero mayor que cero.");
            var number = tokens.Select((token,index) => (token,index)).FirstOrDefault(t => Numbers.ContainsKey(t.token) || t.token.All(char.IsDigit));
            int? quantity = null; string? nameNumber=null;
            if (tokens.Any(t=>t is "medio" or "media" or "mitad")) return ([], "La cantidad debe ser un entero mayor que cero.");
            // A number inside a catalog name (e.g. Queque tres leches) is not an order quantity.
            if (number.token != null && tokens.Take(number.index).Any(t=>!Noise.Contains(t))
                && !(kind == OrderEditKind.Set && number.index > 0 && tokens[number.index-1] == "a")) {nameNumber=number.token;number=default;}
            if (number.token != null)
            {
                if (!Numbers.TryGetValue(number.token, out var parsed) && !int.TryParse(number.token, NumberStyles.None, CultureInfo.InvariantCulture, out parsed))
                    return ([], "La cantidad indicada es demasiado grande.");
                if (number.index + 2 < tokens.Length && tokens[number.index + 1] == "y" && Numbers.TryGetValue(tokens[number.index + 2], out var units) && parsed >= 20 && units is > 0 and < 10)
                    parsed += units;
                quantity = parsed;
            }
            var term = string.Join(' ', tokens.Where(t => !Noise.Contains(t) && !Numbers.ContainsKey(t) && !t.All(char.IsDigit) && t != "x"));
            if (term.Length == 0 && !quantity.HasValue && !tokens.Any(t=>t is "igual" or "otro" or "otra"))
                return ([], "¿Qué producto y cantidad deseas agregar o cambiar?");
            var matches = Match(part, term, products);
            if (matches.Count > 1 && kind is OrderEditKind.Set or OrderEditKind.Remove)
            {
                var inDraft=matches.Where(p => context.OrderDraftItems.Any(i => i.ProductId == p.Id)).ToArray();
                if (inDraft.Length == 1) matches=inDraft;
            }
            var ordinal = Ordinal(tokens);
            if (ordinal.HasValue)
            {
                var ids = context.OrderDraftItems.Select(i => i.ProductId).ToArray();
                matches = ordinal >= 0 && ordinal < ids.Length ? products.Where(p => p.Id == ids[ordinal.Value]).ToArray() : [];
            }
            else if (matches.Count == 0 && (tokens.Contains("igual") || term.Length == 0))
            {
                var ids = reference?.Type == "PRODUCT" ? reference.Ids : context.OrderDraftItems.Count == 1 ? [context.OrderDraftItems[0].ProductId] : [];
                matches = ids.Length == 1 ? products.Where(p => p.Id == ids[0]).ToArray() : [];
            }
            if (matches.Count != 1) return ([], matches.Count > 1
                ? "Hay varias opciones: " + string.Join(", ", matches.Take(5).Select(p => p.Name)) + ". ¿Cuál deseas?"
                : "No identifiqué un producto activo inequívoco. ¿Cuál producto del catálogo deseas?");
            var product = matches[0];
            if (nameNumber != null && !new[]{product.Name}.Concat(product.Aliases).Any(n=>Words(n).Contains(nameNumber)))
                return ([], "Necesito precisar la cantidad. ¿Cuántas unidades deseas?");
            if (quantity is <= 0) return ([], "La cantidad debe ser un entero mayor que cero.");
            if (!hypothetical && kind is OrderEditKind.Remove or OrderEditKind.Set && context.OrderDraftItems.All(i => i.ProductId != product.Id))
            {
                var old = reference?.Type == "PRODUCT" && reference.Ids.Length == 1 && context.OrderDraftItems.Any(i => i.ProductId == reference.Ids[0]) ? reference.Ids[0]
                    : context.OrderDraftItems.Count == 1 ? context.OrderDraftItems[0].ProductId : null;
                if (kind == OrderEditKind.Set && !quantity.HasValue && old != null && old != product.Id)
                { edits.Add(new(OrderEditKind.Replace, product.Id, context.OrderDraftItems.Single(i => i.ProductId == old).Quantity, old)); continue; }
                return ([], "Ese producto no está en tu pedido. ¿Cuál de los productos del pedido deseas cambiar?");
            }
            if (kind == OrderEditKind.Set && !quantity.HasValue)
                return ([], "¿Qué cantidad deseas para " + product.Name + "?");
            if (kind == OrderEditKind.Remove && quantity.HasValue && context.OrderDraftItems.Single(i=>i.ProductId==product.Id).Quantity != quantity.Value)
                return ([], "¿Deseas quitar el producto completo o ajustar su cantidad?");
            edits.Add(new(kind, product.Id, quantity ?? 1));
        }
        if (edits.GroupBy(e => e.ProductId).Any(g => g.Count() > 1 && g.Any(e => e.Kind != OrderEditKind.Add)))
            return ([], "Has indicado varios cambios para el mismo producto. ¿Qué cantidad o cambio deseas aplicar?");
        return edits.Count == 0 ? ([], "¿Qué producto y cantidad deseas agregar o cambiar?") : (edits, null);
    }

    public static IReadOnlyList<VerifiedOffering> Match(string original, string term, IReadOnlyList<VerifiedOffering> offerings)
    {
        var matcher = new OfferingMentionResolver();
        var options=offerings.Select(p=>p.Option).ToArray();
        var named = OfferingMentionResolver.NamedLiterals(original, options).Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
        if (named.Count > 0) return offerings.Where(o => named.Contains(o.Id)).ToArray();
        var literal=matcher.Resolve(original,options);
        var resolution=literal.Status == ResolutionStatus.Determined ? literal : matcher.ResolveCommercial(term, options);
        if (resolution.Status == ResolutionStatus.Insufficient) resolution = matcher.ResolveCommercial(original, offerings.Select(p => p.Option).ToArray());
        var ids = resolution.Candidates.Select(c => c.Value.Id).ToHashSet(StringComparer.Ordinal);
        return offerings.Where(p => ids.Contains(p.Id)).ToArray();
    }
    public static int? Ordinal(string[] tokens)
    {
        var names = new[] { "primer", "segundo", "tercer", "cuarto", "quinto" };
        for (var i = 0; i < names.Length; i++) if (tokens.Contains(names[i]) || tokens.Contains(names[i] + "o") || tokens.Contains(names[i].TrimEnd('o') + "a")) return i;
        return null;
    }
    private static string[] Split(string text, IReadOnlyList<VerifiedOffering> products)
    {
        // A real catalog name containing a conjunction remains one entity.
        var normalized = SpanishTextNormalizer.Normalize(text);
        if (products.Any(p => SpanishTextNormalizer.ContainsPhrase(normalized, SpanishTextNormalizer.Normalize(p.Name)) && p.Name.Contains(" y ", StringComparison.OrdinalIgnoreCase))) return [text];
        return Regex.Split(text, @"[,;\n]|\s+(?:y|además)\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Where(p => !string.IsNullOrWhiteSpace(p) && SpanishTextNormalizer.Normalize(p) is not ("por favor" or "porfa" or "gracias")).ToArray();
    }
}

