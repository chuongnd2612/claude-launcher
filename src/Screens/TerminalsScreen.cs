using ClaudeLauncher.Sessions;
using ClaudeLauncher.Tui;

namespace ClaudeLauncher.Screens;

/// <summary>
/// A wall of every running session, each tile tailing that session's
/// transcript.
///
/// The tiles are a read-only view: the real terminals belong to Windows
/// Terminal, which owns their input. Enter takes you to the real one.
/// </summary>
public sealed class TerminalsScreen : ScreenBase
{
    private enum LayoutMode
    {
        Tiled,
        Stacked,
        Focus
    }

    private const int MinPaneWidth = 24;
    private const int MinPaneHeight = 5;
    private const int GutterX = 2;
    private const int GutterY = 1;

    private readonly SessionService? _service;
    private readonly HashSet<string> _hidden = new(StringComparer.Ordinal);
    private SessionSnapshot _snapshot;
    private LayoutMode _mode;
    private int _focus;
    private bool _zoom;
    private string? _notice;

    public TerminalsScreen(App app, SessionService service) : base(app)
    {
        _service = service;
        _service.WithEntries = true;
        _snapshot = service.Build();
        _mode = Parse(app.Settings.TerminalLayout);
    }

    /// <summary>Fixture constructor for --selftest.</summary>
    public TerminalsScreen(App app, SessionSnapshot snapshot) : base(app)
    {
        _service = null;
        _snapshot = snapshot;
    }

    public override TimeSpan? RefreshInterval => _service is null ? null : TimeSpan.FromMilliseconds(500);

    public override bool NeedsRedraw()
    {
        if (_service is null) return false;
        _snapshot = _service.Build();
        return true;
    }

    private List<SessionRow> Panes =>
        _snapshot.Sessions.Where(s => !_hidden.Contains(s.SessionId)).ToList();

    public override void Render(ScreenBuffer buffer)
    {
        var y = Widgets.CompactChrome(buffer);
        var margin = Widgets.Margin(buffer);
        var width = buffer.Width - margin * 2;
        var panes = Panes;

        if (_focus >= panes.Count) _focus = Math.Max(0, panes.Count - 1);

        // Breadcrumb, with the right-hand status shortened rather than wrapped.
        var left = panes.Count == 1 ? "Terminals · 1 pane" : $"Terminals · {panes.Count} panes";
        Widgets.SectionTitle(buffer, y, "Home", left);

        var right = $"layout {_mode.ToString().ToLowerInvariant()} · space to cycle";
        if (right.Length + left.Length + 20 > width) right = _mode.ToString().ToLowerInvariant();
        buffer.WriteRight(margin + width - 1, y, right, new Sty(Theme.Dim, Theme.Bg));

        y += 2;

        if (panes.Count == 0)
        {
            buffer.WriteClipped(margin + 1, y, "Nothing is running. Press esc to go back, n to start a session.",
                width - 2, new Sty(Theme.Muted, Theme.Bg, italic: true));
            Footer(buffer, 0);
            return;
        }

        // The strip is the only way to see panes that are off screen, so it
        // survives on short windows; the tips box is what goes first.
        var compactStrip = buffer.Height < 30;
        y = Strip(buffer, y, panes, compactStrip);

        var wantTips = App.Settings.ShowTips && buffer.Height >= 40;
        var bottom = buffer.Height - 4 - (wantTips ? 6 : 0);
        var gridHeight = bottom - y;

        if (gridHeight < 3)
        {
            buffer.WriteClipped(margin + 1, y, "The window is too short for the terminal wall.",
                width - 2, new Sty(Theme.Dim, Theme.Bg, italic: true));
        }
        else
        {
            Grid(buffer, margin, y, width, gridHeight, panes);
            if (wantTips)
            {
                Widgets.Tips(buffer, bottom + 1, new[]
                {
                    "Numbers focus a pane, z zooms it full screen and back",
                    "A pane turns amber when that session may be waiting for you",
                    "These tiles are read-only - press enter to jump to the real terminal"
                });
            }
        }

        if (_notice is not null)
            buffer.WriteClipped(margin + 1, buffer.Height - 5, _notice, width - 2, new Sty(Theme.Amber, Theme.Bg));

        Footer(buffer, panes.Count);
    }

    private void Footer(ScreenBuffer buffer, int count)
    {
        var focus = count > 4 ? "1-9" : "1-4";

        // Widgets.Footer drops hints that would overflow the bar, and the ones
        // it drops are the last - which would be Back. Shorten instead.
        var hints = buffer.Width >= 104
            ? new[]
            {
                new KeyHint(focus, "Focus"),
                new KeyHint("↵", "Attach"),
                new KeyHint("z", "Zoom"),
                new KeyHint("v", "Split right"),
                new KeyHint("s", "Split down"),
                new KeyHint("space", "Layout"),
                new KeyHint("w", "Remove tile"),
                new KeyHint("esc", "Back")
            }
            : new[]
            {
                new KeyHint(focus, "Focus"),
                new KeyHint("↵", "Attach"),
                new KeyHint("z", "Zoom"),
                new KeyHint("v/s", "Split"),
                new KeyHint("space", "Layout"),
                new KeyHint("w", "Remove"),
                new KeyHint("esc", "Back")
            };

        Widgets.Footer(buffer, hints);
    }

    /// <summary>The numbered pane strip, echoing the wizard's step badges.</summary>
    private int Strip(ScreenBuffer buffer, int y, List<SessionRow> panes, bool compact)
    {
        var margin = Widgets.Margin(buffer);

        if (compact)
        {
            var x = margin;
            for (var i = 0; i < panes.Count && x < buffer.Width - margin - 8; i++)
            {
                var active = i == _focus;
                var color = Color(panes[i], active);
                x = buffer.Write(x, y, active ? "●" : "○", new Sty(color, Theme.Bg, bold: active));
                x = buffer.Write(x, y, $" {i + 1} ", new Sty(color, Theme.Bg, bold: active));
                x = buffer.WriteClipped(x, y, panes[i].ProjectName, 14, new Sty(active ? Theme.Text : Theme.Dim, Theme.Bg));
                x = buffer.Write(x, y, "   ", new Sty(Theme.Dim, Theme.Bg));
            }

            return y + 2;
        }

        const int badge = 5;
        var gap = buffer.Width >= 120 ? 10 : 6;
        var cellX = margin;

        for (var i = 0; i < panes.Count; i++)
        {
            if (cellX + badge > buffer.Width - margin) break;

            var active = i == _focus;
            var color = Color(panes[i], active);

            if (active)
            {
                buffer.Box(cellX, y, badge, 3, new Sty(Theme.BlueDeep, Theme.BlueDeep), BoxStyle.Rounded, Theme.BlueDeep);
                buffer.Set(cellX + 2, y + 1, (char)('1' + i), new Sty(Rgb.Hex("#FFFFFF"), Theme.BlueDeep, bold: true));
            }
            else
            {
                buffer.Box(cellX, y, badge, 3, new Sty(color, Theme.Bg));
                buffer.Set(cellX + 2, y + 1, (char)('1' + i), new Sty(color, Theme.Bg, bold: true));
            }

            buffer.WriteClipped(cellX, y + 3, panes[i].ProjectName, badge + gap - 2,
                new Sty(active ? Theme.Text : Theme.Dim, Theme.Bg, bold: active));

            if (i < panes.Count - 1)
                buffer.HLine(cellX + badge + 1, y + 1, gap - 2, '─', new Sty(Theme.BorderMuted, Theme.Bg));

            cellX += badge + gap;
        }

        return y + 5;
    }

    private static Rgb Color(SessionRow row, bool active)
    {
        if (row.State == SessionState.Waiting) return Theme.Amber;
        return active ? Theme.Blue : Theme.Border;
    }

    private void Grid(ScreenBuffer buffer, int x, int y, int width, int height, List<SessionRow> panes)
    {
        if (_zoom || _mode == LayoutMode.Focus && panes.Count == 1)
        {
            Tile(buffer, x, y, width, height, panes[_focus], _focus, true);
            return;
        }

        var (columns, rows) = Shape(panes.Count, width, height);

        if (_mode == LayoutMode.Focus)
        {
            // Focused pane plus a narrow list, which is how more than four fit.
            var sidebar = Math.Clamp(width / 4, 18, 30);
            Tile(buffer, x, y, width - sidebar - GutterX, height, panes[_focus], _focus, true);
            Sidebar(buffer, x + width - sidebar, y, sidebar, height, panes);
            return;
        }

        if (_mode == LayoutMode.Stacked) columns = 1;

        var cellWidth = (width - GutterX * (columns - 1)) / columns;
        var extraWidth = (width - GutterX * (columns - 1)) % columns;
        var cellHeight = (height - GutterY * (rows - 1)) / rows;

        for (var i = 0; i < panes.Count; i++)
        {
            var column = i % columns;
            var row = i / columns;
            if (row >= rows) break;

            var tileX = x + column * (cellWidth + GutterX) + Math.Min(column, extraWidth);
            var tileY = y + row * (cellHeight + GutterY);
            var tileWidth = cellWidth + (column < extraWidth ? 1 : 0);

            // A lone tile on the last row takes the full width.
            var lastRow = row == (panes.Count - 1) / columns;
            if (lastRow && panes.Count % columns == 1 && column == 0 && columns > 1)
                tileWidth = width;

            Tile(buffer, tileX, tileY, tileWidth, cellHeight, panes[i], i, i == _focus);
        }
    }

    private static (int Columns, int Rows) Shape(int count, int width, int height)
    {
        var maxColumns = Math.Max(1, (width + GutterX) / (MinPaneWidth + GutterX));
        var maxRows = Math.Max(1, (height + GutterY) / (MinPaneHeight + GutterY));

        var columns = Math.Clamp((int)Math.Ceiling(Math.Sqrt(count)), 1, maxColumns);
        var rows = Math.Min(maxRows, (count + columns - 1) / columns);
        return (columns, rows);
    }

    private void Sidebar(ScreenBuffer buffer, int x, int y, int width, int height, List<SessionRow> panes)
    {
        Widgets.Panel(buffer, x, y, width, height, false);

        for (var i = 0; i < panes.Count && i < height - 2; i++)
        {
            var row = panes[i];
            var active = i == _focus;
            var rowY = y + 1 + i;

            buffer.Write(x + 2, rowY, active ? "●" : "○",
                new Sty(Color(row, active), Theme.Panel, bold: active));
            buffer.Write(x + 4, rowY, (i + 1).ToString(), new Sty(Theme.Dim, Theme.Panel));
            buffer.WriteClipped(x + 6, rowY, row.ProjectName, width - 8,
                new Sty(active ? Theme.Text : Theme.Muted, Theme.Panel, bold: active));
        }
    }

    private static void Tile(ScreenBuffer buffer, int x, int y, int width, int height,
        SessionRow row, int index, bool focused)
    {
        if (width < 12 || height < 3) return;

        var border = row.State == SessionState.Waiting
            ? Theme.Amber
            : focused ? Theme.Blue : Theme.Border;

        var fill = focused ? Theme.PanelSelected : Theme.Panel;
        buffer.Box(x, y, width, height, new Sty(border, fill), BoxStyle.Rounded, fill);

        // Legends notched into the top border.
        var title = $" {index + 1} · {row.ProjectName} ";
        buffer.WriteClipped(x + 2, y, title, width - 4, new Sty(border, fill, bold: true));

        var state = $" {Format.State(row.State, row.StateAge)} ";
        if (title.Length + state.Length + 6 <= width)
            buffer.WriteRight(x + width - 3, y, state, new Sty(Theme.Dim, fill));

        var inner = width - 4;
        var contentY = y + 1;
        var contentRows = height - 2;
        if (contentRows <= 0 || inner <= 4) return;

        if (!string.IsNullOrEmpty(row.Branch) && contentRows > 3)
        {
            buffer.WriteClipped(x + 2, contentY, row.Branch, inner, new Sty(Theme.Dim, fill, italic: true));
            contentY++;
            contentRows--;
        }

        var lines = Lines(row, inner, contentRows, fill);
        for (var i = 0; i < lines.Count; i++)
            buffer.Write(x + 2, contentY + i, lines[i].Text, lines[i].Style);
    }

    /// <summary>
    /// Builds the visible lines newest first, then reverses - so the cost is the
    /// size of the tile, never the length of the conversation.
    /// </summary>
    private static List<(string Text, Sty Style)> Lines(SessionRow row, int inner, int rows, Rgb fill)
    {
        var lines = new List<(string, Sty)>(rows);

        for (var i = row.Entries.Count - 1; i >= 0 && lines.Count < rows; i--)
        {
            var entry = row.Entries[i];
            var block = new List<(string, Sty)>();

            switch (entry.Kind)
            {
                case EntryKind.UserPrompt:
                    Wrap(block, "› " + entry.Text, inner, new Sty(Theme.Blue, fill), 2);
                    break;
                case EntryKind.AssistantText:
                    Wrap(block, entry.Text, inner, new Sty(Theme.TextSoft, fill), 3);
                    break;
                case EntryKind.Thinking:
                    block.Add(("◆ thinking", new Sty(Theme.Amber, fill)));
                    break;
                default:
                    var text = entry.Target is null ? $"◆ {entry.Text}" : $"◆ {entry.Text} {entry.Target}";
                    block.Add((Clip(text, inner), new Sty(Theme.Muted, fill)));
                    break;
            }

            for (var j = block.Count - 1; j >= 0 && lines.Count < rows; j--) lines.Add(block[j]);
        }

        lines.Reverse();
        return lines;
    }

    private static void Wrap(List<(string, Sty)> into, string text, int width, Sty style, int maxLines)
    {
        var words = text.Split(' ');
        var line = string.Empty;

        foreach (var word in words)
        {
            if (line.Length == 0) line = word;
            else if (line.Length + 1 + word.Length <= width) line += " " + word;
            else
            {
                into.Add((line, style));
                if (into.Count >= maxLines) return;
                line = word;
            }
        }

        if (line.Length > 0 && into.Count < maxLines) into.Add((Clip(line, width), style));
    }

    private static string Clip(string text, int width) =>
        text.Length <= width ? text : text.Substring(0, Math.Max(1, width - 1)) + "…";

    public override ScreenAction HandleKey(ConsoleKeyInfo key)
    {
        _notice = null;
        var panes = Panes;

        switch (key.Key)
        {
            case ConsoleKey.Escape:
                if (_zoom) { _zoom = false; return ScreenAction.None; }
                return ScreenAction.Back;
            case ConsoleKey.LeftArrow:
            case ConsoleKey.UpArrow:
                Move(-1, panes.Count);
                return ScreenAction.None;
            case ConsoleKey.RightArrow:
            case ConsoleKey.DownArrow:
            case ConsoleKey.Tab:
                Move(1, panes.Count);
                return ScreenAction.None;
            case ConsoleKey.Enter:
                return Attach(panes);
            case ConsoleKey.Spacebar:
                Cycle();
                return ScreenAction.None;
        }

        var ch = key.KeyChar;
        if (ch >= '1' && ch <= '9')
        {
            var target = ch - '1';
            if (target < panes.Count) _focus = target;
            return ScreenAction.None;
        }

        switch (char.ToLowerInvariant(ch))
        {
            case 'z':
                if (panes.Count > 0) _zoom = !_zoom;
                return ScreenAction.None;
            case 'w':
                if (panes.Count > 0) _hidden.Add(panes[_focus].SessionId);
                return ScreenAction.None;
            case 'v':
                return Split(panes, vertical: true);
            case 's':
                return Split(panes, vertical: false);
            case 'n':
                return ScreenAction.Push(new ProfileScreen(App));
            case 'q':
                return ScreenAction.Exit;
        }

        return ScreenAction.None;
    }

    private void Cycle()
    {
        _mode = _mode switch
        {
            LayoutMode.Tiled => LayoutMode.Stacked,
            LayoutMode.Stacked => LayoutMode.Focus,
            _ => LayoutMode.Tiled
        };

        App.Settings.TerminalLayout = _mode.ToString().ToLowerInvariant();
        StateStore.SaveSettings(App.Settings);
    }

    private ScreenAction Attach(List<SessionRow> panes)
    {
        if (panes.Count == 0) return ScreenAction.None;

        _notice = TerminalWindow.Raise()
            ? $"Switched to Windows Terminal · look for {panes[_focus].ProjectName}."
            : "Windows Terminal is not running - that session is in another terminal.";

        return ScreenAction.None;
    }

    /// <summary>Starts another Claude in the focused pane's project, beside it.</summary>
    private ScreenAction Split(List<SessionRow> panes, bool vertical)
    {
        if (panes.Count == 0) return ScreenAction.None;

        var row = panes[_focus];
        var profile = App.State.Profiles.FirstOrDefault(p =>
            string.Equals(p.DisplayLabel, row.ProfileName, StringComparison.OrdinalIgnoreCase))
            ?? App.State.Profiles[0];

        _notice = PaneLauncher.Split(profile, row.ProjectPath, vertical, App.Settings.RemoteControl, out var error)
            ? $"Starting Claude in {row.ProjectName}, {(vertical ? "split right" : "split down")}."
            : "Could not open a pane: " + error;

        return ScreenAction.None;
    }

    private void Move(int delta, int count)
    {
        if (count == 0) return;
        _focus = Math.Clamp(_focus + delta, 0, count - 1);
    }

    private static LayoutMode Parse(string? value) => value?.ToLowerInvariant() switch
    {
        "stacked" => LayoutMode.Stacked,
        "focus" => LayoutMode.Focus,
        _ => LayoutMode.Tiled
    };
}
