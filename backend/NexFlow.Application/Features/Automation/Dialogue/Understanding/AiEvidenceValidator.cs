using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;

namespace NexFlow.Application.Features.Automation.Dialogue.Understanding;

internal static class AiEvidenceValidator
{
    internal static TurnUnderstanding Validate(TurnUnderstanding value, AiInterpretation ai, string original)
    {
        var normalized = SpanishTextNormalizer.Normalize(original);
        var correcting = ai.Act == DialogueAct.Correction && !ai.RequiresClarification
            && ai.Evidence.Count(e => e.Slot == "Act") == 1
            && ai.Evidence.Any(e => e.Slot == "Act" && e.NormalizedValue == nameof(DialogueAct.Correction)
                && !string.IsNullOrWhiteSpace(e.Text) && HasFragment(original, e.Text));
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
            var correction = correcting
                && (slot == "Service" && ai.Directive == ConversationDirective.ChangeService
                    || slot == "Location" && ai.Directive == ConversationDirective.ChangeLocation);
            if (!correction && (local.Status is ResolutionStatus.Determined or ResolutionStatus.Ambiguous)
                || extracted == null || local.Code == "ModuleDenied") return local;
            var evidence = Evidence(slot, extracted);
            if (correction && evidence == null) return local;
            var candidate = evidence != null ? new OfferingMentionResolver().ResolveEvidence(evidence.Text, allowed)
                : new OfferingMentionResolver().Resolve(Supplied(extracted) ? extracted : "", allowed, true);
            if (evidence != null && candidate.Status is ResolutionStatus.Invalid or ResolutionStatus.Insufficient)
            {
                var matches = allowed.Where(o => new[] { o.Name }.Concat(o.Aliases ?? [])
                    .Any(label => SurfaceEquivalent(evidence.Text, label))).DistinctBy(o => o.Id).ToArray();
                if (matches.Length > 1) return SlotResolution<ResolvedBusinessEntity>.Ambiguous(matches.Select(o =>
                    new SlotCandidate<ResolvedBusinessEntity>(new(o.Id, o.Name, o.LocationId), evidence.Text, EvidenceSource.ValidatedLlm, o.Id)), "MultipleEntities");
                if (matches.Length == 1) candidate = SlotResolution<ResolvedBusinessEntity>.Match(new(matches[0].Id, matches[0].Name, matches[0].LocationId), evidence.Text, matches[0].Id, EvidenceSource.ValidatedLlm);
            }
            if (!candidate.TryGetValue(out var entity)) return candidate;
            return string.Concat(SpanishTextNormalizer.Terms(extracted)) == string.Concat(SpanishTextNormalizer.Terms(entity.Name)) || extracted == entity.Id ? candidate
                : SlotResolution<ResolvedBusinessEntity>.Invalid("UnsubstantiatedEntity");
        }
        var name = value.Name;
        if (name.Status is ResolutionStatus.Insufficient or ResolutionStatus.Invalid && name.Candidates.Count == 0 && ai.CustomerName != null
            && name.Code is not ("NegatedPresentation" or "BusinessEntityNotPerson"))
        {
            var evidence = Evidence("Name", ai.CustomerName);
            var span = evidence?.Text ?? (HasFragment(original, ai.CustomerName) ? ai.CustomerName : "");
            var resolved = new NameSlotResolver().ResolveEvidence(span);
            name = resolved.TryGetValue(out var candidate) && SpanishTextNormalizer.Normalize(candidate) == SpanishTextNormalizer.Normalize(ai.CustomerName)
                ? resolved : SlotResolution<string>.Invalid("UnsubstantiatedName");
        }
        if (name.TryGetValue(out var person) && value.AuthorizedLocations.Concat(value.AuthorizedServices).Concat(value.AuthorizedProducts)
            .Any(option => new[] { option.Name }.Concat(option.Aliases ?? []).Any(label =>
                string.Concat(SpanishTextNormalizer.Terms(label)) == string.Concat(SpanishTextNormalizer.Terms(person)))))
            name = SlotResolution<string>.Invalid("BusinessEntityNotPerson");
        var date = value.Date;
        if (date.Code != "RelativeOffsetNeedsClarification"
            && (date.Status == ResolutionStatus.Insufficient || correcting && Evidence("Date", ai.Date) != null) && ai.Date != null)
        {
            var evidence = Evidence("Date", ai.Date);
            var span = evidence?.Text ?? (Supplied(ai.Date) ? ai.Date : "");
            var resolver = new BusinessDateResolver();
            var canonical = evidence?.CanonicalText;
            var resolved = resolver.ResolveEvidence(canonical is { Length: > 0 and <= 100 } && SurfaceEquivalent(span, canonical) ? canonical : span, value.Context.LocalBusinessNow);
            var remainder = span.Length > 0 ? resolver.Resolve(original.Replace(span, " ", StringComparison.OrdinalIgnoreCase), value.Context.LocalBusinessNow) : date;
            date = resolved.TryGetValue(out var candidate) && candidate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) == ai.Date
                && (correcting || remainder.Status == ResolutionStatus.Insufficient && remainder.Code != "SemanticComposition")
                ? resolved : resolved.Status == ResolutionStatus.Ambiguous ? resolved : SlotResolution<DateOnly>.Invalid("UnsubstantiatedDate");
        }
        var time = value.Time;
        if ((time.Status == ResolutionStatus.Insufficient || correcting && Evidence("Time", ai.Time) != null) && ai.Time != null)
        {
            var evidence = Evidence("Time", ai.Time);
            var span = evidence?.Text ?? (Supplied(ai.Time) ? ai.Time : "");
            var resolver = new TimePreferenceResolver();
            var resolved = resolver.ResolveEvidence(span);
            var remainder = span.Length > 0 ? resolver.Resolve(original.Replace(span, " ", StringComparison.OrdinalIgnoreCase)) : time;
            time = resolved.TryGetValue(out var candidate) && candidate.Kind == TimePreferenceKind.Exact
                && candidate.ExactTime!.Value.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture) == ai.Time
                && (correcting || remainder.Status == ResolutionStatus.Insufficient && remainder.Code != "SemanticComposition")
                ? resolved : resolved.Status == ResolutionStatus.Ambiguous ? resolved : SlotResolution<TimePreference>.Invalid("UnsubstantiatedTime");
        }
        var validated = value with { Name = name, Location = Entity("Location", value.Location, ai.Location, value.AuthorizedLocations),
            Service = Entity("Service", value.Service, ai.Service, value.AuthorizedServices), Date = date, Time = time };
        if (new[] { validated.Name.Code, validated.Location.Code, validated.Service.Code, validated.Date.Code, validated.Time.Code }
            .Any(code => code.StartsWith("Unsubstantiated", StringComparison.Ordinal)))
            NexFlow.Application.Features.Automation.ProcessMessage.Services.ConversationDiagnostics.Count("SemanticEvidence", "Rejected");
        return validated;
    }

    internal static bool HasFragment(string source, string fragment)
    {
        for (var offset = 0; offset <= source.Length - fragment.Length; offset++)
            if (source.AsSpan(offset, fragment.Length).Equals(fragment.AsSpan(), StringComparison.OrdinalIgnoreCase)
                && (offset == 0 || !char.IsLetterOrDigit(source[offset - 1]))
                && (offset + fragment.Length == source.Length || !char.IsLetterOrDigit(source[offset + fragment.Length]))) return true;
        return false;
    }

    private static bool SurfaceEquivalent(string source, string canonical)
    {
        var a = SpanishTextNormalizer.Terms(source); var b = SpanishTextNormalizer.Terms(canonical);
        if (string.Concat(a) == string.Concat(b)) return true;
        if (a.Length != b.Length || a.Length == 0) return false;
        var changed = 0;
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] == b[i]) continue;
            if (++changed > 1) return false;
            // A bounded spelling edit, or a consonantal abbreviation of that same
            // word. This validates a supplied semantic reference, never an invented ID.
            if (a[i].Length >= 3 && new string(b[i].Where(c => !"aeiou".Contains(c)).ToArray()) == a[i]) continue;
            if (Math.Max(a[i].Length, b[i].Length) < 7 || Math.Abs(a[i].Length - b[i].Length) > 1) return false;
            var left = a[i]; var right = b[i]; var x = 0; var y = 0; var edits = 0;
            while (x < left.Length && y < right.Length)
            {
                if (left[x] == right[y]) { x++; y++; continue; }
                if (++edits > 1) return false;
                if (left.Length >= right.Length) x++;
                if (right.Length >= left.Length) y++;
            }
            if (edits + left.Length - x + right.Length - y > 1) return false;
        }
        return true;
    }
}
