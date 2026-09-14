namespace ClaudeLauncher.Sessions;

/// <summary>
/// Session ids the launcher started for its own purposes and never wants
/// listed anywhere a person would see it - a session, but not one of theirs.
///
/// A session Claude registers shows up in <see cref="SessionService"/> the
/// moment its own registry file names it, whether or not the launcher put a
/// tile on the wall for it. A hidden probe (<see cref="UsageProbe"/>) is a
/// real `claude` process for exactly that reason: it needs to be hidden
/// before it starts, not filtered after someone has already seen it flicker
/// into the session list and back out.
/// </summary>
public static class HiddenSessions
{
    private static readonly HashSet<string> Ids = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    public static void Hide(string sessionId)
    {
        lock (Gate) Ids.Add(sessionId);
    }

    public static void Unhide(string sessionId)
    {
        lock (Gate) Ids.Remove(sessionId);
    }

    public static bool IsHidden(string sessionId)
    {
        lock (Gate) return Ids.Contains(sessionId);
    }
}
