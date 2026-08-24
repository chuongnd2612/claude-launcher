using ClaudeLauncher.Sessions;
using ClaudeLauncher.Tui;

namespace ClaudeLauncher.Screens;

/// <summary>
/// Everything about one session: what it did, how much it used, and the tail of
/// its transcript. This is the one place a full pass over the file is worth it,
/// because the user asked for exactly this session.
/// </summary>
public sealed class SessionDetailScreen : ScreenBase
{
    private readonly PastSession _session;
    private readonly SessionDetail _detail;
    private int _scroll;

    public SessionDetailScreen(App app, PastSession session) : base(app)
    {
        _session = session;
        SessionReader.Load(session);
        _detail = SessionReader.ScanSession(session.Path);

        // Opens at the end: the newest turn is what anyone wants first.
        _scroll = int.MaxValue;
    }

    /// <summary>Fixture constructor for --selftest.</summary>
    public SessionDetailScreen(App app, PastSession session, SessionDetail detail) : base(app)
    {
        _session = session;
        _detail = detail;
        _scroll = int.MaxValue;
    }

    public override void Render(ScreenBuffer buffer)
    {
        var y = Widgets.CompactChrome(buffer);
        var margin = Widgets.Margin(buffer);
        var width = buffer.Width - margin * 2;

        Widgets.SectionTitle(buffer, y, $"Session {_session.ShortId}", _session.DisplayTitle);
        y += 2;

        // The transcript is what this screen is for, so it gets its rows first
        // and the summary panels give way around it.
        const int minTranscript = 6;
        var bottom = buffer.Height - 4;
        var twoUp = width >= 96;

        var panelWidth = twoUp ? (width - 2) / 2 : width;
        var roomForPanels = bottom - y - minTranscript;

        if (roomForPanels >= 7)
        {
            Widgets.TitledBox(buffer, margin, y, panelWidth, 6, "Session", Theme.VioletSoft);
            Row(buffer, margin + 3, y + 1, "Path", _session.Path, panelWidth);
            Row(buffer, margin + 3, y + 2, "Branch",
                string.IsNullOrEmpty(_session.Branch) ? "-" : _session.Branch, panelWidth);
            Row(buffer, margin + 3, y + 3, "Model", _session.Model ?? "-", panelWidth);
            Row(buffer, margin + 3, y + 4, "Started",
                _detail.StartedUtc is null
                    ? "-"
                    : _detail.StartedUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), panelWidth);

            // Side by side when wide; stacked only when the rows are going spare.
            var stacked = !twoUp && roomForPanels >= 14;

            if (twoUp || stacked)
            {
                var countersX = twoUp ? margin + panelWidth + 2 : margin;
                var countersY = twoUp ? y : y + 7;

                Widgets.TitledBox(buffer, countersX, countersY, panelWidth, 6, "Counters", Theme.VioletSoft);
                Row(buffer, countersX + 3, countersY + 1, "Turns", _detail.Turns.ToString(), panelWidth);
                Row(buffer, countersX + 3, countersY + 2, "Tool calls",
                    $"{_detail.ToolCalls} · {_detail.Files.Count} files touched", panelWidth);
                Row(buffer, countersX + 3, countersY + 3, "Context", Format.Tokens(_session.ContextTokens), panelWidth);
                Row(buffer, countersX + 3, countersY + 4, "Size", $"{_session.SizeBytes / 1024:N0} KB", panelWidth);

                y = countersY + 7;
            }
            else
            {
                // No room for the panel: fold the counters into one line.
                y += 7;
                buffer.WriteClipped(margin + 1, y - 1,
                    $"{_detail.Turns} turns · {_detail.ToolCalls} tool calls · {_detail.Files.Count} files · {Format.Tokens(_session.ContextTokens)}",
                    width - 2, new Sty(Theme.Dim, Theme.Bg));
            }
        }

        var transcriptHeight = bottom - y;
        if (transcriptHeight >= 4) Transcript(buffer, margin, y, width, transcriptHeight);

        Widgets.Footer(buffer, KeyMap.DetailFooter(), KeyMap.Help);
    }

    private void Transcript(ScreenBuffer buffer, int x, int y, int width, int height)
    {
        var label = _detail.MalformedLines > 0
            ? $"Transcript · {_detail.Entries.Count} shown · {_detail.MalformedLines} unreadable"
            : $"Transcript · {_detail.Entries.Count} shown";

        Widgets.TitledBox(buffer, x, y, width, height, label, Theme.Blue);

        var rows = height - 2;
        var maxScroll = Math.Max(0, _detail.Entries.Count - rows);
        if (_scroll > maxScroll) _scroll = maxScroll;
        if (_scroll < 0) _scroll = 0;

        for (var i = 0; i < rows; i++)
        {
            var index = _scroll + i;
            if (index >= _detail.Entries.Count) break;

            var entry = _detail.Entries[index];
            var rowY = y + 1 + i;
            var inner = width - 6;

            switch (entry.Kind)
            {
                case EntryKind.UserPrompt:
                    buffer.Write(x + 3, rowY, "› ", new Sty(Theme.Blue, Theme.Panel, bold: true));
                    buffer.WriteClipped(x + 5, rowY, entry.Text, inner - 2, new Sty(Theme.Blue, Theme.Panel));
                    break;
                case EntryKind.AssistantText:
                    buffer.WriteClipped(x + 5, rowY, entry.Text, inner - 2, new Sty(Theme.TextSoft, Theme.Panel));
                    break;
                case EntryKind.Thinking:
                    buffer.WriteClipped(x + 3, rowY, "◆ thinking", inner, new Sty(Theme.Amber, Theme.Panel));
                    break;
                default:
                    var text = entry.Target is null ? $"◆ {entry.Text}" : $"◆ {entry.Text} {entry.Target}";
                    buffer.WriteClipped(x + 3, rowY, text, inner, new Sty(Theme.Muted, Theme.Panel));
                    break;
            }
        }
    }

    private static void Row(ScreenBuffer buffer, int x, int y, string label, string value, int width)
    {
        buffer.Write(x, y, label.PadRight(11), new Sty(Theme.Dim, Theme.Panel));
        buffer.WriteClipped(x + 12, y, value, Math.Max(4, width - 17), new Sty(Theme.TextSoft, Theme.Panel));
    }

    public override ScreenAction HandleKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                _scroll = Math.Max(0, _scroll - 1);
                return ScreenAction.None;
            case ConsoleKey.DownArrow:
                _scroll++;
                return ScreenAction.None;
            case ConsoleKey.PageUp:
                _scroll = Math.Max(0, _scroll - 10);
                return ScreenAction.None;
            case ConsoleKey.PageDown:
                _scroll += 10;
                return ScreenAction.None;
            case ConsoleKey.Home:
                _scroll = 0;
                return ScreenAction.None;
            case ConsoleKey.End:
                _scroll = int.MaxValue;
                return ScreenAction.None;
            case ConsoleKey.F1:
                return ScreenAction.Push(new KeysScreen(App, "Session log", KeyMap.Detail()));
            case ConsoleKey.Escape:
            case ConsoleKey.Backspace:
                return ScreenAction.Back;
        }

        if (char.ToLowerInvariant(key.KeyChar) == 'q') return ScreenAction.Exit;
        return ScreenAction.None;
    }
}
