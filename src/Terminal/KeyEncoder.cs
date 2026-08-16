namespace ClaudeLauncher.Terminal;

/// <summary>
/// Turns a key press back into the bytes a terminal would send. The launcher
/// reads keys through <see cref="Console.ReadKey"/>, so a tile has to re-encode
/// what the console already decoded.
/// </summary>
public static class KeyEncoder
{
    private static readonly string Esc = ((char)27).ToString();
    private static readonly string Del = ((char)127).ToString();
    private static readonly string Backspace = ((char)8).ToString();

    public static string Encode(ConsoleKeyInfo key)
    {
        var ctrl = (key.Modifiers & ConsoleModifiers.Control) != 0;
        var alt = (key.Modifiers & ConsoleModifiers.Alt) != 0;
        var shift = (key.Modifiers & ConsoleModifiers.Shift) != 0;

        var sequence = Special(key.Key, ctrl, alt, shift);
        if (sequence is not null) return sequence;

        if (ctrl && key.KeyChar == '\0')
        {
            // Console.ReadKey reports Ctrl+letter with a control KeyChar, but
            // Ctrl with a non-letter can arrive bare.
            var letter = key.Key >= ConsoleKey.A && key.Key <= ConsoleKey.Z
                ? (char)(key.Key - ConsoleKey.A + 1)
                : '\0';

            if (letter != '\0') return alt ? Esc + letter : letter.ToString();
        }

        if (key.KeyChar == '\0') return string.Empty;

        return alt ? Esc + key.KeyChar : key.KeyChar.ToString();
    }

    /// <summary>Wraps pasted text so the receiver treats it as data, never as keys.</summary>
    public static string EncodePaste(string text, bool bracketed) =>
        bracketed ? $"{Esc}[200~{text}{Esc}[201~" : text;

    private static string? Special(ConsoleKey key, bool ctrl, bool alt, bool shift)
    {
        // xterm's modifier encoding: 1 + a bit per modifier, as ESC[1;<m><final>.
        var modifier = 1 + (shift ? 1 : 0) + (alt ? 2 : 0) + (ctrl ? 4 : 0);
        var mod = modifier > 1 ? $"1;{modifier}" : string.Empty;

        switch (key)
        {
            case ConsoleKey.UpArrow: return $"{Esc}[{mod}A";
            case ConsoleKey.DownArrow: return $"{Esc}[{mod}B";
            case ConsoleKey.RightArrow: return $"{Esc}[{mod}C";
            case ConsoleKey.LeftArrow: return $"{Esc}[{mod}D";
            case ConsoleKey.Home: return $"{Esc}[{mod}H";
            case ConsoleKey.End: return $"{Esc}[{mod}F";

            case ConsoleKey.Enter: return alt ? Esc + "\r" : "\r";
            case ConsoleKey.Escape: return Esc;

            // Terminals send DEL for backspace; sending BS instead makes the
            // prompt in a full-screen TUI eat the wrong character.
            case ConsoleKey.Backspace: return ctrl ? Backspace : alt ? Esc + Del : Del;

            case ConsoleKey.Tab: return shift ? $"{Esc}[Z" : "\t";

            case ConsoleKey.Delete: return Tilde(3, modifier);
            case ConsoleKey.Insert: return Tilde(2, modifier);
            case ConsoleKey.PageUp: return Tilde(5, modifier);
            case ConsoleKey.PageDown: return Tilde(6, modifier);

            case ConsoleKey.F1: return Ss3('P', modifier);
            case ConsoleKey.F2: return Ss3('Q', modifier);
            case ConsoleKey.F3: return Ss3('R', modifier);
            case ConsoleKey.F4: return Ss3('S', modifier);
            case ConsoleKey.F5: return Tilde(15, modifier);
            case ConsoleKey.F6: return Tilde(17, modifier);
            case ConsoleKey.F7: return Tilde(18, modifier);
            case ConsoleKey.F8: return Tilde(19, modifier);
            case ConsoleKey.F9: return Tilde(20, modifier);
            case ConsoleKey.F10: return Tilde(21, modifier);
            case ConsoleKey.F11: return Tilde(23, modifier);
            case ConsoleKey.F12: return Tilde(24, modifier);

            default: return null;
        }
    }

    private static string Tilde(int code, int modifier) =>
        modifier > 1 ? $"{Esc}[{code};{modifier}~" : $"{Esc}[{code}~";

    private static string Ss3(char final, int modifier) =>
        modifier > 1 ? $"{Esc}[1;{modifier}{final}" : $"{Esc}O{final}";
}
