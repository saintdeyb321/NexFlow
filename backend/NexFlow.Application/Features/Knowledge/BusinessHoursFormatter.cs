using System.Globalization;
using NexFlow.Application.Features.Business;

namespace NexFlow.Application.Features.Knowledge;

internal static class BusinessHoursFormatter
{
    internal static bool IsOpen(BusinessHoursDto? hours) => hours is { IsClosed: false }
        && TimeSpan.TryParseExact(hours.OpenTime, @"hh\:mm", CultureInfo.InvariantCulture, out var open)
        && TimeSpan.TryParseExact(hours.CloseTime, @"hh\:mm", CultureInfo.InvariantCulture, out var close)
        && open < close;

    internal static string Format(IEnumerable<BusinessHoursDto> hours)
    {
        var records = hours.ToList();
        if (records.Any(h => h.DayOfWeek is < 0 or > 6)) throw new InvalidOperationException("Invalid stored day of week.");
        if (records.Count == 0) return "No hay horarios de atención registrados para el alcance consultado.";
        if (records.GroupBy(h => h.DayOfWeek).Any(g => g.Count() != 1)) throw new InvalidOperationException("Duplicate stored day of week.");
        string[] days = ["Domingo", "Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado"];
        var week = new[] { 1, 2, 3, 4, 5, 6, 0 };
        var labels = week.Select(day =>
        {
            var record = records.FirstOrDefault(h => h.DayOfWeek == day);
            if (record is { IsClosed: false } && !IsOpen(record)) throw new InvalidOperationException("Invalid stored business hours.");
            return record == null ? "Sin horario registrado" : record.IsClosed ? "Cerrado" : $"{record.OpenTime} – {record.CloseTime}";
        }).ToArray();
        var groups = new List<string>();
        for (var start = 0; start < week.Length;)
        {
            var end = start;
            while (end + 1 < week.Length && labels[end + 1] == labels[start]) end++;
            var range = start == end ? days[week[start]] : $"{days[week[start]]} a {days[week[end]].ToLowerInvariant()}";
            groups.Add($"{range}: {labels[start]}");
            start = end + 1;
        }
        return string.Join("\n", groups);
    }
}
