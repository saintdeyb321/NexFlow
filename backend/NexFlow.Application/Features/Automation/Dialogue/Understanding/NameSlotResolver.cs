using System.Text.RegularExpressions;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;

namespace NexFlow.Application.Features.Automation.Dialogue.Understanding;

public sealed partial class NameSlotResolver
{
    public SlotResolution<string> Resolve(string text, bool nameWasRequested)
    {
        if (string.IsNullOrWhiteSpace(text)) return SlotResolution<string>.Missing();
        if (text.Length > 2000) return SlotResolution<string>.Invalid("InputTooLong");
        var presentations = Presentation().Matches(text);
        if (presentations.Count > 1) return SlotResolution<string>.Ambiguous([], "MultiplePresentations");
        if (presentations.Count == 0 && !nameWasRequested) return SlotResolution<string>.Missing();
        if (presentations.Count == 1 && SpanishTextNormalizer.Normalize(text[..presentations[0].Index]).Split(' ').LastOrDefault() == "no")
            return SlotResolution<string>.Invalid("NegatedPresentation");
        var raw = presentations.Count == 1 ? presentations[0].Groups["name"].Value.Trim() : text.Trim();
        var normalized = SpanishTextNormalizer.Normalize(raw);
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (raw.Length > 120 || !NameCharacters().IsMatch(raw) || tokens.Any(NonNames.Contains))
            return SlotResolution<string>.Invalid("NotAPersonName");
        if (tokens.Length == 1) return SlotResolution<string>.Incomplete(raw, raw, "SurnameMissing");
        if (tokens.Length > 8) return SlotResolution<string>.Invalid("NameTooLong");
        return SlotResolution<string>.Match(string.Join(' ', raw.Split(' ', StringSplitOptions.RemoveEmptyEntries)), raw, source: EvidenceSource.ValidatedLiteral);
    }

    private static readonly HashSet<string> NonNames = new(StringComparer.Ordinal)
    { "hola", "quiero", "necesito", "reservar", "reserva", "sede", "horario", "hoy", "manana", "pasado", "si", "no", "gracias", "asesor", "como", "cuando", "donde" };

    [GeneratedRegex(@"\b(?:me llamo|mi nombre es|soy)\s+(?<name>[\p{L}\p{M}'’ -]+?)(?=,|[.!?;]|$|\s+(?:y\s+)?(?:quiero|necesito|deseo|en|para|pasado|mañana|manana)\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex Presentation();
    [GeneratedRegex(@"^[\p{L}\p{M}]+(?:['’ -][\p{L}\p{M}]+)*$", RegexOptions.CultureInvariant, 100)]
    private static partial Regex NameCharacters();
}
