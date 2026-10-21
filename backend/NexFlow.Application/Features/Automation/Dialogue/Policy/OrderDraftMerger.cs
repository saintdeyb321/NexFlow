using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Domain.Exceptions;

namespace NexFlow.Application.Features.Automation.Dialogue.Policy;

public static class OrderDraftMerger
{
    public static List<OrderDraftItem> Apply(Guid workspace, string source, ConversationContextDto context,
        OrderDraftProposal proposal, IReadOnlyList<VerifiedOffering> products)
    {
        if (proposal.WorkspaceId != workspace || proposal.SourceMessageId != source || proposal.ExpectedStateVersion != context.StateVersion)
            throw new ConcurrencyException("Order proposal no longer matches the authorized turn.");
        var result = context.OrderDraftItems.Select(i => new OrderDraftItem { ProductId=i.ProductId, ProductName=i.ProductName, Quantity=i.Quantity }).ToList();
        foreach (var edit in proposal.Edits)
        {
            var existing = result.SingleOrDefault(i => i.ProductId == edit.ProductId);
            if (edit.Kind == OrderEditKind.Remove)
            { if (existing == null) throw new ArgumentException("Ese producto no está en el pedido."); result.Remove(existing); continue; }
            var product = products.SingleOrDefault(p => p.Id == edit.ProductId && p.Type == "PRODUCT")
                ?? throw new ArgumentException("Ese producto ya no está activo en el catálogo de esta sede.");
            if (edit.Quantity <= 0) throw new ArgumentException("La cantidad debe ser un entero mayor que cero.");
            if (edit.Kind == OrderEditKind.Replace)
            {
                var previous = result.SingleOrDefault(i => i.ProductId == edit.ReplacesProductId)
                    ?? throw new ArgumentException("Necesito precisar qué producto deseas reemplazar.");
                result.Remove(previous);
            }
            var quantity = edit.Kind is OrderEditKind.Add or OrderEditKind.Replace ? checked((existing?.Quantity ?? 0) + edit.Quantity) : edit.Quantity;
            if (existing != null) { existing.Quantity=quantity; existing.ProductName=product.Name; }
            else result.Add(new() { ProductId=product.Id, ProductName=product.Name, Quantity=quantity });
        }
        if (result.Count > 50) throw new ArgumentException("Podemos preparar hasta 50 productos distintos por solicitud.");
        return result;
    }

    public sealed record Quote(string Text, string Fingerprint, bool Complete, bool NeedsLocation,
        IReadOnlyList<VerifiedFact> Facts, string? Currency);

    public static Quote Price(Guid workspace, IReadOnlyList<OrderDraftItem> items, IReadOnlyList<VerifiedOffering> products, string? location)
    {
        var lines = new List<string>(); var facts = new List<VerifiedFact>(); var totals = new SortedDictionary<string,long>(StringComparer.OrdinalIgnoreCase);
        var complete = items.Count > 0; var needsLocation = false;
        var identity = new List<object>();
        foreach (var item in items)
        {
            var product = products.SingleOrDefault(p => p.Id == item.ProductId);
            if (product == null || item.Quantity <= 0 || product.PriceMinorUnits < 0 || string.IsNullOrWhiteSpace(product.Currency))
            { complete=false; lines.Add($"• {item.Quantity} × {item.ProductName}: no está disponible con un precio vigente."); continue; }
            if (location == null && product.LocationScope.Equals("SPECIFIC", StringComparison.OrdinalIgnoreCase)) needsLocation = true;
            if (location != null && !product.AtLocation(location)) { complete=false; lines.Add($"• {product.Name}: no se ofrece en esta sede."); continue; }
            var subtotal = checked(item.Quantity * product.PriceMinorUnits);
            totals[product.Currency] = checked(totals.GetValueOrDefault(product.Currency) + subtotal);
            lines.Add($"• {item.Quantity} × {product.Name}: {product.Currency} {subtotal / 100m:0.00}");
            facts.Add(new("Product", product.Name, VerifiedFactSource.BusinessRepository, product.Id));
            facts.Add(new("Quantity", item.Quantity.ToString(CultureInfo.InvariantCulture), VerifiedFactSource.ValidatedConversation, product.Id));
            facts.Add(new("UnitPriceMinorUnits", product.PriceMinorUnits.ToString(CultureInfo.InvariantCulture), VerifiedFactSource.BusinessRepository, product.Id));
            facts.Add(new("SubtotalMinorUnits", subtotal.ToString(CultureInfo.InvariantCulture), VerifiedFactSource.AuthorizedWorkflow, product.Id));
            identity.Add(new { product.Id, product.Name, product.PriceMinorUnits, product.Currency, item.Quantity, product.LocationScope, product.LocationIds });
        }
        var text = items.Count == 0 ? "Tu pedido está vacío." : "Así va tu pedido:\n" + string.Join("\n",lines);
        if (totals.Count > 0) text += "\nSubtotal parcial: " + string.Join("; ",totals.Select(t => $"{t.Key} {t.Value / 100m:0.00}")) + ".";
        text += "\nImportes del catálogo; el negocio revisará la solicitud. No incluyen cargos adicionales no registrados.";
        if (totals.Count > 1) text += " No hay un total único porque los productos tienen monedas distintas.";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { workspace,location,identity }))));
        return new(text,hash,complete,needsLocation,Array.AsReadOnly(facts.ToArray()),totals.Count == 1 ? totals.Keys.Single() : null);
    }
}
