namespace ClaudeLauncher.Tui;

/// <summary>
/// The cells a click can land on in the frame now showing, and what each does.
///
/// Rebuilt with every frame rather than kept by the screens: what is under the
/// pointer is whatever was last painted there, and only the drawing code knows
/// where that was. Most regions just press a key, so a click goes down the same
/// path the keyboard does and no screen grows a second way to do anything.
/// </summary>
public static class Clicks
{
    private readonly record struct Region(int X, int Y, int W, int H, Func<ScreenAction> Act);

    private static readonly List<Region> Regions = new();

    /// <summary>The screen the regions route their keys to, set as the frame starts.</summary>
    private static ScreenBase? _screen;

    public static void Begin(ScreenBase screen)
    {
        Regions.Clear();
        _screen = screen;
    }

    public static void Add(int x, int y, int w, int h, Func<ScreenAction> act)
    {
        if (w > 0 && h > 0) Regions.Add(new Region(x, y, w, h, act));
    }

    /// <summary>A region that presses <paramref name="key"/> on the current screen.</summary>
    public static void Key(int x, int y, int w, ConsoleKeyInfo key) =>
        Add(x, y, w, 1, () => _screen?.HandleKey(key) ?? ScreenAction.None);

    /// <summary>
    /// One row of a list: the first click selects it, a click on the row that is
    /// already selected opens it - which is also what a double click comes to.
    /// </summary>
    /// <param name="open">What opening it does, when that is not just Enter.</param>
    public static void Item(int x, int y, int w, int h, bool selected, Action select,
        Func<ScreenAction>? open = null)
    {
        Add(x, y, w, h, () =>
        {
            if (selected) return open?.Invoke() ?? _screen?.HandleKey(Press(ConsoleKey.Enter, '\r')) ?? ScreenAction.None;
            select();
            return ScreenAction.None;
        });
    }

    /// <summary>The last region drawn wins, because it is the one painted on top.</summary>
    public static ScreenAction? Hit(int x, int y)
    {
        var at = IndexAt(x, y);
        return at < 0 ? null : Regions[at].Act();
    }

    private static int IndexAt(int x, int y)
    {
        for (var i = Regions.Count - 1; i >= 0; i--)
        {
            var r = Regions[i];
            if (x >= r.X && x < r.X + r.W && y >= r.Y && y < r.Y + r.H) return i;
        }

        return -1;
    }

    /// <summary>Where the pointer last was, or off the grid before it has moved.</summary>
    private static (int X, int Y) _pointer = (-1, -1);

    /// <summary>
    /// Records where the pointer is. True when that changes what is under it,
    /// which is the only time a hover is worth a repaint - every frame is
    /// written whole, and a sweep across a list reports every cell it crosses.
    /// </summary>
    public static bool MoveTo(int x, int y)
    {
        var before = IndexAt(_pointer.X, _pointer.Y);
        _pointer = (x, y);
        return IndexAt(x, y) != before;
    }

    /// <summary>
    /// Lights whatever the pointer is over, after the screen has drawn. Done
    /// here rather than by each screen, so anything that can be clicked shows
    /// it can be, without any screen knowing about the pointer.
    /// </summary>
    public static void PaintHover(ScreenBuffer buffer)
    {
        var at = IndexAt(_pointer.X, _pointer.Y);
        if (at < 0) return;

        var r = Regions[at];
        buffer.Tint(r.X, r.Y, r.W, r.H, Theme.Hover);
    }

    public static ConsoleKeyInfo Press(ConsoleKey key, char ch = '\0', bool shift = false, bool alt = false,
        bool ctrl = false) => new(ch, key, shift, alt, ctrl);

    /// <summary>The press a command is bound to now, for a button that runs it.</summary>
    public static ConsoleKeyInfo Bound(KeyAction action) => KeyBindings.Of(action).None
        ? default
        : FromHint(KeyBindings.Describe(action)) ?? default;

    /// <summary>
    /// The key a footer hint names, or null when it names none a click could
    /// press: "↑↓", "type", "drag a tile" and digit ranges describe a gesture,
    /// not one key. Of several alternatives ("esc bksp", "v / s") the first wins.
    /// </summary>
    public static ConsoleKeyInfo? FromHint(string hint)
    {
        var text = hint.Trim();

        // "-" is how an unbound command is written, not the minus key.
        if (text.Length == 0 || text == "-") return null;

        var first = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

        // "pgup/pgdn" and "↵/←→" are alternatives run together; "/" and "alt+/"
        // are the key itself.
        var slash = first.IndexOf('/');
        if (slash > 0 && first[slash - 1] != '+') first = first[..slash];

        var ctrl = false;
        var alt = false;
        var shift = false;

        while (true)
        {
            if (first.StartsWith('^') && first.Length > 1) { ctrl = true; first = first[1..]; }
            else if (first.StartsWith("alt+", StringComparison.OrdinalIgnoreCase) && first.Length > 4) { alt = true; first = first[4..]; }
            else if (first.StartsWith('⇧') && first.Length > 1) { shift = true; first = first[1..]; }
            else break;
        }

        ConsoleKey key;
        var ch = '\0';

        switch (first.ToLowerInvariant())
        {
            case "↵": key = ConsoleKey.Enter; ch = '\r'; break;
            case "esc": key = ConsoleKey.Escape; ch = '\u001b'; break;
            case "tab": key = ConsoleKey.Tab; ch = '\t'; break;
            case "space": key = ConsoleKey.Spacebar; ch = ' '; break;
            case "bksp": key = ConsoleKey.Backspace; ch = '\b'; break;
            case "del": key = ConsoleKey.Delete; break;
            case "home": key = ConsoleKey.Home; break;
            case "end": key = ConsoleKey.End; break;
            case "pgup": key = ConsoleKey.PageUp; break;
            case "pgdn": key = ConsoleKey.PageDown; break;
            case "←": key = ConsoleKey.LeftArrow; break;
            case "→": key = ConsoleKey.RightArrow; break;
            case "↑": key = ConsoleKey.UpArrow; break;
            case "↓": key = ConsoleKey.DownArrow; break;
            default:
                if (!Chord.TryParse(first, out var chord) || first.Length > 3) return null;
                key = chord.Key;
                if (first.Length == 1) ch = shift ? char.ToUpperInvariant(first[0]) : first[0];
                break;
        }

        // A control chord arrives with the control character, the way the
        // console reports it, so screens that read KeyChar see what a press gives.
        if (ctrl && key is >= ConsoleKey.A and <= ConsoleKey.Z) ch = (char)(key - ConsoleKey.A + 1);
        if (alt && ch != '\0' && !char.IsLetterOrDigit(ch)) ch = '\0';

        return new ConsoleKeyInfo(ch, key, shift, alt, ctrl);
    }
}
