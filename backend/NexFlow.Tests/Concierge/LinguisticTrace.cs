using System.Text.Json;
using NexFlow.Application.Abstractions.Cache;

namespace NexFlow.Tests.Concierge;

// Explicit synthetic investigation artifacts only; never operational/customer logging.
internal static class LinguisticTrace
{
    internal static void Write(string id, int turn, AssistantConversationTests.Fixture f,
        ConversationContextDto before, ConversationContextDto after, int preparedOffset,
        string input, string response, IEnumerable<string> failures)
    {
        var path = Environment.GetEnvironmentVariable("NEXFLOW_LINGUISTIC_TRACE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(path) || id is not ("H04" or "H12" or "H22" or "H16" or "H27" or "H06")) return;
        var prepared = f.Understood.Skip(preparedOffset).LastOrDefault();
        var resolution = prepared?.Interpretation.BookingProposal?.Understanding;
        string Redact(string value)
        {
            foreach (var name in new[] { before.RealCustomerName, before.PendingCustomerName, after.RealCustomerName, after.PendingCustomerName }
                .Where(n => !string.IsNullOrEmpty(n)).Distinct().OrderByDescending(n => n!.Length))
                value = value.Replace(name!, "[SYNTHETIC_NAME]", StringComparison.OrdinalIgnoreCase);
            return value;
        }
        var data = new {
            Synthetic = true, Case = id, Turn = turn, Input = Redact(input), Response = Redact(response),
            Act = f.Plans.LastOrDefault()?.Act.ToString(), Intent = prepared?.Interpretation.Intent.ToString() ?? f.Plans.LastOrDefault()?.Intent.ToString(),
            RecoveryHandled = prepared == null,
            Route = prepared?.UsedAi == true ? "SemanticMock" : "Local",
            Before = Snapshot(before), After = Snapshot(after), Criteria = failures,
            Entities = new {
                OfferingIds = prepared?.Interpretation.OfferingQuery?.Offerings.Select(o => o.Id),
                Edits = prepared?.Interpretation.OrderProposal?.Edits.Select(e => new { e.ProductId, e.Quantity, e.Kind }),
                DateStatus = resolution?.Date.Status.ToString(), DateCode = resolution?.Date.Code,
                DateCandidates = resolution?.Date.Candidates.Select(c => c.Value.ToString("yyyy-MM-dd")),
                ServiceIds = resolution?.Service.Candidates.Select(c => c.EntityId),
                LocationIds = resolution?.Location.Candidates.Select(c => c.EntityId)
            }
        };
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, $"{id}-{turn:00}.json"), JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static object Snapshot(ConversationContextDto state) => new {
        state.CurrentGoal, state.CurrentStep, state.SelectedLocationId, state.SelectedServiceId,
        state.TargetDate, state.TargetTime, state.PendingRecoveryAction,
        CustomerNamePresent = state.RealCustomerName != null,
        Items = state.OrderDraftItems.Select(i => new { i.ProductId, i.Quantity }),
        Menu = state.RecoveryOptions.Select(o => new { o.Kind, o.Key })
    };
}
