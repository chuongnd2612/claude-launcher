using ClaudeLauncher.Sessions;
using ClaudeLauncher.Tui;

namespace ClaudeLauncher.Screens;

/// <summary>
/// Every session on disk, across every profile and every project, newest
/// first - the one place to find a conversation again without first
/// remembering which account it was under.
///
/// Building the list means a cwd read per project folder across every
/// config dir, so it runs off the render thread and the screen shows what
/// it has while that finishes.
/// </summary>
public sealed class AllSessionsScreen : ScreenBase
{
    private readonly SessionService? _service;
    private List<PastSession> _all;
    private bool _building;
    private string _filter = string.Empty;
    private bool _filtering;
    private int _index;
    private int _scroll;

    public AllSessionsScreen(App app, SessionService service) : base(app)
    {
        _service = service;
        _all = new List<PastSession>();
        Rebuild();
    }

    /// <summary>Fixture constructor for --selftest.</summary>
    public AllSessionsScreen(App app, List<PastSession> sessions) : base(app)
    {
        _service = null;
        _all = sessions;
    }

    // The scan runs on a background task (Rebuild), so the loop has to be told
    // to come back and look rather than block on a key - the same pattern the
    // dashboard uses for its own background read.
    public override TimeSpan? RefreshInterval =>
        _service is null ? null : TimeSpan.FromMilliseconds(_building ? 120 : 2000);

    public override bool NeedsRedraw() => true;

    private void Rebuild()
    {
        if (_service is null || _building) return;

        _building = true;
        var service = _service;

        Task.Run(() =>
        {
            try
            {
                _all = service.BuildAllSessions();
            }
            catch (Exception)
            {
                _all = new List<PastSession>();
            }
            finally
            {
                _building = false;
                ConsoleInput.Wake();
            }
        });
    }

    private List<PastSession> Visible
    {
        get
        {
            if (string.IsNullOrEmpty(_filter)) return _all;

            return _all.Where(s =>
                s.ProfileName.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                s.ProjectName.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                s.SessionId.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                (s.Title?.Contains(_filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (s.FirstPrompt?.Contains(_filter, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        }
    }

    public override void Render(ScreenBuffer buffer)
    {
        var y = Widgets.Chrome(buffer, 2);
        var margin = Widgets.Margin(buffer);
        var width = buffer.Width - margin * 2;
        var items = Visible;

        var status = _building && _all.Count == 0 ? "reading every profile…" : $"{_all.Count} total";
        Widgets.SectionTitle(buffer, y, "All sessions", status);
        y += 2;

        if (_index >= items.Count) _index = Math.Max(0, items.Count - 1);

        var available = Math.Max(6, buffer.Height - 4 - y);
        var panelHeight = Math.Clamp(items.Count + 4, 8, available);
        buffer.Box(margin, y, width, panelHeight, new Sty(Theme.Border, Theme.Panel), BoxStyle.Rounded, Theme.Panel);
        buffer.Write(margin + 2, y, $" Sessions · {items.Count} ", new Sty(Theme.Blue, Theme.Panel, bold: true));

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
            buffer.Write(cursorX, filterRow, "press / to filter by profile, project, title or id",
                new Sty(Theme.Dim, Theme.Panel, italic: true));
        }

        var listTop = y + 2;
        var listHeight = panelHeight - 3;

        if (items.Count == 0)
        {
            var message = _building
                ? "Reading sessions from every profile…"
                : _all.Count == 0
                    ? "No sessions found in any profile."
                    : "No session matches that filter.";
            buffer.Write(margin + 3, listTop + 1, message, new Sty(Theme.Muted, Theme.Panel, italic: true));
        }

        if (_index < _scroll) _scroll = _index;
        if (_index >= _scroll + listHeight) _scroll = _index - listHeight + 1;
        if (_scroll > Math.Max(0, items.Count - listHeight)) _scroll = Math.Max(0, items.Count - listHeight);
        if (_scroll < 0) _scroll = 0;

        var columns = new Columns(margin, width);

        for (var row = 0; row < listHeight; row++)
        {
            var itemIndex = _scroll + row;
            if (itemIndex >= items.Count) break;

            var session = items[itemIndex];
            SessionReader.Load(session); // only what is on screen
            var selected = itemIndex == _index;
            var rowY = listTop + row;
            var bg = selected ? Theme.PanelSelected : Theme.Panel;

            buffer.Fill(margin + 1, rowY, width - 2, 1, bg);
            buffer.Write(margin + 2, rowY, selected ? "▸" : " ", new Sty(Theme.Blue, bg, bold: true));

            var profile = $"{session.ProfileIcon} {session.ProfileName}".Trim();
            buffer.WriteClipped(columns.ProfileX, rowY, profile, columns.ProfileWidth,
                new Sty(ProfileLook.Color(session.ProfileName), bg));

            buffer.WriteClipped(columns.ProjectX, rowY, session.ProjectName, columns.ProjectWidth,
                new Sty(Theme.VioletSoft, bg));

            var right = $"{Format.Ago(session.LastActivityUtc)}   {Format.Tokens(session.ContextTokens),7}";
            var titleWidth = Math.Max(8, columns.TitleWidth - right.Length - 2);
            buffer.WriteClipped(columns.TitleX, rowY, session.DisplayTitle, titleWidth,
                new Sty(selected ? Theme.Blue : Theme.Text, bg, bold: selected));

            buffer.WriteRight(columns.Right, rowY, right, new Sty(Theme.Dim, bg));
        }

        y += panelHeight + 1;

        Widgets.Footer(buffer, _filtering
            ? new[]
            {
                new KeyHint("type", "Filter"),
                new KeyHint("↑↓", "Navigate"),
                new KeyHint("↵", "Apply"),
                new KeyHint("esc", "Clear")
            }
            : KeyMap.AllSessionsFooter(), KeyMap.Help);
    }

    /// <summary>Column geometry: profile and project stay narrow and fixed, the title takes the rest.</summary>
    private readonly struct Columns
    {
        public readonly int ProfileX;
        public readonly int ProfileWidth;
        public readonly int ProjectX;
        public readonly int ProjectWidth;
        public readonly int TitleX;
        public readonly int TitleWidth;
        public readonly int Right;

        public Columns(int margin, int width)
        {
            Right = margin + width - 3;

            ProfileX = margin + 4;
            ProfileWidth = width >= 100 ? 14 : 10;
            ProjectX = ProfileX + ProfileWidth + 2;
            ProjectWidth = width >= 100 ? 18 : 14;
            TitleX = ProjectX + ProjectWidth + 2;
            TitleWidth = Math.Max(10, Right - TitleX);
        }
    }

    public override ScreenAction HandleKey(ConsoleKeyInfo key)
    {
        var items = Visible;

        if (_filtering)
        {
            switch (key.Key)
            {
                case ConsoleKey.F1:
                    return ScreenAction.Push(new KeysScreen(App, "All sessions", KeyMap.AllSessions()));
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
                return Resume(items[_index]);
            case ConsoleKey.F1:
                return ScreenAction.Push(new KeysScreen(App, "All sessions", KeyMap.AllSessions()));
            case ConsoleKey.Escape:
            case ConsoleKey.Backspace:
                return ScreenAction.Back;
        }

        if (KeyBindings.Is(KeyAction.Filter, key)) { _filtering = true; return ScreenAction.None; }
        if (KeyBindings.Is(KeyAction.Quit, key)) return ScreenAction.Exit;

        if (items.Count == 0) return ScreenAction.None;

        if (KeyBindings.Is(KeyAction.ResumeLog, key))
            return ScreenAction.Push(new SessionDetailScreen(App, items[_index]));

        return ScreenAction.None;
    }

    private void Move(int delta, int count)
    {
        if (count == 0) return;
        _index = Math.Clamp(_index + delta, 0, count - 1);
    }

    /// <summary>
    /// Points the profile and project at the session picked, then resumes it
    /// exactly as the per-project picker does - into a tile when tiles are on
    /// and this console is the target, otherwise handed to the wrapper.
    /// </summary>
    private ScreenAction Resume(PastSession session)
    {
        var profile = App.State.Profiles.FirstOrDefault(p =>
            StateStore.ExpandHome(p.ConfigDir).Equals(session.ConfigDir, StringComparison.OrdinalIgnoreCase));
        if (profile is null) return ScreenAction.None;

        App.Profile = profile;
        App.Project = new ProjectEntry { Name = session.ProjectName, Path = session.ProjectPath };

        var openIn = LaunchTarget.Normalize(App.Settings.DefaultOpenIn);

        if (App.Settings.TerminalTiles && openIn == LaunchTarget.Current)
        {
            try
            {
                var tile = Terminal.TerminalTile.Start(
                    session.ProjectPath, session.ProjectName,
                    session.ConfigDir, 100, 30, session.SessionId);

                App.AddTerminal(tile);
                return ScreenAction.Root(new TerminalsScreen(App, new SessionService(App.State), tile));
            }
            catch (Exception)
            {
                // No pseudo console available: fall through to the wrapper.
            }
        }

        return ScreenAction.Resume(session.SessionId, openIn);
    }
}
