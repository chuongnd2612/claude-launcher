using ClaudeLauncher.Tui;

namespace ClaudeLauncher.Screens;

/// <summary>Step 3 - choose how Claude Code should start.</summary>
public sealed class SessionScreen : ScreenBase
{
    private sealed record Option(string Mode, string Glyph, string Title, string Detail, string Hint);

    private static readonly Option[] Launching =
    {
        new("new", "▶", "New session", "Start a fresh conversation in this project", "claude"),
        new("continue", "→", "Continue", "Pick up the most recent conversation", "claude --continue"),
        new("resume", "↻", "Resume", "Choose from earlier sessions to resume", "claude --resume"),
        new("chat", "▣", "Chat here", "Type to Claude inside the launcher, no new window", "stream")
    };

    /// <summary>
    /// With terminal tiles on, every option already opens inside the launcher,
    /// so a separate "Chat here" row would be the same thing under a second
    /// name. Three rows, and Enter lands on Claude straight away.
    /// </summary>
    private static readonly Option[] Tiled =
    {
        new("new", "▶", "New session", "Start a fresh conversation in this project", "claude"),
        new("continue", "→", "Continue", "Pick up the most recent conversation", "claude --continue"),
        new("resume", "↻", "Resume", "Choose from earlier sessions to resume", "claude --resume")
    };

    private readonly Option[] Options;

    private int _index;
    private string _openIn;
    private string? _notice;

    public SessionScreen(App app) : base(app)
    {
        _openIn = LaunchTarget.Normalize(app.Settings.DefaultOpenIn);
        Options = InLauncher(app, _openIn) ? Tiled : Launching;

        var preferred = Array.FindIndex(Options, o => o.Mode == app.Settings.DefaultMode);
        _index = preferred >= 0 ? preferred : 0;
    }

    /// <summary>
    /// A session opens inside the launcher only when tiles are on *and* the
    /// target is this console. Choosing a tab or a pane is an explicit ask for
    /// a real window, and must keep winning.
    /// </summary>
    private static bool InLauncher(App app, string openIn) =>
        app.Settings.TerminalTiles && openIn == LaunchTarget.Current;

    public override void Render(ScreenBuffer buffer)
    {
        var y = Widgets.Chrome(buffer, 2);
        var margin = Widgets.Margin(buffer);
        var profile = App.Profile!;
        var project = App.Project!;

        Widgets.SectionTitle(buffer, y, $"{profile.DisplayIcon}  {profile.DisplayLabel}  ·  {project.Name}", "How should Claude start?");
        y += 2;

        var width = buffer.Width - margin * 2;
        var cardWidth = Math.Min(width, Math.Max(44, width * 2 / 3));

        // Every option has to stay visible: an option you cannot see is one you
        // do not know exists. Cards lose their gaps first, then their boxes.
        // The summary is counted in, so gaps go before it does.
        // A card is 3 rows and each one after the first starts a stride later,
        // so n cards need (n-1)*stride + 3 - counting one row short drops the
        // last option instead of switching to the compact list.
        const int summaryRows = 8;
        var room = buffer.Height - 4 - y;
        var stride = room >= (Options.Length - 1) * 4 + 3 + summaryRows ? 4 : 3;
        var compact = room < (Options.Length - 1) * 3 + 3;

        if (compact)
        {
            CompactOptions(buffer, margin, y, cardWidth);
            y += Options.Length;
        }

        for (var i = 0; i < Options.Length && !compact; i++)
        {
            var option = Options[i];
            var selected = i == _index;
            var cardY = y + i * stride;
            if (cardY + 3 > buffer.Height - 4) break;

            Widgets.Panel(buffer, margin, cardY, cardWidth, 3, selected);
            var bg = selected ? Theme.PanelSelected : Theme.Panel;

            buffer.Write(margin + 2, cardY + 1, option.Glyph,
                new Sty(selected ? Theme.Blue : Theme.Muted, bg, bold: true));

            var textX = margin + 5;
            buffer.WriteClipped(textX, cardY + 1, option.Title, 16,
                new Sty(selected ? Theme.Blue : Theme.Text, bg, bold: true));

            buffer.WriteClipped(textX + 16, cardY + 1, option.Detail, cardWidth - (textX - margin) - 18 - option.Hint.Length,
                new Sty(Theme.Muted, bg));

            buffer.WriteRight(margin + cardWidth - 3, cardY + 1, option.Hint,
                new Sty(selected ? Theme.VioletSoft : Theme.Dim, bg, italic: true));
        }

        var summaryY = compact ? y + 1 : y + Options.Length * stride;
        if (summaryY + 7 <= buffer.Height - 4)
        {
            Widgets.TitledBox(buffer, margin, summaryY, width, 7, "Launch summary", Theme.VioletSoft);
            Row(buffer, margin + 3, summaryY + 1, "Profile", profile.DisplayLabel, width);
            Row(buffer, margin + 3, summaryY + 2, "Config", StateStore.ExpandHome(profile.ConfigDir), width);
            Row(buffer, margin + 3, summaryY + 3, "Project", project.Name, width);
            Row(buffer, margin + 3, summaryY + 4, "Path", project.Path, width);
            Row(buffer, margin + 3, summaryY + 5, "Opens in", LaunchTarget.Describe(_openIn), width);
        }

        if (_notice is not null)
            buffer.WriteClipped(margin + 1, buffer.Height - 5, _notice, width - 2, new Sty(Theme.Amber, Theme.Bg));

        Widgets.Footer(buffer, new[]
        {
            new KeyHint("↑↓", "Navigate"),
            new KeyHint("↵", "Launch"),
            new KeyHint("o", "Open in"),
            new KeyHint("n/c/r", "Quick mode"),
            new KeyHint("esc", "Back"),
            new KeyHint("q", "Quit")
        });
    }

    /// <summary>One line per option, for windows too short to box them.</summary>
    private void CompactOptions(ScreenBuffer buffer, int margin, int y, int width)
    {
        for (var i = 0; i < Options.Length; i++)
        {
            var option = Options[i];
            var selected = i == _index;
            var rowY = y + i;
            var bg = selected ? Theme.PanelSelected : Theme.Panel;

            buffer.Fill(margin, rowY, width, 1, bg);
            buffer.Write(margin + 1, rowY, option.Glyph, new Sty(selected ? Theme.Blue : Theme.Muted, bg, bold: true));
            buffer.WriteClipped(margin + 4, rowY, option.Title, 16,
                new Sty(selected ? Theme.Blue : Theme.Text, bg, bold: selected));
            buffer.WriteClipped(margin + 21, rowY, option.Detail, Math.Max(0, width - 23),
                new Sty(Theme.Muted, bg));
        }
    }

    private static void Row(ScreenBuffer buffer, int x, int y, string label, string value, int width)
    {
        buffer.Write(x, y, label.PadRight(9), new Sty(Theme.Dim, Theme.Panel));
        buffer.WriteClipped(x + 10, y, value, width - 14, new Sty(Theme.TextSoft, Theme.Panel));
    }

    public override ScreenAction HandleKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                _index = Math.Max(0, _index - 1);
                return ScreenAction.None;
            case ConsoleKey.DownArrow:
            case ConsoleKey.Tab:
                _index = Math.Min(Options.Length - 1, _index + 1);
                return ScreenAction.None;
            case ConsoleKey.Enter:
            case ConsoleKey.Spacebar:
                return Choose(Options[_index].Mode);
            case ConsoleKey.LeftArrow:
                _openIn = LaunchTarget.Next(_openIn, -1);
                return ScreenAction.None;
            case ConsoleKey.RightArrow:
                _openIn = LaunchTarget.Next(_openIn);
                return ScreenAction.None;
            case ConsoleKey.Escape:
            case ConsoleKey.Backspace:
                return ScreenAction.Back;
        }

        switch (char.ToLowerInvariant(key.KeyChar))
        {
            case 'o': _openIn = LaunchTarget.Next(_openIn); return ScreenAction.None;
            case 'n': return Choose("new");
            case 'c': return Choose("continue");
            case 'r': return Choose("resume");
            case 'h': return Choose("chat");
            case 'q': return ScreenAction.Exit;
        }

        return ScreenAction.None;
    }

    /// <summary>
    /// The newest recorded session for this project, or null when there is
    /// none - in which case Continue simply starts a fresh conversation.
    /// </summary>
    private string? LatestSessionId()
    {
        var recorded = Sessions.SessionReader.ListProjectSessions(
            StateStore.ExpandHome(App.Profile!.ConfigDir), App.Project!.Path);

        return recorded
            .OrderByDescending(s => s.LastActivityUtc)
            .Select(s => s.SessionId)
            .FirstOrDefault();
    }

    /// <summary>
    /// Starts a tile for this project, or returns null and leaves a notice when
    /// a pseudo console is unavailable, so the caller can fall back.
    /// </summary>
    private ScreenAction? OpenTile(string? resumeSessionId = null)
    {
        try
        {
            var tile = Terminal.TerminalTile.Start(
                App.Project!.Path, App.Project!.Name,
                StateStore.ExpandHome(App.Profile!.ConfigDir), 100, 30, resumeSessionId);

            App.AddTerminal(tile);

            // Straight onto the wall: a session is rarely the only one, and
            // landing there means the others stay in sight. Zoom (z) is the
            // full-window view when one is wanted.
            return ScreenAction.Root(new TerminalsScreen(App, new Sessions.SessionService(App.State), tile));
        }
        catch (Exception ex)
        {
            // A pseudo console needs Windows 10 1809 or newer. Falling back
            // beats refusing to open a session.
            _notice = "terminal unavailable: " + ex.Message;
            return null;
        }
    }

    /// <summary>
    /// Resume shows the picker when this project has transcripts, so the choice
    /// is made against titles and prompts rather than inside Claude's own list.
    /// With none to show, it falls through to a bare --resume.
    /// </summary>
    private ScreenAction Choose(string mode)
    {
        // A session opened inside the launcher keeps the launcher running and
        // owns the process, so it never writes result.json - there is nothing
        // for the wrapper to launch.
        //
        // New, Continue and Resume all land straight on Claude when tiles are
        // on and the target is this console. Resume still shows its picker
        // first, because choosing which conversation is the point of it.
        if (InLauncher(App, _openIn) && mode is "new" or "continue")
        {
            // Continue resolves to the newest recorded session rather than
            // passing --continue: a tile without an id cannot be told apart
            // from its project's other sessions on the wall.
            var tile = OpenTile(mode == "continue" ? LatestSessionId() : null);
            if (tile is not null) return tile;

            // The tile could not start and OpenTile left a notice saying why.
            // Falling through would hand the launch to the wrapper and exit,
            // taking the explanation with it.
            return ScreenAction.None;
        }

        if (mode == "chat")
        {
            var session = new Sessions.StreamSession(App.Profile!, App.Project!.Path);
            session.Start();

            // Registered so it survives leaving the screen and can be found
            // again from Home or the terminal wall.
            App.Chats.Add(session);
            return ScreenAction.Push(new ChatScreen(App, session));
        }

        if (mode != "resume") return ScreenAction.Finish(mode, _openIn);

        if (InLauncher(App, _openIn))
        {
            var recorded = Sessions.SessionReader.ListProjectSessions(
                StateStore.ExpandHome(App.Profile!.ConfigDir), App.Project!.Path);

            // Nothing recorded yet, so there is nothing to pick from: a bare
            // --continue is the closest honest answer.
            if (recorded.Count > 0) return ScreenAction.Push(new ResumeScreen(App, _openIn));

            var latest = OpenTile();
            if (latest is not null) return latest;

            return ScreenAction.None;
        }

        var sessions = Sessions.SessionReader.ListProjectSessions(
            StateStore.ExpandHome(App.Profile!.ConfigDir), App.Project!.Path);

        if (sessions.Count == 0) return ScreenAction.Finish(mode, _openIn);

        return ScreenAction.Push(new ResumeScreen(App, _openIn));
    }
}
