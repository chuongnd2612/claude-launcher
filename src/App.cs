using ClaudeLauncher.Tui;

namespace ClaudeLauncher;

public enum ActionKind
{
    None,
    Push,
    Replace,
    Pop,
    Root,
    Quit,
    Finish
}

public sealed class ScreenAction
{
    public ActionKind Kind { get; private init; } = ActionKind.None;
    public ScreenBase? Next { get; private init; }
    public string? Mode { get; private init; }
    public string? OpenIn { get; private init; }

    /// <summary>Set when resuming a specific conversation rather than letting Claude ask.</summary>
    public string? SessionId { get; private init; }

    public static ScreenAction None { get; } = new();
    public static ScreenAction Back { get; } = new() { Kind = ActionKind.Pop };
    public static ScreenAction Exit { get; } = new() { Kind = ActionKind.Quit };

    public static ScreenAction Push(ScreenBase next) => new() { Kind = ActionKind.Push, Next = next };

    public static ScreenAction Replace(ScreenBase next) => new() { Kind = ActionKind.Replace, Next = next };

    /// <summary>Drops the whole stack and starts again at one screen, for going back to the hub.</summary>
    public static ScreenAction Root(ScreenBase next) => new() { Kind = ActionKind.Root, Next = next };

    /// <summary>The default keeps existing call sites launching in the current console.</summary>
    public static ScreenAction Finish(string mode, string openIn = LaunchTarget.Current) =>
        new() { Kind = ActionKind.Finish, Mode = mode, OpenIn = openIn };

    /// <summary>Finish, resuming one specific conversation.</summary>
    public static ScreenAction Resume(string sessionId, string openIn) =>
        new() { Kind = ActionKind.Finish, Mode = "resume", OpenIn = openIn, SessionId = sessionId };
}

public abstract class ScreenBase
{
    protected ScreenBase(App app) => App = app;

    protected App App { get; }

    public abstract void Render(ScreenBuffer buffer);

    public abstract ScreenAction HandleKey(ConsoleKeyInfo key);

    /// <summary>
    /// Routes one input event. Screens that care about the mouse override this;
    /// everything else keeps handling keys and ignores the rest, so adding the
    /// mouse changed no existing screen.
    /// </summary>
    public virtual ScreenAction HandleInput(InputEvent input) =>
        input.Kind == InputKind.Key ? HandleKey(input.Key) : ScreenAction.None;

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

    /// <summary>
    /// Chat sessions this launcher owns and can be returned to. They keep
    /// running when their screen is closed, which is what makes several of them
    /// usable at once - so they are tracked here rather than by the screen.
    /// </summary>
    public List<Sessions.StreamSession> Chats { get; } = new();

    /// <summary>
    /// Terminal tiles: the same sessions seen through a pseudo console instead
    /// of a parsed conversation. Tracked alongside chats, and torn down the same
    /// way, because they are children of this process too.
    /// </summary>
    public List<Terminal.TerminalTile> Terminals { get; } = new();

    /// <summary>
    /// Adds a tile and records the open set, so closing the launcher does not
    /// also lose the list of what was open.
    /// </summary>
    public void AddTerminal(Terminal.TerminalTile tile)
    {
        Terminals.Add(tile);
        RememberTerminals();
    }

    /// <summary>
    /// Writes down what is open now. Called as tiles come and go rather than at
    /// exit, because teardown is exactly when the list has already been emptied
    /// - and because a launcher that is killed never reaches an exit path.
    /// </summary>
    public void RememberTerminals()
    {
        Workspace.Remember(Terminals
            .Where(t => !t.HasExited && !string.IsNullOrEmpty(t.SessionId))
            .Select(t => new WorkspaceEntry
            {
                SessionId = t.SessionId,
                ProjectName = t.ProjectName,
                ProjectPath = t.ProjectPath,
                ConfigDir = t.ConfigDir
            }));
    }

    /// <summary>
    /// Reopens the terminals that were up last time, resuming each conversation
    /// rather than starting a fresh one. Returns how many came back.
    /// </summary>
    public int RestoreTerminals(out string? failure)
    {
        failure = null;
        var restored = 0;

        foreach (var entry in Workspace.Restorable())
        {
            if (Terminals.Any(t => !t.HasExited && t.SessionId == entry.SessionId)) continue;

            try
            {
                Terminals.Add(Terminal.TerminalTile.Start(
                    entry.ProjectPath, entry.ProjectName,
                    StateStore.ExpandHome(entry.ConfigDir), 100, 30, entry.SessionId));

                restored++;
            }
            catch (Exception ex)
            {
                failure ??= ex.Message;
            }
        }

        if (restored > 0) RememberTerminals();
        return restored;
    }

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
        ConsoleInput.Start();
        Console.CancelKeyPress += OnCancel;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        try
        {
            Loop();
        }
        finally
        {
            Console.CancelKeyPress -= OnCancel;
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
            ConsoleInput.Stop();

            // These are our children; leaving them behind would orphan a Claude
            // process the user has no way to find again.
            StopSessions();

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
        // Ctrl+C belongs to whatever is focused - usually Claude, which uses it
        // to interrupt a turn. It used to stop every session and let the process
        // die, so one keystroke inside a terminal tile took the whole launcher
        // and every other session with it.
        e.Cancel = true;

        // The console swallowed the key to raise this event, so hand it on.
        ConsoleInput.Inject(new ConsoleKeyInfo((char)3, ConsoleKey.C, false, false, true));
    }

    /// <summary>
    /// Ends everything this launcher started. Safe to call twice: both Dispose
    /// implementations are idempotent, and the lists are emptied as we go.
    /// </summary>
    private void StopSessions()
    {
        foreach (var chat in Chats)
        {
            try { chat.Dispose(); } catch (Exception) { /* nothing useful left to do */ }
        }

        Chats.Clear();

        foreach (var terminal in Terminals)
        {
            try { terminal.Dispose(); } catch (Exception) { /* nothing useful left to do */ }
        }

        Terminals.Clear();
    }

    private void OnProcessExit(object? sender, EventArgs e) => StopSessions();

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

            var input = WaitForInput(width, height, Current);
            if (input is null) continue; // resized, or the screen asked for a repaint

            Apply(Current.HandleInput(input.Value));
        }
    }

    /// <summary>
    /// Blocks until something happens: input, a tile drawing, a resize, or the
    /// screen's own refresh falling due.
    ///
    /// This used to poll every 35ms, which sat in front of every keystroke. It
    /// now waits on the input thread and on a tile signalling new output, so a
    /// key is handled as soon as Windows has it and an echo is painted as soon
    /// as the child produces it.
    /// </summary>
    private static InputEvent? WaitForInput(int width, int height, ScreenBase screen)
    {
        var interval = screen.RefreshInterval;
        var next = interval is null ? DateTime.MaxValue : DateTime.UtcNow + interval.Value;

        while (true)
        {
            // Long enough to cost nothing while idle; the wake-up is what makes
            // it responsive, not this number.
            var slice = interval is null ? TimeSpan.FromMilliseconds(120) : interval.Value;
            if (ConsoleInput.Wait(slice, out var input)) return input;

            if (Term.Width != width || Term.Height != height) return null;

            // A tile that produced output asks for the repaint directly, so this
            // is no longer the only thing keeping the screen current.
            if (screen.NeedsRedraw()) return null;

            if (DateTime.UtcNow >= next && interval is not null) next = DateTime.UtcNow + interval.Value;
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

            case ActionKind.Root:
                if (action.Next is not null)
                {
                    _stack.Clear();
                    _stack.Add(action.Next);
                }

                break;
            case ActionKind.Quit:
                _stack.Clear();
                break;
            case ActionKind.Finish:
                if (Profile is not null && Project is not null)
                {
                    LaunchMode = action.Mode ?? "new";
                    LaunchOpenIn = LaunchTarget.Normalize(action.OpenIn ?? Settings.DefaultOpenIn);
                    StateStore.WriteResult(Profile, Project, LaunchMode, LaunchOpenIn, action.SessionId,
                        Settings.RemoteControl);
                }

                _stack.Clear();
                break;
        }
    }
}
