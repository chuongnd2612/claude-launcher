using ClaudeLauncher.Tui;

namespace ClaudeLauncher.Terminal;

/// <summary>
/// Receives the decoded actions a VT stream asks for. The parser holds no
/// screen state and the screen does no parsing, so each can be tested alone
/// against a recorded capture.
/// </summary>
public interface IVtSink
{
    /// <summary>A printable character, already decoded from UTF-8.</summary>
    void Print(char ch);

    /// <summary>A C0 control: carriage return, line feed, backspace, tab, bell.</summary>
    void Execute(char control);

    /// <summary>
    /// A CSI sequence. <paramref name="parameters"/> holds the numeric
    /// parameters with omitted ones left as -1, so a handler can tell
    /// <c>ESC[;5H</c> from <c>ESC[1;5H</c>.
    /// </summary>
    void Csi(char final, ReadOnlySpan<int> parameters, bool question);

    /// <summary>An OSC string, e.g. 0 for the window title.</summary>
    void Osc(int command, string text);

    /// <summary>A two-character escape such as <c>ESC c</c> or a charset select.</summary>
    void EscapeSequence(char final, char intermediate);
}

[Flags]
public enum CellAttrs
{
    None = 0,
    Bold = 1 << 0,
    Dim = 1 << 1,
    Italic = 1 << 2,
    Underline = 1 << 3,
    Inverse = 1 << 4,

    /// <summary>Set when the cell carries an explicit colour rather than the theme default.</summary>
    HasFg = 1 << 5,
    HasBg = 1 << 6
}

/// <summary>
/// One grid cell. Colour is stored as measured 24-bit RGB; Claude emits
/// truecolor and never indexed colour, so there is no palette to resolve.
/// </summary>
public struct TerminalCell
{
    public char Ch;
    public Rgb Fg;
    public Rgb Bg;
    public CellAttrs Attrs;

    public static TerminalCell Blank => new() { Ch = ' ', Attrs = CellAttrs.None };
}
