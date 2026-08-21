using System.Diagnostics;
using System.Text.Json;

namespace ClaudeLauncher.Sessions;

/// <summary>How far back the dashboard looks.</summary>
public enum Period
{
    Today,
    Week,
    All
}

/// <summary>What one profile has spent, as Claude itself recorded it.</summary>
public sealed class ProfileUsage
{
    public string Label { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Account { get; set; } = string.Empty;

    public double CostUsd { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public int WebSearches { get; set; }

    /// <summary>Projects this profile has a usage record for.</summary>
    public int Projects { get; set; }

    /// <summary>False when Claude recorded no cost at all, so the row shows a dash.</summary>
    public bool HasCost { get; set; }
}

/// <summary>One project's share of the period.</summary>
public sealed class ProjectActivity
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int Sessions { get; set; }
    public int Prompts { get; set; }
    public double CostUsd { get; set; }
    public bool HasCost { get; set; }
}

/// <summary>The counts across every profile for the chosen period.</summary>
public sealed class ActivityTotals
{
    public int Sessions { get; set; }
    public int Prompts { get; set; }
    public int Live { get; set; }
    public int Waiting { get; set; }
    public int? BusiestHour { get; set; }

    public int FilesTouched { get; set; }
    public int Edits { get; set; }
    public int Commands { get; set; }
    public int PullRequests { get; set; }
}

/// <summary>Everything the dashboard draws, built off the render thread.</summary>
public sealed class DashboardData
{
    public static readonly DashboardData Empty = new();

    public Period Period { get; set; } = Period.Today;
    public List<ProfileUsage> Profiles { get; } = new();
    public List<ProjectActivity> Projects { get; } = new();
    public ActivityTotals Totals { get; set; } = new();

    /// <summary>How much was read, and how long it took - shown so the numbers can be trusted.</summary>
    public long BytesScanned { get; set; }
    public long Milliseconds { get; set; }

    /// <summary>True when the scan hit its ceiling, so the counts are a floor.</summary>
    public bool Capped { get; set; }

    public double TotalCost => Profiles.Sum(p => p.CostUsd);
    public long TotalOutput => Profiles.Sum(p => p.OutputTokens);
}

/// <summary>
/// Adds up what Claude has already written down.
///
/// Two different kinds of number live here, and they are kept apart on purpose.
/// Cost and tokens come from Claude's own record in .claude.json, which carries
/// no timestamps - so those are totals, not a period, and the screen must not
/// claim otherwise. Everything with a date on it - sessions, prompts, edits,
/// pull requests - is counted from lines that carry a timestamp, which is what
/// makes "today" mean today.
///
/// Measured on this machine: 4 transcripts touched today come to 33.6 MB and
/// scan in 37 ms, so a period's counts are affordable. All 254 transcripts are
/// 395 MB, which is why the whole-history case is capped rather than promised.
/// </summary>
public static class Metrics
{
    /// <summary>Past this the scan stops and says the counts are a floor.</summary>
    private const long ScanCeiling = 512L * 1024 * 1024;

    private static readonly Dictionary<Period, DashboardData> Recent = new();
    private static readonly Dictionary<Period, DateTime> RecentAt = new();
    private static readonly HashSet<Period> Building = new();

    /// <summary>Long enough that Home can show these without reading anything twice a minute.</summary>
    private static readonly TimeSpan Fresh = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The last answer, refreshing it in the background when it has gone stale.
    ///
    /// Home draws these numbers on every frame, and building them reads tens of
    /// megabytes - so it returns what it has and asks again at most once a
    /// minute. Null until the first answer, which is why the band on Home simply
    /// is not there for the first moment rather than showing zeroes.
    /// </summary>
    public static DashboardData? Cached(LauncherState state, SessionSnapshot snapshot, Period period,
        Action? changed = null)
    {
        lock (Recent)
        {
            var have = Recent.TryGetValue(period, out var data);
            var stale = !RecentAt.TryGetValue(period, out var at) || DateTime.UtcNow - at > Fresh;

            if (have && !stale) return data;
            if (Building.Contains(period)) return have ? data : null;

            Building.Add(period);

            Task.Run(() =>
            {
                try
                {
                    var built = Build(state, snapshot, period);

                    lock (Recent)
                    {
                        Recent[period] = built;
                        RecentAt[period] = DateTime.UtcNow;
                    }
                }
                catch (Exception)
                {
                    lock (Recent) RecentAt[period] = DateTime.UtcNow;
                }
                finally
                {
                    lock (Recent) Building.Remove(period);
                    changed?.Invoke();
                }
            });

            return have ? data : null;
        }
    }

    public static DashboardData Build(LauncherState state, SessionSnapshot snapshot, Period period)
    {
        var watch = Stopwatch.StartNew();
        var data = new DashboardData { Period = period };

        var since = Start(period);
        var costs = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var prompts = new Dictionary<string, (int Prompts, HashSet<string> Sessions)>(StringComparer.OrdinalIgnoreCase);
        var hours = new int[24];
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var prs = new HashSet<string>(StringComparer.Ordinal);
        var sessions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in state.Profiles)
        {
            var configDir = StateStore.ExpandHome(profile.ConfigDir);

            var usage = ReadUsage(configDir);
            usage.Label = profile.DisplayLabel;
            usage.Icon = profile.DisplayIcon;
            usage.Key = profile.Name;
            usage.Account = SessionReader.ReadAccount(configDir)?.Label ?? string.Empty;
            data.Profiles.Add(usage);

            foreach (var (path, cost) in ReadProjectCosts(configDir))
            {
                costs[path] = costs.TryGetValue(path, out var was) ? was + cost : cost;
            }

            // Prompts carry a timestamp and a project, which is what makes the
            // activity numbers period-accurate without touching a transcript.
            foreach (var prompt in ReadHistory(configDir, since))
            {
                var key = prompt.Project;
                if (!prompts.TryGetValue(key, out var row))
                    row = (0, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

                row.Prompts++;
                if (prompt.SessionId.Length > 0)
                {
                    row.Sessions.Add(prompt.SessionId);
                    sessions.Add(prompt.SessionId);
                }

                prompts[key] = row;
                hours[prompt.WhenLocal.Hour]++;
            }

            data.BytesScanned += Scan(configDir, since, data, files, prs);
            if (data.BytesScanned >= ScanCeiling) data.Capped = true;
        }

        data.Profiles.Sort((a, b) => b.CostUsd.CompareTo(a.CostUsd));

        foreach (var (path, row) in prompts)
        {
            var has = costs.TryGetValue(path, out var cost);
            data.Projects.Add(new ProjectActivity
            {
                Path = path,
                Name = Leaf(path),
                Prompts = row.Prompts,
                Sessions = row.Sessions.Count,
                CostUsd = cost,
                HasCost = has
            });
        }

        data.Projects.Sort((a, b) => b.Prompts.CompareTo(a.Prompts));

        data.Totals.Prompts = prompts.Values.Sum(p => p.Prompts);
        data.Totals.Sessions = sessions.Count;
        data.Totals.Live = snapshot.Sessions.Count;
        data.Totals.Waiting = snapshot.Sessions.Count(s => s.State == SessionState.Waiting);
        data.Totals.FilesTouched = files.Count;
        data.Totals.PullRequests = prs.Count;

        var busiest = -1;
        for (var hour = 0; hour < 24; hour++)
        {
            if (hours[hour] > 0 && (busiest < 0 || hours[hour] > hours[busiest])) busiest = hour;
        }

        data.Totals.BusiestHour = busiest < 0 ? null : busiest;

        watch.Stop();
        data.Milliseconds = watch.ElapsedMilliseconds;
        return data;
    }

    public static DateTime Start(Period period) => period switch
    {
        Period.Today => DateTime.Today.ToUniversalTime(),
        Period.Week => DateTime.Today.AddDays(-6).ToUniversalTime(),
        _ => DateTime.UnixEpoch
    };

    public static string Describe(Period period) => period switch
    {
        Period.Today => "today",
        Period.Week => "last 7 days",
        _ => "all time"
    };

    // ---- Claude's own cost record -------------------------------------------

    /// <summary>
    /// Sums lastModelUsage across every project in one config dir.
    ///
    /// This block has no timestamps, so it is a total for as long as Claude has
    /// been keeping it - not a period. Cache reads are counted separately and
    /// never folded into the token figure: they run to billions here against
    /// millions of output tokens, and adding them makes the number meaningless.
    /// </summary>
    private static ProfileUsage ReadUsage(string configDir)
    {
        var usage = new ProfileUsage();

        try
        {
            var path = Path.Combine(configDir, ".claude.json");
            if (!File.Exists(path)) return usage;

            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);

            if (!document.RootElement.TryGetProperty("projects", out var projects) ||
                projects.ValueKind != JsonValueKind.Object)
            {
                return usage;
            }

            foreach (var project in projects.EnumerateObject())
            {
                if (project.Value.ValueKind != JsonValueKind.Object) continue;
                if (!project.Value.TryGetProperty("lastModelUsage", out var models) ||
                    models.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var counted = false;

                foreach (var model in models.EnumerateObject())
                {
                    if (model.Value.ValueKind != JsonValueKind.Object) continue;

                    usage.InputTokens += Long(model.Value, "inputTokens");
                    usage.OutputTokens += Long(model.Value, "outputTokens");
                    usage.CacheReadTokens += Long(model.Value, "cacheReadInputTokens");
                    usage.WebSearches += (int)Long(model.Value, "webSearchRequests");

                    var cost = Double(model.Value, "costUSD");
                    if (cost > 0)
                    {
                        usage.CostUsd += cost;
                        usage.HasCost = true;
                    }

                    counted = true;
                }

                if (counted) usage.Projects++;
            }
        }
        catch
        {
            // A config we cannot read contributes nothing rather than failing.
        }

        return usage;
    }

    private static List<(string Path, double Cost)> ReadProjectCosts(string configDir)
    {
        var results = new List<(string, double)>();

        try
        {
            var path = Path.Combine(configDir, ".claude.json");
            if (!File.Exists(path)) return results;

            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);

            if (!document.RootElement.TryGetProperty("projects", out var projects) ||
                projects.ValueKind != JsonValueKind.Object)
            {
                return results;
            }

            foreach (var project in projects.EnumerateObject())
            {
                if (project.Value.ValueKind != JsonValueKind.Object) continue;
                if (!project.Value.TryGetProperty("lastModelUsage", out var models) ||
                    models.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var cost = 0.0;
                foreach (var model in models.EnumerateObject())
                {
                    if (model.Value.ValueKind == JsonValueKind.Object) cost += Double(model.Value, "costUSD");
                }

                if (cost > 0) results.Add((project.Name, cost));
            }
        }
        catch
        {
        }

        return results;
    }

    // ---- prompts, which carry a time and a project --------------------------

    private readonly record struct Prompt(string Project, string SessionId, DateTime WhenLocal);

    private static List<Prompt> ReadHistory(string configDir, DateTime sinceUtc)
    {
        var results = new List<Prompt>();
        var path = ClaudePaths.HistoryFile(configDir);

        try
        {
            if (!File.Exists(path)) return results;

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (line.Length < 10) continue;

                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;

                    // Claude writes this as a string of epoch milliseconds.
                    if (!root.TryGetProperty("timestamp", out var stamp)) continue;

                    var raw = stamp.ValueKind == JsonValueKind.String ? stamp.GetString() : stamp.ToString();
                    if (!long.TryParse(raw, out var millis)) continue;

                    var when = DateTimeOffset.FromUnixTimeMilliseconds(millis).UtcDateTime;
                    if (when < sinceUtc) continue;

                    results.Add(new Prompt(
                        root.TryGetProperty("project", out var project) ? project.GetString() ?? string.Empty : string.Empty,
                        root.TryGetProperty("sessionId", out var id) ? id.GetString() ?? string.Empty : string.Empty,
                        when.ToLocalTime()));
                }
                catch (JsonException)
                {
                }
            }
        }
        catch
        {
        }

        return results;
    }

    // ---- what the work itself looked like -----------------------------------

    /// <summary>
    /// Counts edits, commands, files and pull requests from transcript lines
    /// that fall inside the period.
    ///
    /// Substring work, not JSON parsing: these files reach 36 MB and the counts
    /// are of exact literals Claude writes for every tool call. Only assistant
    /// lines are counted, because that is where a tool call is made - a result
    /// line echoing the same name would double it.
    /// </summary>
    private static long Scan(string configDir, DateTime sinceUtc, DashboardData data,
        HashSet<string> files, HashSet<string> prs)
    {
        var scanned = 0L;
        var stamp = sinceUtc.ToString("yyyy-MM-dd");

        string[] transcripts;
        try
        {
            transcripts = Directory.GetFiles(ClaudePaths.ProjectsDir(configDir), "*.jsonl",
                SearchOption.AllDirectories);
        }
        catch
        {
            return 0;
        }

        foreach (var path in transcripts)
        {
            try
            {
                var info = new FileInfo(path);

                // A file untouched since the period began holds nothing in it.
                if (info.LastWriteTimeUtc < sinceUtc) continue;
                if (scanned >= ScanCeiling) return scanned;

                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, 128 * 1024, FileOptions.SequentialScan);
                using var reader = new StreamReader(stream);

                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    scanned += line.Length;
                    if (line.Length < 40) continue;

                    if (line.Contains("\"type\":\"pr-link\"", StringComparison.Ordinal))
                    {
                        // These carry a timestamp of their own, and without
                        // checking it every pull request the file ever mentioned
                        // counted as today's - 57 of them on the first run.
                        if (!InPeriod(line, stamp, sinceUtc)) continue;

                        var url = Value(line, "\"prUrl\":\"");
                        if (url.Length > 0) prs.Add(url);
                        continue;
                    }

                    if (!line.Contains("\"type\":\"assistant\"", StringComparison.Ordinal)) continue;
                    if (!InPeriod(line, stamp, sinceUtc)) continue;

                    data.Totals.Edits += Count(line, "\"name\":\"Edit\"") + Count(line, "\"name\":\"Write\"");
                    data.Totals.Commands += Count(line, "\"name\":\"Bash\"");

                    // Distinct paths, so ten edits to one file are one file.
                    var from = 0;
                    while (true)
                    {
                        var at = line.IndexOf("\"file_path\":\"", from, StringComparison.Ordinal);
                        if (at < 0) break;

                        var value = Value(line[at..], "\"file_path\":\"");
                        if (value.Length > 0) files.Add(value);
                        from = at + 13;
                    }
                }
            }
            catch
            {
                // Skip a transcript we cannot read rather than abandoning the count.
            }
        }

        return scanned;
    }

    /// <summary>
    /// True when a line's own timestamp is inside the period. The cheap check
    /// comes first: for "today" the date alone answers it.
    /// </summary>
    private static bool InPeriod(string line, string stamp, DateTime sinceUtc)
    {
        var at = line.IndexOf("\"timestamp\":\"", StringComparison.Ordinal);
        if (at < 0) return false;

        var value = line.AsSpan(at + 13);
        if (value.Length < 10) return false;

        var date = value[..10];
        if (date.SequenceEqual(stamp)) return true;

        return DateTime.TryParse(value[..Math.Min(24, value.Length)], out var when) &&
               when.ToUniversalTime() >= sinceUtc;
    }

    private static int Count(string line, string needle)
    {
        var count = 0;
        var from = 0;

        while (true)
        {
            var at = line.IndexOf(needle, from, StringComparison.Ordinal);
            if (at < 0) return count;

            count++;
            from = at + needle.Length;
        }
    }

    private static string Value(string line, string key)
    {
        var at = line.IndexOf(key, StringComparison.Ordinal);
        if (at < 0) return string.Empty;

        var start = at + key.Length;
        var end = line.IndexOf('"', start);
        return end <= start ? string.Empty : line[start..end];
    }

    private static long Long(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out var number)
            ? number
            : 0;

    private static double Double(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out var number)
            ? number
            : 0;

    private static string Leaf(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var name = Path.GetFileName(trimmed);
        return name.Length == 0 ? trimmed : name;
    }
}
