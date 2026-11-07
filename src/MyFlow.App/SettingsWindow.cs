using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MyFlow.Core;
using MyFlow.Core.Transcription;

namespace MyFlow.App;

/// <summary>
/// One scrolling page of settings: permissions, shortcut, microphone, speech model and cleanup.
/// Built in C# (no XAML) so the whole window can be read top to bottom in one file.
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly AppHost _host;

    private readonly TextBlock _accessibilityStatus = new();
    private readonly ComboBox _shortcut = new();
    private readonly TextBox _customShortcut = new() { Watermark = "e.g. Ctrl+Option+Space", IsVisible = false };
    private readonly ComboBox _mode = new();
    private readonly ComboBox _microphone = new();
    private readonly ComboBox _whisperModel = new();
    private readonly ComboBox _language = new();
    private readonly ComboBox _provider = new();
    private readonly TextBox _ollamaModel = new();
    private readonly TextBox _cloudModel = new();
    private readonly TextBox _apiKey = new() { PasswordChar = '•', Watermark = "Paste API key (saved in your Keychain)" };
    private readonly TextBlock _apiKeyStatus = new() { FontSize = 12, Opacity = 0.7 };
    private readonly TextBox _vocabulary = new() { Watermark = "Names or terms, comma separated", AcceptsReturn = false };
    private readonly CheckBox _restoreClipboard = new() { Content = "Put my previous clipboard back after pasting" };
    private readonly TextBlock _saveStatus = new() { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.75 };
    private readonly StackPanel _ollamaPanel;
    private readonly StackPanel _cloudPanel;
    private readonly DispatcherTimer _permissionPoll = new() { Interval = TimeSpan.FromSeconds(1.5) };

    private static readonly (string Label, string Value)[] ShortcutPresets =
    [
        ("🌐 Globe / fn key", "Globe"),
        ("Right ⌥ Option", "RightOption"),
        ("⌃ Control + ⌥ Option + Space", "Ctrl+Option+Space"),
        ("Custom…", ""),
    ];

    private static readonly (string Label, CleanupProvider Value)[] Providers =
    [
        ("Ollama — on this Mac (recommended)", CleanupProvider.Ollama),
        ("OpenAI — cloud", CleanupProvider.OpenAI),
        ("Claude (Anthropic) — cloud", CleanupProvider.Anthropic),
        ("Off — paste exactly what Whisper heard", CleanupProvider.None),
    ];

    private static readonly (string Label, string Value)[] Languages =
    [
        ("English", "en"), ("Detect automatically", "auto"), ("French", "fr"), ("German", "de"),
        ("Spanish", "es"), ("Italian", "it"), ("Portuguese", "pt"), ("Dutch", "nl"), ("Japanese", "ja"),
    ];

    public SettingsWindow(AppHost host)
    {
        _host = host;
        Title = "MyFlow Settings";
        Width = 560;
        Height = 760;
        MinWidth = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _ollamaPanel = Field("Ollama model", _ollamaModel, "Any model you've pulled with Ollama, e.g. mistral-nemo:12b.");
        _cloudPanel = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Field("Cloud model", _cloudModel, null),
                Field("API key", _apiKey, null),
                Row(Button("Save key", SaveApiKey), Button("Remove key", RemoveApiKey), _apiKeyStatus),
            },
        };

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(28, 24, 28, 28),
                Spacing = 22,
                Children =
                {
                    Header(),
                    Section("Permissions",
                        Row(new TextBlock { Text = "Accessibility", Width = 110, VerticalAlignment = VerticalAlignment.Center },
                            _accessibilityStatus,
                            Button("Grant…", () =>
                            {
                                _host.Permissions.RequestAccessibility();
                                _host.Permissions.OpenAccessibilitySettings();
                            })),
                        Note("Lets MyFlow notice the shortcut and paste into other apps. In System Settings, switch on “MyFlow”."),
                        Row(new TextBlock { Text = "Microphone", Width = 110, VerticalAlignment = VerticalAlignment.Center },
                            new TextBlock { Text = "macOS asks the first time you dictate", Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center },
                            Button("Open…", _host.Permissions.OpenMicrophoneSettings))),
                    Section("Shortcut",
                        Field("Dictation shortcut", _shortcut, null),
                        _customShortcut,
                        Field("Mode", _mode, "Hold: talk while holding the key. Toggle: press once to start and again to stop. Esc cancels."),
                        Note("Tip: select text first, then dictate an instruction like “make this friendlier” to rewrite it.")),
                    Section("Microphone",
                        Field("Input device", _microphone, null)),
                    Section("Transcription (on this Mac)",
                        Field("Whisper model", _whisperModel, "Downloaded once to ~/Library/Application Support/MyFlow/models."),
                        Field("Language", _language, null),
                        Field("Vocabulary", _vocabulary, "Helps Whisper spell names and jargon correctly.")),
                    Section("Polishing",
                        Field("Clean up with", _provider, "Removes filler words and fixes punctuation. If the model isn't available, MyFlow pastes the raw transcript."),
                        _ollamaPanel,
                        _cloudPanel,
                        _restoreClipboard),
                    Row(Button("Save", Save, primary: true), _saveStatus),
                    Note("MyFlow is based on OpenWhispr (MIT License). See THIRD_PARTY_NOTICES.md."),
                },
            },
        };

        LoadValues();
        _shortcut.SelectionChanged += (_, _) => _customShortcut.IsVisible = _shortcut.SelectedIndex == ShortcutPresets.Length - 1;
        _provider.SelectionChanged += (_, _) => UpdateProviderPanels();
        _permissionPoll.Tick += (_, _) => UpdatePermissionStatus();
        _permissionPoll.Start();
        Closed += (_, _) => _permissionPoll.Stop();
        _ = LoadMicrophonesAsync();
    }

    // ---- Values -------------------------------------------------------------------------

    private void LoadValues()
    {
        var s = _host.Settings;

        _shortcut.ItemsSource = ShortcutPresets.Select(p => p.Label).ToList();
        var preset = Array.FindIndex(ShortcutPresets, p => p.Value.Equals(s.Hotkey, StringComparison.OrdinalIgnoreCase));
        _shortcut.SelectedIndex = preset >= 0 ? preset : ShortcutPresets.Length - 1;
        _customShortcut.Text = preset >= 0 ? "" : s.Hotkey;
        _customShortcut.IsVisible = preset < 0;

        _mode.ItemsSource = new[] { "Hold to talk", "Press to start / stop" };
        _mode.SelectedIndex = s.HotkeyMode == HotkeyMode.PushToTalk ? 0 : 1;

        _whisperModel.ItemsSource = WhisperModels.All.Select(m => $"{m.Name} — {m.Description}").ToList();
        _whisperModel.SelectedIndex = Math.Max(0, WhisperModels.All.ToList().FindIndex(m => m.Name == s.WhisperModel));

        _language.ItemsSource = Languages.Select(l => l.Label).ToList();
        _language.SelectedIndex = Math.Max(0, Array.FindIndex(Languages, l => l.Value == s.Language));

        _provider.ItemsSource = Providers.Select(p => p.Label).ToList();
        _provider.SelectedIndex = Math.Max(0, Array.FindIndex(Providers, p => p.Value == s.CleanupProvider));
        _ollamaModel.Text = s.OllamaModel;
        _vocabulary.Text = s.CustomVocabulary;
        _restoreClipboard.IsChecked = s.RestoreClipboard;
        UpdateProviderPanels();
        UpdatePermissionStatus();
    }

    private async Task LoadMicrophonesAsync()
    {
        IReadOnlyList<AudioDevice> devices;
        try
        {
            devices = await _host.Recorder.ListDevicesAsync();
        }
        catch (Exception ex)
        {
            Log.Warn("Couldn't list microphones: " + ex.Message);
            devices = [];
        }
        var items = new List<string> { "System default" };
        items.AddRange(devices.Select(d => d.Name));
        _microphone.ItemsSource = items;
        var index = devices.ToList().FindIndex(d => d.Id == _host.Settings.MicrophoneId);
        _microphone.SelectedIndex = index >= 0 ? index + 1 : 0;
        _microphone.Tag = devices;
    }

    private void UpdateProviderPanels()
    {
        var provider = SelectedProvider();
        _ollamaPanel.IsVisible = provider == CleanupProvider.Ollama;
        _cloudPanel.IsVisible = provider is CleanupProvider.OpenAI or CleanupProvider.Anthropic;
        _cloudModel.Text = provider == CleanupProvider.Anthropic ? _host.Settings.AnthropicModel : _host.Settings.OpenAIModel;
        _apiKey.Text = "";
        var key = KeyName(provider);
        _apiKeyStatus.Text = key != null && _host.Secrets.Get(key) != null ? "A key is saved in your Keychain." : "No key saved.";
    }

    private void UpdatePermissionStatus()
    {
        var trusted = _host.Permissions.IsAccessibilityTrusted();
        _accessibilityStatus.Text = trusted ? "✓ Granted" : "Not granted yet";
        _accessibilityStatus.Foreground = trusted ? Brushes.SeaGreen : Brushes.OrangeRed;
        _accessibilityStatus.VerticalAlignment = VerticalAlignment.Center;
        _accessibilityStatus.Width = 150;
    }

    private CleanupProvider SelectedProvider() => Providers[Math.Max(0, _provider.SelectedIndex)].Value;

    private static string? KeyName(CleanupProvider provider) => provider switch
    {
        CleanupProvider.OpenAI => AppHost.OpenAIKeyName,
        CleanupProvider.Anthropic => AppHost.AnthropicKeyName,
        _ => null,
    };

    private void SaveApiKey()
    {
        var name = KeyName(SelectedProvider());
        var key = _apiKey.Text?.Trim();
        if (name == null || string.IsNullOrEmpty(key)) return;
        try
        {
            _host.Secrets.Set(name, key);
            _apiKey.Text = "";
            _apiKeyStatus.Text = "Saved in your Keychain.";
        }
        catch (Exception ex)
        {
            _apiKeyStatus.Text = ex.Message;
        }
    }

    private void RemoveApiKey()
    {
        var name = KeyName(SelectedProvider());
        if (name == null) return;
        _host.Secrets.Delete(name);
        _apiKeyStatus.Text = "Key removed.";
    }

    private void Save()
    {
        var hotkey = _shortcut.SelectedIndex == ShortcutPresets.Length - 1
            ? _customShortcut.Text?.Trim() ?? ""
            : ShortcutPresets[_shortcut.SelectedIndex].Value;
        if (!HotkeySpec.TryParse(hotkey, out _, out var error))
        {
            _saveStatus.Text = "Shortcut: " + error;
            return;
        }

        var devices = _microphone.Tag as IReadOnlyList<AudioDevice> ?? [];
        var micIndex = _microphone.SelectedIndex - 1;
        var provider = SelectedProvider();
        var old = _host.Settings;

        var settings = old with
        {
            Hotkey = hotkey,
            HotkeyMode = _mode.SelectedIndex == 0 ? HotkeyMode.PushToTalk : HotkeyMode.Toggle,
            MicrophoneId = micIndex >= 0 && micIndex < devices.Count ? devices[micIndex].Id : null,
            WhisperModel = WhisperModels.All[Math.Max(0, _whisperModel.SelectedIndex)].Name,
            Language = Languages[Math.Max(0, _language.SelectedIndex)].Value,
            CustomVocabulary = _vocabulary.Text?.Trim() ?? "",
            CleanupProvider = provider,
            OllamaModel = string.IsNullOrWhiteSpace(_ollamaModel.Text) ? old.OllamaModel : _ollamaModel.Text.Trim(),
            OpenAIModel = provider == CleanupProvider.OpenAI && !string.IsNullOrWhiteSpace(_cloudModel.Text) ? _cloudModel.Text.Trim() : old.OpenAIModel,
            AnthropicModel = provider == CleanupProvider.Anthropic && !string.IsNullOrWhiteSpace(_cloudModel.Text) ? _cloudModel.Text.Trim() : old.AnthropicModel,
            RestoreClipboard = _restoreClipboard.IsChecked == true,
            FirstRunCompleted = true,
        };

        _host.UpdateSettings(settings);
        _saveStatus.Text = "Saved ✓";
    }

    // ---- Small layout helpers -----------------------------------------------------------

    private static Control Header() => new StackPanel
    {
        Spacing = 4,
        Children =
        {
            new TextBlock { Text = "MyFlow", FontSize = 26, FontWeight = FontWeight.SemiBold },
            new TextBlock { Text = "Speak anywhere. Polished text appears where you're typing.", Opacity = 0.7 },
        },
    };

    private static Control Section(string title, params Control[] children)
    {
        var body = new StackPanel { Spacing = 10 };
        foreach (var c in children) body.Children.Add(c);
        return new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = title.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeight.SemiBold, Opacity = 0.55, LetterSpacing = 0.8 },
                new Border
                {
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(16),
                    Background = new SolidColorBrush(Color.Parse("#14808080")),
                    Child = body,
                },
            },
        };
    }

    private static StackPanel Field(string label, Control input, string? note)
    {
        if (input is ComboBox or TextBox) input.HorizontalAlignment = HorizontalAlignment.Stretch;
        var panel = new StackPanel { Spacing = 5, Children = { new TextBlock { Text = label, FontWeight = FontWeight.Medium }, input } };
        if (note != null) panel.Children.Add(Note(note));
        return panel;
    }

    private static TextBlock Note(string text) =>
        new() { Text = text, FontSize = 12, Opacity = 0.65, TextWrapping = TextWrapping.Wrap };

    private static StackPanel Row(params Control[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        foreach (var c in children) row.Children.Add(c);
        return row;
    }

    private static Button Button(string text, Action onClick, bool primary = false)
    {
        var button = new Button { Content = text, Padding = new Thickness(14, 6) };
        if (primary) button.Classes.Add("accent");
        button.Click += (_, _) => onClick();
        return button;
    }
}
