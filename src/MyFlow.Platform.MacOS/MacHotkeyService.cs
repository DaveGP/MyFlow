using System.Diagnostics;
using MyFlow.Core;
using SharpHook;
using SharpHook.Data;

namespace MyFlow.Platform.MacOS;

/// <summary>
/// Global shortcut on macOS.
/// - "Globe": uses OpenWhispr's macos-globe-listener helper, which prints FN_DOWN / FN_UP.
///   While it runs, macOS's own Globe action (emoji picker / dictation) is set to "Do Nothing";
///   the helper restores it when it exits.
/// - Anything else (e.g. "Ctrl+Option+Space"): a SharpHook keyboard hook.
/// The SharpHook hook also always watches for Escape (cancel).
/// Both need Accessibility permission.
/// </summary>
public sealed class MacHotkeyService : IHotkeyService
{
    private readonly string _stateFile;
    private Process? _globe;
    private SimpleGlobalHook? _hook;
    private HotkeySpec? _spec;
    private readonly HashSet<KeyCode> _modifiersDown = [];
    private bool _comboActive;

    public MacHotkeyService(string dataDirectory)
    {
        _stateFile = Path.Combine(dataDirectory, "globe-preference-state.json");
    }

    public event EventHandler? Pressed;
    public event EventHandler? Released;
    public event EventHandler? Interrupted;
    public event EventHandler? CancelRequested;

    public void Start(HotkeySpec shortcut)
    {
        Stop();
        _spec = shortcut;
        if (shortcut.IsGlobe) StartGlobeListener();
        StartKeyboardHook();
        Log.Info($"Listening for shortcut: {shortcut}");
    }

    public void Stop()
    {
        StopGlobeListener();
        if (_hook != null)
        {
            try { _hook.Dispose(); } catch { /* ignore */ }
            _hook = null;
        }
        _modifiersDown.Clear();
        _comboActive = false;
    }

    // ---- Globe / Fn key ---------------------------------------------------------------

    private void StartGlobeListener()
    {
        var helper = MacTools.FindHelper("macos-globe-listener");
        if (helper == null)
        {
            Log.Error("macos-globe-listener helper not found; the Globe shortcut won't work.");
            return;
        }

        // If a previous run crashed while holding the Globe key, put the user's setting back first.
        if (File.Exists(_stateFile))
        {
            try
            {
                Process.Start(new ProcessStartInfo(helper, ["--globe-preference-state", _stateFile, "--restore-leftover-globe-preference"])
                { UseShellExecute = false })?.WaitForExit(3000);
            }
            catch (Exception ex) { Log.Warn("Globe preference restore: " + ex.Message); }
        }

        var psi = new ProcessStartInfo(helper)
        {
            RedirectStandardInput = true,   // closing stdin tells the helper to quit and restore the Globe setting
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("--suppress-system-globe-action");
        psi.ArgumentList.Add("--globe-preference-state");
        psi.ArgumentList.Add(_stateFile);

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => OnGlobeLine(e.Data);
        process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Log.Warn("[globe] " + e.Data); };
        process.Exited += (_, _) => Log.Info($"Globe listener exited ({SafeExitCode(process)}).");
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _globe = process;
    }

    private void OnGlobeLine(string? line)
    {
        switch (line?.Trim())
        {
            case "FN_DOWN": Pressed?.Invoke(this, EventArgs.Empty); break;
            case "FN_UP": Released?.Invoke(this, EventArgs.Empty); break;
            case "FN_INTERRUPTED": Interrupted?.Invoke(this, EventArgs.Empty); break;
        }
    }

    private void StopGlobeListener()
    {
        var process = _globe;
        _globe = null;
        if (process == null) return;
        try
        {
            process.StandardInput.Close(); // EOF → helper restores the Globe setting and exits
            if (!process.WaitForExit(2000)) process.Kill();
        }
        catch { /* already gone */ }
        process.Dispose();
    }

    private static string SafeExitCode(Process p)
    {
        try { return "code " + p.ExitCode; } catch { return "unknown"; }
    }

    // ---- Normal key combos + Escape --------------------------------------------------

    private void StartKeyboardHook()
    {
        var hook = new SimpleGlobalHook(GlobalHookType.Keyboard);
        hook.KeyPressed += OnKeyPressed;
        hook.KeyReleased += OnKeyReleased;
        _hook = hook;
        _ = Task.Run(async () =>
        {
            try
            {
                await hook.RunAsync();
            }
            catch (Exception ex)
            {
                Log.Error("Keyboard hook stopped (is Accessibility permission granted?)", ex);
            }
        });
    }

    private static KeyModifiers ModifierFor(KeyCode key) => key switch
    {
        KeyCode.VcLeftControl or KeyCode.VcRightControl => KeyModifiers.Control,
        KeyCode.VcLeftAlt or KeyCode.VcRightAlt => KeyModifiers.Option,
        KeyCode.VcLeftShift or KeyCode.VcRightShift => KeyModifiers.Shift,
        KeyCode.VcLeftMeta or KeyCode.VcRightMeta => KeyModifiers.Command,
        _ => KeyModifiers.None,
    };

    private KeyModifiers CurrentModifiers() =>
        _modifiersDown.Aggregate(KeyModifiers.None, (acc, k) => acc | ModifierFor(k));

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        var key = e.Data.KeyCode;
        if (key == KeyCode.VcEscape)
        {
            CancelRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        var spec = _spec;
        if (spec == null || spec.IsGlobe)
        {
            return;
        }

        var target = KeyMap.ToKeyCode(spec.Key!);
        var isModifier = ModifierFor(key) != KeyModifiers.None;

        if (key == target && !_comboActive)
        {
            // Single-modifier shortcuts such as "RightOption" are matched on the key alone.
            var modifiersOk = isModifier ? spec.Modifiers == KeyModifiers.None || CurrentModifiers() == spec.Modifiers
                                         : CurrentModifiers() == spec.Modifiers;
            if (modifiersOk)
            {
                _comboActive = true;
                if (!isModifier) e.SuppressEvent = true; // don't type a space into the app
                Pressed?.Invoke(this, EventArgs.Empty);
            }
        }
        else if (key == target && _comboActive && !isModifier)
        {
            e.SuppressEvent = true; // key auto-repeat while held
        }
        else if (_comboActive && !isModifier && target.IsModifierKey())
        {
            // e.g. Right Option held and then a letter typed: that's an Option-shortcut, not dictation.
            _comboActive = false;
            Interrupted?.Invoke(this, EventArgs.Empty);
        }

        if (isModifier) _modifiersDown.Add(key);
    }

    private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        var key = e.Data.KeyCode;
        _modifiersDown.Remove(key);

        var spec = _spec;
        if (spec == null || spec.IsGlobe || !_comboActive) return;

        var target = KeyMap.ToKeyCode(spec.Key!);
        if (key == target)
        {
            _comboActive = false;
            if (ModifierFor(key) == KeyModifiers.None) e.SuppressEvent = true;
            Released?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose() => Stop();
}

internal static class KeyMap
{
    public static bool IsModifierKey(this KeyCode key) => key is
        KeyCode.VcLeftControl or KeyCode.VcRightControl or KeyCode.VcLeftAlt or KeyCode.VcRightAlt or
        KeyCode.VcLeftShift or KeyCode.VcRightShift or KeyCode.VcLeftMeta or KeyCode.VcRightMeta;

    public static KeyCode ToKeyCode(string key)
    {
        switch (key)
        {
            case "Space": return KeyCode.VcSpace;
            case "Enter": return KeyCode.VcEnter;
            case "Tab": return KeyCode.VcTab;
            case "Escape": return KeyCode.VcEscape;
            case "Backspace": return KeyCode.VcBackspace;
            case "Up": return KeyCode.VcUp;
            case "Down": return KeyCode.VcDown;
            case "Left": return KeyCode.VcLeft;
            case "Right": return KeyCode.VcRight;
            case "RightOption": return KeyCode.VcRightAlt;
            case "RightCommand": return KeyCode.VcRightMeta;
            case "RightControl": return KeyCode.VcRightControl;
            case "RightShift": return KeyCode.VcRightShift;
            case "Minus": return KeyCode.VcMinus;
            case "Equals": return KeyCode.VcEquals;
            case "Comma": return KeyCode.VcComma;
            case "Period": return KeyCode.VcPeriod;
            case "Slash": return KeyCode.VcSlash;
            case "Semicolon": return KeyCode.VcSemicolon;
            case "Quote": return KeyCode.VcQuote;
            case "Backslash": return KeyCode.VcBackslash;
            case "Backquote": return KeyCode.VcBackQuote;
        }
        // A-Z, 0-9 and F1-F24 follow the enum's own naming: VcA, Vc1, VcF5.
        return Enum.TryParse<KeyCode>("Vc" + key, out var code) ? code : KeyCode.VcUndefined;
    }
}
