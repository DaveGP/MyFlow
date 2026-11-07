using System.Diagnostics;
using MyFlow.Core.Text;

namespace MyFlow.Core;

public enum DictationState
{
    Idle,
    Recording,
    Transcribing,
    Polishing,
    Editing,
    Inserting,
}

public sealed record DictationStatus(DictationState State, string Message, bool IsEditMode = false);

public sealed record DictationResult(string RawTranscript, string FinalText, bool WasEdit, InsertResult Insert, TimeSpan Elapsed);

/// <summary>
/// The whole dictation flow, independent of OS and UI:
///
///   shortcut down → start recording (+ grab any selected text)
///   shortcut up   → stop → transcribe locally → clean up (or apply the edit) → paste
///
/// If text was selected when dictation started, the speech is treated as an instruction
/// ("make this more formal") and the selection is replaced by the rewritten text.
/// </summary>
public sealed class DictationController
{
    private readonly IAudioRecorder _recorder;
    private readonly ITranscriber _transcriber;
    private readonly ITextCleaner _cleaner;
    private readonly ITextInserter _inserter;
    private readonly ISelectionReader _selection;
    private readonly Func<MyFlowSettings> _settings;
    private readonly object _gate = new();

    private Task<string?>? _selectionTask;
    private CancellationTokenSource? _cts;
    private DictationState _state = DictationState.Idle;

    public static readonly TimeSpan MinimumRecording = TimeSpan.FromMilliseconds(350);

    public DictationController(IAudioRecorder recorder, ITranscriber transcriber, ITextCleaner cleaner,
        ITextInserter inserter, ISelectionReader selection, Func<MyFlowSettings> settings)
    {
        _recorder = recorder;
        _transcriber = transcriber;
        _cleaner = cleaner;
        _inserter = inserter;
        _selection = selection;
        _settings = settings;
    }

    public event EventHandler<DictationStatus>? StatusChanged;
    public event EventHandler<DictationResult>? Completed;
    public event EventHandler<string>? Failed;

    public DictationState State
    {
        get { lock (_gate) return _state; }
    }

    // ---- Wiring for the hotkey service -------------------------------------------------

    public void OnHotkeyPressed()
    {
        var mode = _settings().HotkeyMode;
        var state = State;
        if (mode == HotkeyMode.Toggle && state == DictationState.Recording)
        {
            _ = FinishAsync();
        }
        else if (state == DictationState.Idle)
        {
            _ = BeginAsync();
        }
    }

    public void OnHotkeyReleased()
    {
        if (_settings().HotkeyMode == HotkeyMode.PushToTalk && State == DictationState.Recording)
        {
            _ = FinishAsync();
        }
    }

    // ---- Steps -------------------------------------------------------------------------

    public async Task BeginAsync()
    {
        lock (_gate)
        {
            if (_state != DictationState.Idle) return;
            _state = DictationState.Recording;
            _cts = new CancellationTokenSource();
        }

        var ct = _cts.Token;
        Report(DictationState.Recording, "Listening…");
        try
        {
            // Read the selection while the target app still has focus. Runs alongside
            // the microphone start-up so no speech is lost waiting for it.
            _selectionTask = ReadSelectionSafeAsync(ct);
            await _recorder.StartAsync(_settings().MicrophoneId, ct);
            var selection = await _selectionTask;
            if (!string.IsNullOrWhiteSpace(selection))
            {
                Report(DictationState.Recording, "Listening for an edit instruction…", isEdit: true);
            }
        }
        catch (Exception ex)
        {
            await FailAsync("Couldn't start the microphone: " + ex.Message, ex);
        }
    }

    public async Task<DictationResult?> FinishAsync()
    {
        CancellationToken ct;
        lock (_gate)
        {
            if (_state != DictationState.Recording || _cts == null) return null;
            _state = DictationState.Transcribing;
            ct = _cts.Token;
        }

        var stopwatch = Stopwatch.StartNew();
        string? wavPath = null;
        try
        {
            var recording = await _recorder.StopAsync(ct);
            wavPath = recording.WavPath;
            var selection = _selectionTask != null ? await _selectionTask : null;
            var isEdit = !string.IsNullOrWhiteSpace(selection);

            if (recording.Duration < MinimumRecording)
            {
                Log.Info($"Recording too short ({recording.Duration.TotalMilliseconds:0} ms); ignored.");
                Reset();
                return null;
            }

            Report(DictationState.Transcribing, "Transcribing…", isEdit);
            var raw = TranscriptFilter.Clean(await _transcriber.TranscribeAsync(recording.WavPath, ct));
            Log.Info($"Transcribed {recording.Duration.TotalSeconds:0.0}s of audio in {stopwatch.ElapsedMilliseconds} ms");
            Log.Debug($"Raw transcript: {raw}");

            if (raw.Length == 0 || TranscriptFilter.IsLikelyHallucination(raw, recording.Duration))
            {
                Log.Info("No speech detected.");
                Report(DictationState.Idle, "No speech detected");
                Reset(keepStatus: true);
                return null;
            }

            string finalText;
            if (isEdit)
            {
                Report(DictationState.Editing, "Editing selection…", true);
                finalText = await _cleaner.EditAsync(selection!, raw, ct);
            }
            else
            {
                Report(DictationState.Polishing, "Polishing…");
                finalText = await _cleaner.CleanAsync(raw, ct);
            }
            Log.Debug($"Final text: {finalText}");

            Report(DictationState.Inserting, "Inserting…", isEdit);
            var insert = await _inserter.InsertAsync(finalText, ct);
            stopwatch.Stop();
            Log.Info($"Dictation done in {stopwatch.ElapsedMilliseconds} ms after release ({insert}).");

            var result = new DictationResult(raw, finalText, isEdit, insert, stopwatch.Elapsed);
            Reset();
            Completed?.Invoke(this, result);
            if (insert == InsertResult.CopiedToClipboardOnly)
            {
                Failed?.Invoke(this, "Copied to clipboard, but couldn't paste. Give MyFlow Accessibility permission, then press ⌘V.");
            }
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Reset();
            return null;
        }
        catch (Exception ex)
        {
            await FailAsync(ex.Message, ex);
            return null;
        }
        finally
        {
            TryDelete(wavPath);
        }
    }

    /// <summary>Abandons the current take (Escape, or Fn used for another shortcut).</summary>
    public async Task CancelAsync()
    {
        CancellationTokenSource? cts;
        DictationState state;
        lock (_gate)
        {
            state = _state;
            cts = _cts;
        }
        if (state == DictationState.Idle) return;
        cts?.Cancel();
        if (state == DictationState.Recording)
        {
            try { await _recorder.CancelAsync(); } catch (Exception ex) { Log.Warn("Cancel recording: " + ex.Message); }
            Reset();
        }
        Log.Info("Dictation cancelled.");
    }

    private async Task<string?> ReadSelectionSafeAsync(CancellationToken ct)
    {
        try
        {
            return await _selection.GetSelectedTextAsync(ct);
        }
        catch (Exception ex)
        {
            Log.Warn("Couldn't read selection: " + ex.Message);
            return null;
        }
    }

    private async Task FailAsync(string message, Exception ex)
    {
        Log.Error("Dictation failed", ex);
        if (_recorder.IsRecording)
        {
            try { await _recorder.CancelAsync(); } catch { /* best effort */ }
        }
        Reset();
        Failed?.Invoke(this, message);
    }

    private void Reset(bool keepStatus = false)
    {
        lock (_gate)
        {
            _state = DictationState.Idle;
            _cts?.Dispose();
            _cts = null;
            _selectionTask = null;
        }
        if (!keepStatus) Report(DictationState.Idle, "");
    }

    private void Report(DictationState state, string message, bool isEdit = false) =>
        StatusChanged?.Invoke(this, new DictationStatus(state, message, isEdit));

    private static void TryDelete(string? path)
    {
        if (path == null) return;
        try { File.Delete(path); } catch { /* temp file; ignore */ }
    }
}
