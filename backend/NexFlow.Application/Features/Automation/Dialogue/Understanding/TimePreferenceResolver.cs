using System.Globalization;
using System.Text.RegularExpressions;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Reservations;

namespace NexFlow.Application.Features.Automation.Dialogue.Understanding;

public sealed partial class TimePreferenceResolver
{
    public SlotResolution<TimePreference> Resolve(string text, bool timeWasRequested = false)
    {
        if (text.Length > 2000) return SlotResolution<TimePreference>.Invalid("InputTooLong");
        var normalized = SpanishTextNormalizer.Normalize(text);
        var periods = new List<TimePreference>();
        if (HasPeriod("manana")) periods.Add(new(TimePreferenceKind.Period, From: new(6, 0), Until: new(12, 0)));
        if (HasPeriod("tarde")) periods.Add(new(TimePreferenceKind.Period, From: new(12, 0), Until: new(18, 0)));
        if (HasPeriod("noche")) periods.Add(new(TimePreferenceKind.Period, From: new(18, 0)));
        var matches = ClockMention().Matches(text).ToArray();
        if (matches.Length == 0 && timeWasRequested && BareHour().IsMatch(text.Trim()))
            matches = ClockMention().Matches("a las " + text.Trim()).ToArray();
        var candidates = new HashSet<TimePreference>();
        foreach (var match in matches)
        {
            var hourText = SpanishTextNormalizer.Normalize(match.Groups["hour"].Value);
            var hour = int.TryParse(hourText, out var numeric) ? numeric : Hours[hourText];
            var minute = match.Groups["minute"].Success ? int.Parse(match.Groups["minute"].Value, CultureInfo.InvariantCulture) : 0;
            if (hour > 23 || minute > 59) return SlotResolution<TimePreference>.Invalid("InvalidClockTime");
            var meridian = SpanishTextNormalizer.Normalize(match.Groups["meridian"].Value);
            if (meridian.Length > 0 && hour is < 1 or > 12) return SlotResolution<TimePreference>.Invalid("InvalidMeridianHour");
            var possible = meridian is "pm" or "de la tarde" or "de la noche" ? new[] { hour % 12 + 12 }
                : meridian is "am" or "de la manana" ? new[] { hour % 12 }
                : hour is >= 1 and <= 12 && !match.Groups["minute"].Success ? new[] { hour % 12, hour % 12 + 12 } : new[] { hour };
            var relation = SpanishTextNormalizer.Normalize(match.Groups["relation"].Value);
            foreach (var h in possible)
            {
                var time = new TimeOnly(h, minute);
                if (periods.Count > 0 && !periods.Any(p => Matches(time, p))) continue;
                candidates.Add(relation.StartsWith("despues", StringComparison.Ordinal) ? new(TimePreferenceKind.After, From: time, Until: periods.Count == 1 ? periods[0].Until : null)
                    : relation.StartsWith("antes", StringComparison.Ordinal) ? new(TimePreferenceKind.Before, From: periods.Count == 1 ? periods[0].From : null, Until: time)
                    : new(TimePreferenceKind.Exact, ExactTime: time));
            }
        }
        if (matches.Length > 0 && candidates.Count == 0) return SlotResolution<TimePreference>.Invalid("ConflictingTimePreference");
        if (matches.Length == 0) candidates.UnionWith(periods);
        if (candidates.Count == 0) return SlotResolution<TimePreference>.Missing(timeWasRequested ? "TimeNotUnderstood" : "NotMentioned");
        if (candidates.Count == 1) return SlotResolution<TimePreference>.Match(candidates.Single(), text);
        return SlotResolution<TimePreference>.Ambiguous(candidates.Select(t => new SlotCandidate<TimePreference>(t, text, EvidenceSource.LocalResolver)), "MultipleTimes");

        bool HasPeriod(string period) => new[] { "por la ", "en la " }.Any(p => SpanishTextNormalizer.ContainsPhrase(normalized, p + period))
            || normalized == "de la " + period;
    }

    // Only filters real engine slots. Does not generate availability or convert a preference into a booking.
    public IReadOnlyList<TimeSlotDto> FilterAvailableSlots(IEnumerable<TimeSlotDto> slots, DateOnly date, TimeZoneInfo zone, TimePreference preference)
    {
        if (!Enum.IsDefined(preference.Kind) || preference.Kind == TimePreferenceKind.Exact && !preference.ExactTime.HasValue
            || preference.Kind == TimePreferenceKind.After && !preference.From.HasValue
            || preference.Kind == TimePreferenceKind.Before && !preference.Until.HasValue
            || preference.Kind == TimePreferenceKind.Period && !preference.From.HasValue && !preference.Until.HasValue
            || preference.From.HasValue && preference.Until.HasValue && preference.From >= preference.Until)
            throw new ArgumentException("Invalid time preference.", nameof(preference));
        return slots.Where(s => s.IsAvailable).Where(s =>
        {
            if (s.StartTime.Kind != DateTimeKind.Utc) throw new ArgumentException("Engine slots must be UTC.", nameof(slots));
            var local = TimeZoneInfo.ConvertTimeFromUtc(s.StartTime, zone);
            return DateOnly.FromDateTime(local) == date && Matches(TimeOnly.FromDateTime(local), preference);
        }).OrderBy(s => s.StartTime).ToArray();
    }

    private static bool Matches(TimeOnly time, TimePreference preference) => preference.Kind switch
    {
        TimePreferenceKind.Exact => time == preference.ExactTime,
        TimePreferenceKind.After => time > preference.From && (!preference.Until.HasValue || time < preference.Until),
        TimePreferenceKind.Before => time < preference.Until && (!preference.From.HasValue || time >= preference.From),
        TimePreferenceKind.Period => (!preference.From.HasValue || time >= preference.From) && (!preference.Until.HasValue || time < preference.Until),
        _ => false
    };
    private static readonly Dictionary<string, int> Hours = new(StringComparer.Ordinal)
    {
        ["una"] = 1, ["uno"] = 1, ["dos"] = 2, ["tres"] = 3, ["cuatro"] = 4, ["cinco"] = 5, ["seis"] = 6,
        ["siete"] = 7, ["ocho"] = 8, ["nueve"] = 9, ["diez"] = 10, ["once"] = 11, ["doce"] = 12
    };
    [GeneratedRegex(@"\b(?:(?<relation>despu[eé]s de las|antes de las|a las)\s+)(?<hour>\d{1,2}|una|uno|dos|tres|cuatro|cinco|seis|siete|ocho|nueve|diez|once|doce)(?::(?<minute>\d{2}))?(?:\s*(?<meridian>am|pm|de la ma[nñ]ana|de la tarde|de la noche))?\b|\b(?<hour>\d{1,2}):(?<minute>\d{2})(?:\s*(?<meridian>am|pm))?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex ClockMention();
    [GeneratedRegex(@"^(?:\d{1,2}|una|uno|dos|tres|cuatro|cinco|seis|siete|ocho|nueve|diez|once|doce)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex BareHour();
}
