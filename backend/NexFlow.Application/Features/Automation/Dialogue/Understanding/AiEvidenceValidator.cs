using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;

namespace NexFlow.Application.Features.Automation.Dialogue.Understanding;

internal static class AiEvidenceValidator
{
    internal static TurnUnderstanding Validate(TurnUnderstanding value, AiInterpretation ai, string original)
    {
        var normalized = SpanishTextNormalizer.Normalize(original);
        bool Supplied(string text) => SpanishTextNormalizer.ContainsPhrase(normalized, SpanishTextNormalizer.Normalize(text));
        AiSlotEvidence? Evidence(string slot, string? extracted)
        {
            var matches = (ai.Evidence ?? []).Where(e => e != null && e.Slot == slot).ToArray();
            if (matches.Length != 1) return null;
            var evidence = matches[0];
            return !string.IsNullOrWhiteSpace(evidence.Text) && evidence.Text.Length <= 300
                && HasFragment(original, evidence.Text) && !string.IsNullOrWhiteSpace(evidence.NormalizedValue)
                && (extracted == null || SpanishTextNormalizer.Normalize(extracted) == SpanishTextNormalizer.Normalize(evidence.NormalizedValue)) ? evidence : null;
        }
        SlotResolution<ResolvedBusinessEntity> Entity(string slot, SlotResolution<ResolvedBusinessEntity> local, string? extracted, IReadOnlyList<BusinessEntityOption> allowed)
        {
            if (local.Status is ResolutionStatus.Determined or ResolutionStatus.Ambiguous || extracted == null || local.Code == "ModuleDenied") return local;
            var evidence = Evidence(slot, extracted);
            var candidate = new OfferingMentionResolver().Resolve(evidence?.Text ?? (Supplied(extracted) ? extracted : ""), allowed, true);
            if (!candidate.TryGetValue(out var entity)) return candidate;
            return SpanishTextNormalizer.Normalize(extracted) == SpanishTextNormalizer.Normalize(entity.Name) || extracted == entity.Id ? candidate
                : SlotResolution<ResolvedBusinessEntity>.Invalid("UnsubstantiatedEntity");
        }
        var name = value.Name;
        if (name.Status == ResolutionStatus.Insufficient && name.Candidates.Count == 0 && ai.CustomerName != null)
        {
            var evidence = Evidence("Name", ai.CustomerName);
            var span = evidence?.Text ?? (Supplied(ai.CustomerName) ? ai.CustomerName : "");
            var resolved = new NameSlotResolver().Resolve(span, true);
            name = resolved.TryGetValue(out var candidate) && SpanishTextNormalizer.Normalize(candidate) == SpanishTextNormalizer.Normalize(ai.CustomerName)
                ? resolved : SlotResolution<string>.Invalid("UnsubstantiatedName");
        }
        var date = value.Date;
        if (date.Status == ResolutionStatus.Insufficient && ai.Date != null)
        {
            var evidence = Evidence("Date", ai.Date);
            var span = evidence?.Text ?? (Supplied(ai.Date) ? ai.Date : "");
            var resolver = new BusinessDateResolver();
            var resolved = resolver.ResolveEvidence(span, value.Context.LocalBusinessNow);
            var remainder = span.Length > 0 ? resolver.Resolve(original.Replace(span, " ", StringComparison.OrdinalIgnoreCase), value.Context.LocalBusinessNow) : date;
            date = resolved.TryGetValue(out var candidate) && candidate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) == ai.Date
                && remainder.Status == ResolutionStatus.Insufficient && remainder.Code != "SemanticComposition"
                ? resolved : resolved.Status == ResolutionStatus.Ambiguous ? resolved : SlotResolution<DateOnly>.Invalid("UnsubstantiatedDate");
        }
        var time = value.Time;
        if (time.Status == ResolutionStatus.Insufficient && ai.Time != null)
        {
            var evidence = Evidence("Time", ai.Time);
            var span = evidence?.Text ?? (Supplied(ai.Time) ? ai.Time : "");
            var resolver = new TimePreferenceResolver();
            var resolved = resolver.ResolveEvidence(span);
            var remainder = span.Length > 0 ? resolver.Resolve(original.Replace(span, " ", StringComparison.OrdinalIgnoreCase)) : time;
            time = resolved.TryGetValue(out var candidate) && candidate.Kind == TimePreferenceKind.Exact
                && candidate.ExactTime!.Value.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture) == ai.Time
                && remainder.Status == ResolutionStatus.Insufficient && remainder.Code != "SemanticComposition"
                ? resolved : resolved.Status == ResolutionStatus.Ambiguous ? resolved : SlotResolution<TimePreference>.Invalid("UnsubstantiatedTime");
        }
        return value with { Name = name, Location = Entity("Location", value.Location, ai.Location, value.AuthorizedLocations),
            Service = Entity("Service", value.Service, ai.Service, value.AuthorizedServices), Date = date, Time = time };
    }

    internal static bool HasFragment(string source, string fragment)
    {
        for (var offset = 0; offset <= source.Length - fragment.Length; offset++)
            if (source.AsSpan(offset, fragment.Length).Equals(fragment.AsSpan(), StringComparison.OrdinalIgnoreCase)
                && (offset == 0 || !char.IsLetterOrDigit(source[offset - 1]))
                && (offset + fragment.Length == source.Length || !char.IsLetterOrDigit(source[offset + fragment.Length]))) return true;
        return false;
    }
}
