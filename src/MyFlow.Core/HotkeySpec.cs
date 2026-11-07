namespace MyFlow.Core;

[Flags]
public enum KeyModifiers
{
    None = 0,
    Control = 1,
    Option = 2,   // Alt on Windows
    Shift = 4,
    Command = 8,  // Windows key on Windows
}

/// <summary>
/// A parsed shortcut. Either the special Globe/Fn key, a single modifier key on its own
/// (e.g. "RightOption"), or modifiers plus a main key (e.g. "Ctrl+Option+Space").
/// </summary>
public sealed record HotkeySpec(bool IsGlobe, KeyModifiers Modifiers, string? Key)
{
    public static readonly HotkeySpec Globe = new(true, KeyModifiers.None, null);

    /// <summary>Names accepted for the main key (case-insensitive), besides A-Z, 0-9 and F1-F24.</summary>
    public static readonly IReadOnlySet<string> NamedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Space", "Enter", "Tab", "Escape", "Backspace", "Up", "Down", "Left", "Right",
        "RightOption", "RightCommand", "RightControl", "RightShift",
        "Minus", "Equals", "Comma", "Period", "Slash", "Semicolon", "Quote", "Backslash", "Backquote",
    };

    public static bool TryParse(string? text, out HotkeySpec spec, out string? error)
    {
        spec = Globe;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Shortcut is empty.";
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.Equals("Globe", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Fn", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var mods = KeyModifiers.None;
        string? key = null;
        foreach (var raw in trimmed.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var part = raw.ToLowerInvariant();
            switch (part)
            {
                case "ctrl" or "control" or "⌃":
                    mods |= KeyModifiers.Control; break;
                case "alt" or "option" or "opt" or "⌥":
                    mods |= KeyModifiers.Option; break;
                case "shift" or "⇧":
                    mods |= KeyModifiers.Shift; break;
                case "cmd" or "command" or "win" or "super" or "meta" or "⌘":
                    mods |= KeyModifiers.Command; break;
                default:
                    if (key != null)
                    {
                        error = $"Only one main key is allowed (got '{key}' and '{raw}').";
                        return false;
                    }
                    if (!IsValidKey(raw))
                    {
                        error = $"Unknown key '{raw}'.";
                        return false;
                    }
                    key = Normalize(raw);
                    break;
            }
        }

        if (key == null)
        {
            error = "Add a main key, e.g. Ctrl+Option+Space.";
            return false;
        }

        // A bare letter/number with no modifier would fire while typing normally.
        if (mods == KeyModifiers.None && key.Length == 1)
        {
            error = "A single letter or number needs at least one modifier.";
            return false;
        }

        spec = new HotkeySpec(false, mods, key);
        return true;
    }

    public static HotkeySpec Parse(string text) =>
        TryParse(text, out var spec, out var error) ? spec : throw new FormatException(error);

    private static bool IsValidKey(string key)
    {
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0])) return true;
        if (NamedKeys.Contains(key)) return true;
        if ((key.StartsWith('F') || key.StartsWith('f')) && int.TryParse(key[1..], out var n) && n is >= 1 and <= 24)
            return true;
        return false;
    }

    private static string Normalize(string key)
    {
        if (key.Length == 1) return key.ToUpperInvariant();
        if (key[0] is 'f' or 'F' && int.TryParse(key[1..], out _)) return "F" + key[1..];
        return NamedKeys.First(k => k.Equals(key, StringComparison.OrdinalIgnoreCase));
    }

    public override string ToString()
    {
        if (IsGlobe) return "Globe";
        var parts = new List<string>();
        if (Modifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(KeyModifiers.Option)) parts.Add("Option");
        if (Modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(KeyModifiers.Command)) parts.Add("Cmd");
        parts.Add(Key!);
        return string.Join("+", parts);
    }
}
