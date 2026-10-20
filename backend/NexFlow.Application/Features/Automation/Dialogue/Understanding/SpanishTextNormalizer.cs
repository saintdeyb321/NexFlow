using System.Globalization;
using System.Text;

namespace NexFlow.Application.Features.Automation.Dialogue.Understanding;

public static class SpanishTextNormalizer
{
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return string.Join(' ', new string(text.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    internal static bool ContainsPhrase(string normalized, string phrase)
        => phrase.Length > 0 && (" " + normalized + " ").Contains(" " + phrase + " ", StringComparison.Ordinal);

    internal static string[] Terms(string text)
        => Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !FunctionWords.Contains(t)).ToArray();

    private static readonly HashSet<string> FunctionWords = new(StringComparer.Ordinal)
    { "en", "la", "el", "las", "los", "de", "del", "a", "al", "una", "un", "sede", "sucursal", "servicio", "producto", "por", "favor",
        "quiero", "quisiera", "necesito", "reservar", "consultar", "ver", "me", "muestrame", "puedes", "tienen", "tiene", "elijo", "selecciono", "opcion" };
}
