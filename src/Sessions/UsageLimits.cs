using System.Text.Json;

namespace ClaudeLauncher.Sessions;

/// <summary>Which window a percentage belongs to.</summary>
public enum LimitWindow
{
    None,

    /// <summary>The rolling five-hour session allowance.</summary>
    Session,

    /// <summary>The weekly allowance.</summary>
    Weekly
}

/// <summary>
/// How much of an account's plan is used, as Claude itself last worked it out.
///
/// This is the only place a real percentage can come from. Cost and token counts
/// in .claude.json are running totals with no ceiling recorded beside them, so
/// they can never answer "how close am I to the limit" - but Claude caches the
/// answer to exactly that question under cachedUsageUtilization when it talks to
/// the API, and it does so inside each config dir, which makes it per account.
/// </summary>
public sealed class AccountLimits
{
    /// <summary>False when the account has no cached answer to read.</summary>
    public bool Known { get; set; }

    public int SessionPercent { get; set; }
    public int WeeklyPercent { get; set; }

    public DateTime? SessionResetsUtc { get; set; }
    public DateTime? WeeklyResetsUtc { get; set; }

    /// <summary>
    /// The window Claude marked as the live one. Both are reported at all times;
    /// this is the one it says is actually counting.
    /// </summary>
    public LimitWindow Active { get; set; }

    /// <summary>Claude's own word for how bad it is: normal, warning, critical.</summary>
    public string Severity { get; set; } = "normal";

    /// <summary>When Claude last asked the API. Zero when unknown.</summary>
    public DateTime FetchedUtc { get; set; }

    /// <summary>
    /// The percentage worth showing in one place: the live window's, falling back
    /// to whichever is higher when Claude marked neither as live.
    /// </summary>
    public int Headline => Active switch
    {
        LimitWindow.Session => SessionPercent,
        LimitWindow.Weekly => WeeklyPercent,
        _ => Math.Max(SessionPercent, WeeklyPercent)
    };

    public LimitWindow HeadlineWindow => Active != LimitWindow.None
        ? Active
        : SessionPercent >= WeeklyPercent ? LimitWindow.Session : LimitWindow.Weekly;

    /// <summary>
    /// True once the cache is older than the session window it describes, which
    /// makes the session figure a guess about a window that has since rolled
    /// over. Shown rather than hidden: a stale number presented as current is
    /// worse than one marked as old.
    /// </summary>
    public bool Stale => FetchedUtc == DateTime.MinValue ||
                         DateTime.UtcNow - FetchedUtc > TimeSpan.FromHours(5);

    /// <summary>When the headline window rolls over, if Claude said.</summary>
    public DateTime? HeadlineResetsUtc =>
        HeadlineWindow == LimitWindow.Session ? SessionResetsUtc : WeeklyResetsUtc;
}

/// <summary>Reads the usage percentages Claude cached for one config dir.</summary>
public static class UsageLimits
{
    public static string File(string configDir) => Path.Combine(configDir, ".claude.json");

    /// <summary>
    /// Never throws: a config dir with no cache, an unreadable file or a shape
    /// Claude has since changed all come back as "not known" rather than
    /// stopping the band from drawing.
    /// </summary>
    public static AccountLimits Read(string configDir)
    {
        var limits = new AccountLimits();

        try
        {
            var path = File(configDir);
            if (!System.IO.File.Exists(path)) return limits;

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);

            using var document = JsonDocument.Parse(stream);

            if (!document.RootElement.TryGetProperty("cachedUsageUtilization", out var cached)) return limits;
            if (cached.ValueKind != JsonValueKind.Object) return limits;

            if (cached.TryGetProperty("fetchedAtMs", out var fetched) &&
                fetched.TryGetInt64(out var millis))
            {
                limits.FetchedUtc = DateTimeOffset.FromUnixTimeMilliseconds(millis).UtcDateTime;
            }

            if (!cached.TryGetProperty("utilization", out var usage) ||
                usage.ValueKind != JsonValueKind.Object)
            {
                return limits;
            }

            // The flat pair is the stable part of the shape; the limits array
            // carries the extras - which window is live, and how bad Claude
            // thinks it is - so both are read and the array wins where it exists.
            limits.SessionPercent = Percent(usage, "five_hour", out var sessionResets);
            limits.WeeklyPercent = Percent(usage, "seven_day", out var weeklyResets);
            limits.SessionResetsUtc = sessionResets;
            limits.WeeklyResetsUtc = weeklyResets;
            limits.Known = true;

            if (usage.TryGetProperty("limits", out var rows) && rows.ValueKind == JsonValueKind.Array)
                ReadRows(rows, limits);

            return limits;
        }
        catch (Exception)
        {
            return limits;
        }
    }

    private static void ReadRows(JsonElement rows, AccountLimits limits)
    {
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object) continue;

            var group = Text(row, "group");
            var window = group switch
            {
                "session" => LimitWindow.Session,
                "weekly" => LimitWindow.Weekly,
                _ => LimitWindow.None
            };

            if (window == LimitWindow.None) continue;

            // A weekly row scoped to one model sits beside the unscoped one and
            // is always the smaller number; the plan's limit is the unscoped one.
            if (window == LimitWindow.Weekly && Text(row, "kind") != "weekly_all") continue;

            if (row.TryGetProperty("percent", out var percent) && percent.TryGetInt32(out var value))
            {
                if (window == LimitWindow.Session) limits.SessionPercent = value;
                else limits.WeeklyPercent = value;
            }

            if (row.TryGetProperty("resets_at", out var resets) && When(resets) is { } at)
            {
                if (window == LimitWindow.Session) limits.SessionResetsUtc = at;
                else limits.WeeklyResetsUtc = at;
            }

            var active = row.TryGetProperty("is_active", out var flag) &&
                         flag.ValueKind == JsonValueKind.True;

            if (active) limits.Active = window;

            var severity = Text(row, "severity");
            if (active && severity.Length > 0) limits.Severity = severity;
        }
    }

    private static int Percent(JsonElement usage, string name, out DateTime? resets)
    {
        resets = null;
        if (!usage.TryGetProperty(name, out var block) || block.ValueKind != JsonValueKind.Object) return 0;

        if (block.TryGetProperty("resets_at", out var at)) resets = When(at);

        return block.TryGetProperty("utilization", out var value) && value.TryGetInt32(out var percent)
            ? percent
            : 0;
    }

    private static DateTime? When(JsonElement element) =>
        element.ValueKind == JsonValueKind.String &&
        DateTimeOffset.TryParse(element.GetString(), out var at)
            ? at.UtcDateTime
            : null;

    private static string Text(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
