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
    private int _menuIndex;
    private string? _notice;

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

        // A running tool shows a live elapsed time, so redraw while one is up
        // even when no new events have arrived.
        if (_session.ActiveTool is not null) return true;
        if (_session.Revision == _seenRevision) return false;

        _seenRevision = _session.Revision;
        return true;
    }

    /// <summary>Commands matching what has been typed after the leading slash.</summary>
    private List<SlashCommand> Matches()
    {
        if (_session is null || !_input.StartsWith('/')) return new List<SlashCommand>();

        var typed = _input.Substring(1);
        if (typed.Contains(' ')) return new List<SlashCommand>();   // already past the name

        return _session.Commands
            .Where(c => c.Name.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
            .Take(40)
            .ToList();
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

        // Bottom up: input line, then the permission box or command menu, then
        // whatever height is left goes to the transcript.
        var inputY = buffer.Height - 4;
        var matches = Matches();

        var askHeight = Pending is null ? 0 : 5;
        var menuHeight = Pending is null && matches.Count > 0
            ? Math.Min(matches.Count, Math.Max(3, (buffer.Height - y - 12) / 2)) + 2
            : 0;

        var running = _session?.ActiveTool;
        var runningHeight = running is null ? 0 : 1;

        var transcriptBottom = inputY - askHeight - menuHeight - runningHeight - 1;
        var transcriptHeight = transcriptBottom - y;

        if (transcriptHeight >= 3) Transcript(buffer, margin, y, width, transcriptHeight);

        var below = transcriptBottom + 1;

        if (running is not null)
        {
            var elapsed = Sessions.Format.Duration(running.Elapsed);
            buffer.WriteClipped(margin + 1, below,
                $"◆ {running.Description} · running {elapsed}", width - 2,
                new Sty(Theme.Amber, Theme.Bg));
            below++;
        }

        if (Pending is not null) Ask(buffer, margin, below, width);
        else if (menuHeight > 0) Menu(buffer, margin, below, width, menuHeight, matches);

        InputLine(buffer, margin, inputY, width);

        if (_notice is not null)
            buffer.WriteClipped(margin + 1, inputY + 1, _notice, width - 2, new Sty(Theme.Amber, Theme.Bg));

        Widgets.Footer(buffer, Hints());
    }

    /// <summary>
    /// Continues this conversation in a Windows Terminal pane and closes the
    /// chat. The launcher owns the process, so it would die with the launcher -
    /// but the conversation is on disk, and --resume picks it up exactly.
    /// </summary>
    private ScreenAction Detach()
    {
        if (_session?.SessionId is null)
        {
            _notice = "Nothing to hand over yet — send a message first.";
            return ScreenAction.None;
        }

        var ok = PaneLauncher.Split(_session.Profile, _session.ProjectPath, vertical: true,
            App.Settings.RemoteControl, out var error, _session.SessionId);

        if (!ok)
        {
            _notice = "Could not open a pane: " + error;
            return ScreenAction.None;
        }

        _session.Dispose();
        App.Chats.Remove(_session);
        return ScreenAction.Root(new HomeScreen(App, new SessionService(App.State)));
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

        if (State == ChatState.Working)
        {
            return new[]
            {
                new KeyHint("esc", "Stop"),
                new KeyHint("PgUp/PgDn", "Scroll"),
                new KeyHint("^d", "Detach")
            };
        }

        if (Matches().Count > 0)
        {
            return new[]
            {
                new KeyHint("↑↓", "Pick"),
                new KeyHint("tab", "Complete"),
                new KeyHint("↵", "Complete"),
                new KeyHint("esc", "Clear")
            };
        }

        return new[]
        {
            new KeyHint("type", "Message"),
            new KeyHint("/", "Commands"),
            new KeyHint("↵", "Send"),
            new KeyHint("^d", "Detach"),
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

    /// <summary>Slash-command picker, built from the commands the session reported.</summary>
    private void Menu(ScreenBuffer buffer, int x, int y, int width, int height, List<SlashCommand> matches)
    {
        Widgets.TitledBox(buffer, x, y, width, height, $"Commands · {matches.Count}", Theme.VioletSoft);

        var rows = height - 2;
        _menuIndex = Math.Clamp(_menuIndex, 0, Math.Max(0, matches.Count - 1));
        var start = Math.Max(0, Math.Min(_menuIndex - rows + 1, matches.Count - rows));
        if (start < 0) start = 0;

        for (var i = 0; i < rows; i++)
        {
            var index = start + i;
            if (index >= matches.Count) break;

            var command = matches[index];
            var selected = index == _menuIndex;
            var rowY = y + 1 + i;
            var bg = selected ? Theme.PanelSelected : Theme.Panel;

            buffer.Fill(x + 1, rowY, width - 2, 1, bg);
            buffer.WriteClipped(x + 3, rowY, "/" + command.Name, 24,
                new Sty(selected ? Theme.Blue : Theme.Text, bg, bold: selected));

            var detail = string.IsNullOrEmpty(command.ArgumentHint)
                ? command.Description
                : $"{command.ArgumentHint} — {command.Description}";

            buffer.WriteClipped(x + 28, rowY, detail, Math.Max(0, width - 31), new Sty(Theme.Dim, bg));
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

        var matches = Matches();

        switch (key.Key)
        {
            case ConsoleKey.Escape:
                if (matches.Count > 0) { _input = string.Empty; return ScreenAction.None; }
                if (State == ChatState.Working) { _session.Interrupt(); return ScreenAction.None; }

                // Home, not the wizard step behind this screen: the session keeps
                // running, and Home is the only place it can be found again.
                return ScreenAction.Root(new HomeScreen(App, new SessionService(App.State)));

            case ConsoleKey.Tab:
                if (matches.Count > 0)
                {
                    _input = "/" + matches[Math.Clamp(_menuIndex, 0, matches.Count - 1)].Name + " ";
                    _menuIndex = 0;
                }

                return ScreenAction.None;

            case ConsoleKey.UpArrow:
                if (matches.Count > 0) { _menuIndex = Math.Max(0, _menuIndex - 1); return ScreenAction.None; }
                _follow = false;
                _scroll = Math.Max(0, _scroll - 1);
                return ScreenAction.None;

            case ConsoleKey.DownArrow:
                if (matches.Count > 0) { _menuIndex = Math.Min(matches.Count - 1, _menuIndex + 1); return ScreenAction.None; }
                _scroll++;
                return ScreenAction.None;

            case ConsoleKey.Enter:
                if (State == ChatState.Working || _input.Trim().Length == 0) return ScreenAction.None;

                // Enter completes the highlighted command rather than sending a
                // half-typed name; a second Enter sends it.
                if (matches.Count > 0 && !_input.EndsWith(' '))
                {
                    _input = "/" + matches[Math.Clamp(_menuIndex, 0, matches.Count - 1)].Name + " ";
                    _menuIndex = 0;
                    return ScreenAction.None;
                }

                _session.Send(_input.Trim());
                _input = string.Empty;
                _menuIndex = 0;
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

        // Ctrl+D hands the conversation to a real terminal pane, where it
        // outlives the launcher. Not a plain letter: it must not fire mid-typing.
        if (key.Key == ConsoleKey.D && (key.Modifiers & ConsoleModifiers.Control) != 0) return Detach();

        if (!char.IsControl(key.KeyChar) && State != ChatState.Working)
        {
            _input += key.KeyChar;
            if (_input.StartsWith('/')) _menuIndex = 0;
        }

        return ScreenAction.None;
    }
}
