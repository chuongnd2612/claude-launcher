using ClaudeLauncher.Tui;

namespace ClaudeLauncher.Screens;

/// <summary>Step 2 - pick a project directory (QuickPaths + current directory).</summary>
public sealed class ProjectScreen : ScreenBase
{
    private readonly List<ProjectEntry> _all;
    private readonly ProjectEditor _editor;
    private string _filter = string.Empty;
    private bool _filtering;
    private int _index;
    private int _scroll;

    public ProjectScreen(App app) : base(app)
    {
        _all = new List<ProjectEntry>(app.State.Projects)
        {
            new ProjectEntry { Name = "Current directory", Path = Environment.CurrentDirectory }
        };

        _editor = new ProjectEditor(app, _all);
    }

    /// <summary>
    /// The last row is this shell's own directory, not a project on any list -
    /// it cannot be forgotten, and a new one must land before it.
    /// </summary>
    private bool IsCurrentDirectory(ProjectEntry project) =>
        ReferenceEquals(project, _all[^1]) && project.Name == "Current directory";

    private List<ProjectEntry> Visible
    {
        get
        {
            if (string.IsNullOrEmpty(_filter)) return _all;
            return _all.Where(p =>
                p.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                p.Path.Contains(_filter, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }

    public override void Render(ScreenBuffer buffer)
    {
        var y = Widgets.Chrome(buffer, 1);
        var margin = Widgets.Margin(buffer);
        var profile = App.Profile!;

        Widgets.SectionTitle(buffer, y, $"{profile.DisplayIcon}  {profile.DisplayLabel}", "Select a project");
        y += 2;

        var width = buffer.Width - margin * 2;
        var items = Visible;

        // Grow with the list, but never past the footer, and never so tall that
        // a handful of projects sit in a mostly empty box.
        var available = Math.Max(6, buffer.Height - 4 - y - _editor.Height);
        var panelHeight = Math.Clamp(items.Count + 4, 8, available);
        buffer.Box(margin, y, width, panelHeight, new Sty(Theme.Border, Theme.Panel), BoxStyle.Rounded, Theme.Panel);

        if (_index >= items.Count) _index = Math.Max(0, items.Count - 1);

        buffer.Write(margin + 2, y, $" Projects · {items.Count} ", new Sty(Theme.Blue, Theme.Panel, bold: true));

        // Filter row
        var filterRow = y + 1;
        var filterStyle = _filtering ? new Sty(Theme.Text, Theme.PanelSelected) : new Sty(Theme.Dim, Theme.Panel);
        buffer.Fill(margin + 1, filterRow, width - 2, 1, _filtering ? Theme.PanelSelected : Theme.Panel);
        var cursorX = buffer.Write(margin + 3, filterRow, "⌕ ", new Sty(_filtering ? Theme.Blue : Theme.Dim, filterStyle.Bg));

        if (_filtering)
        {
            cursorX = buffer.Write(cursorX, filterRow, _filter, filterStyle);
            buffer.Write(cursorX, filterRow, "▏", new Sty(Theme.Blue, filterStyle.Bg, bold: true));
        }
        else if (_filter.Length > 0)
        {
            buffer.Write(cursorX, filterRow, _filter, new Sty(Theme.TextSoft, Theme.Panel));
        }
        else
        {
            buffer.Write(cursorX, filterRow, "press / to filter", new Sty(Theme.Dim, Theme.Panel, italic: true));
        }

        // List viewport
        var listTop = y + 2;
        var listHeight = panelHeight - 3;
        if (_index < _scroll) _scroll = _index;
        if (_index >= _scroll + listHeight) _scroll = _index - listHeight + 1;
        if (_scroll > Math.Max(0, items.Count - listHeight)) _scroll = Math.Max(0, items.Count - listHeight);
        if (_scroll < 0) _scroll = 0;

        if (items.Count == 0)
        {
            buffer.Write(margin + 3, listTop + 1, "No project matches that filter.", new Sty(Theme.Muted, Theme.Panel, italic: true));
        }

        for (var row = 0; row < listHeight; row++)
        {
            var itemIndex = _scroll + row;
            if (itemIndex >= items.Count) break;

            var item = items[itemIndex];
            var selected = itemIndex == _index;
            var rowY = listTop + row;
            var bg = selected ? Theme.PanelSelected : Theme.Panel;

            buffer.Fill(margin + 1, rowY, width - 2, 1, bg);
            buffer.Write(margin + 2, rowY, selected ? "▸" : " ", new Sty(Theme.Blue, bg, bold: true));

            var isCurrent = itemIndex == items.Count - 1 && string.IsNullOrEmpty(_filter);
            var nameStyle = selected
                ? new Sty(Theme.Blue, bg, bold: true)
                : new Sty(isCurrent ? Theme.Muted : Theme.Text, bg);

            var nameWidth = Math.Min(26, Math.Max(10, width / 3));
            buffer.WriteClipped(margin + 4, rowY, item.Name, nameWidth, nameStyle);

            var pathX = margin + 4 + nameWidth + 2;
            buffer.WriteClipped(pathX, rowY, item.Path, width - (pathX - margin) - 3,
                new Sty(selected ? Theme.TextSoft : Theme.Dim, bg));
        }

        DrawScrollbar(buffer, margin + width - 2, listTop, listHeight, items.Count, _scroll);

        if (App.Settings.ShowTips)
        {
            Widgets.Tips(buffer, Math.Max(y + panelHeight + 1, buffer.Height - 4 - 5), new[]
            {
                "a adds a folder as a quick path - it works with cd too, not just here",
                "Filter matches both the project name and its full path",
                "\"Current directory\" launches Claude wherever your shell already is"
            });
        }

        _editor.Render(buffer, margin, y + panelHeight + 1, width);

        if (_editor.Notice is not null)
        {
            buffer.WriteClipped(margin + 1, buffer.Height - 5, _editor.Notice, width - 2,
                new Sty(Theme.Amber, Theme.Bg));
        }

        // The editor owns the keyboard while it is up, so it keeps its own
        // hints and no key list - F1 would be a character in a path.
        if (_editor.Active)
        {
            Widgets.Footer(buffer, _editor.Hints);
            return;
        }

        Widgets.Footer(buffer, _filtering
            ? new[]
            {
                new KeyHint("type", "Filter"),
                new KeyHint("↑↓", "Navigate"),
                new KeyHint("↵", "Apply"),
                new KeyHint("esc", "Clear")
            }
            : KeyMap.ProjectFooter(), KeyMap.Help);
    }

    private static void DrawScrollbar(ScreenBuffer buffer, int x, int top, int height, int count, int scroll)
    {
        if (count <= height || height <= 2) return;

        for (var row = 0; row < height; row++)
            buffer.Set(x, top + row, '│', new Sty(Theme.BorderMuted, buffer.BgAt(x, top + row)));

        var thumb = Math.Clamp(height * height / count, 1, height);
        var maxScroll = Math.Max(1, count - height);
        var offset = (int)Math.Round((double)scroll / maxScroll * (height - thumb));

        for (var row = 0; row < thumb; row++)
            buffer.Set(x, top + offset + row, '█', new Sty(Theme.Blue, buffer.BgAt(x, top + offset + row)));
    }

    public override ScreenAction HandleKey(ConsoleKeyInfo key)
    {
        if (_editor.HandleKey(key))
        {
            if (_editor.Select is { } picked) _index = Math.Min(picked, Visible.Count - 1);
            return ScreenAction.None;
        }

        var items = Visible;

        if (_filtering)
        {
            switch (key.Key)
            {
                case ConsoleKey.F1:
                    return ScreenAction.Push(new KeysScreen(App, "Projects", KeyMap.Project()));
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
                    Move(-1, items.Count);
                    return ScreenAction.None;
                case ConsoleKey.DownArrow:
                    Move(1, items.Count);
                    return ScreenAction.None;
            }

            if (!char.IsControl(key.KeyChar))
            {
                _filter += key.KeyChar;
                _index = 0;
            }

            return ScreenAction.None;
        }

        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                Move(-1, items.Count);
                return ScreenAction.None;
            case ConsoleKey.DownArrow:
                Move(1, items.Count);
                return ScreenAction.None;
            case ConsoleKey.F1:
                return ScreenAction.Push(new KeysScreen(App, "Projects", KeyMap.Project()));
            case ConsoleKey.PageUp:
                Move(-8, items.Count);
                return ScreenAction.None;
            case ConsoleKey.PageDown:
                Move(8, items.Count);
                return ScreenAction.None;
            case ConsoleKey.Home:
                _index = 0;
                return ScreenAction.None;
            case ConsoleKey.End:
                _index = Math.Max(0, items.Count - 1);
                return ScreenAction.None;
            case ConsoleKey.Enter:
                if (items.Count == 0) return ScreenAction.None;
                App.Project = items[_index];
                return ScreenAction.Push(new SessionScreen(App));
            case ConsoleKey.Escape:
            case ConsoleKey.Backspace:
                return ScreenAction.Back;
        }

        var ch = char.ToLowerInvariant(key.KeyChar);
        if (ch == '/') { _filtering = true; return ScreenAction.None; }
        if (ch == 'q') return ScreenAction.Exit;

        if (ch == 'a')
        {
            _editor.Begin();
            return ScreenAction.None;
        }

        if (ch == 'd')
        {
            var listed = Visible;
            if (listed.Count > 0 && !IsCurrentDirectory(listed[_index])) _editor.Forget(listed[_index]);
            return ScreenAction.None;
        }

        return ScreenAction.None;
    }

    private void Move(int delta, int count)
    {
        if (count == 0) return;
        _index = Math.Clamp(_index + delta, 0, count - 1);
    }
}
