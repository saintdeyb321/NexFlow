using System.Text.RegularExpressions;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;

namespace NexFlow.Application.Features.Automation.Dialogue.Understanding;

/// <summary>Trailing courtesy clauses outside authorized catalog names, never a global token blacklist.</summary>
internal static class CommercialCourtesy
{
    private static readonly string[][] Endings = [["por", "favor"], ["si", "puedes"], ["porfa"], ["gracias"]];

    internal static string Trim(string text, IReadOnlyList<VerifiedOffering> offerings)
    {
        var spans = Regex.Matches(text, @"[\p{L}\p{M}\p{N}]+", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        var tokens = spans.Select(m => SpanishTextNormalizer.Normalize(m.Value)).ToArray();
        var protectedTokens = new bool[tokens.Length];
        foreach (var name in offerings.SelectMany(o => new[] { o.Name }.Concat(o.Aliases)))
        {
            var words = OrderMentionResolver.Words(name);
            if (words.Length == 0) continue;
            for (var i = 0; i + words.Length <= tokens.Length; i++)
                if (words.Select((w, n) => OfferingMentionResolver.SameWord(w, tokens[i + n])).All(match => match))
                    Array.Fill(protectedTokens, true, i, words.Length);
        }
        var end = tokens.Length;
        while (end > 0)
        {
            var ending = Endings.FirstOrDefault(e => e.Length <= end
                && e.Select((word, i) => tokens[end - e.Length + i] == word && !protectedTokens[end - e.Length + i]).All(match => match));
            if (ending == null) break;
            end -= ending.Length;
        }
        return end == tokens.Length ? text : end == 0 ? "" : text[..(spans[end - 1].Index + spans[end - 1].Length)];
    }
}
