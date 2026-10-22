using System.Text.RegularExpressions;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;

namespace NexFlow.Application.Features.Automation.Dialogue.Understanding;

public sealed partial class NameSlotResolver
{
    public SlotResolution<string> Resolve(string text, bool nameWasRequested, bool compoundAnswer = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return SlotResolution<string>.Missing();
        if (text.Length > 2000) return SlotResolution<string>.Invalid("InputTooLong");
        var presentations = Presentation().Matches(text);
        if (presentations.Count > 1) return SlotResolution<string>.Ambiguous([], "MultiplePresentations");
        if (presentations.Count == 0 && !nameWasRequested && !(compoundAnswer && text.IndexOfAny([',', '\n', ';']) >= 0)) return SlotResolution<string>.Missing();
        if (presentations.Count == 0 && compoundAnswer && text.IndexOfAny([',', '\n', ';']) >= 0)
        {
            var parts = text.Split([',', '\n', ';'], StringSplitOptions.RemoveEmptyEntries)
                .Select(part => Resolve(part.Trim(), true)).ToArray();
            var candidates = parts.Where(part => part.Status == ResolutionStatus.Determined).SelectMany(part => part.Candidates)
                .DistinctBy(candidate => candidate.Value, StringComparer.OrdinalIgnoreCase).ToArray();
            if (candidates.Length == 1) return SlotResolution<string>.Match(candidates[0].Value, candidates[0].RawText, source: candidates[0].Source);
            if (candidates.Length > 1) return SlotResolution<string>.Ambiguous(candidates, "MultipleNames");
        }
        if (presentations.Count == 1 && SpanishTextNormalizer.Normalize(text[..presentations[0].Index]).Split(' ').LastOrDefault() == "no")
            return SlotResolution<string>.Invalid("NegatedPresentation");
        var raw = presentations.Count == 1 ? presentations[0].Groups["name"].Value.Trim() : text.Trim();
        if (presentations.Count == 0)
            raw = raw.Split([',', '\n', ';'], 2)[0].Trim();
        var normalized = SpanishTextNormalizer.Normalize(raw);
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (raw.Length > 120 || !NameCharacters().IsMatch(raw) || tokens.Any(NonNames.Contains))
            return SlotResolution<string>.Invalid("NotAPersonName");
        if (tokens.Any(w => w is "me" or "mi" or "es" or "soy" or "llamo" or "nombre")
            || presentations.Count == 0 && tokens.Length > 1 && !raw.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .All(w => char.IsUpper(w[0]) || w is "de" or "del" or "la" or "las" or "los"))
            return SlotResolution<string>.Missing("SemanticComposition");
        if (tokens.Length == 1) return SlotResolution<string>.Incomplete(raw, raw, "SurnameMissing");
        if (tokens.Length > 8) return SlotResolution<string>.Invalid("NameTooLong");
        return SlotResolution<string>.Match(string.Join(' ', raw.Split(' ', StringSplitOptions.RemoveEmptyEntries)), raw, source: EvidenceSource.ValidatedLiteral);
    }

    internal SlotResolution<string> ResolveEvidence(string span)
    {
        // A semantic name is a literal span, not a spelling correction. Capitalization
        // is not proof of identity; validate shape and reject presentation/instruction words.
        var resolved = Resolve("soy " + span, false);
        return resolved.TryGetValue(out var name) && name == span.Trim() ? resolved
            : SlotResolution<string>.Invalid("UnsubstantiatedName");
    }

    private static readonly HashSet<string> NonNames = new(StringComparer.Ordinal)
    { "hola", "quiero", "necesito", "reservar", "reserva", "sede", "horario", "hoy", "manana", "pasado", "si", "no", "gracias", "asesor", "como", "cuando", "donde" };

    [GeneratedRegex(@"\b(?:me llamo|mi nombre es|soy|nombre\s*:)\s*(?<name>[\p{L}\p{M}'’ -]+?)(?=,|[.!?;\r\n]|$|\s+(?:y\s+)?(?:quiero|quisiera|necesito|deseo|en|para|pasado|mañana|manana)\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex Presentation();
    [GeneratedRegex(@"^[\p{L}\p{M}]+(?:['’ -][\p{L}\p{M}]+)*$", RegexOptions.CultureInvariant, 100)]
    private static partial Regex NameCharacters();
}
