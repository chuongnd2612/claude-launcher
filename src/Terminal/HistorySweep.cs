using System.Diagnostics;

namespace ClaudeLauncher.Terminal;

/// <summary>
/// Searches what has scrolled off the top of a terminal by asking it to show
/// that text again.
///
/// Claude lives on the alternate screen, which has no scrollback the launcher
/// can read: it repaints the same grid and keeps its history to itself. But it
/// scrolls that history for a wheel, so the way to search what is no longer
/// drawn is to scroll back a screenful at a time and search each repaint - the
/// same thing a person does, minus the reading.
///
/// Measured against a live session: one notch moves one line and a repaint
/// lands in about 120 ms, so a screenful is one step rather than one line, and
/// the sweep runs off the render thread with its progress on show.
/// </summary>
public sealed class HistorySweep
{
    public enum Result
    {
        Searching,
        Found,
        Exhausted,
        Stopped,
        CannotScroll
    }

    private readonly TerminalTile _tile;
    private readonly CancellationTokenSource _cancel = new();
    private int _sent;

    private HistorySweep(TerminalTile tile, string query)
    {
        _tile = tile;
        Query = query;
    }

    public string Query { get; }

    public Result State { get; private set; } = Result.Searching;

    /// <summary>Screenfuls of history looked at so far.</summary>
    public int Screens { get; private set; }

    public bool Done => State != Result.Searching;

    /// <summary>How far back the sweep is standing, so the view can be put back.</summary>
    public int Lines => _sent;

    /// <summary>Starts a sweep, or returns one already finished if it cannot run.</summary>
    public static HistorySweep Start(TerminalTile tile, string query, Action? changed = null)
    {
        var sweep = new HistorySweep(tile, query);

        if (!tile.WantsMouse || string.IsNullOrEmpty(query) || tile.HasExited)
        {
            sweep.State = Result.CannotScroll;
            return sweep;
        }

        Task.Run(() => sweep.Sweep(changed));
        return sweep;
    }

    /// <summary>Give up on the current sweep; the caller decides where to leave the view.</summary>
    public void Stop()
    {
        _cancel.Cancel();
        if (State == Result.Searching) State = Result.Stopped;
    }

    /// <summary>A screenful at a time - anything finer just costs repaints.</summary>
    private const int MaxScreens = 400;

    /// <summary>Long enough for a repaint at four times what one measured.</summary>
    private static readonly TimeSpan Repaint = TimeSpan.FromMilliseconds(500);

    private void Sweep(Action? changed)
    {
        try
        {
            while (!_cancel.IsCancellationRequested && Screens < MaxScreens)
            {
                var before = Snapshot(out var rows);
                var page = Math.Max(3, rows - 2);

                Scroll(page);

                if (!WaitForRepaint(before))
                {
                    // Nothing moved, so there is nothing above this: the top.
                    State = Result.Exhausted;
                    break;
                }

                Screens++;
                changed?.Invoke();

                var hits = 0;
                _tile.Read(screen => hits = screen.Find(Query, 1).Count);

                if (hits > 0)
                {
                    State = Result.Found;
                    break;
                }
            }

            if (State == Result.Searching) State = Result.Exhausted;
        }
        catch (Exception)
        {
            State = Result.Stopped;
        }
        finally
        {
            changed?.Invoke();
        }
    }

    /// <summary>
    /// Puts the view back at the live end of the session.
    ///
    /// Pacing decides this, not arithmetic. Three approaches were measured
    /// before this one: sending back exactly what went out lands short, because
    /// a wheel report arriving mid-repaint is dropped; overshooting by half at
    /// speed lands short for the same reason; and stopping at the first still
    /// frame quits early, because a repaint can outlast the wait. What does work
    /// is what a hand does - a notch at a time, giving the screen time to catch
    /// up, until it stops moving. An idle screen really is still: two snapshots
    /// half a second apart matched exactly.
    /// </summary>
    public void ReturnToBottom()
    {
        Stop();
        if (_sent <= 0) return;

        // Two conditions, because either alone is wrong. Distance alone lands
        // short - ninety lines up took a hundred and twenty down, since a wheel
        // report arriving mid-repaint is dropped. Stillness alone stops early -
        // three unchanged frames happen mid-history whenever a repaint lags. So
        // send at least what went out, then keep going until the screen holds.
        // Half again as far as went out: that is the drop rate measured on the
        // way back, and overshooting the bottom costs nothing.
        var least = _sent * 3 / 20 + 6;
        var most = least * 4 + 20;
        _sent = 0;

        Task.Run(() =>
        {
            try
            {
                var still = 0;
                var before = Snapshot(out _);

                for (var step = 0; step < most; step++)
                {
                    _tile.SendWheel(-10, 2, 2);
                    Thread.Sleep(250);

                    var now = Snapshot(out _);
                    still = now == before ? still + 1 : 0;
                    before = now;

                    if (step >= least && still >= 3) break;
                }
            }
            catch (Exception)
            {
                // The view is the child's to own; failing to move it is not our error.
            }
        });
    }

    /// <summary>Wheel bursts are capped per call, so a page goes in several.</summary>
    private void Scroll(int lines)
    {
        var left = Math.Abs(lines);
        var up = lines > 0;

        while (left > 0)
        {
            var chunk = Math.Min(10, left);
            _tile.SendWheel(up ? chunk : -chunk, 2, 2);
            left -= chunk;

            if (up) _sent += chunk;

            // Wheel reports sent back to back while Claude is repainting are
            // dropped - measured as a drift of tens of lines over one sweep.
            if (left > 0) Thread.Sleep(60);
        }
    }

    private string Snapshot(out int rows)
    {
        var text = string.Empty;
        var height = 24;

        _tile.Read(screen =>
        {
            text = screen.ToPlainText();
            height = screen.Rows;
        });

        rows = height;
        return text;
    }

    private bool WaitForRepaint(string before) => WaitForChange(before, _cancel.Token);

    private bool WaitForChange(string before, CancellationToken cancel)
    {
        var watch = Stopwatch.StartNew();

        while (watch.Elapsed < Repaint)
        {
            if (cancel.IsCancellationRequested) return false;

            Thread.Sleep(20);
            if (Snapshot(out _) != before) return true;
        }

        return false;
    }
}
