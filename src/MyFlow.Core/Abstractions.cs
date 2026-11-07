namespace MyFlow.Core;

// Every OS-specific capability MyFlow needs is described by one of these
// interfaces. MyFlow.Platform.MacOS implements them for macOS; a future
// MyFlow.Platform.Windows would implement the same set.

/// <summary>Watches for the dictation shortcut anywhere in the OS.</summary>
public interface IHotkeyService : IDisposable
{
    /// <summary>The shortcut went down (start of a push-to-talk hold, or a toggle press).</summary>
    event EventHandler? Pressed;

    /// <summary>The shortcut was released.</summary>
    event EventHandler? Released;

    /// <summary>Another key was pressed while the shortcut was held (e.g. Fn+Arrow). Cancel the take.</summary>
    event EventHandler? Interrupted;

    /// <summary>Escape was pressed; cancel whatever is in progress.</summary>
    event EventHandler? CancelRequested;

    /// <summary>Starts (or restarts) listening for <paramref name="shortcut"/>.</summary>
    void Start(HotkeySpec shortcut);

    void Stop();
}

/// <summary>Records microphone audio to a 16 kHz mono WAV file.</summary>
public interface IAudioRecorder
{
    Task<IReadOnlyList<AudioDevice>> ListDevicesAsync(CancellationToken ct = default);

    /// <summary>Starts recording. Returns once the device is actually open.</summary>
    Task StartAsync(string? deviceId, CancellationToken ct = default);

    /// <summary>Stops and returns the finished recording.</summary>
    Task<Recording> StopAsync(CancellationToken ct = default);

    /// <summary>Stops and throws the audio away.</summary>
    Task CancelAsync();

    bool IsRecording { get; }
}

public sealed record AudioDevice(string Id, string Name);

public sealed record Recording(string WavPath, TimeSpan Duration);

/// <summary>Turns speech audio into raw text.</summary>
public interface ITranscriber
{
    /// <summary>Downloads the model / starts the engine if needed. Safe to call repeatedly.</summary>
    Task EnsureReadyAsync(IProgress<string>? progress = null, CancellationToken ct = default);

    Task<string> TranscribeAsync(string wavPath, CancellationToken ct = default);
}

/// <summary>A chat-style language model used for cleanup and voice edits.</summary>
public interface ILanguageModel
{
    string DisplayName { get; }

    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct = default);
}

/// <summary>Polishes dictated text and applies spoken edit instructions.</summary>
public interface ITextCleaner
{
    /// <summary>Cleans up a raw transcript (punctuation, filler words). Never answers it.</summary>
    Task<string> CleanAsync(string rawTranscript, CancellationToken ct = default);

    /// <summary>Rewrites <paramref name="selectedText"/> according to a spoken instruction.</summary>
    Task<string> EditAsync(string selectedText, string instruction, CancellationToken ct = default);
}

/// <summary>Puts text into whatever app has keyboard focus.</summary>
public interface ITextInserter
{
    Task<InsertResult> InsertAsync(string text, CancellationToken ct = default);
}

public enum InsertResult
{
    Pasted,
    /// <summary>Couldn't paste (usually missing Accessibility permission); the text was left on the clipboard.</summary>
    CopiedToClipboardOnly,
    Failed,
}

/// <summary>Reads the text currently selected in the focused app.</summary>
public interface ISelectionReader
{
    /// <summary>Returns the selected text, or null when nothing is selected.</summary>
    Task<string?> GetSelectedTextAsync(CancellationToken ct = default);
}

public interface ISettingsStore
{
    MyFlowSettings Load();
    void Save(MyFlowSettings settings);
    string DataDirectory { get; }
}

/// <summary>Stores secrets (API keys) in the OS credential store, never in files.</summary>
public interface ISecretStore
{
    string? Get(string name);
    void Set(string name, string value);
    void Delete(string name);
}

/// <summary>Reports and requests OS privacy permissions.</summary>
public interface IPermissionService
{
    bool IsAccessibilityTrusted();

    /// <summary>Shows the system prompt asking for Accessibility, if not yet granted.</summary>
    void RequestAccessibility();

    void OpenAccessibilitySettings();
    void OpenMicrophoneSettings();
}
