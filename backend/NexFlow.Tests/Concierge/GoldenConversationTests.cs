using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using Xunit;

namespace NexFlow.Tests.Concierge;

public sealed class GoldenConversationTests
{
    public sealed record ExpectedSlot(ResolutionStatus Status, string? Value = null);
    public sealed record GoldenTurn(string Text, string? Step, Dictionary<string, ExpectedSlot> Expected, string[]? Modules = null);
    public sealed record GoldenDialogue(string Id, string Sector, string Service, string Product, string[] Modules,
        SlotResolutionPurpose Purpose, GoldenTurn[] Turns);
    public sealed record GoldenCorpus(string BaselineCommit, string Scope, GoldenDialogue[] Dialogues);
    private static GoldenCorpus Corpus()
    {
        using var stream = typeof(GoldenConversationTests).Assembly.GetManifestResourceStream("NexFlow.Tests.Concierge.GoldenConversations.json")!;
        return JsonSerializer.Deserialize<GoldenCorpus>(stream, new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } })!;
    }
    public static IEnumerable<object[]> Cases() => Corpus().Dialogues.Select(d => new object[] { d.Id });

    [Fact]
    public void Corpus_freezes_the_baseline_and_contains_64_multiturn_dialogues_in_four_sectors()
    {
        var corpus = Corpus();
        Assert.Equal("9c038c898080c0e212f8bd0df4daba07a08d8e8f", corpus.BaselineCommit);
        Assert.Equal("Phase0And1SlotResolutionOnly", corpus.Scope);
        Assert.Equal(64, corpus.Dialogues.Length);
        Assert.Equal(64, corpus.Dialogues.Select(d => d.Id).Distinct().Count());
        Assert.Equal(4, corpus.Dialogues.Select(d => d.Sector).Distinct().Count());
        Assert.All(corpus.Dialogues, d => Assert.InRange(d.Turns.Length, 2, 5));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Golden_slots_are_resolved_without_providers_transactions_or_state_mutation(string id)
    {
        var dialogue = Corpus().Dialogues.Single(d => d.Id == id);
        var fixture = new ConciergeFixture(dialogue.Service, dialogue.Product);
        fixture.Modules.IntersectWith(dialogue.Modules);
        var state = new ConversationContextDto { StateVersion = "existing-string-version" };
        for (var i = 0; i < dialogue.Turns.Length; i++)
        {
            var turn = dialogue.Turns[i]; state.CurrentStep = turn.Step;
            if (turn.Modules != null) { fixture.Modules.Clear(); fixture.Modules.UnionWith(turn.Modules); }
            var before = JsonSerializer.Serialize(state);
            var result = await fixture.Resolver.ResolveAsync(fixture.Workspace, $"{id}-{i}", turn.Text, state, dialogue.Purpose, default);
            Assert.Equal(before, JsonSerializer.Serialize(state));
            Assert.Equal("existing-string-version", result.Context.StateVersion);
            Assert.Equal(fixture.Workspace, result.Context.WorkspaceId);
            Assert.Equal(new DateTime(2026, 10, 20, 16, 0, 0), result.Context.LocalBusinessNow.DateTime);
            Assert.Null(result.Intent);
            foreach (var (slot, expected) in turn.Expected)
            {
                switch (slot)
                {
                    case "Name": Check(result.Name, expected); break;
                    case "Location": Check(result.Location, expected); break;
                    case "Service": Check(result.Service, expected); break;
                    case "Product": Check(result.Product, expected); break;
                    case "Date": Check(result.Date, expected); break;
                    case "Time": Check(result.Time, expected); break;
                    default: Assert.Fail("Unknown golden slot: " + slot); break;
                }
            }
        }
    }

    private static void Check<T>(SlotResolution<T> actual, ExpectedSlot expected)
    {
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.Status == ResolutionStatus.Determined, actual.TryGetValue(out var value));
        if (expected.Value != null) Assert.Equal(expected.Value, Format(value!));
    }
    internal static string Format<T>(T value) => value switch
    {
        string name => name,
        ResolvedBusinessEntity entity => entity.Id,
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimePreference { Kind: TimePreferenceKind.Exact } time => $"Exact:{time.ExactTime:HH:mm}",
        TimePreference { Kind: TimePreferenceKind.Period } time => $"Period:{time.From:HH:mm}-{time.Until:HH:mm}",
        TimePreference { Kind: TimePreferenceKind.After } time => $"After:{time.From:HH:mm}",
        TimePreference time => $"Before:{time.Until:HH:mm}",
        _ => throw new InvalidOperationException("Unsupported golden value.")
    };
}
