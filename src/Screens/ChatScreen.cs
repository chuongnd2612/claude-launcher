using ClaudeLauncher.Sessions;
using ClaudeLauncher.Tui;

namespace ClaudeLauncher.Screens;

/// <summary>
/// A Claude session you type into without leaving the launcher.
///
/// This is a conversation view, not Claude's terminal UI: the launcher owns the
/// process and exchanges structured messages with it, so there is no slash
/// command menu here. What it does give you is approve and deny on tool
/// permissions, and Esc to stop a turn.
/// </summary>
public sealed class ChatScreen : ScreenBase
{
    private readonly StreamSession? _session;
    private readonly IReadOnlyList<ChatLine>? _fixture;
    private readonly ChatState _fixtureState;
    private readonly PermissionAsk? _fixtureAsk;

    private string _input = string.Empty;
    private int _scroll;
    private bool _follow = true;
    private int _seenRevision = -1;

    public ChatScreen(App app, StreamSession session) : base(app)
    {
        _session = session;
    }

    /// <summary>Fixture constructor for --selftest.</summary>
    public ChatScreen(App app, IReadOnlyList<ChatLine> lines, ChatState state, PermissionAsk? ask) : base(app)
    {
        _fixture = lines;
        _fixtureState = state;
        _fixtureAsk = ask;
    }

    private ChatState State => _session?.State ?? _fixtureState;

    private PermissionAsk? Pending => _session?.Pending ?? _fixtureAsk;

    private IReadOnlyList<ChatLine> Lines => _session?.Snapshot() ?? _fixture!;

    public override TimeSpan? RefreshInterval =>
        _session is null ? null : TimeSpan.FromMilliseconds(120);

    public override bool NeedsRedraw()
    {
        if (_session is null) return false;
        if (_session.Revision == _seenRevision) return false;

        _seenRevision = _session.Revision;
        return true;
    }

    public override void Render(ScreenBuffer buffer)
    {
        var y = Widgets.CompactChrome(buffer);
        var margin = Widgets.Margin(buffer);
        var width = buffer.Width - margin * 2;

        var project = _session?.ProjectName ?? "demo";
        var icon = _session?.Profile.DisplayIcon ?? "W";
        var label = _session?.Profile.DisplayLabel ?? "Work";

        Widgets.SectionTitle(buffer, y, $"{icon}  {label}  ·  {project}", StateText());

        var model = _session?.Model;
        if (!string.IsNullOrWhiteSpace(model) && width > 60)
            buffer.WriteRight(margin + width - 1, y, model!, new Sty(Theme.Dim, Theme.Bg));

        y += 2;

        // Bottom up: input line, then the permission box when one is waiting.
        var inputY = buffer.Height - 4;
        var askHeight = Pending is null ? 0 : 5;
        var transcriptBottom = inputY - askHeight - 1;
        var transcriptHeight = transcriptBottom - y;

        if (transcriptHeight >= 3) Transcript(buffer, margin, y, width, transcriptHeight);
        if (Pending is not null) Ask(buffer, margin, transcriptBottom + 1, width);

        InputLine(buffer, margin, inputY, width);

        Widgets.Footer(buffer, Hints());
    }

    private string StateText() => State switch
    {
        ChatState.Starting => "starting…",
        ChatState.Working => "working — esc to stop",
        ChatState.AwaitingPermission => "waiting for you",
        ChatState.Ended => "session ended",
        _ => "ready"
    };

    private KeyHint[] Hints()
    {
        if (Pending is not null)
        {
            return new[]
            {
                new KeyHint("y", "Allow"),
                new KeyHint("a", "Always allow"),
                new KeyHint("n", "Deny"),
                new KeyHint("esc", "Back")
            };
        }

        return State == ChatState.Working
            ? new[]
            {
                new KeyHint("esc", "Stop"),
                new KeyHint("PgUp/PgDn", "Scroll"),
                new KeyHint("^C", "Leave")
            }
            : new[]
            {
                new KeyHint("type", "Message"),
                new KeyHint("↵", "Send"),
                new KeyHint("PgUp/PgDn", "Scroll"),
                new KeyHint("esc", "Back")
            };
    }

    private void Transcript(ScreenBuffer buffer, int x, int y, int width, int height)
    {
        buffer.Box(x, y, width, height, new Sty(Theme.Border, Theme.Panel), BoxStyle.Rounded, Theme.Panel);

        var inner = width - 6;
        var rows = height - 2;
        var wrapped = new List<(string Text, Sty Style)>();

        foreach (var line in Lines) Wrap(wrapped, line, inner);

        var maxScroll = Math.Max(0, wrapped.Count - rows);
        if (_follow) _scroll = maxScroll;
        _scroll = Math.Clamp(_scroll, 0, maxScroll);

        for (var i = 0; i < rows; i++)
        {
            var index = _scroll + i;
            if (index >= wrapped.Count) break;
            buffer.Write(x + 3, y + 1 + i, wrapped[index].Text, wrapped[index].Style);
        }

        if (!_follow && maxScroll > 0)
            buffer.WriteRight(x + width - 3, y, " scrolled — end to follow ", new Sty(Theme.Amber, Theme.Panel));
    }

    private static void Wrap(List<(string, Sty)> into, ChatLine line, int width)
    {
        var (prefix, style) = line.Kind switch
        {
            ChatLineKind.UserPrompt => ("› ", new Sty(Theme.Blue, Theme.Panel, bold: true)),
            ChatLineKind.ToolCall => ("◆ ", new Sty(Theme.Muted, Theme.Panel)),
            ChatLineKind.Thinking => ("◆ ", new Sty(Theme.Amber, Theme.Panel)),
            ChatLineKind.Notice => ("· ", new Sty(Theme.Dim, Theme.Panel, italic: true)),
            ChatLineKind.Error => ("✗ ", new Sty(Theme.Red, Theme.Panel)),
            _ => ("", new Sty(Theme.TextSoft, Theme.Panel))
        };

        var text = line.Kind == ChatLineKind.ToolCall && line.Detail is not null
            ? $"{line.Text} {line.Detail}"
            : line.Text;

        var first = true;
        foreach (var word in (prefix + text).Split(' '))
        {
            if (into.Count > 0 && !first)
            {
                var last = into[^1];
                if (last.Item1.Length + 1 + word.Length <= width)
                {
                    into[^1] = (last.Item1 + " " + word, last.Item2);
                    continue;
                }
            }

            into.Add((first ? word : "  " + word, style));
            first = false;
        }
    }

    private void Ask(ScreenBuffer buffer, int x, int y, int width)
    {
        var ask = Pending!;
        Widgets.TitledBox(buffer, x, y, width, 5, "Permission", Theme.Amber);

        var detail = string.IsNullOrWhiteSpace(ask.Description) ? ask.InputJson : ask.Description;
        buffer.WriteClipped(x + 3, y + 1, $"◆ {ask.Tool}  {detail}", width - 6,
            new Sty(Theme.Amber, Theme.Panel, bold: true));
        buffer.WriteClipped(x + 3, y + 2, "Claude wants to run this tool.", width - 6,
            new Sty(Theme.TextSoft, Theme.Panel));

        var cursor = buffer.Write(x + 3, y + 3, "y", new Sty(Theme.Green, Theme.Panel, bold: true));
        cursor = buffer.Write(cursor, y + 3, " allow    ", new Sty(Theme.Muted, Theme.Panel));
        cursor = buffer.Write(cursor, y + 3, "a", new Sty(Theme.Blue, Theme.Panel, bold: true));
        cursor = buffer.Write(cursor, y + 3, " always allow    ", new Sty(Theme.Muted, Theme.Panel));
        cursor = buffer.Write(cursor, y + 3, "n", new Sty(Theme.Red, Theme.Panel, bold: true));
        buffer.Write(cursor, y + 3, " deny", new Sty(Theme.Muted, Theme.Panel));
    }

    private void InputLine(ScreenBuffer buffer, int x, int y, int width)
    {
        var busy = State is ChatState.Working or ChatState.AwaitingPermission or ChatState.Ended;
        var bg = busy ? Theme.BgSoft : Theme.PanelSelected;

        buffer.Fill(x, y, width, 1, bg);
        buffer.Write(x + 1, y, "›", new Sty(busy ? Theme.Dim : Theme.Blue, bg, bold: true));

        if (busy)
        {
            var message = State switch
            {
                ChatState.Working => "Claude is working…",
                ChatState.AwaitingPermission => "Answer the permission request above.",
                ChatState.Ended => "This session has ended. Press esc to go back.",
                _ => string.Empty
            };

            buffer.WriteClipped(x + 3, y, message, width - 4, new Sty(Theme.Dim, bg, italic: true));
            return;
        }

        // Keep the tail visible while typing a long prompt.
        var room = width - 6;
        var shown = _input.Length <= room ? _input : _input.Substring(_input.Length - room);
        var cursor = buffer.Write(x + 3, y, shown, new Sty(Theme.Text, bg));
        buffer.Write(cursor, y, "▏", new Sty(Theme.Blue, bg, bold: true));
    }

    public override ScreenAction HandleKey(ConsoleKeyInfo key)
    {
        if (_session is null) return key.Key == ConsoleKey.Escape ? ScreenAction.Back : ScreenAction.None;

        // Permission first: nothing else can proceed until it is answered.
        if (Pending is not null)
        {
            switch (char.ToLowerInvariant(key.KeyChar))
            {
                case 'y': _session.Answer(allow: true); return ScreenAction.None;
                case 'a': _session.Answer(allow: true, always: true); return ScreenAction.None;
                case 'n': _session.Answer(allow: false); return ScreenAction.None;
            }

            if (key.Key == ConsoleKey.Escape) return ScreenAction.Back;
            return ScreenAction.None;
        }

        switch (key.Key)
        {
            case ConsoleKey.Escape:
                if (State == ChatState.Working) { _session.Interrupt(); return ScreenAction.None; }
                return ScreenAction.Back;

            case ConsoleKey.Enter:
                if (State == ChatState.Working || _input.Trim().Length == 0) return ScreenAction.None;
                _session.Send(_input.Trim());
                _input = string.Empty;
                _follow = true;
                return ScreenAction.None;

            case ConsoleKey.Backspace:
                if (_input.Length > 0) _input = _input.Substring(0, _input.Length - 1);
                return ScreenAction.None;

            case ConsoleKey.PageUp:
                _follow = false;
                _scroll = Math.Max(0, _scroll - 8);
                return ScreenAction.None;

            case ConsoleKey.PageDown:
                _scroll += 8;
                return ScreenAction.None;

            case ConsoleKey.End:
                _follow = true;
                return ScreenAction.None;
        }

        if (!char.IsControl(key.KeyChar) && State != ChatState.Working) _input += key.KeyChar;
        return ScreenAction.None;
    }
}
