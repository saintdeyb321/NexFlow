using System.Text.RegularExpressions;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;

namespace NexFlow.Application.Features.Automation.Dialogue.Understanding;

/// <summary>Separates an initial courtesy from its content without rewriting entity evidence.</summary>
public static class GreetingRecognizer
{
    public static (bool HasGreeting, string Content) Parse(string text)
    {
        var words = Regex.Matches(text, @"[\p{L}\p{M}\p{N}]+", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (words.Count == 0) return (false, text);
        string Word(int i) => i < words.Count ? ConversationRecoveryService.Normalize(words[i].Value) : "";
        int Courtesy(int i) => (Word(i), Word(i + 1)) switch {
            ("buenos", "dias") or ("buenas", "tardes") or ("buenas", "noches") => 2,
            _ => 0
        };
        var count = Courtesy(0);
        if (count == 0 && (Word(0) is "holi" or "ola" or "hey" or "buenas"
            || Regex.IsMatch(Word(0), "^hola+$", RegexOptions.CultureInvariant))) count = 1;
        if (count == 0) return (false, text);
        count += Courtesy(count);
        return (true, count == words.Count ? "" : text[words[count].Index..]);
    }
}
