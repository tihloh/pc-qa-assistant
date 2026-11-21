using System.Text.RegularExpressions;

namespace PCSoundCapture;

/// <summary>
/// Vosk normally returns punctuation-free text. These deterministic heuristics
/// deliberately favour false negatives over answering every ordinary statement.
/// </summary>
internal static partial class QuestionDetector
{
    private static readonly Regex QuestionStart = new(
        @"\b(?:what|when|where|why|who|whom|whose|which|how|can|could|should|would|will|do|does|did|is|are|am|was|were|may|might|have|has|had|ano|sino|bakit|paano|kailan|saan|alin|ilan|magkano|gaano|puwede|pwede|maaari)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex RequestStart = new(
        @"\b(?:please\s+)?(?:explain|describe|compare|tell\s+me|help\s+me\s+understand|give\s+me\s+an\s+example|ipaliwanag|pakipaliwanag|pakiusap\s+ipaliwanag)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Prefix = new(
        @"^(?:(?:okay|ok|so|well|hey|um|uh|actually|please|anyway|alright)\b[\s,]*)+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Introducer = new(
        @"\b(?:my\s+question\s+is|the\s+question\s+is|i\s+want\s+to\s+ask)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Space = new(@"\s+", RegexOptions.Compiled);

    public static string? ExtractQuestion(string transcript)
    {
        string sentence = Space.Replace(transcript.Trim(), " ");
        if (sentence.Length < 9 || sentence.Length > 1200) return null;
        sentence = Prefix.Replace(sentence, string.Empty).Trim();
        if (sentence.Length < 9) return null;
        // Introductions often precede the actual question in speech.
        Match intro = Introducer.Match(sentence);
        if (intro.Success && intro.Index < 80)
        {
            string suffix = sentence[(intro.Index + intro.Length)..].Trim(' ', ':', ',');
            if (suffix.Length >= 9)
                sentence = suffix;
            else if (intro.Index == 0) return null;
        }
        // A speech segment may contain a lead-in statement then a question.
        // Do not grab a question word buried deep inside an ordinary statement.
        Match q = QuestionStart.Match(sentence);
        Match req = RequestStart.Match(sentence);
        bool question = q.Success && (q.Index == 0 || (q.Index < 36 &&
            (sentence.StartsWith("and ", StringComparison.OrdinalIgnoreCase)
            || sentence.StartsWith("but ", StringComparison.OrdinalIgnoreCase)
            || sentence.StartsWith("i wonder ", StringComparison.OrdinalIgnoreCase))));
        bool request = req.Success && req.Index == 0;
        bool explicitQuestionMark = sentence.EndsWith('?');
        if (!question && !request && !explicitQuestionMark) return null;
        if (q.Success && question && q.Index > 0) sentence = sentence[q.Index..];
        // One-word or extremely short phrases are often recognition artifacts.
        if (sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 3) return null;
        return sentence.TrimEnd(' ', '.', ',', '?') + "?";
    }
}
