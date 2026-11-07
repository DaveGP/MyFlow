using System.Text.RegularExpressions;

namespace MyFlow.Core.Text;

/// <summary>
/// Cleans transcripts and applies voice edits using any <see cref="ILanguageModel"/>.
/// If the model is missing, slow or misbehaves, the raw transcript is used instead,
/// so dictation never fails just because cleanup did.
/// </summary>
public sealed partial class LlmTextCleaner(ILanguageModel? model, TimeSpan? timeout = null) : ITextCleaner
{
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(20);

    public ILanguageModel? Model => model;

    public async Task<string> CleanAsync(string rawTranscript, CancellationToken ct = default)
    {
        var raw = rawTranscript.Trim();
        if (model == null || raw.Length == 0) return raw;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_timeout);
            var output = await model.CompleteAsync(Prompts.CleanupSystem, Prompts.CleanupUser(raw), cts.Token);
            var cleaned = Sanitize(output);
            if (!LooksLikeCleanupOf(raw, cleaned))
            {
                Log.Warn($"Cleanup output rejected (looked like an answer or was empty); using raw transcript.");
                return raw;
            }
            return cleaned;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Log.Warn($"Cleanup timed out after {_timeout.TotalSeconds:0}s; using raw transcript.");
            return raw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn($"Cleanup failed ({model.DisplayName}): {ex.Message}; using raw transcript.");
            return raw;
        }
    }

    public async Task<string> EditAsync(string selectedText, string instruction, CancellationToken ct = default)
    {
        if (model == null)
            throw new InvalidOperationException("Voice editing needs a language model. Turn on Ollama or a cloud model in Settings.");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout + TimeSpan.FromSeconds(20));
        var output = await model.CompleteAsync(Prompts.EditSystem, Prompts.EditUser(selectedText, instruction.Trim()), cts.Token);
        var result = Sanitize(output);
        if (result.Length == 0) throw new InvalidOperationException("The model returned an empty edit.");
        return result;
    }

    [GeneratedRegex(@"<think>.*?</think>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ThinkBlock();

    [GeneratedRegex(@"^\s*</?(transcript|selected_text|instruction|output|text)>\s*|\s*</?(transcript|selected_text|instruction|output|text)>\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex WrapperTags();

    [GeneratedRegex(@"^(here is|here's|sure[,!]?|cleaned( up)? (text|transcript)|the cleaned)[^\n:]{0,60}:\s*\n?", RegexOptions.IgnoreCase)]
    private static partial Regex Preamble();

    /// <summary>Strips the wrappers, reasoning tags and chatty preambles small models sometimes add.</summary>
    internal static string Sanitize(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return "";
        var text = ThinkBlock().Replace(output, "").Trim();
        text = Preamble().Replace(text, "").Trim();
        // Apply twice: a model may emit both an opening and a closing tag.
        text = WrapperTags().Replace(text, "").Trim();
        text = WrapperTags().Replace(text, "").Trim();
        if (text.Length >= 2 && ((text[0] == '"' && text[^1] == '"') || (text[0] == '“' && text[^1] == '”')))
            text = text[1..^1].Trim();
        return text;
    }

    /// <summary>
    /// Guards against the model answering the transcript instead of cleaning it.
    /// Cleaned text should be about as long as the input (filler removal only shrinks it).
    /// </summary>
    internal static bool LooksLikeCleanupOf(string raw, string cleaned)
    {
        if (cleaned.Length == 0) return false;
        var maxLength = raw.Length * 1.5 + 40;
        // Short inputs can legitimately shrink a lot ("um, uh, okay" -> "Okay.").
        var minLength = raw.Length > 40 ? raw.Length * 0.3 : 1;
        return cleaned.Length <= maxLength && cleaned.Length >= minLength;
    }
}
