using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MyFlow.Core;

namespace MyFlow.App;

/// <summary>
/// The menu-bar app: a tray icon with a small menu, the floating recording pill,
/// and the settings window. All real work happens in <see cref="AppHost"/>.
/// </summary>
public partial class App : Application
{
    private AppHost? _host;
    private TrayIcon? _tray;
    private NativeMenuItem? _statusItem;
    private NativeMenuItem? _copyLastItem;
    private PillWindow? _pill;
    private SettingsWindow? _settingsWindow;
    private string? _lastText;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _host = new AppHost();
            _pill = new PillWindow();
            _host.ControllerChanged += (_, controller) => Wire(controller);
            _host.StartupProgress += (_, message) => Dispatcher.UIThread.Post(() => SetStatus(message));

            CreateTrayIcon();
            _host.Start();

            if (!_host.Settings.FirstRunCompleted || !_host.Permissions.IsAccessibilityTrusted())
            {
                ShowSettings();
            }

            desktop.ShutdownRequested += (_, _) => _host?.Dispose();
            desktop.Exit += (_, _) => _host?.Dispose();
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void Wire(DictationController controller)
    {
        controller.StatusChanged += (_, status) => Dispatcher.UIThread.Post(() =>
        {
            _pill?.ShowStatus(status);
            SetStatus(status.State == DictationState.Idle
                ? (string.IsNullOrEmpty(status.Message) ? "Ready" : status.Message)
                : status.Message);
        });
        controller.Completed += (_, result) => Dispatcher.UIThread.Post(() =>
        {
            _lastText = result.FinalText;
            if (_copyLastItem != null) _copyLastItem.IsEnabled = true;
            SetStatus($"Ready · last took {result.Elapsed.TotalSeconds:0.0}s");
        });
        controller.Failed += (_, message) => Dispatcher.UIThread.Post(() =>
        {
            _pill?.ShowError(message);
            SetStatus(message);
        });
    }

    private void CreateTrayIcon()
    {
        _statusItem = new NativeMenuItem("Starting…") { IsEnabled = false };
        var hint = new NativeMenuItem(ShortcutHint()) { IsEnabled = false };
        _copyLastItem = new NativeMenuItem("Copy Last Dictation") { IsEnabled = false };
        _copyLastItem.Click += async (_, _) =>
        {
            if (_lastText != null) await Platform.MacOS.MacClipboard.SetTextAsync(_lastText);
        };
        var settings = new NativeMenuItem("Settings…");
        settings.Click += (_, _) => ShowSettings();
        var quit = new NativeMenuItem("Quit MyFlow");
        quit.Click += (_, _) => (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();

        var menu = new NativeMenu();
        menu.Items.Add(_statusItem);
        menu.Items.Add(hint);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(_copyLastItem);
        menu.Items.Add(settings);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quit);

        _tray = new TrayIcon
        {
            Icon = TrayIconArt.Create(),
            ToolTipText = "MyFlow",
            Menu = menu,
            IsVisible = true,
        };
        MacOSProperties.SetIsTemplateIcon(_tray, true); // lets macOS tint it for light/dark menu bars
        TrayIcon.SetIcons(this, [_tray]);

        _host!.ControllerChanged += (_, _) => Dispatcher.UIThread.Post(() => hint.Header = ShortcutHint());
    }

    private string ShortcutHint()
    {
        var s = _host?.Settings;
        if (s == null) return "";
        var key = s.Hotkey.Equals("Globe", StringComparison.OrdinalIgnoreCase) ? "🌐 Globe (fn)" : s.Hotkey;
        return s.HotkeyMode == HotkeyMode.PushToTalk ? $"Hold {key} to dictate" : $"Press {key} to start/stop";
    }

    private void SetStatus(string text)
    {
        if (_statusItem != null) _statusItem.Header = text.Length > 60 ? text[..57] + "…" : text;
    }

    private void ShowSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_host!);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }
}
