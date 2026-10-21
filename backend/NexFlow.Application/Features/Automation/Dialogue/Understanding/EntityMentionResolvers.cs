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
    internal static IReadOnlyList<BusinessEntityOption> NamedLiterals(string text, IReadOnlyList<BusinessEntityOption> options)
    {
        var value = " " + SpanishTextNormalizer.Normalize(text) + " ";
        var hits = new List<(BusinessEntityOption Option, int Start, int End)>();
        foreach (var option in options)
            foreach (var name in new[] { option.Name }.Concat(option.Aliases ?? []))
            {
                var normalized = SpanishTextNormalizer.Normalize(name);
                if (normalized.Length == 0) continue;
                var phrase = " " + normalized + " ";
                for (var start = value.IndexOf(phrase, StringComparison.Ordinal); start >= 0;
                    start = value.IndexOf(phrase, start + 1, StringComparison.Ordinal))
                    hits.Add((option, start, start + phrase.Length));
            }
        // Only subsumed spans lose specificity. Separate mentions remain separate,
        // and equal names/aliases retain ambiguity instead of choosing an ID.
        return hits.Where(h => !hits.Any(other => other.Start <= h.Start && other.End >= h.End
                && other.End - other.Start > h.End - h.Start))
            .Select(h => h.Option).DistinctBy(o => o.Id).ToArray();
    }

    public SlotResolution<ResolvedBusinessEntity> Resolve(string text, IReadOnlyList<BusinessEntityOption> authorizedOfferings, bool directAnswer = false)
        => EntityMentionMatcher.Resolve(text, authorizedOfferings, directAnswer);

    public SlotResolution<ResolvedBusinessEntity> ResolveCommercial(string text, IReadOnlyList<BusinessEntityOption> options)
    {
        var words = SpanishTextNormalizer.Terms(text).Where(w => !CommercialWords.Contains(w)).ToArray();
        if (words.Length == 0) return SlotResolution<ResolvedBusinessEntity>.Missing();
        bool Matches(string name) {
            var terms = SpanishTextNormalizer.Terms(name);
            return words.All(w => terms.Any(t => SameWord(w, t)));
        }
        var exact = options.Where(o => o.Id == text || new[] { o.Name }.Concat(o.Aliases ?? []).Any(n => SpanishTextNormalizer.Normalize(n) == SpanishTextNormalizer.Normalize(text))).ToArray();
        var matches = (exact.Length > 0 ? exact : options.Where(o => new[] { o.Name }.Concat(o.Aliases ?? []).Any(Matches)))
            .DistinctBy(o => o.Id).ToArray();
        return matches.Length switch {
            0 => SlotResolution<ResolvedBusinessEntity>.Missing(),
            1 => SlotResolution<ResolvedBusinessEntity>.Match(new(matches[0].Id, matches[0].Name, matches[0].LocationId), text, matches[0].Id),
            _ => SlotResolution<ResolvedBusinessEntity>.Ambiguous(matches.Select(o => new SlotCandidate<ResolvedBusinessEntity>(new(o.Id, o.Name, o.LocationId), text, EvidenceSource.LocalResolver, o.Id)), "MultipleEntities")
        };
    }
    private static IEnumerable<string> Forms(string word)
    {
        yield return word;
        if (word.Length > 3 && word.EndsWith('s')) yield return word[..^1];
        if (word.Length > 4 && word.EndsWith("es", StringComparison.Ordinal)) yield return word[..^2];
    }
    internal static bool SameWord(string a, string b) => Forms(a).Intersect(Forms(b), StringComparer.Ordinal).Any();
    private static readonly HashSet<string> CommercialWords = new(["cuanto","cuesta","cuestan","cual","que","es","son","esos","estos","ese","esa","dos","tres","tengo","hay","disponible","disponibles","atienden","aqui","dura","duracion","precio","precios","diferencia","entre","compara","comparar","con","recomiendas","para","una","general","ofrecen","tienen"], StringComparer.Ordinal);
}
