using ClaudeLauncher.Sessions;
using ClaudeLauncher.Tui;

namespace ClaudeLauncher.Screens;

/// <summary>
/// Searches everything a session has said, not just what is on its screen.
///
/// A terminal tile can only search the grid it holds, because Claude runs on the
/// alternate screen and keeps its own history to itself. What Claude does leave
/// behind is the transcript it appends to as it goes - so the whole conversation
/// is searchable here, including turns that scrolled away hours ago.
/// </summary>
public sealed class HistorySearchScreen : ScreenBase
{
    private readonly string _path;
    private readonly string _label;
    private List<TranscriptHit> _hits = new();
    private string _query;
    private string _typed = string.Empty;
    private bool _typing;
    private bool _fresh;
    private int _index;
    private int _scroll;
    private bool _truncated;

    public HistorySearchScreen(App app, string label, string transcriptPath, string query) : base(app)
    {
        _label = label;
        _path = transcriptPath;
        _query = query;
        Run();
    }

    /// <summary>Fixture constructor for --selftest.</summary>
    public HistorySearchScreen(App app, string query, List<TranscriptHit> hits) : base(app)
    {
        _label = "claude-launcher";
        _path = string.Empty;
        _query = query;
        _hits = hits;
    }

    private const int Limit = 300;

    private void Run()
    {
        _hits = SessionReader.SearchTranscript(_path, _query, Limit);
        _truncated = _hits.Count >= Limit;
        _index = 0;
        _scroll = 0;
    }

    public override void Render(ScreenBuffer buffer)
    {
        var y = Widgets.CompactChrome(buffer);
        var margin = Widgets.Margin(buffer);
        var width = buffer.Width - margin * 2;

        var found = _hits.Count == 1 ? "1 match" : $"{_hits.Count} matches";
        if (_truncated) found = $"first {_hits.Count} matches";

        Widgets.SectionTitle(buffer, y, "Terminals", $"History · {_label}");
        buffer.WriteRight(margin + width - 1, y, found, new Sty(Theme.Dim, Theme.Bg));
        y += 2;

        if (_index >= _hits.Count) _index = Math.Max(0, _hits.Count - 1);

        // The detail pane is the first thing to go on a short window: the list
        // is what makes the screen usable, the reader is what makes it pleasant.
        var available = buffer.Height - 4 - y;
        var wantDetail = available >= 14;
        var detailHeight = wantDetail ? Math.Min(9, available / 2) : 0;
        var listHeight = Math.Max(4, available - detailHeight - (wantDetail ? 1 : 0));

        DrawList(buffer, margin, y, width, listHeight);

        if (wantDetail && _hits.Count > 0)
            DrawDetail(buffer, margin, y + listHeight + 1, width, detailHeight);

        Widgets.Footer(buffer, KeyMap.HistoryFooter(), KeyMap.Help);
    }

    private void DrawList(ScreenBuffer buffer, int margin, int y, int width, int height)
    {
        buffer.Box(margin, y, width, height, new Sty(Theme.Border, Theme.Panel), BoxStyle.Rounded, Theme.Panel);
        buffer.Write(margin + 2, y, " Matches ", new Sty(Theme.Blue, Theme.Panel, bold: true));

        var queryRow = y + 1;
        var queryBg = _typing ? Theme.PanelSelected : Theme.Panel;
        buffer.Fill(margin + 1, queryRow, width - 2, 1, queryBg);

        var at = buffer.Write(margin + 3, queryRow, "⌕ ", new Sty(_typing ? Theme.Blue : Theme.Dim, queryBg));
        if (_typing)
        {
            at = buffer.Write(at, queryRow, _typed, new Sty(Theme.Text, queryBg));
            buffer.Write(at, queryRow, "▏", new Sty(Theme.Blue, queryBg, bold: true));
        }
        else
        {
            at = buffer.Write(at, queryRow, _query, new Sty(Theme.Amber, Theme.Panel));
            buffer.WriteClipped(at + 3, queryRow, "press / to search for something else",
                Math.Max(0, width - (at + 3 - margin) - 2), new Sty(Theme.Dim, Theme.Panel, italic: true));
        }

        var top = y + 2;
        var rows = height - 3;

        if (_hits.Count == 0)
        {
            var message = _query.Length == 0
                ? "Type a query with / to search this session."
                : $"Nothing in this session's history mentions \"{_query}\".";
            buffer.WriteClipped(margin + 3, top + 1, message, width - 6,
                new Sty(Theme.Muted, Theme.Panel, italic: true));
            return;
        }

        if (_index < _scroll) _scroll = _index;
        if (_index >= _scroll + rows) _scroll = _index - rows + 1;
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, _hits.Count - rows));

        for (var row = 0; row < rows; row++)
        {
            var i = _scroll + row;
            if (i >= _hits.Count) break;

            var hit = _hits[i];
            var selected = i == _index;
            var rowY = top + row;
            var bg = selected ? Theme.PanelSelected : Theme.Panel;

            buffer.Fill(margin + 1, rowY, width - 2, 1, bg);
            buffer.Write(margin + 2, rowY, selected ? "▸" : " ", new Sty(Theme.Blue, bg, bold: true));

            var when = hit.WhenUtc is null ? "" : hit.WhenUtc.Value.ToLocalTime().ToString("HH:mm");
            buffer.Write(margin + 4, rowY, when, new Sty(Theme.Dim, bg));

            buffer.WriteClipped(margin + 10, rowY, hit.Who, 8, new Sty(Colour(hit.Kind), bg));

            // Slide the window so the match is visible even in a long message.
            var textX = margin + 19;
            var textWidth = Math.Max(10, width - 21);
            var from = Math.Max(0, hit.Column - textWidth / 3);
            var snippet = hit.Text[from..];
            Highlighted(buffer, textX, rowY, snippet, textWidth, new Sty(Theme.TextSoft, bg));
        }
    }

    private void DrawDetail(ScreenBuffer buffer, int margin, int y, int width, int height)
    {
        var hit = _hits[_index];
        var when = hit.WhenUtc is null
            ? hit.Who
            : $"{hit.WhenUtc.Value.ToLocalTime():HH:mm} · {hit.Who}";

        buffer.Box(margin, y, width, height, new Sty(Theme.BorderMuted, Theme.Panel), BoxStyle.Rounded, Theme.Panel);
        buffer.Write(margin + 2, y, $" {when} ", new Sty(Colour(hit.Kind), Theme.Panel, bold: true));

        var inner = width - 6;
        var rows = height - 2;
        var style = new Sty(Theme.Text, Theme.Panel);

        for (var row = 0; row < rows; row++)
        {
            var from = row * inner;
            if (from >= hit.Text.Length) break;

            var take = Math.Min(inner, hit.Text.Length - from);
            Highlighted(buffer, margin + 3, y + 1 + row, hit.Text.Substring(from, take), inner, style);
        }
    }

    /// <summary>Writes a run of text with every mention of the query picked out.</summary>
    private void Highlighted(ScreenBuffer buffer, int x, int y, string text, int width, Sty style)
    {
        var mark = new Sty(Theme.Bg, Theme.Amber, bold: true);
        var written = 0;
        var from = 0;

        while (written < width && from < text.Length)
        {
            var at = _query.Length == 0 ? -1 : text.IndexOf(_query, from, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
            {
                buffer.WriteClipped(x + written, y, text[from..], width - written, style);
                return;
            }

            if (at > from)
            {
                var plain = text[from..at];
                buffer.WriteClipped(x + written, y, plain, width - written, style);
                written += plain.Length;
                if (written >= width) return;
            }

            var hit = text.Substring(at, Math.Min(_query.Length, text.Length - at));
            buffer.WriteClipped(x + written, y, hit, width - written, mark);
            written += hit.Length;
            from = at + hit.Length;
        }
    }

    private static Rgb Colour(EntryKind kind) => kind switch
    {
        EntryKind.UserPrompt => Theme.Blue,
        EntryKind.ToolCall => Theme.VioletSoft,
        EntryKind.Thinking => Theme.Dim,
        _ => Theme.Green
    };

    public override ScreenAction HandleKey(ConsoleKeyInfo key)
    {
        if (_typing) return Typing(key);

        switch (key.Key)
        {
            case ConsoleKey.F1:
                return ScreenAction.Push(new KeysScreen(App, "History", KeyMap.History()));
            case ConsoleKey.Escape:
            case ConsoleKey.Backspace:
                return ScreenAction.Back;

            case ConsoleKey.UpArrow:
                Move(-1);
                return ScreenAction.None;

            case ConsoleKey.DownArrow:
                Move(1);
                return ScreenAction.None;

            case ConsoleKey.PageUp:
                Move(-10);
                return ScreenAction.None;

            case ConsoleKey.PageDown:
                Move(10);
                return ScreenAction.None;

            case ConsoleKey.Home:
                _index = 0;
                return ScreenAction.None;

            case ConsoleKey.End:
                _index = Math.Max(0, _hits.Count - 1);
                return ScreenAction.None;
        }

        if (key.KeyChar is '/' or 'f' || (key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.F)
        {
            _typing = true;
            _typed = _query;
            _fresh = true;
            return ScreenAction.None;
        }

        if (key.Key == ConsoleKey.Q) return ScreenAction.Back;

        return ScreenAction.None;
    }

    private ScreenAction Typing(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.F1:
                return ScreenAction.Push(new KeysScreen(App, "History", KeyMap.History()));
            case ConsoleKey.Escape:
                _typing = false;
                return ScreenAction.None;

            case ConsoleKey.Enter:
                _typing = false;
                _query = _typed.Trim();
                if (_path.Length > 0) Run();
                return ScreenAction.None;

            case ConsoleKey.Backspace:
                if (_typed.Length > 0) _typed = _typed[..^1];
                _fresh = false;
                return ScreenAction.None;
        }

        if (key.KeyChar == '\0' || char.IsControl(key.KeyChar)) return ScreenAction.None;

        // The box opens holding the last query so it can be nudged, but typing
        // means a new search - otherwise the first letter lands on the end of
        // the old one and finds nothing.
        _typed = _fresh ? key.KeyChar.ToString() : _typed + key.KeyChar;
        _fresh = false;
        return ScreenAction.None;
    }

    private void Move(int delta)
    {
        if (_hits.Count == 0) return;
        _index = Math.Clamp(_index + delta, 0, _hits.Count - 1);
    }
}
