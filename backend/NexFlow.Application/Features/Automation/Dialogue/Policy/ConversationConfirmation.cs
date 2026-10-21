using NexFlow.Application.Features.Automation.Dialogue.Understanding;

namespace NexFlow.Application.Features.Automation.Dialogue.Policy;

// Exact consent, shared by recovery and transaction routing. No edit distance or LLM consent.
public static class ConversationConfirmation
{
    public static bool IsAffirmative(string text) => !text.Contains('?') && !text.Contains('¿')
        && SpanishTextNormalizer.Normalize(text) is "si" or "si confirmo" or "confirmo" or "confirmar" or "de acuerdo";

    public static bool NeedsClarification(string text)
    {
        if (IsAffirmative(text)) return false;
        var words = SpanishTextNormalizer.Normalize(text).Split(' ');
        return words.FirstOrDefault() == "si" || words.Any(w => w.StartsWith("confirm", StringComparison.Ordinal));
    }
}
