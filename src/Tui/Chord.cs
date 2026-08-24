namespace ClaudeLauncher.Tui;

/// <summary>
/// One key press, as a thing that can be written down and read back.
///
/// Nothing in the launcher parsed a key from text before this: KeyEncoder runs
/// the other way, turning a press into an escape sequence for a child terminal.
/// So the vocabulary here is deliberately the same set KeyEncoder knows how to
/// send - if a key cannot be named there it is not one worth binding.
///
/// Shift is part of the chord for punctuation, where it changes what the key
/// means ('/' against '?'), and ignored for letters and digits, where the screens
/// have always lowercased the character and would otherwise stop matching a
/// capital.
/// </summary>
public readonly struct Chord : IEquatable<Chord>
{
    public readonly ConsoleKey Key;
    public readonly bool Ctrl;
    public readonly bool Alt;
    public readonly bool Shift;

    public Chord(ConsoleKey key, bool ctrl = false, bool alt = false, bool shift = false)
    {
        Key = key;
        Ctrl = ctrl;
        Alt = alt;
        Shift = shift && !Loose(key);
    }

    public bool None => Key == 0;

    /// <summary>True for keys whose Shift state is not part of their identity.</summary>
    private static bool Loose(ConsoleKey key) =>
        key is >= ConsoleKey.A and <= ConsoleKey.Z ||
        key is >= ConsoleKey.D0 and <= ConsoleKey.D9 ||
        key is >= ConsoleKey.NumPad0 and <= ConsoleKey.NumPad9;

    public static Chord From(ConsoleKeyInfo key) => new(
        key.Key,
        (key.Modifiers & ConsoleModifiers.Control) != 0,
        (key.Modifiers & ConsoleModifiers.Alt) != 0,
        (key.Modifiers & ConsoleModifiers.Shift) != 0);

    public bool Matches(ConsoleKeyInfo key) => Equals(From(key));

    public bool Equals(Chord other) =>
        Key == other.Key && Ctrl == other.Ctrl && Alt == other.Alt && Shift == other.Shift;

    public override bool Equals(object? obj) => obj is Chord other && Equals(other);

    public override int GetHashCode() => HashCode.Combine((int)Key, Ctrl, Alt, Shift);

    public override string ToString() => Describe();

    /// <summary>How it is written in keys.json and shown in the footers.</summary>
    public string Describe()
    {
        if (None) return "unbound";

        var name = Name(Key);
        if (name.Length == 0) return "unbound";

        var text = string.Empty;
        if (Ctrl) text += "ctrl+";
        if (Alt) text += "alt+";
        if (Shift) text += "shift+";

        return text + name;
    }

    /// <summary>The short form the footer bar uses, where every column counts.</summary>
    public string Compact()
    {
        if (None) return "-";

        var name = Name(Key);
        if (name.Length == 0) return "-";

        var text = string.Empty;
        if (Ctrl) text += "^";
        if (Alt) text += "alt+";
        if (Shift) text += "⇧";

        return text + Short(name);
    }

    private static string Short(string name) => name switch
    {
        "enter" => "↵",
        "escape" => "esc",
        "backspace" => "bksp",
        "left" => "←",
        "right" => "→",
        "up" => "↑",
        "down" => "↓",
        "pageup" => "pgup",
        "pagedown" => "pgdn",
        "space" => "space",
        _ => name
    };

    public static bool TryParse(string? text, out Chord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var ctrl = false;
        var alt = false;
        var shift = false;

        var parts = text.Trim().ToLowerInvariant().Split('+', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        // Everything before the last token is a modifier. A trailing '+' bound on
        // its own would have been split away, so the key is always last.
        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].Trim())
            {
                case "ctrl" or "control" or "^": ctrl = true; break;
                case "alt" or "meta": alt = true; break;
                case "shift": shift = true; break;
                default: return false;
            }
        }

        var key = KeyNamed(parts[^1].Trim());
        if (key == 0) return false;

        chord = new Chord(key, ctrl, alt, shift);
        return true;
    }

    private static ConsoleKey KeyNamed(string name)
    {
        if (name.Length == 1)
        {
            var ch = name[0];
            if (ch is >= 'a' and <= 'z') return ConsoleKey.A + (ch - 'a');
            if (ch is >= '0' and <= '9') return ConsoleKey.D0 + (ch - '0');

            return ch switch
            {
                '/' => ConsoleKey.Oem2,
                '?' => ConsoleKey.Oem2,
                ',' => ConsoleKey.OemComma,
                '.' => ConsoleKey.OemPeriod,
                '-' => ConsoleKey.OemMinus,
                '=' => ConsoleKey.OemPlus,
                ';' => ConsoleKey.Oem1,
                '\'' => ConsoleKey.Oem7,
                '[' => ConsoleKey.Oem4,
                ']' => ConsoleKey.Oem6,
                '\\' => ConsoleKey.Oem5,
                '`' => ConsoleKey.Oem3,
                _ => 0
            };
        }

        if (name.Length > 1 && name[0] == 'f' && int.TryParse(name[1..], out var number) &&
            number is >= 1 and <= 12)
        {
            return ConsoleKey.F1 + (number - 1);
        }

        return name switch
        {
            "enter" or "return" => ConsoleKey.Enter,
            "escape" or "esc" => ConsoleKey.Escape,
            "space" or "spacebar" => ConsoleKey.Spacebar,
            "tab" => ConsoleKey.Tab,
            "backspace" or "bksp" => ConsoleKey.Backspace,
            "delete" or "del" => ConsoleKey.Delete,
            "insert" or "ins" => ConsoleKey.Insert,
            "home" => ConsoleKey.Home,
            "end" => ConsoleKey.End,
            "pageup" or "pgup" => ConsoleKey.PageUp,
            "pagedown" or "pgdn" => ConsoleKey.PageDown,
            "left" => ConsoleKey.LeftArrow,
            "right" => ConsoleKey.RightArrow,
            "up" => ConsoleKey.UpArrow,
            "down" => ConsoleKey.DownArrow,
            _ => 0
        };
    }

    private static string Name(ConsoleKey key)
    {
        if (key is >= ConsoleKey.A and <= ConsoleKey.Z)
            return ((char)('a' + (key - ConsoleKey.A))).ToString();

        if (key is >= ConsoleKey.D0 and <= ConsoleKey.D9)
            return ((char)('0' + (key - ConsoleKey.D0))).ToString();

        if (key is >= ConsoleKey.F1 and <= ConsoleKey.F12)
            return "f" + (key - ConsoleKey.F1 + 1);

        return key switch
        {
            ConsoleKey.Oem2 => "/",
            ConsoleKey.OemComma => ",",
            ConsoleKey.OemPeriod => ".",
            ConsoleKey.OemMinus => "-",
            ConsoleKey.OemPlus => "=",
            ConsoleKey.Oem1 => ";",
            ConsoleKey.Oem7 => "'",
            ConsoleKey.Oem4 => "[",
            ConsoleKey.Oem6 => "]",
            ConsoleKey.Oem5 => "\\",
            ConsoleKey.Oem3 => "`",
            ConsoleKey.Enter => "enter",
            ConsoleKey.Escape => "escape",
            ConsoleKey.Spacebar => "space",
            ConsoleKey.Tab => "tab",
            ConsoleKey.Backspace => "backspace",
            ConsoleKey.Delete => "delete",
            ConsoleKey.Insert => "insert",
            ConsoleKey.Home => "home",
            ConsoleKey.End => "end",
            ConsoleKey.PageUp => "pageup",
            ConsoleKey.PageDown => "pagedown",
            ConsoleKey.LeftArrow => "left",
            ConsoleKey.RightArrow => "right",
            ConsoleKey.UpArrow => "up",
            ConsoleKey.DownArrow => "down",
            _ => string.Empty
        };
    }
}
