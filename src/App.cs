using ClaudeLauncher.Tui;

namespace ClaudeLauncher;

public enum ActionKind
{
    None,
    Push,
    Replace,
    Pop,
    Quit,
    Finish
}

public sealed class ScreenAction
{
    public ActionKind Kind { get; private init; } = ActionKind.None;
    public ScreenBase? Next { get; private init; }
    public string? Mode { get; private init; }
    public string? OpenIn { get; private init; }

    public static ScreenAction None { get; } = new();
    public static ScreenAction Back { get; } = new() { Kind = ActionKind.Pop };
    public static ScreenAction Exit { get; } = new() { Kind = ActionKind.Quit };

    public static ScreenAction Push(ScreenBase next) => new() { Kind = ActionKind.Push, Next = next };

    public static ScreenAction Replace(ScreenBase next) => new() { Kind = ActionKind.Replace, Next = next };

    /// <summary>The default keeps existing call sites launching in the current console.</summary>
    public static ScreenAction Finish(string mode, string openIn = LaunchTarget.Current) =>
        new() { Kind = ActionKind.Finish, Mode = mode, OpenIn = openIn };
}

public abstract class ScreenBase
{
    protected ScreenBase(App app) => App = app;

    protected App App { get; }

    public abstract void Render(ScreenBuffer buffer);

    public abstract ScreenAction HandleKey(ConsoleKeyInfo key);

    /// <summary>
    /// Non-null for screens whose content changes on its own. Screens that only
    /// react to keys leave this null and the loop stays event driven, as before.
    /// </summary>
    public virtual TimeSpan? RefreshInterval => null;

    /// <summary>Called on each interval; true repaints the frame.</summary>
    public virtual bool NeedsRedraw() => false;
}

public sealed class App
{
    private readonly List<ScreenBase> _stack = new();
    private readonly ScreenBuffer _buffer = new();

    public App(LauncherState state, UiSettings settings)
    {
        State = state;
        Settings = settings;
    }

    public LauncherState State { get; }

    public UiSettings Settings { get; }

    public ProfileEntry? Profile { get; set; }

    public ProjectEntry? Project { get; set; }

    /// <summary>Set once a launch mode has been chosen.</summary>
    public string? LaunchMode { get; private set; }

    /// <summary>Where the launch was sent: current console, tab, or a split pane.</summary>
    public string? LaunchOpenIn { get; private set; }

    public ScreenBuffer Buffer => _buffer;

    private ScreenBase Current => _stack[_stack.Count - 1];

    /// <summary>Runs the wizard. Several screens can be pre-stacked so Esc walks back naturally.</summary>
    public void Run(params ScreenBase[] initial)
    {
        _stack.Clear();
        _stack.AddRange(initial);
        if (_stack.Count == 0) return;

        Term.Setup("⚡ CLAUDE LAUNCHER");
        Console.CancelKeyPress += OnCancel;

        try
        {
            Loop();
        }
        finally
        {
            Console.CancelKeyPress -= OnCancel;
            Term.Restore();
        }
    }

    /// <summary>Renders one screen and returns the frame as plain text (used by --selftest).</summary>
    public string RenderToText(ScreenBase screen, int width, int height)
    {
        _buffer.Resize(width, height);
        _buffer.Clear();
        screen.Render(_buffer);
        return _buffer.ToPlainText();
    }

    private void OnCancel(object? sender, ConsoleCancelEventArgs e)
    {
        Term.Restore();
    }

    private void Loop()
    {
        var width = 0;
        var height = 0;

        while (_stack.Count > 0)
        {
            if (Term.Width != width || Term.Height != height)
            {
                width = Term.Width;
                height = Term.Height;
                _buffer.Resize(width, height);
            }

            _buffer.PaintBackground = Settings.PaintBackground;
            _buffer.Clear();
            Current.Render(_buffer);
            _buffer.Flush();

            var key = WaitForKey(width, height, Current);
            if (key is null) continue; // resized, or the screen asked for a repaint

            Apply(Current.HandleKey(key.Value));
        }
    }

    private static ConsoleKeyInfo? WaitForKey(int width, int height, ScreenBase screen)
    {
        var interval = screen.RefreshInterval;
        var next = interval is null ? DateTime.MaxValue : DateTime.UtcNow + interval.Value;

        while (true)
        {
            try
            {
                if (Console.KeyAvailable) return Console.ReadKey(intercept: true);
            }
            catch (InvalidOperationException)
            {
                return Console.ReadKey(intercept: true);
            }

            if (Term.Width != width || Term.Height != height) return null;

            if (DateTime.UtcNow >= next)
            {
                // null already means "redraw" to the caller, same as a resize.
                if (screen.NeedsRedraw()) return null;
                next = DateTime.UtcNow + interval!.Value;
            }

            Thread.Sleep(35);
        }
    }

    private void Apply(ScreenAction action)
    {
        switch (action.Kind)
        {
            case ActionKind.Push:
                if (action.Next is not null) _stack.Add(action.Next);
                break;
            case ActionKind.Replace:
                if (action.Next is not null) _stack[_stack.Count - 1] = action.Next;
                break;
            case ActionKind.Pop:
                if (_stack.Count > 1) _stack.RemoveAt(_stack.Count - 1);
                else _stack.Clear();
                break;
            case ActionKind.Quit:
                _stack.Clear();
                break;
            case ActionKind.Finish:
                if (Profile is not null && Project is not null)
                {
                    LaunchMode = action.Mode ?? "new";
                    LaunchOpenIn = LaunchTarget.Normalize(action.OpenIn ?? Settings.DefaultOpenIn);
                    StateStore.WriteResult(Profile, Project, LaunchMode, LaunchOpenIn);
                }

                _stack.Clear();
                break;
        }
    }
}
