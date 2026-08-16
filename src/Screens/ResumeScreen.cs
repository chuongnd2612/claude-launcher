using ClaudeLauncher.Sessions;
using ClaudeLauncher.Tui;

namespace ClaudeLauncher.Screens;

/// <summary>
/// Picks which earlier conversation to resume, instead of handing Claude a bare
/// --resume and letting it ask.
/// </summary>
public sealed class ResumeScreen : ScreenBase
{
    private readonly List<PastSession> _all;
    private readonly string _openIn;
    private string _filter = string.Empty;
    private bool _filtering;
    private int _index;
    private int _scroll;

    public ResumeScreen(App app, string openIn) : base(app)
    {
        _openIn = openIn;
        _all = SessionReader.ListProjectSessions(
            StateStore.ExpandHome(app.Profile!.ConfigDir), app.Project!.Path);
    }

    /// <summary>Fixture constructor for --selftest.</summary>
    public ResumeScreen(App app, List<PastSession> sessions) : base(app)
    {
        _openIn = LaunchTarget.Current;
        _all = sessions;
    }

    private List<PastSession> Visible
    {
        get
        {
            if (string.IsNullOrEmpty(_filter)) return _all;

            return _all.Where(s =>
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

        Widgets.SectionTitle(buffer,
            y, $"{App.Profile!.DisplayIcon}  {App.Profile.DisplayLabel}  ·  {App.Project!.Name}  ·  Resume",
            "Which session?");
        y += 2;

        if (_index >= items.Count) _index = Math.Max(0, items.Count - 1);

        var available = Math.Max(6, buffer.Height - 4 - y);
        var panelHeight = Math.Clamp(items.Count + 4, 8, available);
        buffer.Box(margin, y, width, panelHeight, new Sty(Theme.Border, Theme.Panel), BoxStyle.Rounded, Theme.Panel);
        buffer.Write(margin + 2, y, $" Sessions · {_all.Count} ", new Sty(Theme.Blue, Theme.Panel, bold: true));

        // Filter row, matching the project screen's grammar.
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
            buffer.Write(cursorX, filterRow, "press / to filter by prompt or id",
                new Sty(Theme.Dim, Theme.Panel, italic: true));
        }

        var listTop = y + 2;
        var listHeight = panelHeight - 3;

        if (items.Count == 0)
        {
            var message = _all.Count == 0
                ? "No earlier sessions for this project."
                : "No session matches that filter.";
            buffer.Write(margin + 3, listTop + 1, message, new Sty(Theme.Muted, Theme.Panel, italic: true));
        }

        if (_index < _scroll) _scroll = _index;
        if (_index >= _scroll + listHeight) _scroll = _index - listHeight + 1;
        if (_scroll > Math.Max(0, items.Count - listHeight)) _scroll = Math.Max(0, items.Count - listHeight);
        if (_scroll < 0) _scroll = 0;

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

            buffer.WriteClipped(margin + 4, rowY, session.ShortId, 9, new Sty(Theme.VioletSoft, bg));

            var right = $"{Format.Ago(session.LastActivityUtc)}   {Format.Tokens(session.ContextTokens),7}";
            var titleWidth = Math.Max(10, width - 20 - right.Length);
            buffer.WriteClipped(margin + 14, rowY, session.DisplayTitle, titleWidth,
                new Sty(selected ? Theme.Blue : Theme.Text, bg, bold: selected));

            buffer.WriteRight(margin + width - 3, rowY, right, new Sty(Theme.Dim, bg));
        }

        y += panelHeight + 1;

        // The prompt that started the highlighted session, which is usually
        // what tells them apart when the titles are similar.
        if (items.Count > 0 && y + 4 <= buffer.Height - 4)
        {
            var session = items[_index];
            SessionReader.Load(session);

            Widgets.TitledBox(buffer, margin, y, width, 4, "Opening prompt", Theme.VioletSoft);
            buffer.WriteClipped(margin + 3, y + 1, session.FirstPrompt ?? "(not recorded)", width - 6,
                new Sty(Theme.TextSoft, Theme.Panel, italic: session.FirstPrompt is null));

            var meta = session.Model is null ? session.SessionId : $"{session.SessionId} · {session.Model}";
            buffer.WriteClipped(margin + 3, y + 2, meta, width - 6, new Sty(Theme.Dim, Theme.Panel));
        }

        Widgets.Footer(buffer, _filtering
            ? new[]
            {
                new KeyHint("type", "Filter"),
                new KeyHint("↑↓", "Navigate"),
                new KeyHint("↵", "Apply"),
                new KeyHint("esc", "Clear")
            }
            : new[]
            {
                new KeyHint("↑↓", "Navigate"),
                new KeyHint("↵", "Resume"),
                new KeyHint("c", "Chat here"),
                new KeyHint("/", "Filter"),
                new KeyHint("l", "Logs"),
                new KeyHint("d", "Delete"),
                new KeyHint("esc", "Back")
            });
    }

    public override ScreenAction HandleKey(ConsoleKeyInfo key)
    {
        var items = Visible;

        if (_filtering)
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
                return ScreenAction.Resume(items[_index].SessionId, _openIn);
            case ConsoleKey.Escape:
            case ConsoleKey.Backspace:
                return ScreenAction.Back;
        }

        var ch = char.ToLowerInvariant(key.KeyChar);
        if (ch == '/') { _filtering = true; return ScreenAction.None; }
        if (ch == 'q') return ScreenAction.Exit;

        if (items.Count == 0) return ScreenAction.None;

        // Pick the conversation back up inside the launcher rather than in a
        // terminal. Claude reloads it from disk, so nothing is lost either way.
        if (ch == 'c')
        {
            var session = new Sessions.StreamSession(App.Profile!, App.Project!.Path);
            session.Start(items[_index].SessionId);
            App.Chats.Add(session);
            return ScreenAction.Push(new ChatScreen(App, session));
        }

        if (ch == 'l') return ScreenAction.Push(new SessionDetailScreen(App, items[_index]));

        if (ch == 'd')
        {
            var session = items[_index];
            return ScreenAction.Push(new DeleteSessionScreen(App, session, () => _all.Remove(session)));
        }

        return ScreenAction.None;
    }

    private void Move(int delta, int count)
    {
        if (count == 0) return;
        _index = Math.Clamp(_index + delta, 0, count - 1);
    }
}
