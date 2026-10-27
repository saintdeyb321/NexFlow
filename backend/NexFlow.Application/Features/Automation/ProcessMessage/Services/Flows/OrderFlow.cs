using System.Security.Cryptography;
using System.Text;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Automation.Dialogue.Policy;
using NexFlow.Application.Features.Automation.Dialogue.Understanding;
using NexFlow.Application.Features.Business.Offerings;
using NexFlow.Application.Features.Notifications;
using NexFlow.Application.Features.Orders.DTOs;
using NexFlow.Domain.Enums;
using NexFlow.Domain.Exceptions;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services.Flows;

public interface IOrderFlow
{
    Task<string?> TryResumeAsync(Guid workspaceId, string phone, string conversationId, string sourceMessageId, ConversationContextDto context, CancellationToken ct);
    Task<string> ProcessAsync(Guid workspaceId, string phone, string conversationId, string customerName, string messageText, AiInterpretation interpretation, string sourceMessageId, CancellationToken ct);
    Task<ResponsePlan> ProcessPlannedAsync(Guid workspaceId, string phone, string conversationId, string customerName, string messageText, AiInterpretation interpretation, string sourceMessageId, DialogueAct act, CancellationToken ct);
}

public sealed class OrderFlow(IOrderRepository orders, INotificationService notifications, IContextRecoveryService contexts,
    IOfferingService offerings, IEntitlementService entitlements, IClock? clock = null, ILocationRepository? locations = null) : IOrderFlow
{
    public async Task<string?> TryResumeAsync(Guid workspaceId, string phone, string conversationId, string sourceMessageId, ConversationContextDto context, CancellationToken ct)
    {
        var pending=context.CurrentGoal == "ORDER" && context.CurrentStep?.StartsWith("ORDER_COMMIT",StringComparison.Ordinal) == true;
        if (context.Mode == "Human" || context.OrderLastSourceMessageId != sourceMessageId
            || !pending && (context.CurrentGoal != null || context.OrderDraftItems.Count != 0)) return null;
        var id=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceMessageId)));
        var existing=await orders.GetOrderByIdAsync(workspaceId,id,ct);
        if (existing == null) return null;
        if (existing.ConsumerPhone != phone || existing.ConversationId != conversationId
            || pending && !existing.Items.OrderBy(i=>i.ProductId).Select(i=>(i.ProductId,i.Quantity))
                .SequenceEqual(context.OrderDraftItems.OrderBy(i=>i.ProductId).Select(i=>(i.ProductId,i.Quantity))))
            throw new ConcurrencyException("Order recovery identity mismatch.");
        if (pending)
        {
            // Completing an already durable effect is not a new licensed purchase.
            await notifications.NotifyOnceAsync(workspaceId,"ORDERS",NotificationType.NewCommercialRequest,existing.Id,"Pedido recibido",
                $"{existing.ConsumerName} envió una solicitud con {existing.Items.Count} ítems para revisión.","/orders",ct);
            context.CurrentGoal=null;context.CurrentStep=null;context.OrderDraftItems.Clear();context.MissingFields.Clear();context.LastQuestion=null;
            context.OrderLastResponse=$"Tu solicitud de pedido fue enviada. El negocio la revisará. Código: {existing.Id[..6]}.";
            await contexts.SaveContextAsync(workspaceId,phone,context,ct);
        }
        return context.OrderLastResponse;
    }

    public async Task<string> ProcessAsync(Guid workspaceId, string phone, string conversationId, string customerName, string messageText, AiInterpretation interpretation, string sourceMessageId, CancellationToken ct)
        => (await ProcessPlannedAsync(workspaceId,phone,conversationId,customerName,messageText,interpretation,sourceMessageId,DialogueAct.RequestedData,ct)).FallbackText;

    public async Task<ResponsePlan> ProcessPlannedAsync(Guid workspaceId, string phone, string conversationId, string customerName, string messageText, AiInterpretation interpretation, string sourceMessageId, DialogueAct act, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceMessageId);
        var context = await contexts.GetOrRecoverContextAsync(workspaceId,phone,ct);
        if (context.Mode == "Human") throw new ConcurrencyException("Order is under human control.");
        ResponsePlan Plan(string text, OperationStatus status = OperationStatus.Collecting, OrderDraftMerger.Quote? quote = null) => new(workspaceId,sourceMessageId,context.StateVersion,
            ConversationIntent.Order,act,status,quote?.Facts ?? [],[],[],null,quote?.Currency,null,text);
        if (await TryResumeAsync(workspaceId,phone,conversationId,sourceMessageId,context,ct) is { } replay) return Plan(replay,OperationStatus.Succeeded);
        var modules = (await entitlements.GetAvailableModuleCodesAsync(workspaceId,ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (AiIntentAccess.GetDeniedResponse(ConversationIntent.Order,modules) is { } denied)
        {
            // TryResume already checked this source for a durable effect. Preserve a
            // different pending source until its own recovery can establish the outcome.
            if (context.OrderDraftItems.Count > 0 && (context.CurrentStep?.StartsWith("ORDER_COMMIT",StringComparison.Ordinal) != true
                || context.OrderLastSourceMessageId == sourceMessageId))
            { context.CurrentStep=null;return await Remember(denied,OperationStatus.Denied); }
            return Plan(denied,OperationStatus.Denied);
        }
        if (context.OrderLastSourceMessageId == sourceMessageId && context.OrderLastResponse != null) return Plan(context.OrderLastResponse);
        if (context.PendingRecoveryAction != null)
            return Plan("Resuelve primero la decisión pendiente sobre el borrador. No he enviado la solicitud.");
        if (context.CurrentGoal is not (null or "ORDER")) return Plan("Completa la operación actual o solicita cancelar su preparación antes de iniciar un pedido.");
        var resuming = context.CurrentStep?.StartsWith("ORDER_COMMIT",StringComparison.Ordinal) == true && context.OrderLastSourceMessageId == sourceMessageId;
        if (context.CurrentStep?.StartsWith("ORDER_COMMIT",StringComparison.Ordinal) == true && !resuming) throw new ConcurrencyException("An earlier order commit must recover before processing another turn.");
        var proposal = interpretation.OrderProposal;
        if (proposal != null && (proposal.WorkspaceId != workspaceId || proposal.SourceMessageId != sourceMessageId || proposal.ExpectedStateVersion != context.StateVersion))
            throw new ConcurrencyException("Stale or foreign order proposal.");
        if (proposal?.Kind == OrderTurnKind.Clarify) return await Remember(proposal.Clarification ?? "¿Cuál producto deseas agregar o cambiar?");
        var products = (await offerings.GetProductsAsync(workspaceId, proposal?.LocationId ?? context.SelectedLocationId,null,ct))
            .Where(p => p.IsActive).Select(VerifiedOffering.Capture).ToArray();
        if (proposal?.Kind == OrderTurnKind.Start)
        {
            if (products.Length == 0)
                return await Remember("No hay productos activos para preparar una solicitud en este momento. Puedes solicitar asistencia humana.", OperationStatus.NotAvailable);
            context.CurrentGoal = "ORDER";
            context.CurrentStep = "ORDER_COLLECT_ITEMS";
            context.LastQuestion = "¿Qué productos y cantidades deseas solicitar? Puedes consultar el catálogo antes de elegir.";
            return await Remember(context.LastQuestion);
        }
        if (proposal == null)
        {
            var result = new OrderMentionResolver().Resolve(messageText,products,context,OfferingReference.Read(workspaceId,context));
            var kind = OrderMentionResolver.IsSubmit(messageText) || ConversationConfirmation.IsAffirmative(messageText) ? OrderTurnKind.Submit
                : OrderMentionResolver.IsQuestion(messageText) ? OrderTurnKind.Summary : result.Error == null ? OrderTurnKind.Edit : OrderTurnKind.Summary;
            proposal = new(workspaceId,sourceMessageId,context.StateVersion,kind,Array.AsReadOnly(result.Edits.ToArray()));
        }
        string? locationName = null;
        try
        {
            var staged = OrderDraftMerger.Apply(workspaceId,sourceMessageId,context,proposal,products);
            var location = proposal.LocationId ?? context.SelectedLocationId;
            if (location != null)
            {
                var authorized = locations == null ? [] : (await locations.GetLocationsAsync(workspaceId, ct)).Where(l => l.Id == location).ToArray();
                if (authorized.Length != 1 || string.IsNullOrWhiteSpace(authorized[0].Name))
                {
                    context.CurrentQuestion = null;
                    return await Remember("La sede seleccionada ya no está disponible. Indica otra sede para revisar tu solicitud.", OperationStatus.NotAvailable);
                }
                locationName = authorized[0].Name;
            }
            var quote = OrderDraftMerger.Price(workspaceId,staged,products,location,locationName);
            var needsLocation=quote.NeedsLocation || location==null && context.CurrentStep=="ORDER_LOCATION";
            if (proposal.Kind == OrderTurnKind.Hypothetical)
                return await Remember("Si eligieras esas cantidades, quedaría así:\n" + quote.Text + "\nNo he cambiado tu pedido. Para aplicarlo, indica la nueva cantidad. ¿Qué deseas ajustar?",OperationStatus.Informational,quote);
            if (proposal.Kind == OrderTurnKind.Submit)
            {
                if (!quote.Complete) return await Remember(quote.Text + "\n¿Qué producto deseas corregir o quitar?",OperationStatus.NotAvailable,quote);
                if (needsLocation) { context.CurrentStep="ORDER_LOCATION"; return await Remember(quote.Text + "\n¿En cuál sede deseas solicitar estos productos?",quote:quote); }
                var now = clock?.UtcNow ?? DateTime.UtcNow;
                var reviewed = ConversationQuestion.HasCurrentOrderReview(workspaceId, conversationId, context, quote.Fingerprint, now);
                var consent = reviewed && context.CurrentQuestion!.SourceMessageId != sourceMessageId
                    && ConversationConfirmation.IsAffirmative(messageText) && !proposal.RequireConfirmation;
                // A checkpoint for this same inbound may recover an already
                // authorized effect. It must not acquire a different quote.
                if (resuming && context.CurrentStep == "ORDER_COMMIT:" + quote.Fingerprint)
                    return await Submit(quote);
                if (reviewed && !consent)
                    return await Remember("Tu solicitud sigue lista para enviar. Puedes responder «*Confirmar*» o «Sí» para autorizar este resumen, o indicar qué deseas cambiar. También puedes escribir «*Editar*» o «*Descartar*».",
                        OperationStatus.AwaitingConfirmation, quote, preserveQuestion: true);
                if (!consent)
                {
                    context.CurrentGoal="ORDER";
                    context.CurrentStep="ORDER_CONFIRM:" + quote.Fingerprint;
                    context.LastQuestion="¿Confirmas que la enviemos al negocio con estos importes?";
                    return await Remember("Revisa el resumen vigente de tu solicitud.\n\n" + quote.Text + "\n\n" + context.LastQuestion
                        + "\nPuedes responder «*Confirmar*» o «Sí» para enviar la solicitud, indicar un cambio o escribir «*Descartar*» para abandonar el borrador.",OperationStatus.AwaitingConfirmation,quote);
                }
                return await Submit(quote);
            }
            if (proposal.Kind is OrderTurnKind.Edit or OrderTurnKind.Location)
            {
                context.OrderDraftItems = staged; context.SelectedLocationId=location;
                context.CurrentGoal="ORDER"; context.CurrentStep=needsLocation ? "ORDER_LOCATION" : "ORDER_REVIEW:" + quote.Fingerprint;
                if (proposal.Edits.Count > 0) new OfferingReference(workspaceId,"PRODUCT",proposal.Edits.Select(e => e.ProductId).Distinct().ToArray(),location).Remember(context);
            }
            var question = needsLocation ? "¿En cuál sede deseas solicitar estos productos?" : "¿Deseas agregar o cambiar algo, o enviar la solicitud para revisión?";
            context.LastQuestion=question;
            return await Remember(quote.Text + "\n\n" + question,quote:quote);
        }
        catch (OverflowException) { return await Remember("La cantidad o el importe supera el máximo permitido. ¿Qué cantidad menor deseas?",OperationStatus.NotAvailable); }
        catch (ArgumentException ex) { return await Remember(ex.Message + " ¿Qué producto deseas corregir?",OperationStatus.NotAvailable); }

        async Task<ResponsePlan> Remember(string text, OperationStatus status = OperationStatus.Collecting, OrderDraftMerger.Quote? quote = null, bool preserveQuestion = false)
        {
            context.OrderLastSourceMessageId=sourceMessageId; context.OrderLastResponse=text;
            if (status == OperationStatus.AwaitingConfirmation && !preserveQuestion)
                ConversationQuestion.Remember(workspaceId, sourceMessageId, context, ExpectedReply.ConfirmOrder,
                    ConversationIntent.Order, text, clock?.UtcNow ?? DateTime.UtcNow, conversationId);
            await contexts.SaveContextAsync(workspaceId,phone,context,ct);
            return Plan(text,status,quote);
        }
        async Task<ResponsePlan> Submit(OrderDraftMerger.Quote? quote)
        {
            if (context.OrderDraftItems.Count == 0) return await Remember("Tu pedido está vacío. ¿Qué producto deseas agregar?");
            var id=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceMessageId)));
            // CAS is acquired before the durable business effect, without holding a transaction across network calls.
            if (!resuming) {
                context.CurrentStep="ORDER_COMMIT:" + quote!.Fingerprint; context.OrderLastSourceMessageId=sourceMessageId; context.OrderLastResponse=null;
                await contexts.SaveContextAsync(workspaceId,phone,context,ct);
            }
            var currentModules=(await entitlements.GetAvailableModuleCodesAsync(workspaceId,ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (AiIntentAccess.GetDeniedResponse(ConversationIntent.Order,currentModules) is { } revoked) {
                context.CurrentStep=null; return await Remember(revoked,OperationStatus.Denied);
            }
            var request=new OrderRecord { Id=id,ConversationId=conversationId,ConsumerPhone=phone,ConsumerName=customerName,
                Status=OrderStatus.PendingReview, RequestedLocationId=context.SelectedLocationId,
                ExpectedCatalog=context.OrderDraftItems.ToDictionary(i=>i.ProductId,i=>{
                    var p=products.Single(p=>p.Id==i.ProductId);return new OrderCatalogExpectation(p.Name,p.PriceMinorUnits,p.Currency);
                }),
                Notes="Solicitud pendiente de revisión por el negocio. Precios del catálogo al enviarla." + (context.SelectedLocationId == null ? "" : " Sede solicitada: " + context.SelectedLocationId + "."),
                Items=context.OrderDraftItems.Select(i => new OrderItemRecord { ProductId=i.ProductId,ProductName=i.ProductName,Quantity=i.Quantity }).ToList() };
            OrderRecord saved;
            try { saved=await orders.CreateOrderAsync(workspaceId,request,ct); }
            catch (OrderCatalogChangedException ex) { return await RecoverCatalog(ex); }
            catch (ArgumentException ex) { context.CurrentStep=null; return await Remember(ex.Message + " Corrige o quita el producto antes de enviar.",OperationStatus.NotAvailable); }
            await notifications.NotifyOnceAsync(workspaceId,"ORDERS",NotificationType.NewCommercialRequest,saved.Id,"Pedido recibido",
                $"{saved.ConsumerName} envió una solicitud con {saved.Items.Count} ítems para revisión.","/orders",ct);
            context.CurrentGoal=null; context.CurrentStep=null; context.OrderDraftItems.Clear(); context.MissingFields.Clear();context.LastQuestion=null;
            return await Remember($"Tu solicitud de pedido fue enviada. El negocio la revisará. Código: {saved.Id[..6]}.",OperationStatus.Succeeded,quote);
        }

        async Task<ResponsePlan> RecoverCatalog(OrderCatalogChangedException conflict)
        {
            // A known catalog rejection rolled back the transaction. Still inspect the
            // original identity before recovering, never create a second business effect.
            if (await TryResumeAsync(workspaceId,phone,conversationId,sourceMessageId,context,ct) is { } completed)
                return Plan(completed,OperationStatus.Succeeded);
            var authorized=(await entitlements.GetAvailableModuleCodesAsync(workspaceId,ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (AiIntentAccess.GetDeniedResponse(ConversationIntent.Order,authorized) is { } deniedNow)
            { context.CurrentStep=null;return await Remember(deniedNow,OperationStatus.Denied); }
            if (conflict.Change == OrderCatalogChange.LocationDeleted) context.SelectedLocationId=null;
            var current=(await offerings.GetProductsAsync(workspaceId,context.SelectedLocationId,null,ct)).Where(p=>p.IsActive).Select(VerifiedOffering.Capture).ToArray();
            var fresh=OrderDraftMerger.Price(workspaceId,context.OrderDraftItems,current,context.SelectedLocationId,
                context.SelectedLocationId == null ? null : locationName);
            var description=conflict.Change switch {
                OrderCatalogChange.Location => "El catálogo cambió: un producto ya no se ofrece en la sede seleccionada.",
                OrderCatalogChange.LocationDeleted => "El catálogo cambió: la sede seleccionada ya no está disponible.",
                OrderCatalogChange.ProductUnavailable => "Cambió la disponibilidad de un producto del catálogo.",
                _ => "El catálogo cambió."
            };
            var changes=from previous in products
                join updated in current on previous.Id equals updated.Id
                where context.OrderDraftItems.Any(i=>i.ProductId==previous.Id)
                select previous.Currency != updated.Currency ? $"• {updated.Name}: moneda de {previous.Currency} a {updated.Currency}."
                    : previous.PriceMinorUnits != updated.PriceMinorUnits ? $"• {updated.Name}: precio de {previous.Currency} {previous.PriceMinorUnits/100m:0.00} a {updated.Currency} {updated.PriceMinorUnits/100m:0.00}."
                    : previous.Name != updated.Name ? $"• El producto {previous.Name} ahora se llama {updated.Name}." : null;
            var details=changes.Where(c=>c!=null).ToArray();
            if(details.Length>0) description+="\n"+string.Join("\n",details);
            var needsLocation=conflict.Change == OrderCatalogChange.LocationDeleted || fresh.NeedsLocation;
            context.CurrentStep=needsLocation ? "ORDER_LOCATION" : fresh.Complete ? "ORDER_CONFIRM:" + fresh.Fingerprint : "ORDER_REVIEW:" + fresh.Fingerprint;
            var question=needsLocation ? "¿En cuál sede deseas solicitar el pedido?"
                : fresh.Complete ? "¿Confirmas que enviemos la solicitud con estos importes actualizados?" : "¿Qué producto deseas corregir o quitar, o qué sede prefieres?";
            context.LastQuestion=question;
            return await Remember(description + "\n" + fresh.Text + "\n" + question,
                fresh.Complete && !needsLocation ? OperationStatus.AwaitingConfirmation : OperationStatus.NotAvailable,fresh);
        }
    }
}

