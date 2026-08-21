using System.Diagnostics;
using ClaudeLauncher.Sessions;
using ClaudeLauncher.Tui;

namespace ClaudeLauncher.Screens;

/// <summary>
/// The hub: what is running right now, where it is, and how to get back to it.
/// Shown instead of the wizard when at least one Claude session is alive.
/// </summary>
public sealed class HomeScreen : ScreenBase
{
    private readonly SessionService? _service;
    private SessionSnapshot _snapshot;
    private int _index;
    private int _scroll;
    private string? _notice;

    /// <summary>
    /// Terminals that were open when the launcher last closed. They die with it
    /// by design, so the only thing worth keeping was the list - this is the
    /// offer to bring them all back.
    /// </summary>
    private List<WorkspaceEntry> Restorable => Workspace.Restorable()
        .Where(e => !App.Terminals.Any(t => !t.HasExited && t.SessionId == e.SessionId))
        .ToList();

    public HomeScreen(App app, SessionService service) : base(app)
    {
        _service = service;
        // Sessions this launcher started are always ours to show, whatever
        // Claude records them as.
        _service.OwnedSessionIds = () => app.Chats
            .Where(c => c.SessionId is not null)
            .Select(c => c.SessionId!)
            .Concat(app.Terminals.Select(t => t.SessionId))
            .Where(id => !string.IsNullOrEmpty(id))
            .ToArray();

        _snapshot = service.Build();
    }

    /// <summary>Fixture constructor: --selftest renders a fixed snapshot so CI output is stable.</summary>
    public HomeScreen(App app, SessionSnapshot snapshot) : base(app)
    {
        _service = null;
        _snapshot = snapshot;
    }

    /// <summary>Sessions come and go while this screen is open, so it repaints on a timer.</summary>
    public override TimeSpan? RefreshInterval => _service is null ? null : TimeSpan.FromSeconds(1);

    public override bool NeedsRedraw()
    {
        if (_service is null) return false;
        _snapshot = _service.Build();
        return true;
    }

    /// <summary>
    /// Sessions on disk, plus the chats this launcher owns. A chat has no
    /// session id until Claude's first reply, so it cannot be found on disk yet
    /// - and a chat you just started must not be missing from the one screen
    /// that exists to find it again.
    /// </summary>
    private IReadOnlyList<SessionRow> Rows
    {
        get
        {
            var rows = new List<SessionRow>(_snapshot.Sessions);

            foreach (var chat in App.Chats)
            {
                if (chat.State == ChatState.Ended) continue;
                if (chat.SessionId is not null && rows.Any(r => r.SessionId == chat.SessionId)) continue;

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

            return rows;
        }
    }

    public override void Render(ScreenBuffer buffer)
    {
        var y = Widgets.CompactChrome(buffer);
        var margin = Widgets.Margin(buffer);
        var width = buffer.Width - margin * 2;

        var running = Rows.Count;
        var status = running == 1 ? "1 session running" : $"{running} sessions running";
        if (_snapshot.SessionsToday > 0) status += $" · {_snapshot.SessionsToday} started today";

        Widgets.SectionTitle(buffer, y, "Home", status);
        y += 2;

        // The dashboard's headline numbers here rather than only behind d: what
        // Claude has done today, and what each profile has cost, is the first
        // thing worth knowing on opening the launcher. The one-line form is a
        // fallback for when the panels below cannot fit.
        var dashboard = _service is null
            ? null
            : Metrics.Cached(App.State, _snapshot, Period.Today, ConsoleInput.Wake);

        var panelsFit = dashboard is not null && buffer.Height >= 34;
        if (!panelsFit) y = Summary(buffer, margin, y, width, dashboard);

        // Running sessions
        var listHeight = Math.Clamp(running + 3, 5, Math.Max(5, buffer.Height - y - 14));
        Widgets.TitledBox(buffer, margin, y, width, listHeight, $"Running sessions · {running}", Theme.Blue);

        if (running == 0)
        {
            buffer.WriteClipped(margin + 3, y + 1, "No Claude sessions running.", width - 6,
                new Sty(Theme.Muted, Theme.Panel, italic: true));
            buffer.WriteClipped(margin + 3, y + 2, "Press n to start one.", width - 6,
                new Sty(Theme.Dim, Theme.Panel));
        }
        else
        {
            var columns = new Columns(margin, width);
            Header(buffer, y + 1, columns);
            DrawRows(buffer, margin, y + 2, width, listHeight - 3, columns);
        }

        y += listHeight + 1;

        // Recent projects
        var recentHeight = Math.Min(_snapshot.Recent.Count + 2, 8);
        if (_snapshot.Recent.Count > 0 && y + recentHeight + 5 <= buffer.Height - 4)
        {
            Widgets.TitledBox(buffer, margin, y, width, recentHeight,
                $"Recent projects · {_snapshot.Recent.Count}", Theme.VioletSoft);

            for (var i = 0; i < Math.Min(_snapshot.Recent.Count, recentHeight - 2); i++)
            {
                var project = _snapshot.Recent[i];
                var row = y + 1 + i;
                buffer.WriteClipped(margin + 3, row, project.Name, 22, new Sty(Theme.Text, Theme.Panel));
                buffer.WriteClipped(margin + 27, row, project.Path, width - 40,
                    new Sty(Theme.Dim, Theme.Panel));
                buffer.WriteRight(margin + width - 3, row, Format.Ago(project.LastUsedUtc),
                    new Sty(Theme.Muted, Theme.Panel));
            }

            y += recentHeight + 1;
        }

        // The dashboard's own panels, appended here rather than kept behind d.
        // They are the last thing drawn, so a short window loses them before it
        // loses the session list - and the summary line covers that case.
        if (panelsFit && dashboard is not null)
        {
            var room = buffer.Height - 6 - y;
            if (room >= 7)
            {
                var bottom = buffer.Height - 6;
                var columns = width >= 108 ? 2 : 1;
                var columnWidth = columns == 2 ? (width - 2) / 2 : width;

                if (columns == 2)
                {
                    var left = DashboardPanels.Usage(buffer, margin, y, columnWidth, dashboard, bottom, App.Settings);
                    DashboardPanels.Work(buffer, margin, left + 1, columnWidth, dashboard, bottom, App.Settings);

                    var secondX = margin + columnWidth + 2;
                    var right = DashboardPanels.Activity(buffer, secondX, y, columnWidth, dashboard, bottom, App.Settings);
                    DashboardPanels.Projects(buffer, secondX, right + 1, columnWidth, dashboard, bottom, App.Settings);
                }
                else
                {
                    var next = DashboardPanels.Usage(buffer, margin, y, columnWidth, dashboard, bottom, App.Settings);
                    DashboardPanels.Activity(buffer, margin, next + 1, columnWidth, dashboard, bottom, App.Settings);
                }
            }
            else
            {
                // Not enough for a box, but the one-liner still fits.
                Summary(buffer, margin, y, width, dashboard);
            }
        }

        // A newer release is worth one line and a key, not a dialog in front of
        // what you came here to do. A real notice always wins the row.
        var update = UpdateBanner.Line();
        var line = _notice ?? update?.Text;

        if (line is not null && y < buffer.Height - 5)
        {
            buffer.WriteClipped(margin + 1, y, line, width - 2,
                new Sty(_notice is null ? update!.Value.Color : Theme.Amber, Theme.Bg));
        }

        var hints = new List<KeyHint>
        {
            new("↑↓", "Navigate"),
            new("↵", "Open"),
            new("a", "Attach"),
            new("n", "New")
        };

        if (Restorable.Count > 0) hints.Add(new KeyHint("r", "Reopen last"));

        hints.Add(new KeyHint("t", "Tile"));
        hints.Add(new KeyHint("d", "Dashboard"));
        hints.Add(new KeyHint("k", "Kill"));
        hints.Add(new KeyHint("p", "Profile"));
        hints.Add(new KeyHint("q", "Quit"));

        Widgets.Footer(buffer, hints.ToArray());
    }

    /// <summary>
    /// Column geometry, measured from the right edge so the state text is never
    /// the thing that gets squeezed. The model is dropped first on narrow windows.
    /// </summary>
    private readonly struct Columns
    {
        public readonly int ProjectX;
        public readonly int ProjectWidth;
        public readonly int TaskX;
        public readonly int TaskWidth;
        public readonly int StateX;
        public readonly int StateWidth;
        public readonly int Right;
        public readonly bool ShowModel;

        public Columns(int margin, int width)
        {
            ShowModel = width >= 92;
            Right = margin + width - 3;

            ProjectX = margin + 5;
            ProjectWidth = width >= 110 ? 18 : 14;
            TaskX = ProjectX + ProjectWidth + 2;

            StateWidth = 16;
            var rightBlock = ShowModel ? 21 : 9;
            StateX = Math.Max(TaskX + 10, Right - rightBlock - StateWidth);
            TaskWidth = Math.Max(8, StateX - TaskX - 2);
        }
    }

    /// <summary>The live chat behind a row, matched by id or by project before one exists.</summary>
    private StreamSession? Chat(SessionRow row) => App.Chats.FirstOrDefault(c =>
        c.State != ChatState.Ended &&
        (c.SessionId is not null && c.SessionId == row.SessionId ||
         string.IsNullOrEmpty(row.SessionId) &&
         string.Equals(c.ProjectPath, row.ProjectPath, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Two dim lines: today's counts, and what each profile has cost. Cached and
    /// refreshed in the background, so drawing it costs nothing per frame - and
    /// absent entirely until the first answer arrives, rather than showing
    /// zeroes that are about to change.
    /// </summary>
    private int Summary(ScreenBuffer buffer, int margin, int y, int width, DashboardData? data)
    {
        if (data is null) return y;

        var totals = data.Totals;
        var counts = $"today · {totals.Sessions} sessions · {totals.Prompts} prompts · " +
                     $"{totals.FilesTouched} files · {totals.Edits} edits · {totals.PullRequests} PRs";

        buffer.WriteClipped(margin + 1, y, counts, width - 2, new Sty(Theme.Muted, Theme.Bg));

        var spend = new List<(string Text, Rgb Color)>();
        foreach (var profile in data.Profiles)
        {
            if (profile.OutputTokens == 0 && !profile.HasCost) continue;

            var text = App.Settings.ShowCosts && profile.HasCost
                ? $"{profile.Icon} {profile.Label} ${profile.CostUsd:0.00}"
                : $"{profile.Icon} {profile.Label} {Format.Tokens(profile.OutputTokens)}";

            spend.Add((text, ProfileLook.Color(profile.Key)));
        }

        if (spend.Count == 0) return y + 2;

        // On the same row when there is room, because two dim lines above the
        // session list is already as much as this screen can spare.
        var wanted = spend.Sum(entry => entry.Text.Length + 3);

        if (counts.Length + wanted + 8 <= width)
        {
            var at = margin + width - wanted;
            foreach (var (text, color) in spend)
            {
                at = buffer.Write(at, y, text, new Sty(color, Theme.Bg));
                at = buffer.Write(at, y, "   ", new Sty(Theme.Dim, Theme.Bg));
            }

            return y + 2;
        }

        if (y + 1 >= buffer.Height - 6) return y + 2;

        var second = margin + 1;
        foreach (var (text, color) in spend)
        {
            if (second + text.Length + 3 > margin + width) break;
            second = buffer.Write(second, y + 1, text, new Sty(color, Theme.Bg));
            second = buffer.Write(second, y + 1, "   ", new Sty(Theme.Dim, Theme.Bg));
        }

        return y + 3;
    }

    private static void Header(ScreenBuffer buffer, int y, in Columns c)
    {
        var style = new Sty(Theme.Dim, Theme.Panel, italic: true);
        buffer.Write(c.ProjectX, y, "project", style);
        buffer.Write(c.TaskX, y, "task", style);
        buffer.Write(c.StateX, y, "state", style);
        buffer.WriteRight(c.Right, y, c.ShowModel ? "context   model" : "context", style);
    }

    private void DrawRows(ScreenBuffer buffer, int margin, int top, int width, int rows, in Columns c)
    {
        if (rows <= 0) return;

        if (_index >= Rows.Count) _index = Math.Max(0, Rows.Count - 1);
        if (_index < _scroll) _scroll = _index;
        if (_index >= _scroll + rows) _scroll = _index - rows + 1;

        for (var i = 0; i < rows; i++)
        {
            var itemIndex = _scroll + i;
            if (itemIndex >= Rows.Count) break;

            var row = Rows[itemIndex];
            var selected = itemIndex == _index;

            // Claude records no status for SDK sessions, so a chat we own would
            // read "unknown" while it is plainly working. Ask the session itself.
            var chat = Chat(row);
            var state = chat is null
                ? row.State
                : chat.Pending is not null ? SessionState.Waiting
                : chat.State == ChatState.Working ? SessionState.Running
                : SessionState.Idle;
            var rowY = top + i;
            var bg = selected ? Theme.PanelSelected : Theme.Panel;

            buffer.Fill(margin + 1, rowY, width - 2, 1, bg);
            buffer.Write(margin + 2, rowY, selected ? "▸" : " ", new Sty(Theme.Blue, bg, bold: true));

            buffer.WriteClipped(c.ProjectX, rowY, row.ProjectName, c.ProjectWidth,
                new Sty(selected ? Theme.Blue : Theme.Text, bg, bold: selected));

            buffer.WriteClipped(c.TaskX, rowY, row.Task, c.TaskWidth, new Sty(Theme.TextSoft, bg));

            var stateColor = state switch
            {
                SessionState.Running => Theme.Green,
                SessionState.Waiting => Theme.Amber,
                SessionState.Unknown => Theme.Dim,
                _ => Theme.Muted
            };

            var stateText = chat is null
                ? Format.State(state, row.StateAge)
                : $"{Format.State(state, row.StateAge)} · chat";

            buffer.WriteClipped(c.StateX, rowY, stateText, c.StateWidth, new Sty(stateColor, bg));

            var right = c.ShowModel
                ? $"{Format.Tokens(row.ContextTokens),7}   {row.Model ?? "-",-10}"
                : $"{Format.Tokens(row.ContextTokens),7}";
            buffer.WriteRight(c.Right, rowY, right, new Sty(Theme.Dim, bg));
        }
    }

    public override ScreenAction HandleKey(ConsoleKeyInfo key)
    {
        _notice = null;

        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                Move(-1);
                return ScreenAction.None;
            case ConsoleKey.DownArrow:
            case ConsoleKey.Tab:
                Move(1);
                return ScreenAction.None;
            case ConsoleKey.Home:
                _index = 0;
                return ScreenAction.None;
            case ConsoleKey.End:
                _index = Math.Max(0, Rows.Count - 1);
                return ScreenAction.None;
            case ConsoleKey.Enter:
                return Open();
            case ConsoleKey.Escape:
                return ScreenAction.Exit;
        }

        switch (char.ToLowerInvariant(key.KeyChar))
        {
            case 'n':
            case 'p':
                return ScreenAction.Push(new ProfileScreen(App));
            case 'r':
                return Restore();
            case 't':
                if (_service is null || Rows.Count == 0) return ScreenAction.None;
                return ScreenAction.Push(new TerminalsScreen(App, _service));
            case 'a':
                return Attach();
            case 'k':
                return Kill();
            case 'd':
                return _service is null
                    ? ScreenAction.None
                    : ScreenAction.Push(new DashboardScreen(App, _service));
            case 'u':
                return UpdateBanner.Pressed(App);
            case 'q':
                return ScreenAction.Exit;
        }

        return ScreenAction.None;
    }

    /// <summary>
    /// Raises Windows Terminal and names the pane to look for.
    ///
    /// It deliberately does not call "wt focus-pane": that takes Windows
    /// Terminal's own pane id, which cannot be read back from the CLI. Guessing
    /// one would sometimes switch the user to an unrelated pane, which is worse
    /// than telling them where to look.
    /// </summary>
    /// <summary>
    /// Enter opens the wall on the chosen session. Everything the launcher owns
    /// is shown there, and a session in someone else's terminal at least gets
    /// its tile put in front of you - raising a window and saying "find the
    /// pane yourself" was never much of an answer. `a` still raises it.
    /// </summary>
    private ScreenAction Open()
    {
        if (Rows.Count == 0) return ScreenAction.Push(new ProfileScreen(App));

        var row = Rows[_index];
        return ScreenAction.Root(new TerminalsScreen(App, new SessionService(App.State), row.SessionId));
    }

    private ScreenAction Attach()
    {
        if (Rows.Count == 0) return ScreenAction.Push(new ProfileScreen(App));

        var row = Rows[_index];

        // A chat the launcher owns reopens where it left off; there is no
        // terminal to raise for it.
        var chat = Chat(row);
        if (chat is not null) return ScreenAction.Push(new ChatScreen(App, chat));

        if (!TerminalWindow.Raise())
        {
            _notice = "Windows Terminal is not running - that session is in another terminal.";
            return ScreenAction.None;
        }

        _notice = $"Switched to Windows Terminal · look for the pane in {row.ProjectName}.";
        return ScreenAction.None;
    }

    /// <summary>Brings back every terminal that was open last time, in one go.</summary>
    private ScreenAction Restore()
    {
        var waiting = Restorable.Count;
        if (waiting == 0)
        {
            _notice = "nothing to reopen - no terminals were open last time";
            return ScreenAction.None;
        }

        var restored = App.RestoreTerminals(out var failure);

        if (restored == 0)
        {
            _notice = failure is null ? "could not reopen those terminals" : "could not reopen: " + failure;
            return ScreenAction.None;
        }

        var service = _service ?? new SessionService(App.State);
        return ScreenAction.Root(new TerminalsScreen(App, service));
    }

    private ScreenAction Kill()
    {
        if (Rows.Count == 0) return ScreenAction.None;

        var row = Rows[_index];
        return ScreenAction.Push(new KillSessionScreen(App, row, () =>
        {
            if (_service is not null) _snapshot = _service.Build();
        }));
    }

    private void Move(int delta)
    {
        if (Rows.Count == 0) return;
        _index = Math.Clamp(_index + delta, 0, Rows.Count - 1);
    }
}
