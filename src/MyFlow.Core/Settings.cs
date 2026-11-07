using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyFlow.Core;

public enum HotkeyMode
{
    /// <summary>Hold the shortcut while speaking; release to finish.</summary>
    PushToTalk,
    /// <summary>Press once to start, press again to finish.</summary>
    Toggle,
}

public enum CleanupProvider
{
    None,
    Ollama,
    OpenAI,
    Anthropic,
}

/// <summary>Everything the user can configure. Saved as JSON (no secrets in here).</summary>
public sealed record MyFlowSettings
{
    /// <summary>"Globe" (the Fn/🌐 key) or a combo such as "Ctrl+Option+Space".</summary>
    public string Hotkey { get; init; } = "Globe";

    public HotkeyMode HotkeyMode { get; init; } = HotkeyMode.PushToTalk;

    /// <summary>Microphone id from <see cref="IAudioRecorder.ListDevicesAsync"/>; null = system default.</summary>
    public string? MicrophoneId { get; init; }

    /// <summary>Whisper model name, e.g. "large-v3-turbo-q5_0" or "small.en".</summary>
    public string WhisperModel { get; init; } = "large-v3-turbo-q5_0";

    /// <summary>Spoken language code ("en") or "auto".</summary>
    public string Language { get; init; } = "en";

    public CleanupProvider CleanupProvider { get; init; } = CleanupProvider.Ollama;

    public string OllamaUrl { get; init; } = "http://127.0.0.1:11434";
    public string OllamaModel { get; init; } = "mistral-nemo:12b";
    public string OpenAIModel { get; init; } = "gpt-5-mini";
    public string AnthropicModel { get; init; } = "claude-opus-5-5";

    /// <summary>Put the user's previous clipboard text back after pasting.</summary>
    public bool RestoreClipboard { get; init; } = true;

    /// <summary>Words Whisper should be nudged towards (names, jargon). Comma separated.</summary>
    public string CustomVocabulary { get; init; } = "";

    public bool FirstRunCompleted { get; init; }

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
}

/// <summary>Stores settings in ~/Library/Application Support/MyFlow/settings.json (or the OS equivalent).</summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string _path;

    public JsonSettingsStore(string? dataDirectory = null)
    {
        DataDirectory = dataDirectory ?? AppPaths.DataDirectory;
        Directory.CreateDirectory(DataDirectory);
        _path = Path.Combine(DataDirectory, "settings.json");
    }

    public string DataDirectory { get; }

    public MyFlowSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                return JsonSerializer.Deserialize<MyFlowSettings>(File.ReadAllText(_path), MyFlowSettings.JsonOptions)
                       ?? new MyFlowSettings();
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Couldn't read settings, using defaults: {ex.Message}");
        }
        return new MyFlowSettings();
    }

    public void Save(MyFlowSettings settings)
    {
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, MyFlowSettings.JsonOptions));
        File.Move(tmp, _path, overwrite: true);
    }
}

public static class AppPaths
{
    /// <summary>~/Library/Application Support/MyFlow on macOS, %APPDATA%\MyFlow on Windows.</summary>
    public static string DataDirectory { get; } = Path.Combine(
        OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MyFlow");

    public static string ModelsDirectory => Path.Combine(DataDirectory, "models");
    public static string LogsDirectory => Path.Combine(DataDirectory, "logs");
    public static string TempDirectory => Path.Combine(Path.GetTempPath(), "MyFlow");
}
