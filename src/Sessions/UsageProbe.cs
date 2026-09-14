using ClaudeLauncher.Terminal;

namespace ClaudeLauncher.Sessions;

/// <summary>
/// Asks Claude for its own usage figure, for a config dir the wall already
/// has a live, trusted terminal open in - one hidden process per profile,
/// run only because Alt+G was just pressed.
///
/// There is no API for this - /usage only exists as a slash command inside a
/// real interactive session, and running it through -p sends the words
/// "/usage" to the model as a plain message instead of recognising the
/// command, which answers nothing and spends a real turn. So this drives an
/// actual `claude` process under a pseudo console exactly the way a terminal
/// tile does, types /usage into it once it looks settled, waits for
/// Claude's own cache to move, and closes it - a few seconds of a process
/// nobody sees, in exchange for a figure that would otherwise sit stale
/// until somebody typed the command themselves.
///
/// Deliberately not on a timer: a version of this that woke up on its own
/// was refused as an unattended agent spawn, so <see cref="Refresh"/> only
/// ever runs from TerminalsScreen's own Alt+G handler, on a real key press.
///
/// Reuses the live pane's own project path as the child's working directory
/// on purpose: that path is already trusted (a session is running in it this
/// moment), so the workspace-trust dialog - which nothing here could answer -
/// never has a reason to appear.
/// </summary>
public static class UsageProbe
{
    private static readonly Dictionary<string, DateTime> LastAt = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> InFlight = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    /// <summary>
    /// How soon a second Alt+G for the same profile is allowed to start another
    /// process, rather than just re-show what the last one already found. Short
    /// on purpose - this exists to stop a double press or a quick off-then-on
    /// spawning two processes for the same answer, not to ration how often
    /// someone may ask.
    /// </summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan StartupBudget = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan AnswerBudget = TimeSpan.FromSeconds(12);

    /// <summary>
    /// Runs the probe for one config dir if it is not already running and the
    /// interval has passed - a no-op otherwise, so this is cheap to call once a
    /// frame for every profile currently on the wall.
    /// </summary>
    public static void Refresh(string configDir, string cwd, Action? changed)
    {
        lock (Gate)
        {
            if (InFlight.Contains(configDir)) return;
            if (LastAt.TryGetValue(configDir, out var at) && DateTime.UtcNow - at < Interval) return;

            InFlight.Add(configDir);
        }

        Task.Run(() => Run(configDir, cwd, changed));
    }

    private static void Run(string configDir, string cwd, Action? changed)
    {
        var sessionId = Guid.NewGuid().ToString();

        // Hidden before it is started, not after: the registry can name a
        // session within a second of the process existing, which is sooner
        // than anything checking back in here would notice otherwise.
        HiddenSessions.Hide(sessionId);

        TerminalTile? tile = null;

        try
        {
            var before = FetchedAtMs(configDir);

            tile = TerminalTile.Start(cwd, "usage probe", configDir, 80, 24, newSessionId: sessionId);

            WaitForQuiet(tile);
            tile.Write("/usage\r");
            WaitForAnswer(configDir, before);
        }
        catch (Exception)
        {
            // A profile whose account cannot spawn this run - out of handles,
            // the exe moved - simply keeps its old figure. Nothing here is
            // worth surfacing to someone who never asked for it.
        }
        finally
        {
            tile?.Close();

            lock (Gate)
            {
                InFlight.Remove(configDir);
                LastAt[configDir] = DateTime.UtcNow;
            }

            // The watcher would pick this up on its own within about a second,
            // but there is no reason to wait out that second when the write
            // just happened on this very thread.
            Metrics.RefreshBand();
            changed?.Invoke();

            HiddenSessions.Unhide(sessionId);
        }
    }

    /// <summary>
    /// Claude's own startup paints in bursts; this waits for a pause after the
    /// first one; before typing, the same way a person would.
    /// </summary>
    private static void WaitForQuiet(TerminalTile tile)
    {
        var deadline = DateTime.UtcNow + StartupBudget;
        var lastRevision = -1L;
        var quietSince = DateTime.UtcNow;

        while (DateTime.UtcNow < deadline)
        {
            var revision = tile.Revision;

            if (revision != lastRevision)
            {
                lastRevision = revision;
                quietSince = DateTime.UtcNow;
            }
            else if (revision > 0 && DateTime.UtcNow - quietSince > TimeSpan.FromMilliseconds(500))
            {
                return;
            }

            Thread.Sleep(100);
        }

        // Slower than usual is not a reason to give up - /usage is sent either
        // way, on whatever Claude has painted by the deadline.
    }

    private static void WaitForAnswer(string configDir, long before)
    {
        var deadline = DateTime.UtcNow + AnswerBudget;

        while (DateTime.UtcNow < deadline)
        {
            if (FetchedAtMs(configDir) > before) return;
            Thread.Sleep(300);
        }
    }

    private static long FetchedAtMs(string configDir)
    {
        var limits = UsageLimits.Read(configDir);
        return limits.FetchedUtc == DateTime.MinValue
            ? 0
            : new DateTimeOffset(limits.FetchedUtc, TimeSpan.Zero).ToUnixTimeMilliseconds();
    }
}
