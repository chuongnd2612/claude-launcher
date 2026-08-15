namespace ClaudeLauncher.Sessions;

/// <summary>Compact renderings used by the session screens.</summary>
public static class Format
{
    /// <summary>"now", "4m", "2h", "3d" - fits the narrow columns in the design.</summary>
    public static string Ago(DateTime utc)
    {
        if (utc == DateTime.MinValue) return "-";

        var span = DateTime.UtcNow - utc;
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;

        if (span.TotalMinutes < 1) return "now";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours}h ago";
        return $"{(int)span.TotalDays}d ago";
    }

    /// <summary>"12m 04s" under an hour, "2h 11m" above it.</summary>
    public static string Duration(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        if (span.TotalMinutes < 1) return $"{span.Seconds}s";
        if (span.TotalHours < 1) return $"{span.Minutes}m {span.Seconds:00}s";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours}h {span.Minutes:00}m";
        return $"{(int)span.TotalDays}d";
    }

    /// <summary>"41k", "184k", "2.41M".</summary>
    public static string Tokens(long value)
    {
        if (value <= 0) return "-";
        if (value < 1_000) return value.ToString();
        if (value < 1_000_000) return $"{value / 1000}k";
        return $"{value / 1_000_000.0:0.00}M";
    }

    /// <summary>Minutes and up, no seconds: "46s", "4m", "2h 11m".</summary>
    public static string Coarse(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        if (span.TotalMinutes < 1) return $"{span.Seconds}s";
        if (span.TotalHours < 1) return $"{span.Minutes}m";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours}h {span.Minutes:00}m";
        return $"{(int)span.TotalDays}d";
    }

    /// <summary>
    /// The state column. Seconds only matter while something is actively
    /// working, so idle and waiting round to minutes.
    /// The question mark on "waiting" is deliberate: Claude publishes only busy
    /// and idle, so a session waiting on a prompt is inferred, not known.
    /// </summary>
    public static string State(SessionState state, TimeSpan age) => state switch
    {
        SessionState.Running => $"running {Duration(age)}",
        SessionState.Waiting => $"waiting? {Coarse(age)}",
        SessionState.Idle => $"idle {Coarse(age)}",
        _ => "unknown"
    };
}
