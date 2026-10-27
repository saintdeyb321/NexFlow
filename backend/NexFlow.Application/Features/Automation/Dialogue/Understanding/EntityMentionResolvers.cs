using NexFlow.Application.Features.Automation.Dialogue.Contracts;

namespace NexFlow.Application.Features.Automation.Dialogue.Understanding;

// Runtime names/aliases only. Bounded spelling evidence never breaks a collision.
internal static class EntityMentionMatcher
{
    internal static SlotResolution<ResolvedBusinessEntity> Resolve(string text, IReadOnlyList<BusinessEntityOption> options, bool directAnswer, bool spellingEvidence = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return SlotResolution<ResolvedBusinessEntity>.Missing();
        if (text.Length > 2000) return SlotResolution<ResolvedBusinessEntity>.Invalid("InputTooLong");
        var entities = options.Where(o => !string.IsNullOrWhiteSpace(o.Id) && !string.IsNullOrWhiteSpace(o.Name)).ToArray();
        string Key(string value) => string.Join(' ', SpanishTextNormalizer.Terms(value));
        IEnumerable<string> Keys(BusinessEntityOption option) => new[] { option.Name }.Concat(option.Aliases ?? []).Select(Key).Where(k => k.Length > 0);
        var term = Key(text);
        var exact = entities.Where(o => string.Equals(text.Trim(), o.Id, StringComparison.Ordinal) || Keys(o).Contains(term, StringComparer.Ordinal)).ToArray();
        var full = entities.Where(o => Keys(o).Any(k => SpanishTextNormalizer.ContainsPhrase(term, k))).ToArray();
        // Removing word boundaries is not a fuzzy identity guess. Preserve every
        // collision, including names which differ only in spacing.
        var fragments = term.Split(' ');
        var compactFragments = new HashSet<string>(StringComparer.Ordinal);
        for (var start = 0; start < fragments.Length; start++)
        {
            var joined = "";
            for (var end = start; end < Math.Min(start + 8, fragments.Length); end++)
            { joined += fragments[end]; compactFragments.Add(joined); }
        }
        var compact = entities.Where(o => Keys(o).Any(k => compactFragments.Contains(k.Replace(" ", "")))).ToArray();
        if (exact.Length > 0 || full.Length > 0 || compact.Length > 0) return Result(exact.Concat(full).Concat(compact));
        var tokens = SpanishTextNormalizer.Terms(text).Where(t => t.Length >= 3).ToHashSet(StringComparer.Ordinal);
        var partial = tokens.Count == 0 ? [] : entities.Where(o => Keys(o).Any(k => tokens.IsSubsetOf(k.Split(' ')))).ToArray();
        if (partial.Length > 0) return Result(partial);
        if (spellingEvidence && fragments.Length is > 0 and <= 8)
        {
            var scored = entities.Select(o => (Option: o, Score: Keys(o).Select(k => Score(term, k)).DefaultIfEmpty(int.MaxValue).Min()))
                .Where(x => x.Score <= 1).ToArray();
            // Require a full confidence margin: a second plausible entity remains
            // ambiguous even if its spelling score is slightly worse.
            if (scored.Length > 0) return Result(scored.Select(x => x.Option));
        }
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

    private static int Score(string source, string target)
    {
        var a = source.Split(' '); var b = target.Split(' ');
        if (a.Length == 1 && a[0].Length >= 5 && b.Any(word => word.StartsWith(a[0], StringComparison.Ordinal))) return 1;
        if (a.Length != b.Length) return int.MaxValue;
        var score = 0;
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] == b[i]) continue;
            if (Math.Min(a[i].Length, b[i].Length) < 5 || Math.Abs(a[i].Length - b[i].Length) > 1 || ++score > 1) return int.MaxValue;
            var x = 0; var y = 0; var edits = 0;
            while (x < a[i].Length && y < b[i].Length)
            {
                if (a[i][x] == b[i][y]) { x++; y++; continue; }
                if (++edits > 1) return int.MaxValue;
                if (a[i].Length >= b[i].Length) x++;
                if (b[i].Length >= a[i].Length) y++;
            }
            if (edits + a[i].Length - x + b[i].Length - y > 1) return int.MaxValue;
        }
        return score;
    }
}

public sealed class LocationMentionResolver
{
    public SlotResolution<ResolvedBusinessEntity> Resolve(string text, IReadOnlyList<BusinessEntityOption> authorizedLocations, bool directAnswer = false)
        => EntityMentionMatcher.Resolve(text, authorizedLocations, directAnswer);
    public SlotResolution<ResolvedBusinessEntity> ResolveEvidence(string text, IReadOnlyList<BusinessEntityOption> authorizedLocations)
        => EntityMentionMatcher.Resolve(text, authorizedLocations, true, true);
}

public sealed class OfferingMentionResolver
{
    public SlotResolution<ResolvedBusinessEntity> ResolveEvidence(string text, IReadOnlyList<BusinessEntityOption> authorizedOfferings)
        => EntityMentionMatcher.Resolve(text, authorizedOfferings, true, true);
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
