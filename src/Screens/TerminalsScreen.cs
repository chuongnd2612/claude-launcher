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

    // Search state. The hit list is rebuilt every frame rather than tracked,
    // because Claude repaints its whole screen constantly and a remembered
    // position would point at whatever moved into that cell since.
    private bool _finding;
    private string _query = string.Empty;
    private List<TerminalMatch> _hits = new();
    private int _hit;
    private HistorySweep? _sweep;

    /// <summary>
    /// Where the dividers sit; equal shares until they are moved. Columns are
    /// kept per row, so each terminal can be given the width it needs without
    /// dragging the ones above and below it with it.
    /// </summary>
    private Dictionary<int, PaneSplits> _columnsByRow = new();
    private PaneSplits _rows = new();

    private PaneSplits Columns(int row)
    {
        if (_columnsByRow.TryGetValue(row, out var splits)) return splits;

        splits = new PaneSplits();
        _columnsByRow[row] = splits;
        return splits;
    }

    /// <summary>The divider being dragged: its axis, index and row, or null.</summary>
    private (bool Vertical, int Index, int Row)? _dragging;

    /// <summary>
    /// A tile being taken hold of: where it started, where it would land, and
    /// whether the pointer has actually moved with the button down.
    ///
    /// That last flag is not optional. A move with no button held arrives as
    /// MouseUp - see ConsoleInput - so without it, sweeping the mouse across the
    /// wall after any click would reorder it.
    /// </summary>
    private (int From, int To, bool Moved)? _carry;

    /// <summary>Divider positions from the last frame, for hit testing a drag.</summary>
    private readonly List<(bool Vertical, int Index, int Row, int At, int From, int To)> _dividers = new();

    /// <summary>The grid the last frame drew, so a key knows what it is resizing.</summary>
    private (int Columns, int Rows, int X, int Y, int Width, int Height) _grid;

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
        _columnsByRow = PaneSplits.ParseRows(app.Settings.TerminalSplits);

        // Seeded before the Panes call below, because Stable() appends whatever
        // it has not seen - so seeding after it would put every restored
        // terminal ahead of the slot it was saved in.
        //
        // A remembered project path can be adopted by a different new chat in
        // the same project, through the provisional swap in Stable(). That is
        // the point: the slot belongs to the project until a session claims it.
        foreach (var key in (app.Settings.TerminalOrder ?? string.Empty)
                 .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!_order.Contains(key, StringComparer.Ordinal)) _order.Add(key);
        }

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

    /// <summary>
    /// Moves one pane along the wall, shifting the rest rather than swapping:
    /// the pane numbers are read as a sequence, so putting one third has to
    /// renumber what it passes.
    ///
    /// The remembered order can hold keys for sessions that are not on the wall,
    /// which is why this never does index arithmetic against it. The panes that
    /// are showing get lifted out and re-inserted at the front in their new
    /// order; the rest trail behind, keeping their slots for when they return.
    /// </summary>
    /// <returns>False when the pane is already at that end, so a key can say so.</returns>
    private bool MovePane(List<SessionRow> panes, int from, int to)
    {
        if (from < 0 || from >= panes.Count) return false;
        if (to < 0 || to >= panes.Count || to == from) return false;

        var keys = panes.Select(DraftKey).ToList();
        var moved = keys[from];
        keys.RemoveAt(from);
        keys.Insert(to, moved);

        _order.RemoveAll(k => keys.Contains(k, StringComparer.Ordinal));
        _order.InsertRange(0, keys);

        var name = panes[from].ProjectName;
        SaveOrder(keys);

        // Follow the pane, but do not start typing into it. Focus() hands the
        // keyboard over, which is right when you are switching panes and wrong
        // here: someone arranging the wall released it on purpose, and would
        // have found their next wall key inside Claude instead.
        var released = _released;
        Focus(to);
        _released = released;

        // Focus() clears the notice, so it has to be set after, not before.
        _notice = $"moved {name} to pane {to + 1}";
        return true;
    }

    /// <summary>Slots remembered, for the same reason Workspace caps its own list.</summary>
    private const int MaxRemembered = 24;

    /// <summary>
    /// Writes the order down. What is on the wall goes first, then everything
    /// remembered before it - merged rather than replaced, so one run with two
    /// tiles open does not throw away the rest of an arrangement.
    /// </summary>
    private void SaveOrder(IReadOnlyList<string> keys)
    {
        var all = keys.ToList();
        all.AddRange(_order.Where(k => !all.Contains(k, StringComparer.Ordinal)));

        App.Settings.TerminalOrder = string.Join('|', all.Take(MaxRemembered));
        StateStore.SaveSettings(App.Settings);
    }

    public override void Render(ScreenBuffer buffer)
    {
        var y = Widgets.CompactChrome(buffer);
        var margin = Widgets.Margin(buffer);
        var width = buffer.Width - margin * 2;
        var panes = Panes;

        _rects.Clear();

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

        if (_finding)
            SearchBar(buffer, margin + 1, buffer.Height - 5, width - 2);
        else if (_notice is not null)
            buffer.WriteClipped(margin + 1, buffer.Height - 5, _notice, width - 2, new Sty(Theme.Amber, Theme.Bg));

        Footer(buffer, panes.Count);
    }

    /// <summary>
    /// The keys that are live right now, which is not the same set in every
    /// context: a focused terminal takes almost all of them, and a released one
    /// takes almost none. Only a few fit on the bar - F1 has the rest.
    /// </summary>
    private void Footer(ScreenBuffer buffer, int count)
    {
        var panes = Panes;
        var terminal = panes.Count > 0 && _focus < panes.Count ? LiveTerminal(panes[_focus]) : null;
        var alive = terminal is not null && !terminal.HasExited;

        if (alive && _finding)
        {
            Widgets.Footer(buffer, KeyMap.FindFooter(), KeyMap.Help);
            return;
        }

        // A terminal takes every key, so the only hint that can be honest is the
        // one that gets the keyboard back.
        if (alive && !_released)
        {
            Widgets.Footer(buffer, KeyMap.TerminalFooter(_zoom), KeyMap.Help);
            return;
        }

        // A released tile used to show the wall's own footer, which promised
        // Enter would attach when it actually starts typing again.
        if (alive)
        {
            Widgets.Footer(buffer, KeyMap.ReleasedFooter(), KeyMap.Help);
            return;
        }

        var live = panes.Count > 0 ? Live(panes[_focus]) : null;

        // A focused chat takes the letters, so its hints show the keys that
        // still work rather than ones a message would eat.
        if (live is not null)
        {
            Widgets.Footer(buffer, KeyMap.ChatFooter(live.Pending is not null), KeyMap.Help);
            return;
        }

        Widgets.Footer(buffer, KeyMap.WallFooter(count, Splitting, buffer.Width >= 104), KeyMap.Help);
    }

    /// <summary>The key list for whichever context currently owns the keyboard.</summary>
    private ScreenAction Keys()
    {
        var panes = Panes;
        var terminal = panes.Count > 0 && _focus < panes.Count ? LiveTerminal(panes[_focus]) : null;
        var alive = terminal is not null && !terminal.HasExited;

        var (context, groups) = alive switch
        {
            true when _finding => ("Terminals · find", KeyMap.Find()),
            true when !_released => ("Terminals · typing", KeyMap.Terminal()),
            true => ("Terminals · released", KeyMap.Released()),
            _ => panes.Count > 0 && Live(panes[_focus]) is { } live
                ? ("Terminals · chat tile", KeyMap.ChatTile(live.Pending is not null))
                : ("Terminals · the wall", KeyMap.Wall(Splitting))
        };

        return ScreenAction.Push(new KeysScreen(App, context, groups));
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

            // Two panes of the same project under different profiles are
            // otherwise the same row twice.
            var mark = panes[i].ProfileIcon;
            if (mark.Length > 0)
            {
                x = buffer.Write(x, y, "  " + mark,
                    new Sty(ProfileLook.Color(panes[i].ProfileName), Theme.Bg, bold: active));
            }

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

        // Widths and heights come from the dividers rather than plain division,
        // so a pane can be given the room its content actually needs.
        var heights = _rows.Cells(rows, height - GutterY * (rows - 1), MinPaneHeight);

        _grid = (columns, rows, x, y, width, height);
        _dividers.Clear();

        var tops = new int[rows];
        var offset = y;
        for (var r = 0; r < rows; r++)
        {
            tops[r] = offset;
            offset += heights[r] + GutterY;
        }

        for (var i = 0; i < panes.Count; i++)
        {
            var column = i % columns;
            var row = i / columns;
            if (row >= rows) break;

            // Each row divides its own width, so one terminal can be widened
            // without moving the panes in the row below.
            var widths = Columns(row).Cells(columns, width - GutterX * (columns - 1), MinPaneWidth);

            var tileX = x;
            for (var c = 0; c < column; c++) tileX += widths[c] + GutterX;

            var tileWidth = widths[column];

            // A lone tile on the last row takes the full width.
            var lastRow = row == (panes.Count - 1) / columns;
            if (lastRow && panes.Count % columns == 1 && column == 0 && columns > 1)
                tileWidth = width;

            Tile(buffer, tileX, tops[row], tileWidth, heights[row], panes[i], i, i == _focus);
        }

        // Remember where the gutters landed: a drag has to know what it grabbed,
        // and only a divider with panes on both sides can be moved. A column
        // divider belongs to one row now, and only spans that row.
        for (var r = 0; r < rows; r++)
        {
            if (r * columns >= panes.Count) break;

            var widths = Columns(r).Cells(columns, width - GutterX * (columns - 1), MinPaneWidth);
            var lastRow = r == (panes.Count - 1) / columns;
            if (lastRow && panes.Count % columns == 1 && columns > 1) continue;

            var at = x;
            for (var c = 0; c < columns - 1; c++)
            {
                if (r * columns + c + 1 >= panes.Count) break;

                at += widths[c];
                _dividers.Add((true, c, r, at, tops[r], tops[r] + heights[r]));
                at += GutterX;
            }
        }

        var down = y;
        for (var r = 0; r < rows - 1; r++)
        {
            down += heights[r];
            _dividers.Add((false, r, 0, down, x, x + width));
            down += GutterY;
        }

        DrawDividers(buffer);
    }

    /// <summary>
    /// Marks each gutter so it reads as something you can take hold of, rather
    /// than as empty space between tiles.
    /// </summary>
    private void DrawDividers(ScreenBuffer buffer)
    {
        foreach (var divider in _dividers)
        {
            var live = _dragging is { } drag &&
                       drag.Vertical == divider.Vertical && drag.Index == divider.Index;

            var style = new Sty(live ? Theme.Blue : Theme.BorderMuted, Theme.Bg, bold: live);

            // The two grips sit a third and a quarter of the way along, so a
            // column divider and a row divider never land on the same cell.
            if (divider.Vertical)
            {
                var third = divider.From + (divider.To - divider.From) / 3;
                for (var i = -1; i <= 1; i++) buffer.Set(divider.At, third + i, '┃', style);
                continue;
            }

            var quarter = divider.From + (divider.To - divider.From) / 4;
            for (var i = -2; i <= 2; i++) buffer.Set(quarter + i, divider.At, '━', style);
        }
    }

    /// <summary>The divider within a cell or two of a point, if there is one.</summary>
    private (bool Vertical, int Index, int Row)? DividerAt(int x, int y)
    {
        foreach (var divider in _dividers)
        {
            if (divider.Vertical)
            {
                if (Math.Abs(x - divider.At) <= 1 && y >= divider.From && y <= divider.To)
                    return (true, divider.Index, divider.Row);

                continue;
            }

            if (Math.Abs(y - divider.At) <= 0 && x >= divider.From && x <= divider.To)
                return (false, divider.Index, divider.Row);
        }

        return null;
    }

    /// <summary>Puts a dragged divider where the pointer is.</summary>
    private void DragTo(int x, int y)
    {
        if (_dragging is not { } drag) return;

        if (drag.Vertical)
        {
            if (_grid.Width <= 0) return;
            var fraction = (double)(x - _grid.X) / _grid.Width;
            if (Columns(drag.Row).Place(_grid.Columns, drag.Index, fraction)) SaveSplits();
            return;
        }

        if (_grid.Height <= 0) return;
        var down = (double)(y - _grid.Y) / _grid.Height;
        _rows.Place(_grid.Rows, drag.Index, down);
    }

    /// <summary>
    /// Moves the divider beside the focused pane. Rows are not persisted: they
    /// follow the window's height, which changes on its own.
    /// </summary>
    private void Resize(bool vertical, int steps)
    {
        if (_grid.Columns == 0) return;

        var by = 0.03 * steps;

        if (vertical)
        {
            var column = _focus % Math.Max(1, _grid.Columns);
            var band = _focus / Math.Max(1, _grid.Columns);
            var index = column < _grid.Columns - 1 ? column : column - 1;
            var moved = Columns(band).Nudge(_grid.Columns, index, column < _grid.Columns - 1 ? by : -by);

            _notice = moved
                ? null
                : "that pane is as narrow as it goes · alt+shift+0 makes them even again";

            if (moved) SaveSplits();
            return;
        }

        var row = _focus / Math.Max(1, _grid.Columns);
        var below = row < _grid.Rows - 1 ? row : row - 1;
        if (!_rows.Nudge(_grid.Rows, below, row < _grid.Rows - 1 ? by : -by))
            _notice = "that pane is as short as it goes · alt+shift+0 makes them even again";
    }

    private void EvenOut()
    {
        // Every row, not only the focused one: "make them even" means the wall.
        foreach (var splits in _columnsByRow.Values) splits.Reset(_grid.Columns);
        _rows.Reset(_grid.Rows);

        SaveSplits();
        _notice = "panes share the wall evenly again";
    }

    private void SaveSplits()
    {
        App.Settings.TerminalSplits = PaneSplits.FormatRows(_columnsByRow);
        StateStore.SaveSettings(App.Settings);
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

    /// <summary>
    /// Where each pane was last drawn, so a click can be resolved back to it.
    /// Rebuilt every frame; the layout is the only thing that knows the rects.
    /// </summary>
    private readonly List<(int X, int Y, int W, int H, int Index)> _rects = new();

    /// <summary>The pane drawn at a point, or -1 for none. Shared by click and drag.</summary>
    private int Under(int x, int y)
    {
        foreach (var rect in _rects)
        {
            if (x >= rect.X && x < rect.X + rect.W && y >= rect.Y && y < rect.Y + rect.H)
                return rect.Index;
        }

        return -1;
    }

    /// <summary>The pane being carried, once the drag is real.</summary>
    private bool Held(int index) => _carry is { Moved: true } carry && carry.From == index;

    /// <summary>The pane it would drop onto.</summary>
    private bool Landing(int index) =>
        _carry is { Moved: true } carry && carry.To == index && carry.To != carry.From;

    private void Tile(ScreenBuffer buffer, int x, int y, int width, int height,
        SessionRow row, int index, bool focused)
    {
        if (width < 12 || height < 3) return;

        _rects.Add((x, y, width, height, index));

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

        var border = Held(index) ? Theme.Blue
            : Landing(index) ? Theme.Amber
            : row.State == SessionState.Waiting ? Theme.Amber
            : focused ? Theme.Blue : Theme.Border;

        var fill = focused ? Theme.PanelSelected : Theme.Panel;
        buffer.Box(x, y, width, height, new Sty(border, fill), BoxStyle.Rounded, fill);

        // Legends notched into the top border.
        var title = $" {index + 1} · {row.ProjectName} ";
        buffer.WriteClipped(x + 2, y, title, width - 4, new Sty(border, fill, bold: true));

        // While a tile is being carried, the badge says so - it is the one slot
        // in the border that is already there to be borrowed.
        var state = Held(index) ? " moving "
            : Landing(index) ? " drop here "
            : $" {Format.State(row.State, row.StateAge)} ";

        if (title.Length + state.Length + 6 <= width)
        {
            buffer.WriteRight(x + width - 3, y, state,
                new Sty(Held(index) ? Theme.Blue : Landing(index) ? Theme.Amber : Theme.Dim,
                    fill, bold: Held(index) || Landing(index)));
        }

        var titled = Named(buffer, x, y, width, row, title.Length, state.Length, fill);
        Whose(buffer, x, y, width, row, null, titled, state.Length, fill);

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

        var border = Held(index) ? Theme.Blue
            : Landing(index) ? Theme.Amber
            : terminal.HasExited ? Theme.Dim
            : typing ? Theme.Blue : focused ? Theme.BorderAccent : Theme.Border;

        var fill = focused ? Theme.PanelSelected : Theme.Panel;
        buffer.Box(x, y, width, height, new Sty(border, fill), BoxStyle.Rounded, fill);

        var title = $" {index + 1} · {row.ProjectName} ";
        buffer.WriteClipped(x + 2, y, title, width - 4, new Sty(border, fill, bold: true));

        var searching = focused && _finding;
        var carrying = Held(index) || Landing(index);
        var badge = Held(index) ? " moving "
            : Landing(index) ? " drop here "
            : terminal.HasExited ? " ended "
            : searching ? " find " : typing ? " typing " : " terminal ";

        if (title.Length + badge.Length + 6 <= width)
        {
            buffer.WriteRight(x + width - 3, y, badge,
                new Sty(Held(index) ? Theme.Blue
                    : Landing(index) ? Theme.Amber
                    : searching ? Theme.Amber : typing ? Theme.Blue : Theme.Dim,
                    fill, bold: carrying));
        }

        var named = Named(buffer, x, y, width, row, title.Length, badge.Length, fill);
        Whose(buffer, x, y, width, row, terminal, named, badge.Length, fill);

        var inner = width - 4;
        var innerRows = height - 2;
        if (inner < 20 || innerRows < 4)
        {
            buffer.WriteClipped(x + 2, y + 1, "too small", Math.Max(0, width - 4), new Sty(Theme.Dim, fill));
            return;
        }

        terminal.Resize(inner, innerRows);

        // Re-searching here rather than only on a keystroke is what keeps the
        // highlight on the text it found: the pane below it repaints constantly.
        if (searching && _query.Length > 0) Recompute(terminal, keepPlace: true);

        var search = searching && _hits.Count > 0
            ? new SearchHighlight(_hits, _hit)
            : (SearchHighlight?)null;

        terminal.Read(screen =>
            TerminalRender.Draw(buffer, screen, x + 2, y + 1, inner, innerRows, fill, typing, search));
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
            var altKey = (key.Modifiers & ConsoleModifiers.Alt) != 0;

            // While the search bar is open it owns the keyboard: what you type
            // is the query, not a message to Claude.
            if (_finding) return Search(key, terminal, ctrlKey || altKey);

            if ((ctrlKey || altKey) && key.Key == ConsoleKey.F)
            {
                OpenSearch();
                return ScreenAction.None;
            }

            if (ctrlKey && key.Key == ConsoleKey.Oem6)
            {
                _released = !_released;
                _notice = _released ? "keyboard released · ctrl+] to type" : null;
                return ScreenAction.None;
            }

            // Without this the picker cannot be reached at all from a wall of
            // terminal tiles: plain t is a character to the child, and so was
            // ctrl+t, so every route to "open another terminal" was swallowed.
            if ((ctrlKey || (key.Modifiers & ConsoleModifiers.Alt) != 0) && key.Key == ConsoleKey.T)
                return ScreenAction.Push(new NewTerminalScreen(App));

            // Closing has to be reachable while typing too, and Ctrl+C is not it:
            // that belongs to Claude, which uses it to interrupt a turn.
            if ((ctrlKey || (key.Modifiers & ConsoleModifiers.Alt) != 0) && key.Key == ConsoleKey.W)
            {
                CloseTerminal(panes[_focus], terminal);
                return ScreenAction.None;
            }

            if ((key.Modifiers & ConsoleModifiers.Alt) != 0 && key.Key == ConsoleKey.S)
            {
                ToggleSelecting();
                return ScreenAction.None;
            }

            // Zoom without giving the keyboard back first: reading one pane
            // closely is something you want mid-sentence, not after stepping
            // out of the terminal and back in.
            if ((key.Modifiers & ConsoleModifiers.Alt) != 0 && key.Key == ConsoleKey.Z)
            {
                _zoom = !_zoom;
                _notice = _zoom ? "zoomed · alt+z to show the wall again" : null;
                return ScreenAction.None;
            }

            if (key.Key == ConsoleKey.F1) return Keys();
            if (ResizeKey(key)) return ScreenAction.None;
            if (ReorderKey(key, panes)) return ScreenAction.None;

            // Switching panes has to work mid-sentence, so a few Alt chords are
            // kept back from the child. Alt is the safe half of the keyboard:
            // Claude's own UI uses Esc, Tab, the arrows and Ctrl, all of which
            // stay its own.
            if (Switch(key, panes.Count)) return ScreenAction.None;

            // Shift+PageUp/PageDown reads our scrollback - but only a program on
            // the primary screen fills one. Claude lives on the alternate screen
            // and scrolls its own view, so there the key belongs to it.
            if ((key.Modifiers & ConsoleModifiers.Shift) != 0 &&
                key.Key is ConsoleKey.PageUp or ConsoleKey.PageDown)
            {
                var ours = false;
                terminal.Read(screen =>
                {
                    if (screen.IsAlternate) return;
                    screen.ScrollBy(key.Key == ConsoleKey.PageUp ? 10 : -10);
                    ours = true;
                });

                if (ours) return ScreenAction.None;

                terminal.Send(new ConsoleKeyInfo(key.KeyChar, key.Key, false, false, false));
                return ScreenAction.None;
            }

            if (!_released)
            {
                // Typing means you want to be back at the prompt, not still
                // reading history.
                terminal.Read(screen => screen.ScrollToBottom());
                terminal.Send(key);
                return ScreenAction.None;
            }

            if (key.Key == ConsoleKey.Enter)
            {
                _released = false;
                return ScreenAction.None;
            }
        }

        // Resizing works from anywhere on the wall, including with a chat tile
        // focused: alt+shift is not a chord any tile wants for itself.
        if (ResizeKey(key)) return ScreenAction.None;

        // Before the chat and wall blocks below, both of which match arrows
        // without looking at modifiers - reached after them, this chord would
        // quietly turn into plain focus movement.
        if (ReorderKey(key, panes)) return ScreenAction.None;

        if (key.Key == ConsoleKey.F1) return Keys();

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
                if (panes.Count > 0) Remove(panes[_focus]);
                return ScreenAction.None;
            case 'v':
                return Splitting ? Split(panes, vertical: true) : ScreenAction.None;
            case 's':
                return Splitting ? Split(panes, vertical: false) : ScreenAction.None;
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
    /// Takes a pane off the wall. A terminal this launcher started is stopped
    /// outright - hiding it would leave a Claude running with no way back to it -
    /// while a session in someone else's terminal is only hidden, because it is
    /// not ours to end.
    /// </summary>
    private void Remove(SessionRow row)
    {
        var terminal = LiveTerminal(row);

        if (terminal is not null)
        {
            CloseTerminal(row, terminal);
            return;
        }

        _hidden.Add(row.SessionId);
        _notice = "tile removed - the session keeps running";
    }

    /// <summary>
    /// Hands the console back to its own selection for a moment.
    ///
    /// Reading the mouse means turning off quick edit, and quick edit is what
    /// drags text out of a console - so the two cannot both be on. This borrows
    /// it back: the mouse stops focusing tiles and scrolling, and dragging
    /// selects and copies the way it does anywhere else.
    /// </summary>
    private void ToggleSelecting()
    {
        var wanted = !ConsoleInput.Selecting;

        if (!ConsoleInput.SetSelecting(wanted))
        {
            _notice = "this terminal does not hand over its mouse, so selection already works";
            return;
        }

        _notice = wanted
            ? "select mode · drag to select and copy · alt+s to give the mouse back"
            : "mouse back on · click focuses a tile, wheel scrolls it";
    }

    private void OpenSearch()
    {
        _finding = true;
        _query = string.Empty;
        _hits = new List<TerminalMatch>();
        _hit = 0;
    }

    private void CloseSearch(TerminalTile terminal)
    {
        // A sweep leaves the terminal standing in its own history, so closing
        // the search has to walk it back to the live end.
        _sweep?.ReturnToBottom();
        _sweep = null;

        _finding = false;
        _query = string.Empty;
        _hits = new List<TerminalMatch>();
        _hit = 0;

        // Leaving a search where it landed would strand the pane in history.
        terminal.Read(screen => screen.ScrollToBottom());
    }

    /// <summary>
    /// The search bar's own key handling. Typing narrows and jumps to the first
    /// hit the way a browser does; enter walks the rest.
    /// </summary>
    private ScreenAction Search(ConsoleKeyInfo key, TerminalTile terminal, bool chord)
    {
        if (chord && key.Key == ConsoleKey.F)
        {
            CloseSearch(terminal);
            return ScreenAction.None;
        }

        switch (key.Key)
        {
            case ConsoleKey.F1:
                return Keys();
            case ConsoleKey.Escape:
                CloseSearch(terminal);
                return ScreenAction.None;

            case ConsoleKey.Tab:
                return History(terminal);

            case ConsoleKey.Enter:
                // Past the last hit on this screen, enter keeps going into what
                // has scrolled off it rather than wrapping round to the top.
                if (_hits.Count == 0 && _query.Length > 0 && (key.Modifiers & ConsoleModifiers.Shift) == 0)
                {
                    Sweep(terminal);
                    return ScreenAction.None;
                }

                Step(terminal, (key.Modifiers & ConsoleModifiers.Shift) != 0 ? -1 : 1);
                return ScreenAction.None;

            case ConsoleKey.DownArrow:
                Step(terminal, 1);
                return ScreenAction.None;

            case ConsoleKey.UpArrow:
                Step(terminal, -1);
                return ScreenAction.None;

            case ConsoleKey.Backspace:
                if (_query.Length > 0)
                {
                    _query = _query[..^1];
                    Restart(terminal);
                }

                return ScreenAction.None;
        }

        if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
        {
            _query += key.KeyChar;
            Restart(terminal);
        }

        return ScreenAction.None;
    }

    /// <summary>Re-runs a changed query and lands on the first hit.</summary>
    private void Restart(TerminalTile terminal)
    {
        // Editing the query is a new search, not a continuation of the last sweep.
        _sweep?.Stop();
        _sweep = null;

        Recompute(terminal, keepPlace: false);
        if (_hits.Count > 0) terminal.Read(screen => screen.Reveal(_hits[0].Line));
    }

    private void Step(TerminalTile terminal, int delta)
    {
        Recompute(terminal, keepPlace: true);
        if (_hits.Count == 0) return;

        _hit = ((_hit + delta) % _hits.Count + _hits.Count) % _hits.Count;
        terminal.Read(screen => screen.Reveal(_hits[_hit].Line));
    }

    /// <summary>
    /// Searches the terminal again, keeping the current hit if that exact match
    /// is still there. Cheap enough to run per frame: it is a few thousand
    /// characters, and anything remembered across a repaint would be a lie.
    /// </summary>
    private void Recompute(TerminalTile terminal, bool keepPlace)
    {
        var was = keepPlace && _hit < _hits.Count ? _hits[_hit] : default;

        var hits = new List<TerminalMatch>();
        terminal.Read(screen => hits = screen.Find(_query));
        _hits = hits;

        if (_hits.Count == 0 || !keepPlace)
        {
            _hit = 0;
            return;
        }

        var at = _hits.FindIndex(h => h.Line == was.Line && h.Col == was.Col);
        _hit = at >= 0 ? at : Math.Clamp(_hit, 0, _hits.Count - 1);
    }

    /// <summary>
    /// Keeps looking for the query in what has scrolled off the top of this
    /// terminal, by scrolling it back a screenful at a time.
    /// </summary>
    private void Sweep(TerminalTile terminal)
    {
        _sweep?.Stop();
        _sweep = HistorySweep.Start(terminal, _query, ConsoleInput.Wake);
    }

    /// <summary>What the bar says while a sweep runs, or nothing when none does.</summary>
    private string? SweepStatus() => _sweep switch
    {
        null => null,
        { State: HistorySweep.Result.CannotScroll } => "this terminal does not scroll for us",
        { State: HistorySweep.Result.Searching } => $"searching back · {_sweep.Screens} screens",
        { State: HistorySweep.Result.Found } => $"found {_sweep.Screens} screens back · esc scrolls back down",
        { State: HistorySweep.Result.Exhausted } => _sweep.Screens == 0
            ? "nothing above this screen"
            : $"not in the last {_sweep.Screens} screens · esc scrolls back down",
        _ => null
    };

    /// <summary>
    /// Searches everything this session ever said, rather than the screenful it
    /// is showing. Claude keeps its own scrollback to itself, but it writes each
    /// turn to a transcript as it goes - and that we can read.
    /// </summary>
    private ScreenAction History(TerminalTile terminal)
    {
        var query = _query;
        var path = ClaudePaths.TranscriptFile(terminal.ConfigDir, terminal.ProjectPath, terminal.SessionId);

        CloseSearch(terminal);
        return ScreenAction.Push(new HistorySearchScreen(App, terminal.ProjectName, path, query));
    }

    private void SearchBar(ScreenBuffer buffer, int x, int y, int width)
    {
        var count = SweepStatus() ?? (_hits.Count > 0
            ? $"{_hit + 1}/{_hits.Count} on screen"
            : _query.Length == 0 ? "type to search" : "not on screen · enter searches back");

        // The bar lands on the bottom pane border, so it is padded to read as a
        // label sitting on that line rather than a hole punched through it.
        var bar = $" find: {_query}_   {count}   enter next · tab whole session · esc close ";
        var colour = _hits.Count == 0 && _query.Length > 0 ? Theme.Red : Theme.Amber;
        buffer.WriteClipped(x, y, bar, width, new Sty(colour, Theme.Bg));
    }

    /// <summary>
    /// Writes what this session is called after the project name, and reports
    /// how much of the border has been used.
    ///
    /// Several panes of one project is the normal way to work, and they were
    /// then identical down to the profile: the session name is the only thing
    /// that says which conversation is which. It comes from Claude's own title
    /// for the session, falling back to the name it derived and then to the
    /// short id, so there is always something.
    /// </summary>
    private int Named(ScreenBuffer buffer, int x, int y, int width, SessionRow row,
        int titleWidth, int badgeWidth, Rgb fill)
    {
        var name = row.Task.Trim();

        // A name that only repeats the project earns none of the border.
        if (name.Length == 0 ||
            string.Equals(name, row.ProjectName, StringComparison.OrdinalIgnoreCase))
        {
            return titleWidth;
        }

        // The name takes what it needs first and the profile tag gets the rest,
        // because the tag can fall back to its icon and still say which profile
        // this is - while a pane with no name is indistinguishable from the pane
        // beside it when both are the same project.
        var free = width - 4 - titleWidth - badgeWidth - 2;
        var room = Math.Min(free - 4, Math.Max(10, free / 2));
        if (room < 6) return titleWidth;

        var text = name.Length > room - 2 ? name[..(room - 3)] + "…" : name;
        var written = buffer.Write(x + 2 + titleWidth, y, "· " + text, new Sty(Theme.TextSoft, fill))
                      - (x + 2 + titleWidth);

        return titleWidth + written;
    }

    /// <summary>Writes the profile tag into the top border, if there is room for it.</summary>
    private void Whose(ScreenBuffer buffer, int x, int y, int width, SessionRow row,
        TerminalTile? terminal, int titleWidth, int badgeWidth, Rgb fill)
    {
        // Two cells of border either side, and one space before the badge.
        var room = width - 4 - titleWidth - badgeWidth - 2;
        if (room < 3) return;

        var tag = ProfileTag(row, terminal, room - 2);
        if (tag.Length == 0) return;

        // Each profile in its own colour: a wall of panes that differ only by
        // project name is the thing this is here to fix.
        var color = ProfileLook.Color(row.ProfileName);
        var at = buffer.Write(x + 2 + titleWidth, y, " ", new Sty(color, fill));

        // The icon leads, bold, and the rest follows dimmer - so the mark reads
        // first and the words are there when you look.
        var icon = tag.Split(' ')[0];
        at = buffer.Write(at, y, icon, new Sty(color, fill, bold: true));

        var rest = tag.Length > icon.Length ? tag[icon.Length..] : string.Empty;
        if (rest.Length > 0) at = buffer.WriteClipped(at, y, rest, room - icon.Length - 2, new Sty(color, fill));

        buffer.Write(at, y, " ", new Sty(color, fill));
    }

    /// <summary>
    /// Which profile a pane runs under, and who Claude is signed in as there.
    ///
    /// With several profiles open at once the panes are otherwise identical, and
    /// the whole point of a profile is that the session on the other side of it
    /// is a different account with different work.
    /// </summary>
    private string ProfileTag(SessionRow row, TerminalTile? terminal, int room)
    {
        var icon = row.ProfileIcon;
        var label = row.ProfileName;
        var account = row.Account;

        // A tile the launcher started knows its own config dir even when the
        // registry has not caught up with it yet.
        if (label.Length == 0 && terminal is not null)
        {
            var profile = App.State.Profiles.FirstOrDefault(p =>
                string.Equals(StateStore.ExpandHome(p.ConfigDir).TrimEnd('\\', '/'),
                    terminal.ConfigDir.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase));

            if (profile is not null)
            {
                icon = profile.DisplayIcon;
                label = profile.DisplayLabel;
            }

            if (account.Length == 0)
                account = SessionReader.ReadAccount(terminal.ConfigDir)?.Label ?? string.Empty;
        }

        if (label.Length == 0 && account.Length == 0) return string.Empty;

        // Give up the parts that matter least first, rather than clipping the
        // whole thing to something that reads as a different name.
        var full = $"{icon} {label} · {account}".Trim();
        if (account.Length == 0) full = $"{icon} {label}".Trim();

        if (full.Length <= room) return full;

        var short_ = $"{icon} {label}".Trim();
        if (short_.Length <= room) return short_;

        return icon.Length > 0 && icon.Length <= room ? icon : string.Empty;
    }

    /// <summary>Stops a terminal we own and takes its pane with it.</summary>
    private void CloseTerminal(SessionRow row, TerminalTile terminal)
    {
        var name = terminal.ProjectName;

        try
        {
            terminal.Dispose();
        }
        catch (Exception)
        {
            // Already gone; removing it from the wall is still right.
        }

        App.Terminals.Remove(terminal);

        // Closing is the one way out of the remembered set; merging alone would
        // hand it straight back on the next reopen.
        Workspace.Forget(terminal.SessionId);
        App.RememberTerminals();

        if (!string.IsNullOrEmpty(row.SessionId)) _hidden.Add(row.SessionId);

        _released = false;
        _focus = Math.Max(0, _focus - 1);

        // The conversation is on disk either way, so this is undoable.
        _notice = $"closed {name} · reopen it from Home with r, or resume it";
    }

    /// <summary>
    /// Home, never a plain Back: a session opened from the wizard lands on the
    /// wall as the root screen, and popping a one-deep stack ends the loop -
    /// which would quit the launcher instead of leaving the wall.
    /// </summary>
    /// <summary>
    /// Alt+Shift and an arrow moves the divider beside the focused pane, and
    /// Alt+Shift+0 makes the shares even again. Shift is what keeps these clear
    /// of plain Alt+arrows, which step between panes.
    /// </summary>
    private bool ResizeKey(ConsoleKeyInfo key)
    {
        if ((key.Modifiers & ConsoleModifiers.Alt) == 0) return false;
        if ((key.Modifiers & ConsoleModifiers.Shift) == 0) return false;

        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
                Resize(vertical: true, -1);
                return true;

            case ConsoleKey.RightArrow:
                Resize(vertical: true, 1);
                return true;

            case ConsoleKey.UpArrow:
                Resize(vertical: false, -1);
                return true;

            case ConsoleKey.DownArrow:
                Resize(vertical: false, 1);
                return true;

            case ConsoleKey.D0:
            case ConsoleKey.NumPad0:
                EvenOut();
                return true;
        }

        return false;
    }

    /// <summary>
    /// Ctrl+Shift and an arrow moves the focused pane along the wall. Alt+Shift
    /// already resizes, so order takes Ctrl - and Alt has to be off, because
    /// AltGr arrives as Ctrl+Alt and would otherwise reshuffle the wall from a
    /// dead key.
    /// </summary>
    private bool ReorderKey(ConsoleKeyInfo key, List<SessionRow> panes)
    {
        if ((key.Modifiers & ConsoleModifiers.Control) == 0) return false;
        if ((key.Modifiers & ConsoleModifiers.Shift) == 0) return false;
        if ((key.Modifiers & ConsoleModifiers.Alt) != 0) return false;

        var step = key.Key switch
        {
            ConsoleKey.LeftArrow or ConsoleKey.UpArrow => -1,
            ConsoleKey.RightArrow or ConsoleKey.DownArrow => 1,
            _ => 0
        };

        if (step == 0) return false;

        if (!MovePane(panes, _focus, _focus + step))
            _notice = step < 0 ? "that pane is already first" : "that pane is already last";

        return true;
    }

    private ScreenAction Leave() =>
        ScreenAction.Root(new HomeScreen(App, new SessionService(App.State)));

    /// <summary>
    /// Alt+1..9 jumps to a pane and Alt+arrow steps between them, whether or not
    /// a terminal currently owns the keyboard. Returns true when it handled the
    /// key, so it is not also sent to the child.
    /// </summary>
    private bool Switch(ConsoleKeyInfo key, int count)
    {
        if ((key.Modifiers & ConsoleModifiers.Alt) == 0 || count == 0) return false;

        if (key.Key >= ConsoleKey.D1 && key.Key <= ConsoleKey.D9)
        {
            var wanted = key.Key - ConsoleKey.D1;
            if (wanted >= count) return true;

            Focus(wanted);
            return true;
        }

        switch (key.Key)
        {
            case ConsoleKey.RightArrow:
            case ConsoleKey.DownArrow:
                Focus((_focus + 1) % count);
                return true;
            case ConsoleKey.LeftArrow:
            case ConsoleKey.UpArrow:
                Focus((_focus - 1 + count) % count);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Moves focus and hands the keyboard straight to the new pane, so switching
    /// while typing lands you typing rather than in a released state.
    /// </summary>
    private void Focus(int index)
    {
        _focus = index;
        _released = false;
        _notice = null;
        _menuIndex = 0;
    }

    public override ScreenAction HandleInput(InputEvent input)
    {
        // A press whose release never arrived - the window lost focus mid-drag -
        // must not leave a tile still held.
        if (input.Kind == InputKind.Key)
        {
            _carry = null;
            return HandleKey(input.Key);
        }

        // A divider being dragged owns the mouse until it is let go, so the
        // pointer can cross a tile on the way without focusing it.
        if (_dragging is not null)
        {
            if (input.Kind == InputKind.MouseDrag) DragTo(input.X, input.Y);
            if (input.Kind is InputKind.MouseUp or InputKind.MouseDown) _dragging = null;
            return ScreenAction.None;
        }

        if (input.Kind == InputKind.MouseDown && DividerAt(input.X, input.Y) is { } grabbed)
        {
            _dragging = grabbed;
            _notice = "drag to resize · let go to keep it";
            return ScreenAction.None;
        }

        // Carrying a tile: follow the pointer, and drop it where it was let go.
        if (input.Kind == InputKind.MouseDrag)
        {
            if (_carry is not { } carry) return ScreenAction.None;

            if (_rects.Count < 2)
            {
                _notice = "one pane is showing · ctrl+shift+arrows moves it, space shows the wall";
                return ScreenAction.None;
            }

            var over = Under(input.X, input.Y);
            _carry = (carry.From, over, true);

            _notice = over < 0 || over == carry.From
                ? $"holding pane {carry.From + 1} · drop it on another to move it"
                : $"moving pane {carry.From + 1} to slot {over + 1} · let go to drop";

            return ScreenAction.None;
        }

        if (input.Kind == InputKind.MouseUp)
        {
            // Cleared first and always: a button-less move arrives here too, so
            // anything conditional would leave a tile held across the wall.
            var carried = _carry;
            _carry = null;

            if (carried is not { Moved: true } drop) return ScreenAction.None;
            if (drop.To < 0 || drop.To == drop.From)
            {
                _notice = null;
                return ScreenAction.None;
            }

            MovePane(Panes, drop.From, drop.To);
            return ScreenAction.None;
        }

        var panes = Panes;
        var hit = _rects.FirstOrDefault(r =>
            input.X >= r.X && input.X < r.X + r.W &&
            input.Y >= r.Y && input.Y < r.Y + r.H);

        if (hit.W == 0 || hit.Index >= panes.Count)
        {
            // Clicking off the tiles is the mouse's Ctrl+]: the wall takes its
            // keys back, so the next one is a wall command rather than another
            // character typed into whichever terminal happened to have focus.
            if (input.Kind == InputKind.MouseDown && !_released)
            {
                _released = true;
                _notice = "keyboard released · click a tile or ctrl+] to type";
            }

            return ScreenAction.None;
        }

        if (input.Kind == InputKind.MouseDown)
        {
            Focus(hit.Index);

            // Pending, not moving: with Moved false this is still just a click,
            // and stays one unless the pointer moves with the button down.
            _carry = (hit.Index, hit.Index, false);
            return ScreenAction.None;
        }

        // The wheel scrolls whichever pane is under the pointer, focused or not -
        // looking back at a pane should not mean taking the keyboard off another.
        var wheeled = LiveTerminal(panes[hit.Index]);
        if (wheeled is null) return ScreenAction.None;

        var alternate = false;
        wheeled.Read(screen => alternate = screen.IsAlternate);

        if (alternate)
        {
            // Claude paints the alternate screen and keeps its own history, so
            // the notch goes to it as a mouse report - the same thing any
            // terminal would send. Coordinates are relative to the pane.
            wheeled.SendWheel(input.Delta, input.X - (hit.X + 2), input.Y - (hit.Y + 1));
            return ScreenAction.None;
        }

        wheeled.Read(screen => screen.ScrollBy(input.Delta * 3));
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
    /// <summary>
    /// Splitting hands the session to a Windows Terminal pane, which is the
    /// opposite of what terminal tiles are for: with them on, everything is
    /// meant to live in this one window.
    /// </summary>
    private bool Splitting => !App.Settings.TerminalTiles;

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
