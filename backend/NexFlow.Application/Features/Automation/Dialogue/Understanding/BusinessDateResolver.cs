using System.Globalization;
using System.Text.RegularExpressions;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;

namespace NexFlow.Application.Features.Automation.Dialogue.Understanding;

public sealed partial class BusinessDateResolver
{
    internal static bool IsOnlyDateExpression(string text)
    {
        var raw = IsoDate().Replace(text, " "); raw = NumericDate().Replace(raw, " ");
        var normalized = SpanishTextNormalizer.Normalize(raw);
        var remaining = WrittenDate().Replace(normalized, " ");
        remaining = RelativeDate().Replace(remaining, " "); remaining = Weekday().Replace(remaining, " ");
        if (raw == text && remaining == normalized) return false;
        return remaining.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => w is "mejor" or "el" or "la" or "para" or "fecha" or "dia" or "es" or "cambia" or "cambiar");
    }

    public SlotResolution<DateOnly> Resolve(string text, DateTimeOffset localBusinessNow, bool dateWasRequested = false)
    {
        if (text.Length > 2000) return SlotResolution<DateOnly>.Invalid("InputTooLong");
        var today = DateOnly.FromDateTime(localBusinessNow.DateTime);
        var dates = new HashSet<DateOnly>();
        var normalized = SpanishTextNormalizer.Normalize(text);
        string? invalid = null;
        foreach (Match match in IsoDate().Matches(text))
            AddDate(Number(match, "day"), Number(match, "month"), Number(match, "year"));
        foreach (Match match in NumericDate().Matches(text))
            AddDate(Number(match, "day"), Number(match, "month"), match.Groups["year"].Success ? Number(match, "year") : null);
        foreach (Match match in WrittenDate().Matches(normalized))
        {
            if (!Months.TryGetValue(match.Groups["month"].Value, out var month)) { invalid = "UnknownMonth"; continue; }
            AddDate(Number(match, "day"), month, match.Groups["year"].Success ? Number(match, "year") : null);
        }
        foreach (Match match in RelativeDate().Matches(normalized))
        {
            var prefix = normalized[..match.Index];
            if (match.Value == "manana" && new[] { "por la ", "en la ", "de la ", "esta " }.Any(p => prefix.EndsWith(p, StringComparison.Ordinal))) continue;
            dates.Add(today.AddDays(match.Value == "hoy" ? 0 : match.Value == "manana" ? 1 : 2));
        }
        foreach (Match match in Weekday().Matches(normalized))
        {
            var day = Weekdays[match.Groups["day"].Value];
            var qualifier = match.Groups["qualifier"].Value;
            if (dates.Count > 0 && qualifier.Length == 0)
            {
                if (!dates.Any(d => d.DayOfWeek == day)) invalid = "WeekdayMismatch";
                continue;
            }
            var delta = ((int)day - (int)today.DayOfWeek + 7) % 7;
            if (qualifier is "este" or "esta")
            {
                var monday = today.AddDays(-((int)today.DayOfWeek + 6) % 7);
                var date = monday.AddDays(((int)day + 6) % 7);
                if (date < today) invalid = "PastDate"; else dates.Add(date);
            }
            else if (qualifier.Length > 0) dates.Add(today.AddDays(delta == 0 ? 7 : delta));
            else { dates.Add(today.AddDays(delta)); dates.Add(today.AddDays(delta + 7)); }
        }
        if (invalid != null) return SlotResolution<DateOnly>.Invalid(invalid);
        if (dates.Count == 0) return SlotResolution<DateOnly>.Missing(dateWasRequested ? "DateNotUnderstood" : "NotMentioned");
        if (dates.Count == 1) return SlotResolution<DateOnly>.Match(dates.Single(), text);
        return SlotResolution<DateOnly>.Ambiguous(dates.Order().Select(d => new SlotCandidate<DateOnly>(d, text, EvidenceSource.LocalResolver)), "MultipleDates");

        void AddDate(int day, int month, int? year)
        {
            try
            {
                var resolvedYear = year ?? (month < today.Month ? today.Year + 1 : today.Year);
                var date = new DateOnly(resolvedYear, month, day);
                if (date < today) invalid = "PastDate"; else dates.Add(date);
            }
            catch (ArgumentOutOfRangeException) { invalid = "InvalidCalendarDate"; }
        }
    }

    private static int Number(Match match, string group) => int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);
    private static readonly Dictionary<string, int> Months = new(StringComparer.Ordinal)
    {
        ["enero"] = 1, ["ene"] = 1, ["febrero"] = 2, ["feb"] = 2, ["marzo"] = 3, ["mar"] = 3,
        ["abril"] = 4, ["abr"] = 4, ["mayo"] = 5, ["may"] = 5, ["junio"] = 6, ["jun"] = 6,
        ["julio"] = 7, ["jul"] = 7, ["agosto"] = 8, ["ago"] = 8, ["septiembre"] = 9, ["setiembre"] = 9,
        ["sep"] = 9, ["set"] = 9, ["octubre"] = 10, ["oct"] = 10, ["noviembre"] = 11, ["nov"] = 11, ["diciembre"] = 12, ["dic"] = 12
    };
    private static readonly Dictionary<string, DayOfWeek> Weekdays = new(StringComparer.Ordinal)
    {
        ["lunes"] = DayOfWeek.Monday, ["martes"] = DayOfWeek.Tuesday, ["miercoles"] = DayOfWeek.Wednesday,
        ["jueves"] = DayOfWeek.Thursday, ["viernes"] = DayOfWeek.Friday, ["sabado"] = DayOfWeek.Saturday, ["domingo"] = DayOfWeek.Sunday
    };
    [GeneratedRegex(@"\b(?<year>\d{4})-(?<month>\d{1,2})-(?<day>\d{1,2})\b", RegexOptions.CultureInvariant, 100)]
    private static partial Regex IsoDate();
    [GeneratedRegex(@"(?<![\d/])(?<day>\d{1,2})/(?<month>\d{1,2})(?:/(?<year>\d{4}))?(?![\d/])", RegexOptions.CultureInvariant, 100)]
    private static partial Regex NumericDate();
    [GeneratedRegex(@"\b(?<day>\d{1,2}) (?:de )?(?<month>enero|ene|febrero|feb|marzo|mar|abril|abr|mayo|may|junio|jun|julio|jul|agosto|ago|septiembre|setiembre|sep|set|octubre|oct|noviembre|nov|diciembre|dic)(?: (?:de )?(?<year>\d{4}))?\b", RegexOptions.CultureInvariant, 100)]
    private static partial Regex WrittenDate();
    [GeneratedRegex(@"\b(?:pasado manana|manana|hoy)\b", RegexOptions.CultureInvariant, 100)]
    private static partial Regex RelativeDate();
    [GeneratedRegex(@"\b(?:(?<qualifier>el proximo|el siguiente|proximo|proxima|este|esta) )?(?<day>lunes|martes|miercoles|jueves|viernes|sabado|domingo)\b", RegexOptions.CultureInvariant, 100)]
    private static partial Regex Weekday();
}
