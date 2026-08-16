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

    private string _filter = string.Empty;
    private bool _filtering;
    private bool _adding;
    private string _draft = string.Empty;
    private int _index;
    private int _scroll;
    private string? _notice;

    public NewTerminalScreen(App app) : base(app)
    {
        _projects = app.State.Projects.ToList();
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

        var available = Math.Max(6, buffer.Height - 4 - y - (_adding ? 3 : 0));
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

        if (_adding && y + 3 <= buffer.Height - 4)
        {
            Widgets.TitledBox(buffer, margin, y, width, 3, "Folder path", Theme.VioletSoft);
            var x = buffer.Write(margin + 3, y + 1, _draft, new Sty(Theme.Text, Theme.Panel));
            buffer.Write(x, y + 1, "▏", new Sty(Theme.Blue, Theme.Panel, bold: true));
        }

        if (_notice is not null)
            buffer.WriteClipped(margin + 1, buffer.Height - 5, _notice, width - 2, new Sty(Theme.Amber, Theme.Bg));

        Widgets.Footer(buffer, _adding
            ? new[]
            {
                new KeyHint("type", "Folder path"),
                new KeyHint("↵", "Add"),
                new KeyHint("esc", "Cancel")
            }
            : new[]
            {
                new KeyHint("↑↓", "Navigate"),
                new KeyHint("↵", "Open terminal"),
                new KeyHint("a", "Add folder"),
                new KeyHint("/", "Filter"),
                new KeyHint("esc", "Back")
            });
    }

    public override ScreenAction HandleKey(ConsoleKeyInfo key)
    {
        if (_adding) return Adding(key);
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
                _adding = true;
                _draft = string.Empty;
                _notice = null;
                return ScreenAction.None;
            case 'd':
                return Forget(items);
        }

        return ScreenAction.None;
    }

    private ScreenAction Adding(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                _adding = false;
                _draft = string.Empty;
                return ScreenAction.None;
            case ConsoleKey.Backspace:
                if (_draft.Length > 0) _draft = _draft.Substring(0, _draft.Length - 1);
                return ScreenAction.None;
            case ConsoleKey.Enter:
                Add();
                return ScreenAction.None;
        }

        if (!char.IsControl(key.KeyChar)) _draft += key.KeyChar;
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

    private void Add()
    {
        var path = _draft.Trim().Trim('"');
        if (path.Length == 0) return;

        path = Environment.ExpandEnvironmentVariables(path);
        if (path.StartsWith("~", StringComparison.Ordinal))
            path = Path.Combine(StateStore.Home, path.TrimStart('~', '\\', '/'));

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception)
        {
            _notice = "that is not a usable path";
            return;
        }

        // A folder that does not exist would start Claude in nothing, so it is
        // refused here rather than failing later with a pty error.
        if (!Directory.Exists(full))
        {
            _notice = "no such folder: " + full;
            return;
        }

        var entry = new ProjectEntry { Name = new DirectoryInfo(full).Name, Path = full };

        if (!StateStore.AddProject(entry))
        {
            _notice = "already on the list";
            _adding = false;
            return;
        }

        App.State.Projects.Add(entry);
        _projects.Add(entry);
        _adding = false;
        _draft = string.Empty;
        _filter = string.Empty;
        _index = _projects.Count - 1;
        _notice = $"added {entry.Name}";
    }

    /// <summary>Removes a project the launcher added; the wrapper's own are left alone.</summary>
    private ScreenAction Forget(List<ProjectEntry> items)
    {
        if (items.Count == 0) return ScreenAction.None;

        var project = items[_index];
        if (!StateStore.RemoveAddedProject(project.Path))
        {
            _notice = "that one comes from the wrapper - edit QuickPaths to change it";
            return ScreenAction.None;
        }

        _projects.RemoveAll(p => string.Equals(p.Path, project.Path, StringComparison.OrdinalIgnoreCase));
        App.State.Projects.RemoveAll(p => string.Equals(p.Path, project.Path, StringComparison.OrdinalIgnoreCase));
        _notice = $"removed {project.Name}";
        return ScreenAction.None;
    }

    private ScreenAction Open(ProjectEntry project)
    {
        var profile = App.Profile ?? App.State.Profiles[0];

        try
        {
            var tile = TerminalTile.Start(project.Path, project.Name,
                StateStore.ExpandHome(profile.ConfigDir), 100, 30);

            App.Terminals.Add(tile);
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
