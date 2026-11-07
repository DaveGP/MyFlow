using MyFlow.Core;
using MyFlow.Core.LanguageModels;
using MyFlow.Core.Text;
using MyFlow.Core.Transcription;
using MyFlow.Platform.MacOS;

namespace MyFlow.App;

/// <summary>
/// Builds and owns all of MyFlow's services. This is the one place that knows which
/// concrete (macOS) implementations back the Core interfaces — to add Windows later,
/// pick different implementations here.
/// </summary>
public sealed class AppHost : IDisposable
{
    public const string OpenAIKeyName = "openai-api-key";
    public const string AnthropicKeyName = "anthropic-api-key";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(30) };
    private WhisperServerTranscriber? _transcriber;
    private DictationController? _controller;

    public AppHost()
    {
        Settings = SettingsStore.Load();
        Hotkeys = new MacHotkeyService(SettingsStore.DataDirectory);
        Hotkeys.Pressed += (_, _) => _controller?.OnHotkeyPressed();
        Hotkeys.Released += (_, _) => _controller?.OnHotkeyReleased();
        Hotkeys.Interrupted += (_, _) => _ = _controller?.CancelAsync();
        Hotkeys.CancelRequested += (_, _) =>
        {
            if (_controller?.State != DictationState.Idle) _ = _controller?.CancelAsync();
        };
    }

    public ISettingsStore SettingsStore { get; } = new JsonSettingsStore();
    public ISecretStore Secrets { get; } = new KeychainSecretStore();
    public IPermissionService Permissions { get; } = new MacPermissionService();
    public IHotkeyService Hotkeys { get; }
    public IAudioRecorder Recorder { get; } = new FfmpegAudioRecorder();
    public MyFlowSettings Settings { get; private set; }
    public DictationController Controller => _controller ?? throw new InvalidOperationException("Not started.");
    public LlmTextCleaner? Cleaner { get; private set; }

    /// <summary>Fires whenever <see cref="Controller"/> is replaced (after a settings change).</summary>
    public event EventHandler<DictationController>? ControllerChanged;

    public event EventHandler<string>? StartupProgress;

    /// <summary>(Re)builds the pipeline from the current settings and starts listening for the shortcut.</summary>
    public void Start()
    {
        _transcriber?.Dispose();
        _transcriber = new WhisperServerTranscriber(_http, () => MacTools.FindTool("whisper-server"),
            Settings.WhisperModel, Settings.Language, Settings.CustomVocabulary);

        Cleaner = new LlmTextCleaner(CreateLanguageModel());
        _controller = new DictationController(Recorder, _transcriber, Cleaner,
            new MacTextInserter(() => Settings), new MacSelectionReader(), () => Settings);
        ControllerChanged?.Invoke(this, _controller);

        if (HotkeySpec.TryParse(Settings.Hotkey, out var spec, out var error))
        {
            Hotkeys.Start(spec);
        }
        else
        {
            Log.Warn($"Invalid shortcut '{Settings.Hotkey}' ({error}); using Globe.");
            Hotkeys.Start(HotkeySpec.Globe);
        }

        // Load the speech model and the cleanup model in the background so the first dictation is fast.
        var progress = new Progress<string>(m => StartupProgress?.Invoke(this, m));
        var transcriber = _transcriber;
        _ = Task.Run(async () =>
        {
            try
            {
                await transcriber.EnsureReadyAsync(progress);
            }
            catch (Exception ex)
            {
                Log.Error("Speech model failed to load", ex);
                StartupProgress?.Invoke(this, "Speech model failed to load: " + ex.Message);
            }
        });
        if (Cleaner.Model is OllamaLanguageModel ollama)
        {
            _ = Task.Run(async () =>
            {
                if (await ollama.IsAvailableAsync())
                {
                    try { await ollama.WarmUpAsync(); } catch { /* optional */ }
                }
                else
                {
                    Log.Warn($"{ollama.DisplayName} isn't available; dictation will be inserted without cleanup.");
                }
            });
        }
    }

    public ILanguageModel? CreateLanguageModel() => Settings.CleanupProvider switch
    {
        CleanupProvider.Ollama => new OllamaLanguageModel(_http, Settings.OllamaUrl, Settings.OllamaModel),
        CleanupProvider.OpenAI => new OpenAILanguageModel(_http, () => Secrets.Get(OpenAIKeyName), Settings.OpenAIModel),
        CleanupProvider.Anthropic => new AnthropicLanguageModel(() => Secrets.Get(AnthropicKeyName), Settings.AnthropicModel),
        _ => null,
    };

    public void UpdateSettings(MyFlowSettings settings)
    {
        Settings = settings;
        SettingsStore.Save(settings);
        Start();
    }

    public void Dispose()
    {
        Hotkeys.Dispose();
        _transcriber?.Dispose();
        _http.Dispose();
    }
}
