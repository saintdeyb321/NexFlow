using NexFlow.Application.Features.Automation.Dialogue.Contracts;

namespace NexFlow.Application.Features.Automation.Dialogue.Understanding;

// Exact tokens/aliases only; no edit-distance guess that could silently choose another entity.
internal static class EntityMentionMatcher
{
    internal static SlotResolution<ResolvedBusinessEntity> Resolve(string text, IReadOnlyList<BusinessEntityOption> options, bool directAnswer)
    {
        if (string.IsNullOrWhiteSpace(text)) return SlotResolution<ResolvedBusinessEntity>.Missing();
        if (text.Length > 2000) return SlotResolution<ResolvedBusinessEntity>.Invalid("InputTooLong");
        var entities = options.Where(o => !string.IsNullOrWhiteSpace(o.Id) && !string.IsNullOrWhiteSpace(o.Name)).ToArray();
        string Key(string value) => string.Join(' ', SpanishTextNormalizer.Terms(value));
        IEnumerable<string> Keys(BusinessEntityOption option) => new[] { option.Name }.Concat(option.Aliases ?? []).Select(Key).Where(k => k.Length > 0);
        var term = Key(text);
        var exact = entities.Where(o => string.Equals(text.Trim(), o.Id, StringComparison.Ordinal) || Keys(o).Contains(term, StringComparer.Ordinal)).ToArray();
        if (exact.Length > 0) return Result(exact);
        var full = entities.Where(o => Keys(o).Any(k => SpanishTextNormalizer.ContainsPhrase(term, k))).ToArray();
        if (full.Length > 0) return Result(full);
        var tokens = SpanishTextNormalizer.Terms(text).Where(t => t.Length >= 3).ToHashSet(StringComparer.Ordinal);
        var partial = tokens.Count == 0 ? [] : entities.Where(o => Keys(o).Any(k => tokens.IsSubsetOf(k.Split(' ')))).ToArray();
        if (partial.Length > 0) return Result(partial);
        return directAnswer && term.Length > 0 ? SlotResolution<ResolvedBusinessEntity>.Invalid("UnknownEntity") : SlotResolution<ResolvedBusinessEntity>.Missing();

        SlotResolution<ResolvedBusinessEntity> Result(IEnumerable<BusinessEntityOption> matches)
        {
            var distinct = matches.DistinctBy(o => o.Id).OrderBy(o => o.Id, StringComparer.Ordinal).ToArray();
            return distinct.Length == 1
                ? SlotResolution<ResolvedBusinessEntity>.Match(new(distinct[0].Id, distinct[0].Name, distinct[0].LocationId), text, distinct[0].Id)
                : SlotResolution<ResolvedBusinessEntity>.Ambiguous(distinct.Select(o => new SlotCandidate<ResolvedBusinessEntity>(
                    new(o.Id, o.Name, o.LocationId), text, EvidenceSource.LocalResolver, o.Id)), "MultipleEntities");
        }
    }
}

public sealed class LocationMentionResolver
{
    public SlotResolution<ResolvedBusinessEntity> Resolve(string text, IReadOnlyList<BusinessEntityOption> authorizedLocations, bool directAnswer = false)
        => EntityMentionMatcher.Resolve(text, authorizedLocations, directAnswer);
}

public sealed class OfferingMentionResolver
{
    public SlotResolution<ResolvedBusinessEntity> Resolve(string text, IReadOnlyList<BusinessEntityOption> authorizedOfferings, bool directAnswer = false)
        => EntityMentionMatcher.Resolve(text, authorizedOfferings, directAnswer);
}
