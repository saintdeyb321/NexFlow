using System.Collections.ObjectModel;
using System.Text.Json;

namespace NexFlow.Application.Features.Automation.Dialogue.Contracts;

public sealed record VerifiedResponseBlock(string Id, string Text);

// Only the trusted formatter may declare paragraphs independent. A model can
// group opaque keys in their ORIGINAL order, never provide text, omit constraints
// or change the ordinal references already remembered by the dialogue.
public sealed class VerifiedResponseLayout(string introduction, IEnumerable<VerifiedResponseBlock> blocks, string conclusion)
{
    public string Introduction { get; } = introduction;
    public ReadOnlyCollection<VerifiedResponseBlock> Blocks { get; } = Array.AsReadOnly(blocks.ToArray());
    public string Conclusion { get; } = conclusion;
    public string CanonicalText => Render(Blocks.Select(b => b.Id));
    public bool IsBounded => Blocks.Count is >= 2 and <= 5 && Blocks.Select(b => b.Id).Distinct(StringComparer.Ordinal).Count() == Blocks.Count
        && Blocks.All(b => b.Id.Length is > 0 and <= 12 && !string.IsNullOrWhiteSpace(b.Text) && b.Text.Length <= 1000)
        && Introduction.Length <= 200 && Conclusion.Length <= 500 && CanonicalText.Length <= 3000;

    public string? VerifyAndRender(string json)
    {
        if (!IsBounded || json.Length > 512) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2
                || !root.TryGetProperty("Version", out var version) || !version.TryGetInt32(out var number) || number != 2
                || !root.TryGetProperty("Paragraphs", out var paragraphs) || paragraphs.ValueKind != JsonValueKind.Array
                || paragraphs.GetArrayLength() < 1 || paragraphs.GetArrayLength() > Blocks.Count) return null;
            var groups = new List<string[]>();
            foreach (var group in paragraphs.EnumerateArray())
            {
                if (group.ValueKind != JsonValueKind.Array || group.GetArrayLength() < 1 || group.GetArrayLength() > Blocks.Count
                    || group.EnumerateArray().Any(i => i.ValueKind != JsonValueKind.String)) return null;
                groups.Add(group.EnumerateArray().Select(i => i.GetString()!).ToArray());
            }
            if (!groups.SelectMany(g => g).SequenceEqual(Blocks.Select(b => b.Id), StringComparer.Ordinal)) return null;
            return Introduction + "\n" + string.Join("\n\n", groups.Select(ids => string.Join("\n", ids.Select(id => Blocks.Single(b => b.Id == id).Text)))) + "\n" + Conclusion;
        }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private string Render(IEnumerable<string> ids) => Introduction + "\n" + string.Join("\n", ids.Select(id => Blocks.Single(b => b.Id == id).Text)) + "\n" + Conclusion;
}
