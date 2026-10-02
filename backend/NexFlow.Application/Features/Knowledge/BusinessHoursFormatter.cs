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
        if (records.Count == 0) return "No hay horarios de atención registrados para esta sede.";
        string[] days = ["Domingo", "Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado"];
        return string.Join("\n", new[] { 1, 2, 3, 4, 5, 6, 0 }.Select(day =>
        {
            var record = records.FirstOrDefault(h => h.DayOfWeek == day);
            return record == null ? $"{days[day]}: Sin horario registrado"
                : record.IsClosed ? $"{days[day]}: Cerrado"
                : $"{days[day]}: {record.OpenTime} – {record.CloseTime}";
        }));
    }
}
