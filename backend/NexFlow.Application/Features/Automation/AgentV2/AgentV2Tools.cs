using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.Automation.Agent;
using NexFlow.Application.Features.Business.Offerings;
using NexFlow.Application.Features.Knowledge;
using NexFlow.Application.Features.Automation.Dialogue.Understanding;

namespace NexFlow.Application.Features.Automation.AgentV2;

public sealed record AgentV2Tool(AgentV2ToolDefinition Definition, string[] Modules,
    Func<AgentV2ToolCall, ConversationContextDto, CancellationToken, Task<AgentV2ToolResult>> Execute);

// Capability adapters, not language classifiers. No business-sector branch in the runtime.
public sealed class AgentV2Tools(IBusinessReadTools reads, IOfferingService offerings, IAgentBookingDrafts drafts, IClock clock)
{
    private static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private static JsonElement Schema(params string[] properties) => JsonSerializer.SerializeToElement(new {
        type = "object", properties = properties.ToDictionary(p => p, _ => new { type = "string", maxLength = 200 }), additionalProperties = false });
    private static void Check(JsonElement args, params string[] fields)
    {
        if (args.ValueKind != JsonValueKind.Object || args.EnumerateObject().Any(p => !fields.Contains(p.Name, StringComparer.Ordinal)))
            throw new ArgumentException("Unregistered tool argument.");
    }
    private static string? Text(JsonElement args, string name) => args.TryGetProperty(name, out var value)
        ? value.ValueKind == JsonValueKind.String && value.GetString() is { Length: <= 200 } text ? text : throw new ArgumentException("Invalid bounded tool argument.") : null;

    public IReadOnlyDictionary<string, AgentV2Tool> For(AgentTurnRequest request, AgentV2Workspace configuration, IReadOnlySet<string> modules)
    {
        var registered = new Dictionary<string, AgentV2Tool>(StringComparer.Ordinal);
        void Read(string name, string description, KnowledgeTopic topic, params string[] required)
        {
            if (required.Any(m => !modules.Contains(m))) return;
            registered.Add(name, new(new(name, description, Schema("search", "locationId")), required, async (call, _, ct) => {
                Check(call.Arguments, "search", "locationId");
                var data = await reads.ReadAsync(reads.Open(request.WorkspaceId), new KnowledgeRead(topic, Text(call.Arguments, "locationId"), Text(call.Arguments, "search")), ct);
                return Result(call, data, request.WorkspaceId, required, topic.ToString());
            }));
        }
        Read("get_business_profile", "Public business information", KnowledgeTopic.Profile);
        Read("list_locations", "Authorized locations", KnowledgeTopic.Locations, "LOCATIONS");
        Read("get_hours", "Verified business/location hours", KnowledgeTopic.BusinessHours, "BUSINESS_HOURS");
        Read("search_knowledge", "Business FAQs and policies", KnowledgeTopic.Faqs, "FAQ");
        Read("list_services", "Services; publication is not appointment availability", KnowledgeTopic.Services, "SERVICES");
        Read("search_products", "Products; publication is not physical stock", KnowledgeTopic.Products, "CATALOG");
        registered.Add("handoff", new(new("handoff", "Request human assistance; does not promise acceptance or notification. evidence must quote the current user message", Schema("evidence")), [], (call, state, ct) => {
            Check(call.Arguments, "evidence");
            var evidence = Text(call.Arguments, "evidence");
            if (string.IsNullOrWhiteSpace(evidence) || !AiEvidenceValidator.HasFragment(request.Text, evidence)) throw new ArgumentException("Unsubstantiated handoff request.");
            var prepared = AgentResponseAuthority.Prepare(request, state, new AgentTurnResult(true, "AgentV2Handoff", new(AgentDecision.HumanAssistance, evidence, ""), [], 0, 0), modules, clock.UtcNow);
            return Task.FromResult(new AgentV2ToolResult(call.Id, call.Name, "HumanAssistanceRequested",
                [new(call.Id + ":0", "HumanAssistanceRequested", prepared.Result.VerifiedText!, "BackendDecision")], [], prepared.Next));
        }));
        if (configuration.CapabilityPacks.Contains("appointments", StringComparer.Ordinal) && modules.Contains("SERVICES") && modules.Contains("RESERVATIONS")) {
            registered.Add("check_appointment_availability", new(new("check_appointment_availability", "Real appointment availability, not confirmation", Schema("locationId", "serviceId", "date", "time")), ["SERVICES", "RESERVATIONS"], async (call, _, ct) => {
                Check(call.Arguments, "locationId", "serviceId", "date", "time");
                if (!DateOnly.TryParseExact(Text(call.Arguments, "date"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) throw new ArgumentException("Invalid local date.");
                TimeOnly? time = null;
                if (Text(call.Arguments, "time") is { } clock) time = TimeOnly.ParseExact(clock, "HH:mm", CultureInfo.InvariantCulture);
                var data = await reads.ReadAsync(reads.Open(request.WorkspaceId), new AvailabilityRead(Text(call.Arguments, "locationId") ?? "", Text(call.Arguments, "serviceId") ?? "", day.ToDateTime(TimeOnly.MinValue), time), ct);
                return Result(call, data, request.WorkspaceId, ["SERVICES", "RESERVATIONS"], "Availability");
            }));
            var slot = new { type = "object", properties = new { Value = new { type = "string", maxLength = 160 }, Evidence = new { type = "string", maxLength = 300 } }, required = new[] { "Value", "Evidence" }, additionalProperties = false };
            var schema = JsonSerializer.SerializeToElement(new { type = "object", properties = new { Location = slot, Service = slot, Date = slot, Time = slot, Name = slot }, additionalProperties = false });
            registered.Add("prepare_appointment", new(new("prepare_appointment", "Reversible preparation only, never consent or booking creation", schema), ["SERVICES", "RESERVATIONS"], async (call, state, ct) => {
                Check(call.Arguments, "Location", "Service", "Date", "Time", "Name");
                var patch = JsonSerializer.Deserialize<AgentBookingPatch>(call.Arguments, Json) ?? throw new ArgumentException("Invalid preparation.");
                var staged = await drafts.StageAsync(request, state, patch, 0, ct);
                return new(call.Id, call.Name, staged.Clarification == null ? "PreparedOnly" : "Clarification", [], ["SERVICES", "RESERVATIONS"], staged.Context);
            }));
        }
        if (configuration.CapabilityPacks.Contains("accommodation", StringComparer.Ordinal) && modules.Contains("SERVICES") && configuration.RoomTypeServiceIds.Length > 0) {
            registered.Add("search_room_types", new(new("search_room_types", "Configured room types from SERVICES; no room-night inventory or tariff calculation", Schema()), ["SERVICES"], async (call, _, ct) => {
                Check(call.Arguments);
                var types = (await offerings.GetServicesAsync(request.WorkspaceId, null, null, ct)).Where(s => s.IsActive && configuration.RoomTypeServiceIds.Contains(s.Id, StringComparer.Ordinal)).Take(5).ToArray();
                var facts = types.SelectMany(s => new[] { ("RoomType", s.Name, s.Id), ("ListedPriceMinorUnits", s.PriceMinorUnits.ToString(CultureInfo.InvariantCulture), s.Id), ("Currency", s.Currency, s.Id) })
                    .Select((f, i) => new AgentV2Fact(call.Id + ":" + i, f.Item1, f.Item2, "BusinessRepository", f.Item3)).ToArray();
                return new(call.Id, call.Name, facts.Length == 0 ? "NotFound" : "Found", facts, ["SERVICES"]);
            }));
            Read("get_stay_rules", "Only policies actually present in authorized FAQs", KnowledgeTopic.Faqs, "FAQ");
        }
        return registered;
    }
    private static AgentV2ToolResult Result(AgentV2ToolCall call, BusinessReadResult result, Guid workspace, string[] modules, string source)
    {
        if (result.WorkspaceId != workspace || result.Source != source || result.Facts.Any(f => f.Sensitive)) throw new InvalidOperationException("Tool provenance rejected.");
        var facts = result.Status == BusinessReadStatus.Found ? result.Facts.Select(f => new { f.Key, f.Value, Source = f.Source.ToString(), f.EntityId }).ToList() : [];
        if (result.Status == BusinessReadStatus.Found && source == "Locations" && result.Knowledge is { } knowledge)
            facts.AddRange(knowledge.References.Take(5).Select(r => new { Key = "Location", Value = r.Name, Source = "BusinessRepository", EntityId = (string?)r.Id }));
        return new(call.Id, call.Name, result.Status.ToString(), facts.Select((f, i) => new AgentV2Fact(call.Id + ":" + i, f.Key, f.Value, f.Source, f.EntityId)).ToArray(), modules);
    }
}
