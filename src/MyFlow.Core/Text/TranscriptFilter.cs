using System.Text.RegularExpressions;

namespace MyFlow.Core.Text;

/// <summary>Removes the artifacts Whisper produces on silence or noise.</summary>
public static partial class TranscriptFilter
{
    // Whisper marks non-speech with bracketed/parenthesised tags such as
    // [BLANK_AUDIO], [Music], (silence), (keyboard clicking).
    [GeneratedRegex(@"\[[^\]]{1,40}\]|\((?:[^)]{0,40})\)|♪+", RegexOptions.CultureInvariant)]
    private static partial Regex NonSpeechTags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    // Phrases Whisper hallucinates on near-silent audio (learned from YouTube captions).
    private static readonly string[] Hallucinations =
    [
        "thank you.", "thanks for watching!", "thanks for watching.", "thank you for watching.",
        "you", "bye.", "please subscribe.", ".",
    ];

    /// <summary>Cleans a raw Whisper transcript. Returns "" when nothing was actually said.</summary>
    public static string Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var text = NonSpeechTags().Replace(raw, " ");
        text = Whitespace().Replace(text, " ").Trim();
        return text;
    }

    /// <summary>
    /// True if the transcript is probably a hallucination from a very short/quiet clip.
    /// Only applied to short recordings so real dictation of "Thank you." still works.
    /// </summary>
    public static bool IsLikelyHallucination(string text, TimeSpan recordingDuration) =>
        recordingDuration < TimeSpan.FromSeconds(1.5) &&
        Hallucinations.Contains(text.Trim().ToLowerInvariant());
}
