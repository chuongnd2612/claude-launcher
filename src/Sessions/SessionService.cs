using System.Text.Json;

namespace ClaudeLauncher.Sessions;

/// <summary>
/// Builds the Home screen's snapshot from what is on disk. Scans are cheap
/// (a handful of small JSON files plus a 64 KB tail per live session) but they
/// still never run on the render thread - callers refresh on a timer.
/// </summary>
public sealed class SessionService
{
    private readonly LauncherState _state;
    private readonly Dictionary<string, SessionReader.TranscriptFacts> _facts = new();
    private readonly Dictionary<string, DateTime> _factsAt = new();

    public SessionService(LauncherState state) => _state = state;

    /// <summary>Transcript tails are re-read at most this often per session.</summary>
    private static readonly TimeSpan FactsTtl = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Decode transcript entries too. Off for the Home list, which only needs
    /// counters; on for the terminal wall, which draws the conversation.
    /// </summary>
    public bool WithEntries { get; set; }

    /// <summary>
    /// Sessions the launcher owns. They run through the SDK entrypoint, which is
    /// otherwise filtered out - but ours are exactly the ones worth listing,
    /// because the user can step straight back into them.
    /// </summary>
    public Func<IReadOnlyCollection<string>>? OwnedSessionIds { get; set; }

    public SessionSnapshot Build()
    {
        var rows = new List<SessionRow>();
        var sessionsToday = 0;
        var owned = OwnedSessionIds?.Invoke() ?? (IReadOnlyCollection<string>)Array.Empty<string>();
        var recent = new List<RecentProject>();

        foreach (var profile in _state.Profiles)
        {
            var configDir = StateStore.ExpandHome(profile.ConfigDir);

            if (recent.Count == 0) recent = SessionReader.ReadRecentProjects(configDir, 6);

            foreach (var entry in SessionReader.ReadRegistry(configDir))
            {
                // Background agents driven by the SDK are not terminals anyone can
                // go back to, so they do not belong on this list. An unrecognised
                // or missing entrypoint is kept: better an extra row than a hidden one.
                // Our own chat sessions use that entrypoint too, so they are let
                // through by id - they are the ones you can step straight back into.
                if (!string.IsNullOrEmpty(entry.Entrypoint) &&
                    !string.Equals(entry.Entrypoint, "cli", StringComparison.OrdinalIgnoreCase) &&
                    !owned.Contains(entry.SessionId))
                {
                    continue;
                }

                // Claude also registers its own background work - a startup hook
                // or a plugin - as kind "bg" while still reporting entrypoint
                // "cli", so the check above lets it through. Starting one session
                // then showed three panes: the real one and two of these.
                if (string.Equals(entry.Kind, "bg", StringComparison.OrdinalIgnoreCase) &&
                    !owned.Contains(entry.SessionId))
                {
                    continue;
                }

                var startedAt = DateTimeOffset.FromUnixTimeMilliseconds(entry.StartedAt).UtcDateTime;
                if (startedAt.Date == DateTime.UtcNow.Date) sessionsToday++;

                if (!SessionReader.IsAlive(entry.Pid, entry.ProcStart)) continue;

                var facts = Facts(configDir, entry);
                var row = new SessionRow
                {
                    SessionId = entry.SessionId,
                    ProfileName = profile.DisplayLabel,
                    ProfileIcon = profile.DisplayIcon,
                    ProjectPath = entry.Cwd,
                    ProjectName = ProjectName(entry.Cwd),
                    Task = Task(entry, facts),
                    ContextTokens = facts.ContextTokens,
                    Model = ShortModel(facts.Model),
                    Pid = entry.Pid,
                    Branch = facts.Branch,
                    Entries = facts.Entries
                };

                Classify(entry, row);
                rows.Add(row);
            }
        }

        // Anything wanting attention floats to the top; the rest stay stable.
        rows = rows.OrderByDescending(r => r.State == SessionState.Waiting)
            .ThenBy(r => r.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.SessionId, StringComparer.Ordinal)
            .ToList();

        return new SessionSnapshot
        {
            Sessions = rows,
            Recent = recent,
            SessionsToday = sessionsToday
        };
    }

    /// <summary>
    /// Claude publishes only "busy" and "idle", so "waiting for input" is a
    /// guess: idle, but only just. It is rendered with a question mark for that
    /// reason - being vague beats being confidently wrong.
    /// </summary>
    private static void Classify(ClaudeSessionFile entry, SessionRow row)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var since = entry.StatusUpdatedAt > 0 ? entry.StatusUpdatedAt : entry.StartedAt;
        row.StateAge = TimeSpan.FromMilliseconds(Math.Max(0, now - since));

        // Only a missing status is unknown. updatedAt is NOT a heartbeat - Claude
        // stamps it when the status changes, so a session idle since yesterday
        // legitimately carries a 17-hour-old timestamp and is still perfectly fine.
        if (string.IsNullOrEmpty(entry.Status))
        {
            row.State = SessionState.Unknown;
            return;
        }

        if (entry.Status == "busy")
        {
            row.State = SessionState.Running;
            return;
        }

        row.State = row.StateAge < TimeSpan.FromSeconds(30) ? SessionState.Waiting : SessionState.Idle;
    }

    private SessionReader.TranscriptFacts Facts(string configDir, ClaudeSessionFile entry)
    {
        var key = entry.SessionId;

        if (_factsAt.TryGetValue(key, out var at) && DateTime.UtcNow - at < FactsTtl &&
            _facts.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var path = ClaudePaths.TranscriptFile(configDir, entry.Cwd, entry.SessionId);
        var facts = SessionReader.ReadTranscriptTail(path, WithEntries);

        _facts[key] = facts;
        _factsAt[key] = DateTime.UtcNow;
        return facts;
    }

    private static string Task(ClaudeSessionFile entry, SessionReader.TranscriptFacts facts)
    {
        if (!string.IsNullOrWhiteSpace(facts.Title)) return facts.Title!;
        if (!string.IsNullOrWhiteSpace(entry.Name)) return entry.Name!;
        return entry.SessionId.Length >= 8 ? entry.SessionId.Substring(0, 8) : entry.SessionId;
    }

    private static string ProjectName(string cwd)
    {
        var trimmed = cwd.TrimEnd('\\', '/');
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name;
    }

    /// <summary>"claude-opus-5" reads better than the full id in a narrow column.</summary>
    private static string? ShortModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return null;
        var value = model!.StartsWith("claude-", StringComparison.Ordinal) ? model.Substring(7) : model;
        var dash = value.LastIndexOf('-');
        return dash > 0 && value.Length - dash > 6 ? value.Substring(0, dash) : value;
    }

}
