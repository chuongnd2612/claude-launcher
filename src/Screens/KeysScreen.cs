using ClaudeLauncher.Tui;

namespace ClaudeLauncher.Screens;

/// <summary>
/// Every key the screen underneath answers to, grouped and scrollable.
///
/// A pushed screen rather than a true overlay: the launcher has no modal layer,
/// and Esc popping the stack is already what closing means everywhere else. It
/// exists because the footer can only honestly carry four or five hints, and the
/// wall alone has around twenty - the rest used to be undiscoverable.
/// </summary>
public sealed class KeysScreen : ScreenBase
{
    private readonly string _context;
    private readonly KeyGroup[] _groups;
    private int _scroll;

    public KeysScreen(App app, string context, KeyGroup[] groups) : base(app)
    {
        _context = context;
        _groups = groups;
    }

    /// <summary>
    /// The lines to draw: a group title, then its hints, then a blank. Flattened
    /// up front so scrolling and column-splitting both work on one list rather
    /// than re-deriving the layout.
    /// </summary>
    private List<(string? Title, KeyHint? Hint)> Lines()
    {
        var lines = new List<(string?, KeyHint?)>();

        foreach (var group in _groups)
        {
            if (lines.Count > 0) lines.Add((null, null));
            lines.Add((group.Title, null));

            foreach (var hint in group.Hints) lines.Add((null, hint));
        }

        return lines;
    }

    /// <summary>The widest key column, so the labels line up down the page.</summary>
    private int KeyWidth()
    {
        var widest = 0;

        foreach (var group in _groups)
        foreach (var hint in group.Hints)
        {
            if (hint.Key.Length > widest) widest = hint.Key.Length;
        }

        return Math.Min(widest, 18);
    }

    public override void Render(ScreenBuffer buffer)
    {
        var y = Widgets.CompactChrome(buffer);
        var margin = Widgets.Margin(buffer);
        var width = buffer.Width - margin * 2;

        Widgets.SectionTitle(buffer, y, "Keys", _context);
        y += 2;

        var lines = Lines();
        var keyWidth = KeyWidth();

        // Two columns once there is room for both, the same threshold the
        // dashboard uses - a key list is unreadable stretched across 200 columns.
        var columns = width >= 108 ? 2 : 1;
        var columnWidth = (width - (columns - 1) * 4) / columns;

        var bottom = buffer.Height - 5;
        var rows = Math.Max(1, bottom - y);
        var capacity = rows * columns;

        _scroll = Math.Max(0, Math.Min(_scroll, Math.Max(0, lines.Count - capacity)));

        var shown = lines.Skip(_scroll).Take(capacity).ToList();

        for (var i = 0; i < shown.Count; i++)
        {
            var column = i / rows;
            var x = margin + column * (columnWidth + 4);
            var row = y + i % rows;
            var (title, hint) = shown[i];

            if (title is not null)
            {
                buffer.WriteClipped(x, row, title.ToUpperInvariant(), columnWidth,
                    new Sty(Theme.Amber, Theme.Bg, bold: true));

                continue;
            }

            if (hint is not { } key) continue;

            // A hint with no key is a plain note, so it gets the whole width.
            if (key.Key.Length == 0)
            {
                buffer.WriteClipped(x + 2, row, key.Label, columnWidth - 2,
                    new Sty(Theme.Dim, Theme.Bg, italic: true));

                continue;
            }

            buffer.WriteClipped(x + 2, row, key.Key, keyWidth,
                new Sty(Theme.Blue, Theme.Bg, bold: true));

            buffer.WriteClipped(x + 2 + keyWidth + 2, row, key.Label,
                Math.Max(0, columnWidth - keyWidth - 4), new Sty(Theme.Muted, Theme.Bg));
        }

        var more = lines.Count > capacity;
        if (more)
        {
            var at = Math.Min(lines.Count, _scroll + capacity);
            buffer.WriteRight(margin + width - 1, buffer.Height - 5,
                $"{at} of {lines.Count}", new Sty(Theme.Dim, Theme.Bg));
        }

        Widgets.Footer(buffer, more
            ? new[] { new KeyHint("↑↓", "Scroll"), new KeyHint("pgup/pgdn", "Page"), new KeyHint("esc", "Close") }
            : new[] { new KeyHint("esc", "Close") });
    }

    public override ScreenAction HandleKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
            case ConsoleKey.Backspace:
            case ConsoleKey.F1:
                return ScreenAction.Back;

            case ConsoleKey.UpArrow:
                _scroll = Math.Max(0, _scroll - 1);
                return ScreenAction.None;

            case ConsoleKey.DownArrow:
                _scroll++;
                return ScreenAction.None;

            case ConsoleKey.PageUp:
                _scroll = Math.Max(0, _scroll - 10);
                return ScreenAction.None;

            case ConsoleKey.PageDown:
                _scroll += 10;
                return ScreenAction.None;

            case ConsoleKey.Home:
                _scroll = 0;
                return ScreenAction.None;
        }

        if (KeyBindings.Is(KeyAction.EditKeys, key))
            return ScreenAction.Push(new KeysEditScreen(App));

        if (KeyBindings.Is(KeyAction.Quit, key)) return ScreenAction.Back;

        return ScreenAction.None;
    }
}
