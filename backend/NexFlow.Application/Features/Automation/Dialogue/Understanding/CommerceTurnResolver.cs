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

public sealed class CommerceTurnResolver(IWorkspaceSlotResolver slots, OrderMentionResolver orders, IClock? clock = null) : ICommerceTurnResolver
{
    public async Task<PreparedDialogueTurn?> ResolveAsync(Guid workspace, string source, string text, ConversationContextDto context,
        HashSet<string> modules, CancellationToken ct, AiInterpretation? semantic = null)
    {
        if (SpanishTextNormalizer.Normalize(text) == "ver mas" && OfferingReference.Read(workspace, context) is { Broad: true } page)
        {
            var intent = page.Type == "PRODUCT" ? ConversationIntent.ProductQuery : ConversationIntent.ServiceQuery;
            if (AiIntentAccess.GetDeniedResponse(intent, modules) is { } pageDenied)
                return new(DialogueAct.Information, new() { Intent = intent }, Response: pageDenied);
            var current = await slots.ReadCommerceAsync(workspace, context, page.Type == "PRODUCT", page.Type == "SERVICE", false, page.LocationId, ct);
            var values = page.Type == "PRODUCT" ? current.Products : current.Services;
            if (page.Type == "SERVICE" && page.Question == OfferingQuestion.Availability)
                values = Array.AsReadOnly(values.Where(s => s.RequiresReservation && s.DurationMinutes >= 5).ToArray());
            var next = Page(workspace, source, context, page.Type, values, page.LocationId, page.PageOffset + page.Ids.Length, page.Question);
            if (next.PageFingerprint != page.PageFingerprint || next.PageOffset >= next.TotalCount)
                next = Page(workspace, source, context, page.Type, values, page.LocationId, 0, page.Question)
                    with { PageRestarted = next.PageFingerprint != page.PageFingerprint };
            return new(DialogueAct.Information, new() { Intent = intent, QueryKind = OfferingQueryKind.Broad, OfferingQuery = next });
        }
        if (semantic is { QueryKind: OfferingQueryKind.Broad, RequiresClarification: false }
            && semantic.Intent is ConversationIntent.ProductQuery or ConversationIntent.ServiceQuery
            && !(modules.Contains("CATALOG") && modules.Contains("SERVICES") && InformationalRequest.UnscopedCatalog(text)))
        {
            var type = semantic.Intent == ConversationIntent.ProductQuery ? "PRODUCT" : "SERVICE";
            if (AiIntentAccess.GetDeniedResponse(semantic.Intent, modules) is { } broadDenied)
                return new(DialogueAct.Information, semantic, Response: broadDenied);
            // A broad catalog is global unless this turn explicitly scopes it.
            // The reservation's location is not an implicit catalog filter.
            string? locationId = null;
            if (!string.IsNullOrWhiteSpace(semantic.Location))
            {
                var location = await slots.ResolveAsync(workspace, source, text, context,
                    modules.Contains("RESERVATIONS") && modules.Contains("SERVICES") ? SlotResolutionPurpose.Reservation : SlotResolutionPurpose.Information,
                    new(true, false, false), ct);
                if (!location.Location.TryGetValue(out var site))
                    return new(DialogueAct.Information, semantic, Response: "¿De qué sede deseas consultar los servicios?");
                locationId = site.Id;
            }
            var data = await slots.ReadCommerceAsync(workspace, context, type == "PRODUCT", type == "SERVICE", false, locationId, ct);
            var values = type == "PRODUCT" ? data.Products : data.Services;
            if (type == "SERVICE" && semantic.Question == OfferingQuestion.Availability)
                values = Array.AsReadOnly(values.Where(s => s.RequiresReservation && s.DurationMinutes >= 5).ToArray());
            return new(DialogueAct.Information, new() { Intent = semantic.Intent, QueryKind = OfferingQueryKind.Broad,
                OfferingQuery = Page(workspace, source, context, type, values, locationId, 0,
                    semantic.Question ?? OfferingQuestion.Facts) });
        }
        if (semantic is { UseOfferingReference: true, Question: not null, RequiresClarification: false }
            && semantic.Intent is ConversationIntent.ProductQuery or ConversationIntent.ServiceQuery)
        {
            var type = semantic.Intent == ConversationIntent.ProductQuery ? "PRODUCT" : "SERVICE";
            if (AiIntentAccess.GetDeniedResponse(semantic.Intent, modules) is { } referenceDenied)
                return new(DialogueAct.Information, semantic, Response: referenceDenied);
            var previousOffering = OfferingReference.Read(workspace, context);
            var acts = semantic.Evidence.Where(e => e.Slot == "Act").ToArray();
            if (previousOffering?.Type != type || previousOffering.Ids.Length != 1 || acts.Length != 1
                || acts[0].NormalizedValue != semantic.Act?.ToString() || !AiEvidenceValidator.HasFragment(text, acts[0].Text)) return null;
            var data = await slots.ReadCommerceAsync(workspace, context, type == "PRODUCT", type == "SERVICE", false, previousOffering.LocationId, ct);
            var found = data.Products.Concat(data.Services).Where(o => o.Type == type && o.Id == previousOffering.Ids[0]).ToArray();
            return new(DialogueAct.InformationalInterruption, new() { Intent = semantic.Intent,
                OfferingQuery = new(workspace, source, context.StateVersion, type, semantic.Question.Value,
                    Array.AsReadOnly(found), false, previousOffering.LocationId) });
        }
        var semanticEvidence = semantic?.Evidence?.Where(e => e != null && e.Slot == "Act").ToArray() ?? [];
        var semanticStart = semantic is { Intent: ConversationIntent.Order, Act: DialogueAct.StartOperation, RequiresClarification: false }
            && semanticEvidence.Length == 1 && semanticEvidence[0].NormalizedValue == nameof(DialogueAct.StartOperation)
            && !string.IsNullOrWhiteSpace(semanticEvidence[0].Text) && semanticEvidence[0].Text.Length <= 300
            && AiEvidenceValidator.HasFragment(text, semanticEvidence[0].Text)
            && (!OrderMentionResolver.IsQuestion(text) || semantic.OrderAction == OrderTurnKind.Edit && semantic.Items.Count > 0)
            && !OrderMentionResolver.IsHypothetical(text);
        if (semantic is { Intent: ConversationIntent.Order, Act: DialogueAct.OperationInformation, RequiresClarification: false,
                Directive: ConversationDirective.None }
            && semanticEvidence.Length == 1 && semanticEvidence[0].NormalizedValue == nameof(DialogueAct.OperationInformation)
            && !string.IsNullOrWhiteSpace(semanticEvidence[0].Text) && semanticEvidence[0].Text.Length <= 300
            && AiEvidenceValidator.HasFragment(text, semanticEvidence[0].Text))
        {
            var denial = AiIntentAccess.GetDeniedResponse(ConversationIntent.Order, modules);
            if (denial != null) return new(DialogueAct.OperationInformation, new() { Intent = ConversationIntent.Order }, Response: denial);
            var available = await slots.ReadCommerceAsync(workspace, context, true, false, true, context.SelectedLocationId, ct);
            return new(DialogueAct.OperationInformation, new() { Intent = ConversationIntent.Order }, Response:
                available.Products.Count == 0
                    ? "No hay productos activos para preparar una solicitud en este momento. Puedes solicitar asistencia humana."
                    : "Puedes preparar una solicitud de pedido con los productos del catálogo y sus cantidades. El negocio la revisará para coordinar contigo; no confirma stock ni procesa pagos. La entrega o el recojo se coordinan con el negocio.\n\n"
                        + (context.CurrentGoal == "ORDER" ? "Tu selección sigue guardada. ¿Deseas revisarla o cambiar algún dato?" : "¿Qué productos y cantidades te gustaría solicitar?"));
        }
        if (context.CurrentGoal == "ORDER" && semantic is { Intent: ConversationIntent.Order })
            return await ResolveOrderActionAsync(workspace, source, text, context, modules, semantic, ct);
        if (semanticStart && context.CurrentGoal == null && semantic!.OrderAction == OrderTurnKind.Edit && semantic.Items.Count > 0)
            return await ResolveOrderActionAsync(workspace, source, text, context, modules, semantic, ct, starting: true);
        if (!semanticStart && !OrderMentionResolver.IsCandidate(text, context)) return null;
        var words = OrderMentionResolver.Words(text); var normalized = SpanishTextNormalizer.Normalize(text);
        var activeOrder = context.CurrentGoal == "ORDER";
        var hypothetical = OrderMentionResolver.IsHypothetical(text);
        var catalogRequest = InformationalRequest.Catalog(text);
        var unscopedCatalog = InformationalRequest.UnscopedCatalog(text);
        var dualCatalog = unscopedCatalog && modules.Contains("CATALOG") && modules.Contains("SERVICES");
        var politePurchase = OrderMentionResolver.PolitePurchase(text);
        var submit = OrderMentionResolver.IsSubmit(text) || activeOrder
            && NexFlow.Application.Features.Automation.Dialogue.Policy.ConversationConfirmation.IsAffirmative(text);
        var summary = activeOrder && !hypothetical && (words.Any(w => w is "total" or "subtotal") || normalized is "cuanto seria" or "cuanto es" or "mostrar pedido" or "listar pedido" or "ver pedido" or "mi pedido");
        var changing = words.Any(w => w is "agrega" or "agregar" or "quita" or "quitar" or "elimina" or "eliminar" or "saca" or "cambia" or "mejor" or "pon");
        var polite = !hypothetical && !words.Contains("no") && (politePurchase || changing && words.Contains("puedes") || words.Contains("me") && words.Any(w=>w is "apuntas" or "vendes"));
        var buying = !catalogRequest && (semanticStart || politePurchase || words.Any(w => w is "quiero" or "quisiera" or "necesito" or "comprar" or "llevo" or "dame" or "agrega" or "agregar" or "anade" or "apuntas" or "apuntar" or "vendes" or "pedir"))
            && !words.Contains("no") && (!OrderMentionResolver.IsQuestion(text) || polite);
        var internalLocation = activeOrder && (context.CurrentStep == "ORDER_LOCATION" || words.Any(w => w is "sede" or "en" or "cambia")) && !OrderMentionResolver.IsQuestion(text);
        if (changing && OrderMentionResolver.IsQuestion(text) && !hypothetical && !polite)
            return new(DialogueAct.Information, new() { Intent=ConversationIntent.Order }, Response: "¿Deseas modificar el pedido o solo consultar esa posibilidad?");
        var readOnly = !submit && !summary && !polite && (catalogRequest || hypothetical || OrderMentionResolver.IsQuestion(text)) && !changing;
        var broadOfferings = words.Any(w => w is "productos" or "servicios")
            && words.Any(w => w is "que" or "muestrame" or "lista" or "catalogo");
        // Resolve authorized offerings before deciding that a read-only question
        // lacks a factual subject. Unresolved references still reach semantics below.
        var explicitOrder=semanticStart || politePurchase || words.Any(w=>w is "comprar" or "pedido" or "producto" or "productos" or "agrega" or "agregar" or "anade" or "dame" or "llevo" or "apuntas" or "vendes");
        var weakPurchase=buying && !activeOrder && !explicitOrder;
        if (weakPurchase && !modules.Contains("CATALOG")) return null;
        var order = activeOrder && !readOnly || buying || submit;
        if (!weakPurchase && (order || hypothetical || summary) && AiIntentAccess.GetDeniedResponse(ConversationIntent.Order, modules) is { } denied)
            return new(DialogueAct.Information, new() { Intent = ConversationIntent.Order }, Response: denied);
        if (words.Contains("no") && order && semantic == null) return null;

        // No catalog lookup is necessary for explicit submit/summary. OrderFlow revalidates it.
        if (submit || summary) return Order(submit ? OrderTurnKind.Submit : OrderTurnKind.Summary, []);
        var reference = OfferingReference.Read(workspace, context);
        // Read-only catalog questions do not inherit a transactional filter.
        string? scope = readOnly ? null : context.SelectedLocationId ?? reference?.LocationId;
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
            var result = orders.Resolve(text, products, context, CurrentPurchaseReference(workspace, context), hypothetical);
            // An unresolved instruction, or a bare mention of an existing line,
            // needs semantic understanding rather than an implicit Add(1).
            var bareExisting = result.Edits.Any(e => e.Kind == OrderEditKind.Add && e.Quantity == 1
                    && context.OrderDraftItems.Any(i => i.ProductId == e.ProductId))
                && !buying && !changing && !words.Any(w => OrderMentionResolver.TryQuantity(w, out _));
            if (semantic == null && activeOrder && (bareExisting || result.Error != null)) return null;
            if (weakPurchase && new OfferingMentionResolver().Resolve(text,catalog.Services.Select(s=>s.Option).ToArray()).Candidates.Count > 0
                && (result.Edits.Count > 0 || new OfferingMentionResolver().Resolve(text,catalog.Products.Select(p=>p.Option).ToArray()).Candidates.Count > 0))
                return new(DialogueAct.SemanticAmbiguity,new(){Intent=ConversationIntent.General},Response:"¿Deseas consultar un servicio o preparar un pedido de productos?");
            if (result.Edits.Count == 0 && weakPurchase && semantic == null && result.Error?.StartsWith("Hay varias opciones:",StringComparison.Ordinal) != true) return null;
            if (AiIntentAccess.GetDeniedResponse(ConversationIntent.Order,modules) is { } unavailable)
                return new(DialogueAct.Information,new(){Intent=ConversationIntent.Order},Response:unavailable);
            // Starting preparation does not require an item. Unknown items are never
            // inserted: the same draft will collect validated catalog edits later.
            if (!activeOrder && buying && !hypothetical && !changing && result.Edits.Count == 0
                && OrderMentionResolver.Match(text, text, products).Count == 0)
                return Order(OrderTurnKind.Start, []);
            return Order(hypothetical ? OrderTurnKind.Hypothetical : result.Error == null ? OrderTurnKind.Edit : OrderTurnKind.Clarify,
                result.Edits, selectedLocation, result.Error);
        }
        var services=catalog.Services.ToArray();
        if (wantsServices && !modules.Contains("SERVICES")) return Query("SERVICE", [], false, AiIntentAccess.ServicesUnavailable);
        if (wantsProducts && !modules.Contains("CATALOG")) return Query("PRODUCT", [], false, AiIntentAccess.ProductsUnavailable);
        var productMatches = OrderMentionResolver.Match(text, text, products); var serviceMatches = OrderMentionResolver.Match(text, text, services);
        // Punctuation alone does not establish a factual subject. Ask semantics
        // only after looking for a real offering, so named price questions survive.
        if (semantic == null && readOnly && !catalogRequest && !broadOfferings && !hypothetical
            && productMatches.Count == 0 && serviceMatches.Count == 0 && comparisonQuestion() == OfferingQuestion.Facts
            && !OrderMentionResolver.IsQuestion(text.Replace("?", "").Replace("¿", ""))) return null;
        if (dualCatalog && productMatches.Count == 0 && serviceMatches.Count == 0)
        {
            var referenced = reference != null && reference.Ids.All(id => products.Concat(services).Any(o => o.Type == reference.Type && o.Id == id));
            var bookingContext = context.CurrentGoal is "BOOKING" or "RESERVATION"
                && services.Any(s => s.Id == context.SelectedServiceId);
            var orderContext = context.CurrentGoal == "ORDER" && context.OrderDraftItems.Count > 0
                && context.OrderDraftItems.All(item => products.Any(p => p.Id == item.ProductId));
            if (!referenced && !bookingContext && !orderContext)
                return new(DialogueAct.SemanticAmbiguity, new() { Intent = ConversationIntent.General }, Response: InformationalRequest.CatalogScopeQuestion,
                    Choices: InformationalRequest.CatalogScopeChoices);
            wantsServices = referenced ? reference!.Type == "SERVICE" : bookingContext;
            wantsProducts = !wantsServices;
        }
        var broad = (catalogRequest || broadOfferings || words.Contains("catalogo"))
            && !words.Contains("diferencia") && productMatches.Count == 0 && serviceMatches.Count == 0;
        if (broad) return wantsServices ? Query("SERVICE", services, true) : Query("PRODUCT", products, true);
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
        // A question with no factual entity or live reference may be about the
        // operation itself. Let the existing interpreter distinguish it from a
        // catalog search instead of returning a false entity-not-found answer.
        if (matches.Length == 0 && readOnly && !catalogRequest && !anaphora && !comparison && semantic == null) return null;
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
                    OfferingQuery = isBroad && clarification == null ? Page(workspace, source, context, type, values, scope, 0)
                        : new(workspace, source, context.StateVersion, type, comparisonQuestion(), Array.AsReadOnly(values.ToArray()), isBroad, scope, clarification) });
        OfferingQuestion comparisonQuestion() => words.Contains("stock") ? OfferingQuestion.Stock
            : words.Contains("diferencia") || words.Contains("compara") ? OfferingQuestion.Comparison
            : words.Contains("recomiendas") ? OfferingQuestion.Recommendation
            : words.Contains("dura") || words.Contains("duracion") ? OfferingQuestion.Duration
            : words.Contains("disponible") || words.Contains("atienden") ? OfferingQuestion.Availability
            : words.Any(w => w is "cuesta" or "cuanto" or "precio") ? OfferingQuestion.Price : OfferingQuestion.Facts;
        static bool changingProduct(string text) => OrderMentionResolver.Words(text).Any(w => w is "por" or "producto" or "productos") && !text.EndsWith("por favor", StringComparison.OrdinalIgnoreCase);
    }

    internal static OfferingQueryPlan Page(Guid workspace, string source, ConversationContextDto context, string type,
        IReadOnlyList<VerifiedOffering> values, string? scope, int offset, OfferingQuestion question = OfferingQuestion.Facts)
    {
        var ordered = values.OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase).ThenBy(o => o.Id, StringComparer.Ordinal).ToArray();
        var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(ordered.Select(o => o.Id)))));
        return new(workspace, source, context.StateVersion, type, question,
            Array.AsReadOnly(ordered.Skip(offset).Take(5).ToArray()), true, scope,
            PageOffset: offset, TotalCount: ordered.Length, PageFingerprint: fingerprint);
    }

    private async Task<PreparedDialogueTurn> ResolveOrderActionAsync(Guid workspace, string source, string text,
        ConversationContextDto context, HashSet<string> modules, AiInterpretation semantic, CancellationToken ct, bool starting = false)
    {
        PreparedDialogueTurn Plan(OrderTurnKind kind, IReadOnlyList<OrderEdit>? edits = null, string? clarification = null,
            string? location = null) => new(semantic.Act ?? DialogueAct.RequestedData,
                new() { Intent = ConversationIntent.Order, OrderProposal = new(workspace, source, context.StateVersion,
                    kind, Array.AsReadOnly((edits ?? []).ToArray()), LocationId: location, Clarification: clarification,
                    RequireConfirmation: kind == OrderTurnKind.Submit) });
        if (AiIntentAccess.GetDeniedResponse(ConversationIntent.Order, modules) is { } denied)
            return new(DialogueAct.Information, new() { Intent = ConversationIntent.Order }, Response: denied);
        var evidence = semantic.Evidence.Where(e => e.Slot == "Act").ToArray();
        var backed = !semantic.RequiresClarification && evidence.Length == 1
            && evidence[0].NormalizedValue == semantic.Act?.ToString()
            && !string.IsNullOrWhiteSpace(evidence[0].Text) && AiEvidenceValidator.HasFragment(text, evidence[0].Text);
        if (!backed || (starting ? context.CurrentGoal != null : context.CurrentGoal != "ORDER"))
            return Plan(OrderTurnKind.Clarify, clarification: "Conservamos tu borrador. ¿Qué deseas consultar o cambiar?");
        if (semantic.OrderAction == OrderTurnKind.Submit)
        {
            // Natural finish requests prepare a current review. The model cannot
            // provide transaction consent, even when it labels a turn affirmative.
            return Plan(OrderTurnKind.Submit);
        }
        if (semantic.OrderAction == OrderTurnKind.Summary) return Plan(OrderTurnKind.Summary);
        if (semantic.OrderAction == OrderTurnKind.Location && semantic.Act == DialogueAct.Correction)
        {
            var locationEvidence = semantic.Evidence.Where(e => e.Slot == "Location").ToArray();
            var locations = await slots.ReadCommerceLocationsAsync(workspace, true, ct);
            var matched = locationEvidence.Length == 1 && locationEvidence[0].NormalizedValue == semantic.Location
                && AiEvidenceValidator.HasFragment(text, locationEvidence[0].Text)
                ? new LocationMentionResolver().ResolveEvidence(locationEvidence[0].Text, locations) : SlotResolution<ResolvedBusinessEntity>.Missing();
            if (matched.TryGetValue(out var location) && string.Concat(SpanishTextNormalizer.Terms(semantic.Location ?? ""))
                == string.Concat(SpanishTextNormalizer.Terms(location.Name))) return Plan(OrderTurnKind.Location, location: location.Id);
            return Plan(OrderTurnKind.Clarify, clarification: "¿En cuál sede deseas continuar? Conservamos el pedido.");
        }
        if (semantic.OrderAction != OrderTurnKind.Edit || semantic.Items.Count == 0 || semantic.Items.Count > 10
            || !(semantic.Act is DialogueAct.Correction or DialogueAct.RequestedData or DialogueAct.MultipleData
                || starting && semantic.Act == DialogueAct.StartOperation))
            return Plan(OrderTurnKind.Clarify, clarification: "Conservamos los productos y cantidades. ¿Qué deseas cambiar?");
        var catalog = await slots.ReadCommerceAsync(workspace, context, true, false, true, context.SelectedLocationId, ct);
        var reference = semantic.UseOfferingReference ? CurrentPurchaseReference(workspace, context) : null;
        var edits = new List<OrderEdit>();
        foreach (var item in semantic.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Text) || !AiEvidenceValidator.HasFragment(text, item.Text))
                return Plan(OrderTurnKind.Clarify, clarification: "Necesito precisar el producto y su cantidad. El pedido sigue intacto.");
            // Each item span must carry its own quantity; a number elsewhere in
            // the turn cannot be borrowed for a different product.
            var entityText = item.QuantityText != null && AiEvidenceValidator.HasFragment(item.Text, item.QuantityText)
                ? item.Text.Replace(item.QuantityText, " ", StringComparison.OrdinalIgnoreCase) : item.Text;
            var resolver = new OfferingMentionResolver();
            var options = catalog.Products.Select(p => p.Option).ToArray();
            var candidates = resolver.ResolveEvidence(entityText, options).Candidates
                .Concat(resolver.ResolveCommercial(entityText, options).Candidates).Select(c => c.Value).DistinctBy(c => c.Id).ToArray();
            if (candidates.Length == 0 && reference != null && SpanishTextNormalizer.Normalize(entityText).Length == 0)
                candidates = catalog.Products.Where(p => p.Id == reference.Ids[0] && p.AtLocation(reference.LocationId))
                    .Select(p => new ResolvedBusinessEntity(p.Id, p.Name, reference.LocationId)).ToArray();
            if (candidates.Length != 1 || !catalog.Products.Any(p => p.Id == candidates[0].Id
                && new[] { p.Name }.Concat(p.Aliases).Any(name => SpanishTextNormalizer.Normalize(name) == SpanishTextNormalizer.Normalize(item.Product))))
                return Plan(OrderTurnKind.Clarify, clarification: "¿Cuál producto activo deseas cambiar? Conservamos el pedido.");
            var product = candidates[0];
            if (starting && item.Kind != OrderEditKind.Add)
                return Plan(OrderTurnKind.Clarify, clarification: "¿Qué producto y cantidad deseas solicitar? Todavía no hemos preparado una selección.");
            if (item.Kind is not (OrderEditKind.Add or OrderEditKind.Set or OrderEditKind.Remove))
                return Plan(OrderTurnKind.Clarify, clarification: "Indica qué producto deseas quitar y cuál deseas agregar.");
            var quantity = item.Quantity;
            if (item.Kind != OrderEditKind.Remove && (!quantity.HasValue || item.QuantityText == null
                || !AiEvidenceValidator.HasFragment(item.Text, item.QuantityText)
                || !OrderMentionResolver.TryQuantity(item.QuantityText, out var literal) || literal != quantity))
                return Plan(OrderTurnKind.Clarify, clarification: "¿Cuántas unidades deseas? Conservamos las cantidades actuales.");
            if (item.Kind == OrderEditKind.Remove && context.OrderDraftItems.All(i => i.ProductId != product.Id))
                return Plan(OrderTurnKind.Clarify, clarification: "Ese producto no está en tu pedido. ¿Cuál deseas cambiar?");
            if (item.Kind == OrderEditKind.Remove && quantity.HasValue
                && (item.QuantityText == null || !AiEvidenceValidator.HasFragment(item.Text, item.QuantityText)
                    || !OrderMentionResolver.TryQuantity(item.QuantityText, out var removed) || removed != quantity
                    || context.OrderDraftItems.Single(i => i.ProductId == product.Id).Quantity != quantity))
                return Plan(OrderTurnKind.Clarify, clarification: "¿Deseas quitar el producto completo o ajustar su cantidad?");
            edits.Add(new(item.Kind, product.Id, quantity ?? 1));
        }
        if (edits.Select(e => e.ProductId).Distinct().Count() != edits.Count)
            return Plan(OrderTurnKind.Clarify, clarification: "Indica una cantidad final para cada producto. Conservamos el pedido.");
        return Plan(OrderTurnKind.Edit, edits);
    }

    private OfferingReference? CurrentPurchaseReference(Guid workspace, ConversationContextDto context)
    {
        if (clock == null || ConversationQuestion.Current(workspace, context, clock.UtcNow) is not { } question
            || OfferingReference.Read(workspace, context) is not { Type: "PRODUCT", Broad: false, Ids.Length: 1 } reference) return null;
        var factual = question is { Expected: ExpectedReply.Information, Intent: ConversationIntent.ProductQuery };
        var draft = context.CurrentGoal == "ORDER" && question.Intent == ConversationIntent.Order
            && context.OrderDraftItems.Any(i => i.ProductId == reference.Ids[0]);
        return factual || draft ? reference : null;
    }
}

