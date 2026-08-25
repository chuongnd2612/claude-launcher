using ClaudeLauncher.Sessions;
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
    /// Tiles taken off the wall, keyed the way the wall keys them: session id,
    /// or project path while a chat is still waiting for one.
    ///
    /// Kept here rather than on the screen because every route back to the wall
    /// builds a new one - opening a terminal alone lands on a fresh wall - and a
    /// set that lived on the screen took the closing with it, so a session in
    /// someone else's terminal came straight back and could not be got rid of.
    /// </summary>
    public HashSet<string> HiddenTiles { get; } = new(StringComparer.Ordinal);

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
            DrawClosing(0, Chats.Count + Terminals.Count);
            StopSessions(DrawClosing);

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
    ///
    /// Sessions close in parallel because each one waits on its own child to go
    /// away - up to two seconds - and doing that in turn made quitting with a
    /// few terminals open look like the launcher had hung.
    /// </summary>
    private void StopSessions(Action<int, int>? progress = null)
    {
        var work = new List<Task>();
        var total = Chats.Count + Terminals.Count;
        var done = 0;

        foreach (var chat in Chats.ToList())
        {
            work.Add(Task.Run(() =>
            {
                try { chat.Dispose(); }
                catch (Exception) { /* nothing useful left to do */ }
                finally { Interlocked.Increment(ref done); }
            }));
        }

        foreach (var terminal in Terminals.ToList())
        {
            work.Add(Task.Run(() =>
            {
                try { terminal.Dispose(); }
                catch (Exception) { /* nothing useful left to do */ }
                finally { Interlocked.Increment(ref done); }
            }));
        }

        Chats.Clear();
        Terminals.Clear();

        if (work.Count == 0) return;

        var all = Task.WhenAll(work);

        while (!all.Wait(TimeSpan.FromMilliseconds(90)))
            progress?.Invoke(Volatile.Read(ref done), total);

        progress?.Invoke(total, total);
    }

    /// <summary>
    /// The last frame: what is being closed, and how far along it is. Drawn
    /// while the console is still ours, so quitting reads as work in progress
    /// rather than a frozen screen.
    /// </summary>
    private void DrawClosing(int done, int total)
    {
        if (total == 0) return;

        _buffer.PaintBackground = Settings.PaintBackground;
        _buffer.Clear();

        var y = Math.Max(2, _buffer.Height / 2 - 2);
        var width = Math.Min(52, Math.Max(24, _buffer.Width - 8));
        var x = Math.Max(0, (_buffer.Width - width) / 2);

        _buffer.Box(x, y, width, 5, new Sty(Theme.Border, Theme.Panel), BoxStyle.Rounded, Theme.Panel);

        var label = total == 1 ? "Closing 1 session" : $"Closing {total} sessions";
        _buffer.WriteClipped(x + 3, y + 1, label, width - 6, new Sty(Theme.Text, Theme.Panel, bold: true));

        var barWidth = width - 6;
        var filled = total == 0 ? barWidth : (int)Math.Round((double)done / total * barWidth);

        for (var i = 0; i < barWidth; i++)
        {
            var on = i < filled;
            _buffer.Set(x + 3 + i, y + 2, on ? '█' : '░',
                new Sty(on ? Theme.Blue : Theme.Dim, Theme.Panel));
        }

        _buffer.WriteClipped(x + 3, y + 3, $"{done} of {total} stopped", width - 6,
            new Sty(Theme.Dim, Theme.Panel, italic: true));

        _buffer.Flush();
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

            RefreshUsage();

            _buffer.PaintBackground = Settings.PaintBackground;
            _buffer.Clear();
            Current.Render(_buffer);
            _buffer.Flush();

            var input = WaitForInput(width, height, Current, UsageDueUtc);
            if (input is null) continue; // resized, or the band or screen asked for a repaint

            // The band is chrome on every screen, so its own commands are read
            // here rather than in nineteen HandleKey methods - and reading them
            // before the screen is what makes them work inside a focused
            // terminal tile, which otherwise takes every key.
            if (Refreshes(input.Value)) continue;

            Apply(Current.HandleInput(input.Value));
        }
    }

    /// <summary>
    /// When the loop should come round for the band's sake. Never, with no
    /// profiles configured: nothing reads the figures then, so the deadline would
    /// stay in the past and spin the loop.
    /// </summary>
    private DateTime UsageDueUtc =>
        State.Profiles.Count == 0 ? DateTime.MaxValue : Metrics.BandDueUtc;

    /// <summary>
    /// The band's refresh, by key or by clicking the word it starts with.
    /// Returns true when the input was spent on it.
    /// </summary>
    private static bool Refreshes(InputEvent input)
    {
        var asked = input.Kind switch
        {
            InputKind.Key => KeyBindings.Is(KeyAction.RefreshUsage, input.Key),
            InputKind.MouseDown => Widgets.OnUsageButton(input.X, input.Y),
            _ => false
        };

        if (!asked) return false;

        Metrics.RefreshBand();
        return true;
    }

    /// <summary>
    /// Hands the header band each account's share of its plan.
    ///
    /// Cheap to call every frame by design: Metrics.Band returns what it already
    /// has and goes looking again at most once a minute, on a thread of its own,
    /// waking the loop when it has an answer. Nothing here reads a file.
    /// </summary>
    private void RefreshUsage()
    {
        if (State.Profiles.Count == 0) return;

        var accounts = Metrics.Band(State, ConsoleInput.Wake);
        if (accounts is null) return;

        var chips = new List<UsageChip>(accounts.Count);
        foreach (var account in accounts)
        {
            var limits = account.Limits;

            // Both windows, for every account: one says whether to keep going
            // now, the other whether to keep going this week.
            chips.Add(new UsageChip(account.Icon, account.Label,
                ProfileLook.Color(account.Key),
                limits.Known ? limits.SessionPercent : -1,
                limits.Known ? limits.WeeklyPercent : -1,
                limits.Stale));
        }

        Widgets.Usage = chips;
        Widgets.UsageRefreshing = Metrics.BandRefreshing;
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
    private static InputEvent? WaitForInput(int width, int height, ScreenBase screen, DateTime usageDue)
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

            // The band reads its own files at most once a minute, but only when
            // the loop comes round - so on a screen that never asks for a repaint
            // this is the thing that brings it round.
            if (DateTime.UtcNow >= usageDue) return null;

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
