using ClaudeLauncher.Sessions;
using ClaudeLauncher.Terminal;
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

    /// <summary>What has been typed into each chat tile, kept per session.</summary>
    private readonly Dictionary<string, string> _drafts = new(StringComparer.OrdinalIgnoreCase);

    private int _menuIndex;

    private string DraftKey(SessionRow row) =>
        string.IsNullOrEmpty(row.SessionId) ? row.ProjectPath : row.SessionId;

    private string Draft(SessionRow row) =>
        _drafts.TryGetValue(DraftKey(row), out var text) ? text : string.Empty;

    private void SetDraft(SessionRow row, string text) => _drafts[DraftKey(row)] = text;

    /// <summary>Commands matching the draft in the focused chat, for the inline menu.</summary>
    private List<SlashCommand> Matches(StreamSession live, string draft)
    {
        if (!draft.StartsWith('/')) return new List<SlashCommand>();

        var typed = draft.Substring(1);
        if (typed.Contains(' ')) return new List<SlashCommand>();

        return live.Commands
            .Where(c => c.Name.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
            .Take(20)
            .ToList();
    }

    /// <summary>
    /// Values offered once a command is chosen, for commands that document
    /// them - so "/effort " picks from low, medium, high rather than being typed.
    /// </summary>
    private List<string> ValueMatches(StreamSession live, string draft)
    {
        if (!draft.StartsWith('/')) return new List<string>();

        var space = draft.IndexOf(' ');
        if (space < 0) return new List<string>();

        var name = draft.Substring(1, space - 1);
        var command = live.Commands.FirstOrDefault(c =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

        if (command is null || command.Options.Count == 0) return new List<string>();

        var typed = draft.Substring(space + 1);
        if (typed.Contains(' ')) return new List<string>();

        return command.Options
            .Where(o => o.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public TerminalsScreen(App app, SessionService service) : base(app)
    {
        _service = service;
        _service.WithEntries = true;
        // Sessions this launcher started are always ours to show, whatever
        // Claude records them as.
        _service.OwnedSessionIds = () => App.Chats
            .Where(c => c.SessionId is not null)
            .Select(c => c.SessionId!)
            .Concat(App.Terminals.Select(t => t.SessionId))
            .Where(id => !string.IsNullOrEmpty(id))
            .ToArray();

        _snapshot = service.Build();
        _mode = Parse(app.Settings.TerminalLayout);

        // Open on a tile that can be acted on. A read-only terminal tile is a
        // poor landing spot now that chat tiles accept typing.
        var panes = Panes;
        var firstChat = panes.FindIndex(p => Live(p) is not null);
        if (firstChat >= 0) _focus = firstChat;
    }

    /// <summary>
    /// True while a focused terminal tile has given the keyboard back, so wall
    /// commands work again. A terminal needs Esc, Tab and the arrows for itself,
    /// so unlike a chat tile it cannot share them.
    /// </summary>
    private bool _released;

    /// <summary>
    /// The terminal tile behind a row, matched only by session id. Falling back
    /// to the project path would put one terminal in every pane of a project
    /// that has several sessions, with each pane resizing the same pty.
    /// </summary>
    private TerminalTile? LiveTerminal(SessionRow row) =>
        string.IsNullOrEmpty(row.SessionId)
            ? null
            : App.Terminals.FirstOrDefault(t => !t.HasExited && t.SessionId == row.SessionId);

    /// <summary>The live session behind a tile, when the launcher owns it.</summary>
    private StreamSession? Live(SessionRow row) => App.Chats.FirstOrDefault(c =>
        c.State != ChatState.Ended &&
        (c.SessionId is not null && c.SessionId == row.SessionId ||
         string.IsNullOrEmpty(row.SessionId) &&
         string.Equals(c.ProjectPath, row.ProjectPath, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Opens the wall with a freshly started tile focused and ready to type, so
    /// a new session lands among the others rather than on a screen of its own.
    /// </summary>
    public TerminalsScreen(App app, SessionService service, TerminalTile focus) : this(app, service)
    {
        var panes = Panes;
        var index = panes.FindIndex(p => ReferenceEquals(LiveTerminal(p), focus));
        if (index >= 0) _focus = index;
        _released = false;
    }

    /// <summary>Opens the wall focused on one session, by id.</summary>
    public TerminalsScreen(App app, SessionService service, string sessionId) : this(app, service)
    {
        if (string.IsNullOrEmpty(sessionId)) return;

        var index = Panes.FindIndex(p => p.SessionId == sessionId);
        if (index < 0) return;

        _focus = index;
        _released = false;
    }

    /// <summary>Fixture constructor for --selftest.</summary>
    public TerminalsScreen(App app, SessionSnapshot snapshot) : base(app)
    {
        _service = null;
        _snapshot = snapshot;
    }

    // A terminal tile has to keep up with a program drawing itself, not with a
    // transcript on disk, so the wall ticks faster once one is open.
    public override TimeSpan? RefreshInterval =>
        App.Terminals.Count > 0 ? TimeSpan.FromMilliseconds(80)
        : _service is null ? null
        : TimeSpan.FromMilliseconds(500);

    private long _terminalRevision;

    public override bool NeedsRedraw()
    {
        var revision = 0L;
        foreach (var terminal in App.Terminals) revision += terminal.Revision;

        var moved = revision != _terminalRevision;
        _terminalRevision = revision;

        if (_service is null) return moved;

        _snapshot = _service.Build();
        return true;
    }

    /// <summary>
    /// Tile order, fixed as tiles first appear. The underlying list re-sorts as
    /// states change, which would renumber panes under the user's hands while
    /// they type - a pane keeps its number until it goes away.
    /// </summary>
    private readonly List<string> _order = new();

    private List<SessionRow> Panes
    {
        get
        {
            var rows = _snapshot.Sessions.Where(s => !_hidden.Contains(s.SessionId)).ToList();

            // A chat has no session id until Claude's first reply, so it is not
            // on disk yet - but it is running and belongs on the wall.
            foreach (var chat in App.Chats)
            {
                if (chat.State == ChatState.Ended) continue;
                if (chat.SessionId is not null &&
                    (rows.Any(r => r.SessionId == chat.SessionId) || _hidden.Contains(chat.SessionId)))
                {
                    continue;
                }

                if (chat.SessionId is null && rows.Any(r =>
                        string.Equals(r.ProjectPath, chat.ProjectPath, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                rows.Add(new SessionRow
                {
                    SessionId = chat.SessionId ?? string.Empty,
                    ProfileName = chat.Profile.DisplayLabel,
                    ProfileIcon = chat.Profile.DisplayIcon,
                    ProjectName = chat.ProjectName,
                    ProjectPath = chat.ProjectPath,
                    Task = "chat session",
                    Model = chat.Model,
                    State = SessionState.Idle
                });
            }

            // A terminal tile knows its session id before Claude writes anything,
            // so it can be on the wall from the first frame.
            foreach (var terminal in App.Terminals)
            {
                if (terminal.HasExited) continue;
                if (_hidden.Contains(terminal.SessionId)) continue;

                if (rows.Any(r => r.SessionId == terminal.SessionId)) continue;

                rows.Add(new SessionRow
                {
                    SessionId = terminal.SessionId,
                    ProjectName = terminal.ProjectName,
                    ProjectPath = terminal.ProjectPath,
                    Task = "terminal",
                    State = SessionState.Idle
                });
            }

            return Stable(rows);
        }
    }

    /// <summary>Returns rows in the order tiles were first seen, newcomers last.</summary>
    private List<SessionRow> Stable(List<SessionRow> rows)
    {
        foreach (var row in rows)
        {
            var key = DraftKey(row);
            if (_order.Contains(key)) continue;

            // A chat is keyed by project until Claude assigns an id; adopt the
            // id in the same slot so the tile does not jump when it arrives.
            var provisional = _order.IndexOf(row.ProjectPath);
            if (!string.IsNullOrEmpty(row.SessionId) && provisional >= 0) _order[provisional] = key;
            else _order.Add(key);
        }

        return rows
            .OrderBy(r =>
            {
                var index = _order.IndexOf(DraftKey(r));
                return index < 0 ? int.MaxValue : index;
            })
            .ToList();
    }

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

        // One line of chips, never the boxed badges: those read as the wizard's
        // step bar, which this is not - the panes are not steps and have no
        // order to work through. It stays because it is the only way to see a
        // pane that is off screen.
        y = Strip(buffer, y, panes);

        var focused = panes[_focus];
        var live = Live(focused);

        var wantTips = App.Settings.ShowTips && buffer.Height >= 40 && live is null;
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
                    "A chat tile has its own prompt - just type into the focused one",
                    "Arrows or tab move between tiles; a pane turns amber when it needs you",
                    "Tiles from a terminal are read-only - press enter to jump to it"
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

        var panes = Panes;
        var terminal = panes.Count > 0 && _focus < panes.Count ? LiveTerminal(panes[_focus]) : null;

        // A terminal takes every key, so the only hint that can be honest is the
        // one that gets the keyboard back.
        if (terminal is not null && !terminal.HasExited && !_released)
        {
            Widgets.Footer(buffer, new[]
            {
                new KeyHint("type", "Claude's own UI"),
                new KeyHint("^]", "Release keyboard")
            });

            return;
        }

        var live = panes.Count > 0 ? Live(panes[_focus]) : null;

        // A focused chat takes the letters, so its hints show the keys that
        // still work rather than ones a message would eat.
        if (live is not null)
        {
            Widgets.Footer(buffer, live.Pending is not null
                ? new[]
                {
                    new KeyHint("y", "Allow"),
                    new KeyHint("a", "Always"),
                    new KeyHint("n", "Deny"),
                    new KeyHint("esc", "Back")
                }
                : new[]
                {
                    new KeyHint("type", "Message"),
                    new KeyHint("↵", "Send"),
                    new KeyHint("↑↓ tab", "Tile"),
                    new KeyHint("^t", "Terminal"),
                    new KeyHint("^z", "Zoom"),
                    new KeyHint("^l", "Layout"),
                    new KeyHint("esc", "Back")
                });

            return;
        }

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
                new KeyHint("t", "Terminal"),
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
                new KeyHint("t", "Terminal"),
                new KeyHint("w", "Remove"),
                new KeyHint("esc", "Back")
            };

        Widgets.Footer(buffer, hints);
    }

    /// <summary>
    /// The prompt inside a chat tile. Every chat tile has one, so typing goes
    /// straight to whichever tile is focused - no mode to enter first.
    /// </summary>
    private void TileInput(ScreenBuffer buffer, int x, int y, int width, StreamSession live,
        SessionRow row, bool focused, Rgb fill)
    {
        if (live.Pending is not null)
        {
            var ask = live.Pending;
            var cursor = buffer.Write(x, y, "◆ ", new Sty(Theme.Amber, fill, bold: true));
            cursor = buffer.WriteClipped(cursor, y, ask.Tool, Math.Max(4, width / 3), new Sty(Theme.Amber, fill, bold: true));
            cursor = buffer.Write(cursor, y, "  ", new Sty(Theme.Muted, fill));
            cursor = buffer.Write(cursor, y, "y", new Sty(Theme.Green, fill, bold: true));
            cursor = buffer.Write(cursor, y, "/", new Sty(Theme.Dim, fill));
            cursor = buffer.Write(cursor, y, "a", new Sty(Theme.Blue, fill, bold: true));
            cursor = buffer.Write(cursor, y, "/", new Sty(Theme.Dim, fill));
            cursor = buffer.Write(cursor, y, "n", new Sty(Theme.Red, fill, bold: true));
            buffer.WriteClipped(cursor, y, " allow/always/deny", Math.Max(0, x + width - cursor),
                new Sty(Theme.Muted, fill));
            return;
        }

        if (live.State == ChatState.Working)
        {
            var tool = live.ActiveTool;
            var text = tool is null
                ? "working… esc stops it"
                : $"{tool.Description} · {Sessions.Format.Duration(tool.Elapsed)}";

            buffer.WriteClipped(x, y, "◆ " + text, width, new Sty(Theme.Amber, fill));
            return;
        }

        var draft = Draft(row);
        var caret = buffer.Write(x, y, "› ", new Sty(focused ? Theme.Blue : Theme.Dim, fill, bold: focused));

        if (!focused && draft.Length == 0)
        {
            buffer.WriteClipped(caret, y, "", width - 2, new Sty(Theme.Dim, fill));
            return;
        }

        var room = width - 4;
        var shown = draft.Length <= room ? draft : draft.Substring(draft.Length - room);
        caret = buffer.Write(caret, y, shown, new Sty(Theme.Text, fill));
        if (focused) buffer.Write(caret, y, "▏", new Sty(Theme.Blue, fill, bold: true));
    }

    /// <summary>
    /// Command dropdown above the prompt: one command per row with its
    /// description, the way Claude's own menu reads.
    /// </summary>
    private static string Detail(SlashCommand command) =>
        string.IsNullOrEmpty(command.ArgumentHint)
            ? command.Description
            : $"{command.ArgumentHint} · {command.Description}";

    private void TileMenu(ScreenBuffer buffer, int x, int y, int width, int rows,
        List<(string Label, string Detail)> matches)
    {
        _menuIndex = Math.Clamp(_menuIndex, 0, Math.Max(0, matches.Count - 1));

        // Keep the selection in view when the list is longer than the space.
        var start = Math.Max(0, Math.Min(_menuIndex - rows + 1, matches.Count - rows));
        if (start < 0) start = 0;

        var nameWidth = Math.Clamp(width / 3, 12, 24);

        for (var i = 0; i < rows; i++)
        {
            var index = start + i;
            if (index >= matches.Count) break;

            var (label, detail) = matches[index];
            var selected = index == _menuIndex;
            var rowY = y + i;
            var bg = selected ? Theme.PanelSelected : Theme.BgSoft;

            buffer.Fill(x, rowY, width, 1, bg);
            buffer.Write(x, rowY, selected ? "▸ " : "  ", new Sty(Theme.Blue, bg, bold: true));
            buffer.WriteClipped(x + 2, rowY, label, nameWidth,
                new Sty(selected ? Theme.Blue : Theme.Text, bg, bold: selected));

            // The counter sits on the first row, so that row's detail stops short.
            var counter = matches.Count > rows ? $"{_menuIndex + 1}/{matches.Count}" : string.Empty;
            var reserved = i == 0 && counter.Length > 0 ? counter.Length + 2 : 0;
            var detailWidth = width - nameWidth - 4 - reserved;

            if (detail.Length > 0 && detailWidth > 4)
                buffer.WriteClipped(x + 3 + nameWidth, rowY, detail, detailWidth, new Sty(Theme.Dim, bg));

            if (reserved > 0) buffer.WriteRight(x + width - 1, rowY, counter, new Sty(Theme.Dim, bg));
        }
    }

    /// <summary>The numbered pane strip, echoing the wizard's step badges.</summary>
    /// <summary>
    /// A single line of chips: dot, number, project. Enough to find a pane that
    /// scrolled off, without pretending the panes are steps in a sequence.
    /// </summary>
    private int Strip(ScreenBuffer buffer, int y, List<SessionRow> panes)
    {
        var margin = Widgets.Margin(buffer);
        var x = margin;

        for (var i = 0; i < panes.Count && x < buffer.Width - margin - 8; i++)
        {
            var active = i == _focus;
            var color = Color(panes[i], active);

            x = buffer.Write(x, y, active ? "●" : "○", new Sty(color, Theme.Bg, bold: active));
            x = buffer.Write(x, y, $" {i + 1} ", new Sty(color, Theme.Bg, bold: active));
            x = buffer.WriteClipped(x, y, panes[i].ProjectName, 14,
                new Sty(active ? Theme.Text : Theme.Dim, Theme.Bg));
            x = buffer.Write(x, y, "   ", new Sty(Theme.Dim, Theme.Bg));
        }

        return y + 2;
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

    private static TranscriptEntry ToEntry(ChatLine line) => new()
    {
        Kind = line.Kind switch
        {
            ChatLineKind.UserPrompt => EntryKind.UserPrompt,
            ChatLineKind.ToolCall => EntryKind.ToolCall,
            ChatLineKind.Thinking => EntryKind.Thinking,
            _ => EntryKind.AssistantText
        },
        Text = line.Text,
        Target = line.Detail
    };

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

    private void Tile(ScreenBuffer buffer, int x, int y, int width, int height,
        SessionRow row, int index, bool focused)
    {
        if (width < 12 || height < 3) return;

        var terminal = LiveTerminal(row);
        if (terminal is not null)
        {
            TerminalPane(buffer, x, y, width, height, row, index, focused, terminal);
            return;
        }

        // A session we own reports itself directly, which is both fresher than
        // the transcript on disk and the only way to see a pending permission.
        var live = Live(row);
        if (live is not null)
        {
            row = new SessionRow
            {
                SessionId = row.SessionId,
                ProjectName = row.ProjectName,
                ProjectPath = row.ProjectPath,
                Task = row.Task,
                Branch = row.Branch,
                Model = live.Model ?? row.Model,
                ContextTokens = row.ContextTokens,
                StateAge = row.StateAge,
                State = live.Pending is not null ? SessionState.Waiting
                    : live.State == ChatState.Working ? SessionState.Running
                    : SessionState.Idle,
                Entries = live.Snapshot().Select(ToEntry).ToArray()
            };
        }

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

        // A chat tile keeps its last rows for its own prompt, and one more for
        // the command menu while a slash command is being typed.
        var draftText = live is not null && focused ? Draft(row) : string.Empty;
        var matches = live is not null && focused ? Matches(live, draftText) : new List<SlashCommand>();
        var values = live is not null && focused && matches.Count == 0
            ? ValueMatches(live, draftText)
            : new List<string>();

        var inputRows = live is not null && contentRows > 2 ? 1 : 0;
        var listCount = matches.Count > 0 ? matches.Count : values.Count;

        // The dropdown takes what it can up to six rows, never leaving the
        // transcript with less than two.
        var menuRows = listCount > 0
            ? Math.Clamp(Math.Min(listCount, 6), 0, Math.Max(0, contentRows - inputRows - 2))
            : 0;

        contentRows -= menuRows + inputRows;

        if (!string.IsNullOrEmpty(row.Branch) && contentRows > 3)
        {
            buffer.WriteClipped(x + 2, contentY, row.Branch, inner, new Sty(Theme.Dim, fill, italic: true));
            contentY++;
            contentRows--;
        }

        var lines = Lines(row, inner, contentRows, fill);
        for (var i = 0; i < lines.Count; i++)
            buffer.Write(x + 2, contentY + i, lines[i].Text, lines[i].Style);

        if (inputRows == 0 || live is null) return;

        var inputY = y + height - 2;

        if (menuRows > 0)
        {
            // Values are shown in the same dropdown, so choosing a command and
            // then its value is one continuous motion.
            var entries = matches.Count > 0
                ? matches.Select(m => (Label: "/" + m.Name, Detail: Detail(m))).ToList()
                : values.Select(v => (Label: v, Detail: string.Empty)).ToList();

            TileMenu(buffer, x + 2, inputY - menuRows, inner, menuRows, entries);
        }

        TileInput(buffer, x + 2, inputY, inner, live, row, focused, fill);
    }

    /// <summary>
    /// A tile showing Claude's own screen. The pty is resized to the interior, so
    /// Claude lays itself out for the space it actually has rather than being
    /// clipped to it.
    /// </summary>
    private void TerminalPane(ScreenBuffer buffer, int x, int y, int width, int height,
        SessionRow row, int index, bool focused, TerminalTile terminal)
    {
        var typing = focused && !_released;

        var border = terminal.HasExited
            ? Theme.Dim
            : typing ? Theme.Blue : focused ? Theme.BorderAccent : Theme.Border;

        var fill = focused ? Theme.PanelSelected : Theme.Panel;
        buffer.Box(x, y, width, height, new Sty(border, fill), BoxStyle.Rounded, fill);

        var title = $" {index + 1} · {row.ProjectName} ";
        buffer.WriteClipped(x + 2, y, title, width - 4, new Sty(border, fill, bold: true));

        var badge = terminal.HasExited ? " ended " : typing ? " typing " : " terminal ";
        if (title.Length + badge.Length + 6 <= width)
            buffer.WriteRight(x + width - 3, y, badge, new Sty(typing ? Theme.Blue : Theme.Dim, fill));

        var inner = width - 4;
        var innerRows = height - 2;
        if (inner < 20 || innerRows < 4)
        {
            buffer.WriteClipped(x + 2, y + 1, "too small", Math.Max(0, width - 4), new Sty(Theme.Dim, fill));
            return;
        }

        terminal.Resize(inner, innerRows);
        terminal.Read(screen => TerminalRender.Draw(buffer, screen, x + 2, y + 1, inner, innerRows, fill, typing));
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

        // A focused terminal tile takes every key, because Claude's own UI needs
        // Esc, Tab and the arrows. Ctrl+] hands the keyboard back to the wall -
        // the one key a terminal will never want.
        var terminal = panes.Count > 0 && _focus < panes.Count ? LiveTerminal(panes[_focus]) : null;
        if (terminal is not null && !terminal.HasExited)
        {
            var ctrlKey = (key.Modifiers & ConsoleModifiers.Control) != 0;

            if (ctrlKey && key.Key == ConsoleKey.Oem6)
            {
                _released = !_released;
                _notice = _released ? "keyboard released · ctrl+] to type" : null;
                return ScreenAction.None;
            }

            if (!_released)
            {
                terminal.Send(key);
                return ScreenAction.None;
            }

            if (key.Key == ConsoleKey.Enter)
            {
                _released = false;
                return ScreenAction.None;
            }
        }

        var live = panes.Count > 0 ? Live(panes[_focus]) : null;

        // A pending permission owns the keyboard until it is answered.
        if (live?.Pending is not null)
        {
            switch (char.ToLowerInvariant(key.KeyChar))
            {
                case 'y': live.Answer(allow: true); return ScreenAction.None;
                case 'a': live.Answer(allow: true, always: true); return ScreenAction.None;
                case 'n': live.Answer(allow: false); return ScreenAction.None;
            }

            if (key.Key == ConsoleKey.Escape) return Leave();
            return ScreenAction.None;
        }

        // A focused chat tile owns the keyboard: everything printable goes into
        // its prompt. Only keys that can never be part of a message stay as
        // commands, so nothing is swallowed and nothing needs a mode.
        if (live is not null)
        {
            var row = panes[_focus];
            var draft = Draft(row);
            var matches = Matches(live, draft);

            switch (key.Key)
            {
                case ConsoleKey.Escape:
                    if (draft.Length > 0) { SetDraft(row, string.Empty); return ScreenAction.None; }
                    if (live.State == ChatState.Working) { live.Interrupt(); return ScreenAction.None; }
                    return Leave();

                case ConsoleKey.Enter:
                    if (matches.Count > 0 && !draft.EndsWith(' '))
                    {
                        SetDraft(row, "/" + matches[Math.Clamp(_menuIndex, 0, matches.Count - 1)].Name + " ");
                        _menuIndex = 0;
                        return ScreenAction.None;
                    }

                    if (draft.Trim().Length > 0 && live.State != ChatState.Working)
                    {
                        live.Send(draft.Trim());
                        SetDraft(row, string.Empty);
                    }

                    return ScreenAction.None;

                case ConsoleKey.Tab:
                    if (matches.Count > 0)
                    {
                        SetDraft(row, "/" + matches[Math.Clamp(_menuIndex, 0, matches.Count - 1)].Name + " ");
                        _menuIndex = 0;
                        return ScreenAction.None;
                    }

                    Move(1, panes.Count);
                    return ScreenAction.None;

                case ConsoleKey.Backspace:
                    if (draft.Length > 0) SetDraft(row, draft.Substring(0, draft.Length - 1));
                    return ScreenAction.None;

                case ConsoleKey.UpArrow:
                    if (matches.Count > 0) { _menuIndex = Math.Max(0, _menuIndex - 1); return ScreenAction.None; }
                    Move(-1, panes.Count);
                    return ScreenAction.None;

                case ConsoleKey.DownArrow:
                    if (matches.Count > 0) { _menuIndex = Math.Min(matches.Count - 1, _menuIndex + 1); return ScreenAction.None; }
                    Move(1, panes.Count);
                    return ScreenAction.None;

                case ConsoleKey.LeftArrow:
                    Move(-1, panes.Count);
                    return ScreenAction.None;

                case ConsoleKey.RightArrow:
                    Move(1, panes.Count);
                    return ScreenAction.None;
            }

            // Permission answers beat typing, and cannot be typed anyway: the
            // prompt is blocked until the request is settled.
            if (live.Pending is not null)
            {
                switch (char.ToLowerInvariant(key.KeyChar))
                {
                    case 'y': live.Answer(allow: true); return ScreenAction.None;
                    case 'a': live.Answer(allow: true, always: true); return ScreenAction.None;
                    case 'n': live.Answer(allow: false); return ScreenAction.None;
                }

                return ScreenAction.None;
            }

            if ((key.Modifiers & ConsoleModifiers.Control) != 0)
            {
                switch (key.Key)
                {
                    case ConsoleKey.Z: _zoom = !_zoom; return ScreenAction.None;
                    case ConsoleKey.L: Cycle(); return ScreenAction.None;
                    case ConsoleKey.W: _hidden.Add(panes[_focus].SessionId); return ScreenAction.None;

                    // Plain t is a letter to a focused chat tile, so the terminal
                    // tile needs a chord here or it cannot be reached at all from
                    // the tile the wall opens on.
                    case ConsoleKey.T: return ScreenAction.Push(new NewTerminalScreen(App));
                }

                return ScreenAction.None;
            }

            if (!char.IsControl(key.KeyChar))
            {
                SetDraft(row, draft + key.KeyChar);
                if (draft.Length == 0 && key.KeyChar == '/') _menuIndex = 0;
            }

            return ScreenAction.None;
        }

        switch (key.Key)
        {
            case ConsoleKey.Escape:
                if (_zoom) { _zoom = false; return ScreenAction.None; }
                return Leave();
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
            case 't':
                return ScreenAction.Push(new NewTerminalScreen(App));
            case 'q':
                return ScreenAction.Exit;
        }

        return ScreenAction.None;
    }

    /// <summary>
    /// Home, never a plain Back: a session opened from the wizard lands on the
    /// wall as the root screen, and popping a one-deep stack ends the loop -
    /// which would quit the launcher instead of leaving the wall.
    /// </summary>
    private ScreenAction Leave() =>
        ScreenAction.Root(new HomeScreen(App, new SessionService(App.State)));

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
