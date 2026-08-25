using ClaudeLauncher.Tui;

namespace ClaudeLauncher.Screens;

/// <summary>
/// Rebinding, in the app rather than in a file.
///
/// Pick a command, press the key you want, and it is written to keys.json - only
/// the ones that differ from the defaults, so the file stays short and a default
/// that changes later still reaches anyone who never touched it.
///
/// Capture reserves Esc for cancelling, because a screen that swallowed Esc to
/// bind it would leave no way out. Everything else is fair game, including the
/// keys this screen itself uses.
/// </summary>
public sealed class KeysEditScreen : ScreenBase
{
    private readonly List<KeyBinding> _rows;
    private int _index;
    private bool _capturing;
    private string? _notice;
    private int _scroll;

    public KeysEditScreen(App app) : base(app)
    {
        _rows = KeyBindings.All
            .OrderBy(b => b.Scope)
            .ThenBy(b => b.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public override void Render(ScreenBuffer buffer)
    {
        var y = Widgets.CompactChrome(buffer);
        var margin = Widgets.Margin(buffer);
        var width = buffer.Width - margin * 2;

        Widgets.SectionTitle(buffer, y, "Keys", "Change what a key does");
        y += 2;

        var bottom = buffer.Height - 5;
        var rows = Math.Max(1, bottom - y - 2);

        if (_index < _scroll) _scroll = _index;
        if (_index >= _scroll + rows) _scroll = _index - rows + 1;

        var right = margin + width - 2;
        var lastScope = (KeyScope?)null;

        for (var i = _scroll; i < _rows.Count && i < _scroll + rows; i++)
        {
            var row = _rows[i];
            var here = i == _index;
            var fill = here ? Theme.PanelSelected : Theme.Bg;

            if (here) buffer.Fill(margin, y, width, 1, fill);

            // The scope only appears when it changes, so the list reads as
            // groups without spending a row on a heading for each.
            var scope = row.Scope == lastScope ? string.Empty : Where(row.Scope);
            lastScope = row.Scope;

            buffer.Write(margin + 1, y, here ? "›" : " ", new Sty(Theme.Blue, fill, bold: true));
            buffer.WriteClipped(margin + 3, y, scope, 12, new Sty(Theme.Dim, fill));
            buffer.WriteClipped(margin + 16, y, row.Label, Math.Max(8, width - 40),
                new Sty(here ? Theme.Text : Theme.TextSoft, fill, bold: here));

            var chord = KeyBindings.Of(row.Action);
            var changed = !chord.Equals(row.Default);

            var shown = here && _capturing ? "press a key…" : chord.Compact();

            buffer.WriteRight(right, y, shown, new Sty(
                here && _capturing ? Theme.Amber : changed ? Theme.Green : Theme.TextSoft,
                fill, bold: here || changed));

            if (changed && !(here && _capturing))
                buffer.WriteRight(right - shown.Length - 1, y, "•", new Sty(Theme.Green, fill));

            y++;
        }

        if (_rows.Count > rows && y < bottom)
        {
            buffer.WriteRight(right, y, $"{_index + 1} of {_rows.Count}", new Sty(Theme.Dim, Theme.Bg));
            y++;
        }

        var clashes = KeyBindings.Clashes();
        var line = _notice ?? (clashes.Count > 0 ? "clash · " + clashes[0] : null);

        if (line is not null && y < bottom)
        {
            buffer.WriteClipped(margin + 1, y, line, width - 2,
                new Sty(clashes.Count > 0 && _notice is null ? Theme.Red : Theme.Amber, Theme.Bg));
        }

        Widgets.Footer(buffer, _capturing
            ? new[] { new KeyHint("any key", "Bind it"), new KeyHint("esc", "Cancel") }
            : new[]
            {
                new KeyHint("↑↓", "Command"),
                new KeyHint("↵", "Rebind"),
                new KeyHint("del", "Unbind"),
                new KeyHint("r", "Default")
            }, _capturing ? null : KeyMap.Help);
    }

    private static string Where(KeyScope scope) => scope switch
    {
        KeyScope.Everywhere => "everywhere",
        KeyScope.Home => "home",
        KeyScope.Wall => "wall",
        KeyScope.Profiles => "profiles",
        KeyScope.Projects => "projects",
        KeyScope.Session => "session",
        KeyScope.Dashboard => "dashboard",
        KeyScope.Usage => "usage",
        KeyScope.Resume => "resume",
        KeyScope.Settings => "settings",
        KeyScope.Update => "update",
        KeyScope.History => "history",
        _ => string.Empty
    };

    public override ScreenAction HandleKey(ConsoleKeyInfo key)
    {
        if (_capturing) return Capture(key);

        _notice = null;

        switch (key.Key)
        {
            case ConsoleKey.Escape:
            case ConsoleKey.Backspace:
                return ScreenAction.Back;

            case ConsoleKey.UpArrow:
                _index = Math.Max(0, _index - 1);
                return ScreenAction.None;

            case ConsoleKey.DownArrow:
                _index = Math.Min(_rows.Count - 1, _index + 1);
                return ScreenAction.None;

            case ConsoleKey.PageUp:
                _index = Math.Max(0, _index - 10);
                return ScreenAction.None;

            case ConsoleKey.PageDown:
                _index = Math.Min(_rows.Count - 1, _index + 10);
                return ScreenAction.None;

            case ConsoleKey.Home:
                _index = 0;
                return ScreenAction.None;

            case ConsoleKey.End:
                _index = _rows.Count - 1;
                return ScreenAction.None;

            case ConsoleKey.Enter:
                _capturing = true;
                _notice = null;
                return ScreenAction.None;

            case ConsoleKey.Delete:
                Bind(default);
                return ScreenAction.None;
        }

        if (KeyBindings.Is(KeyAction.Refresh, key))
        {
            Bind(_rows[_index].Default);
            return ScreenAction.None;
        }

        return ScreenAction.None;
    }

    /// <summary>
    /// One press becomes the new binding. Esc is kept back so cancelling is
    /// always possible; F1 is taken as a binding like anything else, because a
    /// key list you cannot rebind is an odd exception to make.
    /// </summary>
    private ScreenAction Capture(ConsoleKeyInfo key)
    {
        // Holding a modifier is a key press of its own, and it arrives first:
        // Alt+Z is VK_MENU and then Alt+Z. Ending the capture on the first of
        // those made every chord with a modifier impossible to record - the
        // modifier alone has no name, so it read as a key that cannot be bound.
        if (Modifier(key.Key)) return ScreenAction.None;

        if (key.Key == ConsoleKey.Escape)
        {
            _capturing = false;
            _notice = "left as it was";
            return ScreenAction.None;
        }

        var chord = Chord.From(key);
        if (chord.None || chord.Compact() == "-")
        {
            // Still listening: an unnameable key is a reason to try another one,
            // not to send someone back to the list and in again.
            _notice = "that key cannot be written down · try another, or esc";
            return ScreenAction.None;
        }

        _capturing = false;
        Bind(chord);
        return ScreenAction.None;
    }

    /// <summary>
    /// A key that only ever accompanies another. ConsoleKey has no members for
    /// shift, control or alt - the reader casts the virtual key code straight
    /// across - so these are matched by their numbers.
    /// </summary>
    private static bool Modifier(ConsoleKey key) => (int)key is 16 or 17 or 18 ||
        key is ConsoleKey.LeftWindows or ConsoleKey.RightWindows or ConsoleKey.Applications;

    private void Bind(Chord chord)
    {
        var row = _rows[_index];
        KeyBindings.Set(row.Action, chord);
        StateStore.SaveKeys(KeyBindings.Changed());

        _notice = chord.None
            ? $"{row.Label} is unbound"
            : $"{row.Label} is {chord.Compact()}";
    }
}
