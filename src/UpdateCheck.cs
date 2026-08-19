using System.Text.Json;

namespace ClaudeLauncher;

/// <summary>What the releases page says the newest version is.</summary>
public sealed class UpdateInfo
{
    public string Latest { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string PublishedUtc { get; set; } = string.Empty;
    public string CheckedUtc { get; set; } = string.Empty;
}

/// <summary>
/// Asks GitHub whether there is a newer release, off the render thread.
///
/// Three rules, because a launcher that phones home badly is worse than one
/// that does not: it never blocks startup - the answer arrives when it arrives
/// and the screen is the same until then; it asks at most once every six hours,
/// keeping the answer in a small file so a hundred launches are one request;
/// and it never fails loudly - no network, a rate limit, a proxy in the way, and
/// the launcher is exactly as it was before.
/// </summary>
public static class UpdateCheck
{
    private const string DefaultRepo = "chuongnd2612/claude-launcher";

    /// <summary>Long enough that a day of launches is a handful of requests.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(6);

    /// <summary>The newer release, once one is known. Null until then, and when up to date.</summary>
    public static UpdateInfo? Available { get; private set; }

    /// <summary>True while a check is in flight, so a screen can say so.</summary>
    public static bool Checking { get; private set; }

    /// <summary>
    /// Set once an answer has been seen this run, so a screen can say "up to
    /// date" rather than leaving someone wondering whether it looked at all.
    /// </summary>
    public static string? Answer { get; private set; }

    public static string CacheFile => Path.Combine(StateStore.DataDir, "update.json");

    /// <summary>
    /// Drops the offer. Turning the checks off has to take the banner with it,
    /// or the launcher goes on suggesting an update nobody asked it about.
    /// </summary>
    public static void Forget()
    {
        Available = null;
        Answer = null;
    }

    /// <summary>
    /// Asks now, whatever the settings say.
    ///
    /// Someone pressing "check for updates" is not asking to be told the answer
    /// from six hours ago, and not asking whether the automatic check happens to
    /// be switched on either - they are asking the question themselves.
    /// </summary>
    public static void CheckNow(string current, Action? changed = null)
    {
        if (Checking) return;

        Checking = true;
        Answer = null;

        Task.Run(async () =>
        {
            try
            {
                var info = await Fetch();

                if (info is null)
                {
                    Answer = "could not reach github";
                    return;
                }

                WriteCache(info);

                if (IsNewer(info.Latest, current))
                {
                    Available = info;
                    Answer = null;
                }
                else
                {
                    Available = null;
                    Answer = $"up to date · {info.Latest} is the newest";
                }
            }
            catch (Exception)
            {
                Answer = "could not reach github";
            }
            finally
            {
                Checking = false;
                changed?.Invoke();
            }
        });
    }

    /// <summary>
    /// Starts a check unless one is not wanted: the setting is off, the
    /// environment says no, or the answer on disk is still fresh.
    /// </summary>
    public static void Start(UiSettings settings, string current, Action? changed = null)
    {
        if (!settings.CheckForUpdates) return;
        if (Environment.GetEnvironmentVariable("CLAUDE_LAUNCHER_NO_UPDATE_CHECK") is "1" or "true") return;

        var cached = ReadCache();
        if (cached is not null)
        {
            Offer(cached, current, changed);

            if (DateTime.TryParse(cached.CheckedUtc, out var when) &&
                DateTime.UtcNow - when.ToUniversalTime() < Interval)
            {
                return;
            }
        }

        Checking = true;
        Task.Run(async () =>
        {
            try
            {
                var info = await Fetch();
                if (info is not null)
                {
                    WriteCache(info);
                    Offer(info, current, changed);
                }
            }
            catch (Exception)
            {
                // Offline, blocked, rate limited: none of that is the user's problem.
            }
            finally
            {
                Checking = false;
                changed?.Invoke();
            }
        });
    }

    private static void Offer(UpdateInfo info, string current, Action? changed)
    {
        if (!IsNewer(info.Latest, current)) return;

        Available = info;
        changed?.Invoke();
    }

    private static async Task<UpdateInfo?> Fetch()
    {
        var repo = Environment.GetEnvironmentVariable("CLAUDE_LAUNCHER_REPO");
        if (string.IsNullOrWhiteSpace(repo)) repo = DefaultRepo;

        using var client = new HttpClient { Timeout = Timeout };

        // GitHub rejects a request with no user agent, and asking for the v3
        // media type keeps the shape of the answer stable.
        client.DefaultRequestHeaders.Add("User-Agent", "claude-launcher");
        client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");

        var json = await client.GetStringAsync($"https://api.github.com/repos/{repo}/releases/latest");

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var tag = Text(root, "tag_name");
        if (tag.Length == 0) return null;

        return new UpdateInfo
        {
            Latest = tag,
            Url = Text(root, "html_url"),
            PublishedUtc = Text(root, "published_at"),
            CheckedUtc = DateTime.UtcNow.ToString("o")
        };
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    /// <summary>
    /// Compares two "vX.Y.Z" tags. Anything that does not parse is treated as
    /// not newer: an unreadable answer must never nag someone into an update.
    /// </summary>
    public static bool IsNewer(string latest, string current)
    {
        var a = Parse(latest);
        var b = Parse(current);
        if (a is null || b is null) return false;

        // A build with no version stamped on it reads as 0.0.0. That is a local
        // build, not an old install, and telling someone to update over their
        // own working copy would be wrong.
        if (b[0] == 0 && b[1] == 0 && b[2] == 0) return false;

        for (var i = 0; i < 3; i++)
        {
            if (a[i] != b[i]) return a[i] > b[i];
        }

        return false;
    }

    private static int[]? Parse(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;

        var text = version.Trim();
        if (text.StartsWith('v') || text.StartsWith('V')) text = text[1..];

        // A pre-release or build suffix is not part of the comparison.
        var cut = text.IndexOfAny(new[] { '-', '+' });
        if (cut > 0) text = text[..cut];

        var parts = text.Split('.');
        if (parts.Length is < 2 or > 4) return null;

        var numbers = new int[3];
        for (var i = 0; i < 3; i++)
        {
            if (i >= parts.Length) break;
            if (!int.TryParse(parts[i], out numbers[i])) return null;
        }

        return numbers;
    }

    private static UpdateInfo? ReadCache()
    {
        try
        {
            if (!File.Exists(CacheFile)) return null;
            return JsonSerializer.Deserialize<UpdateInfo>(File.ReadAllText(CacheFile));
        }
        catch
        {
            return null;
        }
    }

    private static void WriteCache(UpdateInfo info)
    {
        try
        {
            Directory.CreateDirectory(StateStore.DataDir);
            File.WriteAllText(CacheFile, JsonSerializer.Serialize(info,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // A cache we cannot write just means asking again next time.
        }
    }
}
