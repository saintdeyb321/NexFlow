using System.Collections.ObjectModel;
using System.Text.Json;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.Shared.DTOs;
using NexFlow.Application.Features.Services.DTOs;

namespace NexFlow.Application.Features.Automation.Dialogue.Contracts;

public enum OrderEditKind { Add, Set, Remove, Replace }
public enum OrderTurnKind { Edit, Summary, Hypothetical, Submit, Location, Clarify, Start }
public enum OfferingQuestion { Facts, Price, Duration, Availability, Stock, Comparison, Recommendation }
public sealed record OrderEdit(OrderEditKind Kind, string ProductId, int Quantity, string? ReplacesProductId = null);

// Server-built proposals contain identities and quantities, never model-supplied prices.
public sealed record OrderDraftProposal(Guid WorkspaceId, string SourceMessageId, string? ExpectedStateVersion,
    OrderTurnKind Kind, ReadOnlyCollection<OrderEdit> Edits, string? LocationId = null, string? Clarification = null);
public sealed record VerifiedOffering(string Id, string Name, string? Description, long PriceMinorUnits, string Currency,
    string Type, string LocationScope, ReadOnlyCollection<string> LocationIds, ReadOnlyCollection<string> Aliases,
    int? DurationMinutes = null, bool RequiresReservation = false)
{
    public bool AtLocation(string? id) => LocationScope.Equals("ALL", StringComparison.OrdinalIgnoreCase)
        || id != null && LocationScope.Equals("SPECIFIC", StringComparison.OrdinalIgnoreCase) && LocationIds.Contains(id);
    public BusinessEntityOption Option => new(Id, Name, Aliases);
    public static VerifiedOffering Capture(BusinessOfferingDto value) => new(value.Id, value.Name, value.Description,
        value.PriceMinorUnits, value.Currency, value.Type, value.LocationScope, Array.AsReadOnly(value.LocationIds.ToArray()),
        Array.AsReadOnly(ReadAliases(value.Metadata).ToArray()), (value as ServiceDto)?.DurationInMinutes,
        (value as ServiceDto)?.RequiresReservation == true);

    private static IEnumerable<string> ReadAliases(Dictionary<string, object> metadata)
    {
        var entry = metadata.FirstOrDefault(p => p.Key.Equals("aliases", StringComparison.OrdinalIgnoreCase)).Value;
        return entry switch {
            JsonElement { ValueKind: JsonValueKind.Array } json => json.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).Take(20).ToArray(),
            IEnumerable<string> names => names.Take(20).ToArray(),
            IEnumerable<object> names => names.OfType<string>().Take(20).ToArray(),
            _ => []
        };
    }
}
public sealed record OfferingQueryPlan(Guid WorkspaceId, string SourceMessageId, string? StateVersion,
    string Type, OfferingQuestion Question, ReadOnlyCollection<VerifiedOffering> Offerings, bool Broad,
    string? LocationId = null, string? Clarification = null);
public sealed record CommerceCatalog(Guid WorkspaceId, string? StateVersion, ReadOnlyCollection<VerifiedOffering> Products,
    ReadOnlyCollection<VerifiedOffering> Services, ReadOnlyCollection<BusinessEntityOption> Locations);

// Uses the existing LastIntent envelope; no extra document, schema or conversation history.
public sealed record OfferingReference(Guid WorkspaceId, string Type, string[] Ids, string? LocationId)
{
    private const string Prefix = "OFFERING:v1:";
    public static OfferingReference? Read(Guid workspace, ConversationContextDto context)
    {
        if (context.LastIntent?.StartsWith(Prefix, StringComparison.Ordinal) != true || context.LastIntent.Length > 4000) return null;
        try {
            var reference = JsonSerializer.Deserialize<OfferingReference>(context.LastIntent[Prefix.Length..]);
            return reference?.WorkspaceId == workspace && reference.Type is "PRODUCT" or "SERVICE"
                && reference.Ids is { Length: > 0 and <= 50 } ? reference : null;
        } catch (JsonException) { return null; }
    }
    public void Remember(ConversationContextDto context) => context.LastIntent = Prefix + JsonSerializer.Serialize(this);
}

