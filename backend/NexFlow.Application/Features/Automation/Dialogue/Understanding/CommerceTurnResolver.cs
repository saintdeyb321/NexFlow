using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using NexFlow.Application.Features.Business.Offerings;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Features.Automation.Dialogue.Understanding;

public interface ICommerceTurnResolver
{
    Task<PreparedDialogueTurn?> ResolveAsync(Guid workspace, string source, string text, ConversationContextDto context,
        HashSet<string> modules, CancellationToken ct, AiInterpretation? semantic = null);
}

public sealed class CommerceTurnResolver(IWorkspaceSlotResolver slots, OrderMentionResolver orders) : ICommerceTurnResolver
{
    public async Task<PreparedDialogueTurn?> ResolveAsync(Guid workspace, string source, string text, ConversationContextDto context,
        HashSet<string> modules, CancellationToken ct, AiInterpretation? semantic = null)
    {
        if (!OrderMentionResolver.IsCandidate(text, context)) return null;
        var words = OrderMentionResolver.Words(text); var normalized = SpanishTextNormalizer.Normalize(text);
        var activeOrder = context.CurrentGoal == "ORDER";
        var hypothetical = OrderMentionResolver.IsHypothetical(text);
        var catalogRequest = InformationalRequest.Catalog(text);
        var unscopedCatalog = InformationalRequest.UnscopedCatalog(text);
        var dualCatalog = unscopedCatalog && modules.Contains("CATALOG") && modules.Contains("SERVICES");
        var politePurchase = OrderMentionResolver.PolitePurchase(text);
        var submit = OrderMentionResolver.IsSubmit(text) || activeOrder && context.CurrentStep?.StartsWith("ORDER_CONFIRM:", StringComparison.Ordinal) == true
            && normalized is "si" or "si confirmo" or "confirmo" or "de acuerdo";
        var summary = activeOrder && !hypothetical && (words.Any(w => w is "total" or "subtotal") || normalized is "cuanto seria" or "cuanto es" or "mostrar pedido" or "listar pedido" or "ver pedido" or "mi pedido");
        var changing = words.Any(w => w is "agrega" or "agregar" or "quita" or "quitar" or "elimina" or "eliminar" or "saca" or "cambia" or "mejor" or "pon");
        var polite = !hypothetical && !words.Contains("no") && (politePurchase || changing && words.Contains("puedes") || words.Contains("me") && words.Any(w=>w is "apuntas" or "vendes"));
        var buying = !catalogRequest && (politePurchase || words.Any(w => w is "quiero" or "quisiera" or "necesito" or "comprar" or "llevo" or "dame" or "agrega" or "agregar" or "anade" or "apuntas" or "apuntar" or "vendes" or "pedir"))
            && !words.Contains("no") && (!OrderMentionResolver.IsQuestion(text) || polite);
        var internalLocation = activeOrder && (context.CurrentStep == "ORDER_LOCATION" || words.Any(w => w is "sede" or "en" or "cambia")) && !OrderMentionResolver.IsQuestion(text);
        if (changing && OrderMentionResolver.IsQuestion(text) && !hypothetical && !polite)
            return new(DialogueAct.Information, new() { Intent=ConversationIntent.Order }, Response: "¿Deseas modificar el pedido o solo consultar esa posibilidad?");
        var readOnly = !submit && !summary && !polite && (catalogRequest || hypothetical || OrderMentionResolver.IsQuestion(text)) && !changing;
        var explicitOrder=politePurchase || words.Any(w=>w is "comprar" or "pedido" or "producto" or "productos" or "agrega" or "agregar" or "anade" or "dame" or "llevo" or "apuntas" or "vendes");
        var weakPurchase=buying && !activeOrder && !explicitOrder;
        if (weakPurchase && !modules.Contains("CATALOG")) return null;
        var order = activeOrder && !readOnly || buying || submit;
        if (!weakPurchase && (order || hypothetical || summary) && AiIntentAccess.GetDeniedResponse(ConversationIntent.Order, modules) is { } denied)
            return new(DialogueAct.Information, new() { Intent = ConversationIntent.Order }, Response: denied);
        if (words.Contains("no") && order) return new(DialogueAct.SemanticAmbiguity, new() { Intent = ConversationIntent.General, RequiresClarification = true });

        // No catalog lookup is necessary for explicit submit/summary. OrderFlow revalidates it.
        if (submit || summary) return Order(submit ? OrderTurnKind.Submit : OrderTurnKind.Summary, []);
        var reference = OfferingReference.Read(workspace, context);
        string? scope = context.SelectedLocationId ?? reference?.LocationId;
        string? selectedLocation = null;
        if (internalLocation || words.Contains("en"))
        {
            var canResolve = order && modules.Contains("ORDERS") && modules.Contains("CATALOG") || modules.Contains("LOCATIONS");
            if (canResolve)
            {
                var choices = await slots.ReadCommerceLocationsAsync(workspace,order,ct);
                var locationText = text.LastIndexOf(" en ",StringComparison.OrdinalIgnoreCase) is var at && at >= 0 ? text[(at + 4)..] : text;
                locationText = string.Join(' ', OrderMentionResolver.Words(locationText).Where(w => w is not ("cambia" or "cambiar" or "sede" or "de" or "a" or "la")));
                var result = new LocationMentionResolver().Resolve(locationText, choices, context.CurrentStep == "ORDER_LOCATION");
                if (result.TryGetValue(out var location)) scope = selectedLocation = location.Id;
                else if (result.Status == ResolutionStatus.Ambiguous) return Order(OrderTurnKind.Clarify, [], clarification: "¿En cuál sede? " + string.Join(", ", result.Candidates.Take(5).Select(c => c.Value.Name)));
            }
        }
        var anaphora=words.Any(w=>w is "ese" or "esa" or "esos" or "estos" or "anterior")
            || normalized is "cuanto cuesta" or "cuanto dura" or "cual es el precio" or "que precio tiene";
        var namedProducts=words.Any(w=>w is "producto" or "productos" or "stock");
        var namedServices=words.Any(w=>w is "servicio" or "servicios" or "dura" or "duracion");
        var wantsServices=namedServices || !namedProducts && (anaphora && reference?.Type == "SERVICE"
            || catalogRequest && (reference?.Type == "SERVICE" || context.CurrentGoal is "BOOKING" or "RESERVATION"
                || modules.Contains("SERVICES") && !modules.Contains("CATALOG"))
            || words.Contains("catalogo") && modules.Contains("SERVICES") && !modules.Contains("CATALOG"));
        var wantsProducts=order || hypothetical || namedProducts || (catalogRequest || words.Contains("catalogo")) && !wantsServices
            || !namedServices && anaphora && reference?.Type == "PRODUCT";
        var catalog = await slots.ReadCommerceAsync(workspace,context,dualCatalog || wantsProducts || !wantsServices,dualCatalog || (!order || weakPurchase) && !hypothetical && (weakPurchase || wantsServices || !wantsProducts),order && !weakPurchase,scope,ct);
        var products=catalog.Products.ToArray();
        if (internalLocation && selectedLocation != null && !words.Any(w => w is "producto" or "productos") && !changingProduct(text))
            return Order(OrderTurnKind.Location, [], selectedLocation);
        if (order || hypothetical)
        {
            if (context.CurrentGoal is "BOOKING" or "RESERVATION") return null;
            var result = orders.Resolve(text, products, context, reference, hypothetical);
            if (weakPurchase && new OfferingMentionResolver().Resolve(text,catalog.Services.Select(s=>s.Option).ToArray()).Candidates.Count > 0
                && (result.Edits.Count > 0 || new OfferingMentionResolver().Resolve(text,catalog.Products.Select(p=>p.Option).ToArray()).Candidates.Count > 0))
                return new(DialogueAct.SemanticAmbiguity,new(){Intent=ConversationIntent.General},Response:"¿Deseas consultar un servicio o preparar un pedido de productos?");
            if (result.Edits.Count == 0 && weakPurchase && semantic == null && result.Error?.StartsWith("Hay varias opciones:",StringComparison.Ordinal) != true) return null;
            if (AiIntentAccess.GetDeniedResponse(ConversationIntent.Order,modules) is { } unavailable)
                return new(DialogueAct.Information,new(){Intent=ConversationIntent.Order},Response:unavailable);
            return Order(hypothetical ? OrderTurnKind.Hypothetical : result.Error == null ? OrderTurnKind.Edit : OrderTurnKind.Clarify,
                result.Edits, selectedLocation, result.Error);
        }
        var services=catalog.Services.ToArray();
        if (wantsServices && !modules.Contains("SERVICES")) return Query("SERVICE", [], false, AiIntentAccess.ServicesUnavailable);
        if (wantsProducts && !modules.Contains("CATALOG")) return Query("PRODUCT", [], false, AiIntentAccess.ProductsUnavailable);
        var productMatches = OrderMentionResolver.Match(text, text, products); var serviceMatches = OrderMentionResolver.Match(text, text, services);
        if (dualCatalog && productMatches.Count == 0 && serviceMatches.Count == 0)
        {
            var referenced = reference != null && reference.Ids.All(id => products.Concat(services).Any(o => o.Type == reference.Type && o.Id == id));
            var bookingContext = context.CurrentGoal is "BOOKING" or "RESERVATION"
                && services.Any(s => s.Id == context.SelectedServiceId);
            var orderContext = context.CurrentGoal == "ORDER" && context.OrderDraftItems.Count > 0
                && context.OrderDraftItems.All(item => products.Any(p => p.Id == item.ProductId));
            if (!referenced && !bookingContext && !orderContext)
                return new(DialogueAct.SemanticAmbiguity, new() { Intent = ConversationIntent.General }, Response: InformationalRequest.CatalogScopeQuestion,
                    Choices: [new("ProductQuery", "Productos", "Action"), new("ServiceQuery", "Servicios", "Action")]);
            wantsServices = referenced ? reference!.Type == "SERVICE" : bookingContext;
            wantsProducts = !wantsServices;
        }
        var broad = (catalogRequest || words.Any(w => w is "productos" or "servicios" or "catalogo")
            && words.Any(w => w is "que" or "muestrame" or "lista" or "catalogo"))
            && !words.Contains("diferencia") && productMatches.Count == 0 && serviceMatches.Count == 0;
        if (broad) return wantsServices ? Query("SERVICE", services.Take(5).ToArray(), true) : Query("PRODUCT", products.Take(5).ToArray(), true);
        var matches = productMatches.Concat(serviceMatches).ToArray();
        if (matches.Length == 0 && reference != null && OrderMentionResolver.Ordinal(words) is { } ordinal && ordinal < reference.Ids.Length)
            matches = products.Concat(services).Where(p => p.Type == reference.Type && p.Id == reference.Ids[ordinal]).ToArray();
        if (matches.Length == 0 && reference != null && (anaphora || words.Contains("diferencia")))
            matches = products.Concat(services).Where(p => p.Type == reference.Type && reference.Ids.Contains(p.Id)).ToArray();
        if (matches.Length == 0 && context.SelectedServiceId != null && words.Any(w => w is "dura" or "cuesta" or "ese"))
        {
            if (!modules.Contains("SERVICES")) return Query("SERVICE", [], false, AiIntentAccess.ServicesUnavailable);
            var selected = services.SingleOrDefault(s=>s.Id==context.SelectedServiceId);
            if (selected != null) matches = [selected];
        }
        var comparison = words.Any(w => w is "diferencia" or "compara" or "comparar");
        if (matches.Length == 0 && comparison && words.Contains("y"))
            matches = text.Split(" y ", StringSplitOptions.RemoveEmptyEntries).SelectMany(p => OrderMentionResolver.Match(p, p, products.Concat(services).ToArray())).DistinctBy(p => p.Id).ToArray();
        if (matches.Length == 0 && words.Contains("recomiendas")) return Query("SERVICE", services.Take(5).ToArray(), false);
        if (matches.Length == 0)
            return wantsProducts || wantsServices ? Query(wantsServices ? "SERVICE" : "PRODUCT", [], false, "No encontré una opción activa inequívoca. ¿Cuál producto o servicio deseas consultar?") : null;
        if (matches.Length > 1 && !comparison) return Query(matches[0].Type, [], false, "Hay varias opciones: " + string.Join(", ", matches.Take(5).Select(p => p.Name)) + ". ¿Cuál deseas consultar?");
        if (matches.Select(m => m.Type).Distinct().Count() > 1) return Query("PRODUCT", [], false, "¿Deseas consultar productos o servicios?");
        return Query(matches[0].Type, matches, false);

        PreparedDialogueTurn Order(OrderTurnKind kind, IReadOnlyList<OrderEdit> edits, string? location = null, string? clarification = null) =>
            new(NexFlow.Application.Features.Automation.Dialogue.Policy.DialoguePolicy.OrderAct(kind,edits,context),
                new() { Intent = ConversationIntent.Order, OrderProposal = new(workspace, source, context.StateVersion, kind, Array.AsReadOnly(edits.ToArray()), location, clarification) });
        PreparedDialogueTurn Query(string type, IReadOnlyList<VerifiedOffering> values, bool isBroad, string? clarification = null) =>
            new(context.CurrentGoal != null ? DialogueAct.InformationalInterruption : DialogueAct.Information,
                new() { Intent = type == "PRODUCT" ? ConversationIntent.ProductQuery : ConversationIntent.ServiceQuery,
                    QueryKind = isBroad ? OfferingQueryKind.Broad : OfferingQueryKind.Specific,
                    OfferingQuery = new(workspace, source, context.StateVersion, type, comparisonQuestion(), Array.AsReadOnly(values.ToArray()), isBroad, scope, clarification) });
        OfferingQuestion comparisonQuestion() => words.Contains("stock") ? OfferingQuestion.Stock
            : words.Contains("diferencia") || words.Contains("compara") ? OfferingQuestion.Comparison
            : words.Contains("recomiendas") ? OfferingQuestion.Recommendation
            : words.Contains("dura") || words.Contains("duracion") ? OfferingQuestion.Duration
            : words.Contains("disponible") || words.Contains("atienden") ? OfferingQuestion.Availability
            : words.Any(w => w is "cuesta" or "cuanto" or "precio") ? OfferingQuestion.Price : OfferingQuestion.Facts;
        static bool changingProduct(string text) => OrderMentionResolver.Words(text).Any(w => w is "por" or "producto" or "productos") && !text.EndsWith("por favor", StringComparison.OrdinalIgnoreCase);
    }
}
