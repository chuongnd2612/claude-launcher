using ClaudeLauncher.Sessions;
using ClaudeLauncher.Terminal;
using ClaudeLauncher.Tui;

namespace ClaudeLauncher.Screens;

/// <summary>
/// Picks a project and opens a terminal for it, without walking back through
/// the wizard. Reached with `t` from the wall, which is where you are when you
/// realise you want another session.
///
/// A path that is not on the list can be typed in and is remembered, so the
/// wrapper's QuickPaths no longer decide what the launcher can open.
/// </summary>
public sealed class NewTerminalScreen : ScreenBase
{
    private readonly List<ProjectEntry> _projects;

    private readonly ProjectEditor _editor;

    private string _filter = string.Empty;
    private bool _filtering;
    private int _index;
    private int _scroll;
    private string? _notice;

    public NewTerminalScreen(App app) : base(app)
    {
        _projects = app.State.Projects.ToList();
        _editor = new ProjectEditor(app, _projects);
    }

    private List<ProjectEntry> Visible => string.IsNullOrEmpty(_filter)
        ? _projects
        : _projects.Where(p =>
            p.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
            p.Path.Contains(_filter, StringComparison.OrdinalIgnoreCase)).ToList();

    public override void Render(ScreenBuffer buffer)
    {
        var y = Widgets.CompactChrome(buffer);
        var margin = Widgets.Margin(buffer);
        var width = buffer.Width - margin * 2;
        var items = Visible;

        Widgets.SectionTitle(buffer, y, "Terminals", "New terminal · pick a project");
        y += 2;

        if (_index >= items.Count) _index = Math.Max(0, items.Count - 1);

        var available = Math.Max(6, buffer.Height - 4 - y - _editor.Height);
        var panelHeight = Math.Clamp(items.Count + 4, 8, available);

        buffer.Box(margin, y, width, panelHeight, new Sty(Theme.Border, Theme.Panel), BoxStyle.Rounded, Theme.Panel);
        buffer.Write(margin + 2, y, $" Projects · {_projects.Count} ", new Sty(Theme.Blue, Theme.Panel, bold: true));

        var filterRow = y + 1;
        var filterBg = _filtering ? Theme.PanelSelected : Theme.Panel;
        buffer.Fill(margin + 1, filterRow, width - 2, 1, filterBg);
        var cursorX = buffer.Write(margin + 3, filterRow, "⌕ ", new Sty(_filtering ? Theme.Blue : Theme.Dim, filterBg));

        if (_filtering)
        {
            cursorX = buffer.Write(cursorX, filterRow, _filter, new Sty(Theme.Text, filterBg));
            buffer.Write(cursorX, filterRow, "▏", new Sty(Theme.Blue, filterBg, bold: true));
        }
        else if (_filter.Length > 0)
        {
            buffer.Write(cursorX, filterRow, _filter, new Sty(Theme.TextSoft, Theme.Panel));
        }
        else
        {
            buffer.Write(cursorX, filterRow, "press / to filter, a to add a folder",
                new Sty(Theme.Dim, Theme.Panel, italic: true));
        }

        var listTop = y + 2;
        var listHeight = panelHeight - 3;

        if (items.Count == 0)
        {
            buffer.Write(margin + 3, listTop + 1,
                _projects.Count == 0 ? "No projects yet. Press a to add one." : "Nothing matches that filter.",
                new Sty(Theme.Muted, Theme.Panel, italic: true));
        }

        if (_index < _scroll) _scroll = _index;
        if (_index >= _scroll + listHeight) _scroll = _index - listHeight + 1;
        if (_scroll > Math.Max(0, items.Count - listHeight)) _scroll = Math.Max(0, items.Count - listHeight);
        if (_scroll < 0) _scroll = 0;

        for (var row = 0; row < listHeight; row++)
        {
            var itemIndex = _scroll + row;
            if (itemIndex >= items.Count) break;

            var project = items[itemIndex];
            var selected = itemIndex == _index;
            var rowY = listTop + row;
            var bg = selected ? Theme.PanelSelected : Theme.Panel;

            buffer.Fill(margin + 1, rowY, width - 2, 1, bg);
            buffer.Write(margin + 2, rowY, selected ? "▸" : " ", new Sty(Theme.Blue, bg, bold: true));
            buffer.WriteClipped(margin + 4, rowY, project.Name, 24,
                new Sty(selected ? Theme.Blue : Theme.Text, bg, bold: selected));
            buffer.WriteClipped(margin + 29, rowY, project.Path, Math.Max(0, width - 34),
                new Sty(Theme.Dim, bg));
        }

        y += panelHeight + 1;

        _editor.Render(buffer, margin, y, width);

        var notice = _editor.Notice ?? _notice;
        if (notice is not null)
            buffer.WriteClipped(margin + 1, buffer.Height - 5, notice, width - 2, new Sty(Theme.Amber, Theme.Bg));

        Widgets.Footer(buffer, _editor.Active
            ? _editor.Hints
            : new[]
            {
                new KeyHint("↑↓", "Navigate"),
                new KeyHint("↵", "Open terminal"),
                new KeyHint("a", "Add folder"),
                new KeyHint("d", "Forget"),
                new KeyHint("/", "Filter"),
                new KeyHint("esc", "Back")
            });
    }

    public override ScreenAction HandleKey(ConsoleKeyInfo key)
    {
        if (_editor.HandleKey(key))
        {
            if (_editor.Select is { } picked) _index = picked;
            return ScreenAction.None;
        }

        if (_filtering) return Filtering(key);

        var items = Visible;

        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                Move(-1, items.Count);
                return ScreenAction.None;
            case ConsoleKey.DownArrow:
            case ConsoleKey.Tab:
                Move(1, items.Count);
                return ScreenAction.None;
            case ConsoleKey.PageUp:
                Move(-5, items.Count);
                return ScreenAction.None;
            case ConsoleKey.PageDown:
                Move(5, items.Count);
                return ScreenAction.None;
            case ConsoleKey.Enter:
                return items.Count == 0 ? ScreenAction.None : Open(items[_index]);
            case ConsoleKey.Escape:
                return ScreenAction.Back;
        }

        switch (char.ToLowerInvariant(key.KeyChar))
        {
            case '/':
                _filtering = true;
                return ScreenAction.None;
            case 'a':
                _editor.Begin();
                _notice = null;
                return ScreenAction.None;
            case 'd':
                if (items.Count > 0) _editor.Forget(items[_index]);
                return ScreenAction.None;
        }

        return ScreenAction.None;
    }

    private ScreenAction Filtering(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                _filter = string.Empty;
                _filtering = false;
                _index = 0;
                return ScreenAction.None;
            case ConsoleKey.Enter:
                _filtering = false;
                return ScreenAction.None;
            case ConsoleKey.Backspace:
                if (_filter.Length > 0) _filter = _filter.Substring(0, _filter.Length - 1);
                _index = 0;
                return ScreenAction.None;
            case ConsoleKey.UpArrow:
                Move(-1, Visible.Count);
                return ScreenAction.None;
            case ConsoleKey.DownArrow:
                Move(1, Visible.Count);
                return ScreenAction.None;
        }

        if (!char.IsControl(key.KeyChar))
        {
            _filter += key.KeyChar;
            _index = 0;
        }

        return ScreenAction.None;
    }

    private ScreenAction Open(ProjectEntry project)
    {
        var profile = App.Profile ?? App.State.Profiles[0];

        try
        {
            var tile = TerminalTile.Start(project.Path, project.Name,
                StateStore.ExpandHome(profile.ConfigDir), 100, 30);

            App.AddTerminal(tile);
            return ScreenAction.Root(new TerminalsScreen(App, new SessionService(App.State), tile));
        }
        catch (Exception ex)
        {
            _notice = "could not start a terminal: " + ex.Message;
            return ScreenAction.None;
        }
    }

    private void Move(int delta, int count)
    {
        if (count == 0) return;
        _index = Math.Clamp(_index + delta, 0, count - 1);
    }
}
