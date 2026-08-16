using ClaudeLauncher.Terminal;
using ClaudeLauncher.Tui;

namespace ClaudeLauncher.Screens;

/// <summary>
/// A whole session shown as Claude draws it, filling the screen.
///
/// This is what "Chat here" opens while the Terminal tiles setting is on. The
/// launcher keeps the chrome and hands everything inside the frame to Claude,
/// so /usage, the model picker and plan mode are the real thing rather than an
/// approximation of them.
/// </summary>
public sealed class TerminalSessionScreen : ScreenBase
{
    private readonly TerminalTile? _tile;
    private readonly TerminalScreen? _fixture;
    private readonly string _project;

    private bool _released;

    public TerminalSessionScreen(App app, TerminalTile tile) : base(app)
    {
        _tile = tile;
        _project = tile.ProjectName;
    }

    /// <summary>Fixture constructor for --selftest, which cannot spawn a child.</summary>
    public TerminalSessionScreen(App app, TerminalScreen fixture, string project) : base(app)
    {
        _fixture = fixture;
        _project = project;
    }

    public override TimeSpan? RefreshInterval =>
        _tile is null ? null : TimeSpan.FromMilliseconds(60);

    private long _revision = -1;

    public override bool NeedsRedraw()
    {
        if (_tile is null) return false;

        var revision = _tile.Revision;
        if (revision == _revision) return false;

        _revision = revision;
        return true;
    }

    public override void Render(ScreenBuffer buffer)
    {
        var y = Widgets.CompactChrome(buffer);
        var margin = Widgets.Margin(buffer);
        var width = buffer.Width - margin * 2;

        var ended = _tile?.HasExited ?? false;
        var typing = !_released && !ended;

        var status = ended ? "ended" : typing ? "typing · ^] releases" : "released · ^] to type";
        Widgets.SectionTitle(buffer, y, "Home", $"{_project} · {status}");
        y += 2;

        var height = Math.Max(4, buffer.Height - y - 4);
        var border = ended ? Theme.Dim : typing ? Theme.Blue : Theme.BorderAccent;
        var fill = Theme.Panel;

        buffer.Box(margin, y, width, height, new Sty(border, fill), BoxStyle.Rounded, fill);

        var inner = width - 4;
        var innerRows = height - 2;

        if (inner < 20 || innerRows < 4)
        {
            buffer.WriteClipped(margin + 2, y + 1, "needs a larger window",
                Math.Max(0, width - 4), new Sty(Theme.Dim, fill));
        }
        else if (_tile is not null)
        {
            _tile.Resize(inner, innerRows);
            _tile.Read(screen =>
                TerminalRender.Draw(buffer, screen, margin + 2, y + 1, inner, innerRows, fill, typing));
        }
        else if (_fixture is not null)
        {
            TerminalRender.Draw(buffer, _fixture, margin + 2, y + 1, inner, innerRows, fill, focused: true);
        }

        Widgets.Footer(buffer, typing
            ? new[]
            {
                new KeyHint("type", "Claude's own UI"),
                new KeyHint("^]", "Release keyboard")
            }
            : new[]
            {
                new KeyHint("^]", "Type again"),
                new KeyHint("t", "Wall"),
                new KeyHint("esc", "Back")
            });
    }

    public override ScreenAction HandleKey(ConsoleKeyInfo key)
    {
        var ended = _tile?.HasExited ?? false;

        // Ctrl+] is the one key Claude will never want, so it is what gets the
        // launcher's own keys back.
        if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.Oem6)
        {
            _released = !_released;
            return ScreenAction.None;
        }

        if (!_released && !ended && _tile is not null)
        {
            _tile.Send(key);
            return ScreenAction.None;
        }

        switch (key.Key)
        {
            case ConsoleKey.Escape:
                // The session keeps running and shows up on Home and the wall,
                // exactly like a chat does when its screen is closed.
                return ScreenAction.Root(new HomeScreen(App, new Sessions.SessionService(App.State)));
            case ConsoleKey.Enter:
                if (!ended) _released = false;
                return ScreenAction.None;
        }

        if (char.ToLowerInvariant(key.KeyChar) == 't')
            return ScreenAction.Root(new HomeScreen(App, new Sessions.SessionService(App.State)));

        return ScreenAction.None;
    }
}
