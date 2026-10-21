namespace NexFlow.Application.Features.Reservations;

public enum ReservationOperationKind
{
    None, CreateReservation, ViewExistingReservation, RescheduleExistingReservation,
    CancelExistingReservation, EditActiveDraft, AmbiguousReservationOperation
}

public sealed record ReservationCursor(DateTime StartTime, Guid Id);
public sealed record ReservationCandidate(Guid Id, string LocationId, string ServiceId, DateTime StartTime, DateTime EndTime, uint RowVersion);
public sealed record ReservationPage(IReadOnlyList<ReservationCandidate> Items, bool HasMore);

public sealed class ReservationContactOptions
{
    // Explicit product policy, never inferred from timezone, server culture or phone ownership.
    // Unset: require international input. PE: accept nine-digit Peruvian mobile numbers on NEW writes.
    public string? NationalNumberCountry { get; set; }
}

public static class ReservationContact
{
    public static bool TryNormalize(string? input, string? nationalNumberCountry, out string canonical)
    {
        canonical = string.Empty;
        if (string.IsNullOrWhiteSpace(input)) return false;
        var value = input.Trim();
        if (value.Any(c => !char.IsAsciiDigit(c) && c is not ('+' or ' ' or '-' or '(' or ')'))) return false;
        var compact = string.Concat(value.Where(c => c is not (' ' or '-' or '(' or ')')));
        var international = compact.StartsWith('+');
        var digits = international ? compact[1..] : compact;
        if (!digits.All(char.IsAsciiDigit)) return false;
        if (!international && digits.Length == 9)
        {
            if (!string.Equals(nationalNumberCountry, "PE", StringComparison.OrdinalIgnoreCase) || digits[0] != '9') return false;
            digits = "51" + digits;
        }
        if (digits.Length is < 10 or > 15 || digits[0] == '0') return false;
        canonical = "+" + digits;
        return true;
    }

    // Only exact international representations are safely equivalent without historical country metadata.
    // A bare national number is deliberately NOT an alias, even if NEW writes have an explicit PE policy.
    public static string[] InternationalAliases(string canonical)
    {
        if (!TryNormalize(canonical, null, out var normalized) || normalized != canonical)
            throw new ArgumentException("Canonical international contact required.");
        return [canonical, canonical[1..]];
    }

    public static bool IsSameInternationalContact(string stored, string canonical)
        => InternationalAliases(canonical).Contains(stored, StringComparer.Ordinal);

    public static void Validate(Guid workspaceId, string contact)
    {
        if (workspaceId == Guid.Empty || !TryNormalize(contact, null, out var normalized) || normalized != contact)
            throw new ArgumentException("An authorized workspace and canonical contact are required.");
    }
}
