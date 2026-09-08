using ClaudeLauncher.Sessions;
using ClaudeLauncher.Tui;

namespace ClaudeLauncher.Screens;

public enum PanePickOutcome
{
    None,
    Cancel,
    Launch
}

/// <summary>What a key press did to the picker, and what to start if it chose.</summary>
public readonly record struct PanePick(
    PanePickOutcome Outcome,
    ProjectEntry? Project = null,
    ProfileEntry? Profile = null,
    string? ResumeId = null)
{
    public static readonly PanePick None = new(PanePickOutcome.None);
    public static readonly PanePick Cancel = new(PanePickOutcome.Cancel);
}

/// <summary>
/// The new-terminal flow, folded small enough to live inside one pane of the
/// wall.
///
/// Reaching another session used to mean leaving the wall for a full screen
/// picker and coming back: three screens away from the terminal you were reading
/// to start the one beside it. This asks for the same three things - project,
/// how it starts, and which conversation when that is Resume - in the space the
/// split just made, so the wall never goes away while you choose.
///
/// It is deliberately not <see cref="NewTerminalScreen"/> in a smaller box. A
/// pane is twenty-odd columns wide in the worst case, so there is no room for
/// paths beside names, no filter mode to enter - typing filters, because nothing
/// else in a pane this size wants the letters - and the profile is one line
/// rather than a screen of its own.
/// </summary>
public sealed class PanePicker
{
    private enum Step
    {
        Project,
        Mode,
        Resume
    }

    /// <summary>
    /// The pane's key while it holds nothing. Not a path anyone can have: it
    /// stands in for one in the pane list, and a real folder answering to it
    /// would be adopted by the placeholder.
    /// </summary>
    public const string Key = "\u0001new-pane";

    private static readonly (string Mode, string Glyph, string Title)[] Modes =
    {
        ("new", "▶", "New session"),
        ("continue", "→", "Continue"),
        ("resume", "↻", "Resume")
    };

    private readonly App _app;
    private readonly List<ProjectEntry> _projects;

    private Step _step = Step.Project;
    private string _filter = string.Empty;
    private int _index;
    private int _scroll;
    private int _mode;
    private int _profile;
    private ProjectEntry? _project;
    private List<PastSession> _sessions = new();
    private string? _notice;

    public PanePicker(App app)
    {
        _app = app;
        _projects = app.State.Projects.ToList();

        var current = app.Profile is null ? -1 : app.State.Profiles.IndexOf(app.Profile);
        _profile = current < 0 ? 0 : current;
    }

    private ProfileEntry Profile =>
        _app.State.Profiles[Math.Clamp(_profile, 0, _app.State.Profiles.Count - 1)];

    private List<ProjectEntry> Visible => string.IsNullOrEmpty(_filter)
        ? _projects
        : _projects.Where(p =>
            p.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
            p.Path.Contains(_filter, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>The hints the wall's footer shows while this pane has the keyboard.</summary>
    public KeyHint[] Footer() => _step switch
    {
        Step.Project => new[]
        {
            new KeyHint("type", "Filter"),
            new KeyHint("↑↓", "Pick"),
            new KeyHint("↵", "Next"),
            new KeyHint("esc", "Cancel")
        },
        Step.Mode => new[]
        {
            new KeyHint("↑↓", "How it starts"),
            new KeyHint("←→", "Profile"),
            new KeyHint("↵", "Start here"),
            new KeyHint("esc", "Back")
        },
        _ => new[]
        {
            new KeyHint("↑↓", "Conversation"),
            new KeyHint("↵", "Resume here"),
            new KeyHint("esc", "Back")
        }
    };

    public void Render(ScreenBuffer buffer, int x, int y, int width, int height, int index, bool focused)
    {
        if (width < 12 || height < 3) return;

        var border = focused ? Theme.Amber : Theme.Border;
        var fill = focused ? Theme.PanelSelected : Theme.Panel;

        buffer.Box(x, y, width, height, new Sty(border, fill), BoxStyle.Rounded, fill);

        var title = $" {index + 1} · new terminal ";
        buffer.WriteClipped(x + 2, y, title, width - 4, new Sty(border, fill, bold: true));

        var step = _step switch
        {
            Step.Project => " project ",
            Step.Mode => " how it starts ",
            _ => " which one "
        };

        if (title.Length + step.Length + 6 <= width)
            buffer.WriteRight(x + width - 3, y, step, new Sty(Theme.Dim, fill));

        var inner = width - 4;
        var rows = height - 2;
        if (rows <= 0 || inner <= 4) return;

        var contentY = y + 1;

        // The notice earns its row only when there is one, so a short pane still
        // shows a list rather than a message about one.
        if (_notice is not null && rows > 2)
        {
            buffer.WriteClipped(x + 2, contentY, _notice, inner, new Sty(Theme.Amber, fill, italic: true));
            contentY++;
            rows--;
        }

        switch (_step)
        {
            case Step.Project:
                RenderProjects(buffer, x, contentY, inner, rows, fill);
                return;
            case Step.Mode:
                RenderModes(buffer, x, contentY, inner, rows, fill);
                return;
            default:
                RenderSessions(buffer, x, contentY, inner, rows, fill);
                return;
        }
    }

    private void RenderProjects(ScreenBuffer buffer, int x, int y, int inner, int rows, Rgb fill)
    {
        var cursor = buffer.Write(x + 2, y, "⌕ ", new Sty(Theme.Blue, fill));
        cursor = buffer.WriteClipped(cursor, y, _filter, Math.Max(0, inner - 4), new Sty(Theme.Text, fill));
        buffer.Write(cursor, y, "▏", new Sty(Theme.Blue, fill, bold: true));

        var items = Visible;
        var listY = y + 1;
        var listRows = rows - 1;

        if (listRows <= 0) return;

        if (items.Count == 0)
        {
            buffer.WriteClipped(x + 2, listY,
                _projects.Count == 0 ? "No projects yet." : "Nothing matches.",
                inner, new Sty(Theme.Muted, fill, italic: true));
            return;
        }

        Scroll(items.Count, listRows);

        for (var row = 0; row < listRows; row++)
        {
            var item = _scroll + row;
            if (item >= items.Count) break;

            var selected = item == _index;
            buffer.Write(x + 2, listY + row, selected ? "▸" : " ", new Sty(Theme.Blue, fill, bold: true));
            buffer.WriteClipped(x + 4, listY + row, items[item].Name, Math.Max(0, inner - 2),
                new Sty(selected ? Theme.Blue : Theme.TextSoft, fill, bold: selected));
        }
    }

    private void RenderModes(ScreenBuffer buffer, int x, int y, int inner, int rows, Rgb fill)
    {
        buffer.WriteClipped(x + 2, y, _project?.Name ?? string.Empty, inner, new Sty(Theme.Text, fill, bold: true));

        var listY = y + 1;
        var listRows = Math.Min(Modes.Length, Math.Max(0, rows - 2));

        for (var row = 0; row < listRows; row++)
        {
            var selected = row == _mode;
            buffer.Write(x + 2, listY + row, selected ? "▸" : " ", new Sty(Theme.Blue, fill, bold: true));
            buffer.Write(x + 4, listY + row, Modes[row].Glyph, new Sty(selected ? Theme.Blue : Theme.Muted, fill));
            buffer.WriteClipped(x + 6, listY + row, Modes[row].Title, Math.Max(0, inner - 4),
                new Sty(selected ? Theme.Blue : Theme.TextSoft, fill, bold: selected));
        }

        // Which account it runs under is the one thing a wrong answer makes
        // expensive, so it stays on screen even when the modes have to be cut.
        var profileY = listY + listRows;
        if (profileY > y + rows - 1) return;

        var profile = Profile;
        var cursor = buffer.Write(x + 2, profileY, profile.DisplayIcon,
            new Sty(ProfileLook.Color(profile.DisplayLabel), fill, bold: true));

        cursor = buffer.WriteClipped(cursor + 1, profileY, profile.DisplayLabel, Math.Max(0, inner - 6),
            new Sty(Theme.TextSoft, fill));

        if (_app.State.Profiles.Count > 1)
            buffer.WriteClipped(cursor + 1, profileY, "←→", inner, new Sty(Theme.Dim, fill));
    }

    private void RenderSessions(ScreenBuffer buffer, int x, int y, int inner, int rows, Rgb fill)
    {
        buffer.WriteClipped(x + 2, y, _project?.Name ?? string.Empty, inner, new Sty(Theme.Text, fill, bold: true));

        var listY = y + 1;
        var listRows = rows - 1;
        if (listRows <= 0) return;

        if (_sessions.Count == 0)
        {
            buffer.WriteClipped(x + 2, listY, "Nothing recorded yet.", inner,
                new Sty(Theme.Muted, fill, italic: true));
            return;
        }

        Scroll(_sessions.Count, listRows);

        for (var row = 0; row < listRows; row++)
        {
            var item = _scroll + row;
            if (item >= _sessions.Count) break;

            var session = _sessions[item];
            SessionReader.Load(session);

            var selected = item == _index;
            buffer.Write(x + 2, listY + row, selected ? "▸" : " ", new Sty(Theme.Blue, fill, bold: true));

            var age = Format.Ago(session.LastActivityUtc);
            buffer.WriteClipped(x + 4, listY + row, session.DisplayTitle, Math.Max(4, inner - age.Length - 4),
                new Sty(selected ? Theme.Blue : Theme.TextSoft, fill, bold: selected));

            buffer.WriteRight(x + 2 + inner, listY + row, age, new Sty(Theme.Dim, fill));
        }
    }

    private void Scroll(int count, int rows)
    {
        if (_index >= count) _index = Math.Max(0, count - 1);
        if (_index < _scroll) _scroll = _index;
        if (_index >= _scroll + rows) _scroll = _index - rows + 1;
        if (_scroll > Math.Max(0, count - rows)) _scroll = Math.Max(0, count - rows);
        if (_scroll < 0) _scroll = 0;
    }

    public PanePick HandleKey(ConsoleKeyInfo key)
    {
        _notice = null;

        return _step switch
        {
            Step.Project => Projects(key),
            Step.Mode => ModeKey(key),
            _ => Sessions(key)
        };
    }

    private PanePick Projects(ConsoleKeyInfo key)
    {
        var items = Visible;

        switch (key.Key)
        {
            case ConsoleKey.Escape:
                return PanePick.Cancel;
            case ConsoleKey.UpArrow:
                Move(-1, items.Count);
                return PanePick.None;
            case ConsoleKey.DownArrow:
            case ConsoleKey.Tab:
                Move(1, items.Count);
                return PanePick.None;
            case ConsoleKey.PageUp:
                Move(-5, items.Count);
                return PanePick.None;
            case ConsoleKey.PageDown:
                Move(5, items.Count);
                return PanePick.None;
            case ConsoleKey.Backspace:
                if (_filter.Length > 0) _filter = _filter.Substring(0, _filter.Length - 1);
                _index = 0;
                return PanePick.None;
            case ConsoleKey.Enter:
                if (items.Count == 0) return PanePick.None;
                _project = items[_index];
                _step = Step.Mode;
                _index = 0;
                _scroll = 0;
                return PanePick.None;
        }

        // Only plain typing is the filter. A chord belongs to the wall around
        // this pane, or ctrl+w would narrow the list instead of closing a pane.
        if (!char.IsControl(key.KeyChar) &&
            (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0)
        {
            _filter += key.KeyChar;
            _index = 0;
        }

        return PanePick.None;
    }

    private PanePick ModeKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                _step = Step.Project;
                _index = 0;
                _scroll = 0;
                return PanePick.None;
            case ConsoleKey.UpArrow:
                _mode = Math.Max(0, _mode - 1);
                return PanePick.None;
            case ConsoleKey.DownArrow:
            case ConsoleKey.Tab:
                _mode = Math.Min(Modes.Length - 1, _mode + 1);
                return PanePick.None;
            case ConsoleKey.LeftArrow:
                CycleProfile(-1);
                return PanePick.None;
            case ConsoleKey.RightArrow:
                CycleProfile(1);
                return PanePick.None;
            case ConsoleKey.Enter:
            case ConsoleKey.Spacebar:
                return Start(Modes[_mode].Mode);
        }

        // Bare letters only. A chord is the wall's - alt+n starting a session
        // because it happens to carry an n would be a key doing two things.
        if ((key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) != 0) return PanePick.None;

        return char.ToLowerInvariant(key.KeyChar) switch
        {
            'n' => Start("new"),
            'c' => Start("continue"),
            'r' => Start("resume"),
            _ => PanePick.None
        };
    }

    private PanePick Sessions(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                _step = Step.Mode;
                _index = 0;
                _scroll = 0;
                return PanePick.None;
            case ConsoleKey.UpArrow:
                Move(-1, _sessions.Count);
                return PanePick.None;
            case ConsoleKey.DownArrow:
            case ConsoleKey.Tab:
                Move(1, _sessions.Count);
                return PanePick.None;
            case ConsoleKey.PageUp:
                Move(-5, _sessions.Count);
                return PanePick.None;
            case ConsoleKey.PageDown:
                Move(5, _sessions.Count);
                return PanePick.None;
            case ConsoleKey.Enter:
                return _sessions.Count == 0
                    ? PanePick.None
                    : new PanePick(PanePickOutcome.Launch, _project, Profile, _sessions[_index].SessionId);
        }

        return PanePick.None;
    }

    /// <summary>
    /// Turns a mode into a launch. Continue resolves to the newest recorded id
    /// rather than passing --continue, for the same reason the session screen
    /// does: a tile without an id cannot be told from its project's others.
    /// </summary>
    private PanePick Start(string mode)
    {
        if (_project is null) return PanePick.None;

        var recorded = SessionReader.ListProjectSessions(
            StateStore.ExpandHome(Profile.ConfigDir), _project.Path);

        if (mode == "resume")
        {
            if (recorded.Count == 0)
            {
                _notice = "nothing recorded here yet";
                return PanePick.None;
            }

            _sessions = recorded;
            _step = Step.Resume;
            _index = 0;
            _scroll = 0;
            return PanePick.None;
        }

        var resume = mode == "continue"
            ? recorded.OrderByDescending(s => s.LastActivityUtc).Select(s => s.SessionId).FirstOrDefault()
            : null;

        return new PanePick(PanePickOutcome.Launch, _project, Profile, resume);
    }

    private void CycleProfile(int delta)
    {
        var profiles = _app.State.Profiles;
        if (profiles.Count < 2) return;

        _profile = (_profile + delta + profiles.Count) % profiles.Count;
    }

    private void Move(int delta, int count)
    {
        if (count == 0) return;
        _index = Math.Clamp(_index + delta, 0, count - 1);
    }
}
