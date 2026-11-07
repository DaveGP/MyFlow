using MyFlow.Core;

namespace MyFlow.Platform.MacOS;

/// <summary>
/// Pastes text into the focused app using OpenWhispr's macos-fast-paste helper:
/// it puts the text on the clipboard, presses ⌘V, and reports when the app has read it,
/// after which we put the user's previous clipboard text back.
/// (In Chrome-family browsers the helper types the text instead, because Chrome
/// sometimes ignores a synthetic ⌘V.) Needs Accessibility permission.
/// </summary>
public sealed class MacTextInserter(Func<MyFlowSettings> settings) : ITextInserter
{
    private const int ExitNotTrusted = 2;

    public async Task<InsertResult> InsertAsync(string text, CancellationToken ct = default)
    {
        var helper = MacTools.FindHelper("macos-fast-paste");
        var restore = settings().RestoreClipboard;
        var previous = restore ? await MacClipboard.GetTextAsync(ct) : "";

        if (helper == null)
        {
            Log.Warn("macos-fast-paste helper not found; leaving text on the clipboard.");
            await MacClipboard.SetTextAsync(text, ct);
            return InsertResult.CopiedToClipboardOnly;
        }

        var session = Guid.NewGuid().ToString("N");
        var result = await MacTools.RunAsync(helper, ["--await-read", "3000", "--session", session],
            stdin: text, timeout: TimeSpan.FromSeconds(8), ct: ct);

        if (result.ExitCode == ExitNotTrusted)
        {
            Log.Warn("Paste failed: MyFlow doesn't have Accessibility permission. Text left on the clipboard.");
            await MacClipboard.SetTextAsync(text, ct);
            return InsertResult.CopiedToClipboardOnly;
        }
        if (result.ExitCode != 0 || !result.StdOut.Contains("PASTE_POSTED"))
        {
            Log.Warn($"Paste helper failed (exit {result.ExitCode}): {result.StdErr.Trim()}");
            await MacClipboard.SetTextAsync(text, ct);
            return InsertResult.CopiedToClipboardOnly;
        }

        var outcome = result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "";
        Log.Debug($"Paste helper: {outcome}");

        // PASTE_READ: the app took the text, so it's safe to put the old clipboard back.
        // PASTE_NOT_READ: leave the dictated text on the clipboard so the user can paste it.
        // PASTE_REPLACED: something else was copied in between; leave that alone.
        // PASTE_TYPED: typed directly (browser); the clipboard was never touched.
        if (restore && outcome.StartsWith("PASTE_READ") && previous.Length > 0)
        {
            await Task.Delay(100, ct);
            await MacClipboard.SetTextAsync(previous, ct);
        }
        return InsertResult.Pasted;
    }
}

/// <summary>
/// Reads the current selection by sending ⌘C (via macos-fast-paste --copy) and checking whether
/// the clipboard changed. The previous clipboard text is restored afterwards.
/// </summary>
public sealed class MacSelectionReader : ISelectionReader
{
    public async Task<string?> GetSelectedTextAsync(CancellationToken ct = default)
    {
        var helper = MacTools.FindHelper("macos-fast-paste");
        if (helper == null) return null;

        var previous = await MacClipboard.GetTextAsync(ct);
        var before = MacClipboard.ChangeCount();

        var result = await MacTools.RunAsync(helper, ["--copy"], timeout: TimeSpan.FromSeconds(3), ct: ct);
        if (result.ExitCode != 0)
        {
            Log.Debug($"Copy helper exit {result.ExitCode}");
            return null;
        }

        // Apps answer ⌘C asynchronously; give them up to ~350 ms.
        for (var i = 0; i < 35 && MacClipboard.ChangeCount() == before; i++)
        {
            await Task.Delay(10, ct);
        }
        if (MacClipboard.ChangeCount() == before)
        {
            return null; // nothing was selected, so nothing was copied
        }

        var selected = await MacClipboard.GetTextAsync(ct);
        await MacClipboard.SetTextAsync(previous, ct);
        return string.IsNullOrWhiteSpace(selected) ? null : selected;
    }
}
