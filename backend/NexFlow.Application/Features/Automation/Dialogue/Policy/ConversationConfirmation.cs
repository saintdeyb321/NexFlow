using NexFlow.Application.Features.Automation.Dialogue.Understanding;

namespace NexFlow.Application.Features.Automation.Dialogue.Policy;

// Exact consent, shared by recovery and transaction routing. No edit distance or LLM consent.
public static class ConversationConfirmation
{
    public static bool IsAffirmative(string text) => !text.Contains('?') && !text.Contains('¿')
        && SpanishTextNormalizer.Normalize(text) is "si" or "si confirmo" or "confirmo" or "confirmar" or "de acuerdo";

    public static bool NeedsClarification(string text)
    {
        if (IsAffirmative(text) || IsNegative(text) || IsCorrection(text)) return false;
        var words = SpanishTextNormalizer.Normalize(text).Split(' ');
        return words.FirstOrDefault() == "si" || words.Contains("pero") || words.Contains("salvo")
            || words.Any(w => w.StartsWith("confirm", StringComparison.Ordinal));
    }

    public static bool IsCorrection(string text)
    {
        var words = SpanishTextNormalizer.Normalize(text).Split(' ');
        return words.Any(w => w is "pero" or "salvo" or "excepto")
            && words.Any(w => w is "otra" or "otro" or "cambiar" or "cambio" or "mejor" or "corregir");
    }

    public static bool IsStatusQuestion(string text) => (text.Contains('?') || text.Contains('¿'))
        && SpanishTextNormalizer.Normalize(text).Split(' ').Any(w => w.StartsWith("confirm", StringComparison.Ordinal));

    public static bool IsNegative(string text)
    {
        var normalized = SpanishTextNormalizer.Normalize(text);
        var words = normalized.Split(' ');
        return normalized is "no" or "no gracias" or "todavia no" or "aun no"
            || words.Contains("no") && words.Any(w => w.StartsWith("confirm", StringComparison.Ordinal));
    }
}
