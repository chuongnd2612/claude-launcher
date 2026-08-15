using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace ClaudeLauncher.Sessions;

/// <summary>
/// Reads what Claude Code already records on disk. Everything here is best
/// effort: the launcher must never fail, and never block a running session,
/// because a file it does not own was missing or half written.
/// </summary>
public static class SessionReader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>Tail size that covers title, model and the last usage block on a multi-MB transcript.</summary>
    private const int TailBytes = 64 * 1024;

    public static List<ClaudeSessionFile> ReadRegistry(string configDir)
    {
        var results = new List<ClaudeSessionFile>();
        var dir = ClaudePaths.SessionsDir(configDir);

        string[] files;
        try { files = Directory.GetFiles(dir, "*.json"); }
        catch { return results; }

        foreach (var file in files)
        {
            try
            {
                var entry = JsonSerializer.Deserialize<ClaudeSessionFile>(File.ReadAllText(file), Options);
                if (entry is not null && entry.Pid > 0) results.Add(entry);
            }
            catch
            {
                // A session file being rewritten, or from a newer Claude. Skip it.
            }
        }

        return results;
    }

    /// <summary>
    /// True when the pid is still the same process. Comparing the start time as
    /// a FILETIME closes the pid-reuse hole; Claude records exactly that value.
    /// Never match on process name - a self-updating Claude runs as
    /// "claude.exe.old.&lt;epoch&gt;".
    /// </summary>
    public static bool IsAlive(int pid, string? procStart)
    {
        Process process;
        try { process = Process.GetProcessById(pid); }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }

        using (process)
        {
            if (string.IsNullOrEmpty(procStart)) return true;
            if (!long.TryParse(procStart, out var expected)) return true;

            try { return process.StartTime.ToFileTime() == expected; }
            catch (Win32Exception) { return true; }          // access denied: assume alive
            catch (InvalidOperationException) { return false; } // exited underneath us
        }
    }

    /// <summary>Facts pulled from the tail of a transcript. Every field is optional.</summary>
    public sealed class TranscriptFacts
    {
        public string? Title { get; set; }
        public string? Model { get; set; }

        /// <summary>
        /// Context carried by the most recent assistant message. This is a real,
        /// complete number from one usage block - unlike a "session total", which
        /// would need the whole multi-MB file and is deliberately left to the
        /// cached index in a later phase rather than shown here as a half sum.
        /// </summary>
        public long ContextTokens { get; set; }

        public DateTime? LastActivityUtc { get; set; }
    }

    /// <summary>
    /// Reads the last 64 KB of a transcript. Measured on a 3.1 MB file that
    /// Claude had open: 13 ms, with the title 538 bytes from the end.
    /// </summary>
    public static TranscriptFacts ReadTranscriptTail(string path)
    {
        var facts = new TranscriptFacts();

        try
        {
            // ReadWrite | Delete so we can never block Claude's own append.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);

            var length = stream.Length;
            var start = Math.Max(0, length - TailBytes);
            stream.Seek(start, SeekOrigin.Begin);

            using var reader = new StreamReader(stream);
            var text = reader.ReadToEnd();

            // Drop a leading partial line when we seeked, and the trailing one:
            // Claude appends while we read, so the last line is often half written.
            var lines = text.Split('\n');
            var first = start > 0 ? 1 : 0;
            var last = lines.Length - 1;

            for (var i = first; i < last; i++) Fold(lines[i], facts);
        }
        catch
        {
            // Missing, locked or unreadable: the caller renders what it has.
        }

        return facts;
    }

    private static void Fold(string line, TranscriptFacts facts)
    {
        if (line.Length < 2) return;

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            if (!root.TryGetProperty("type", out var typeElement)) return;

            var type = typeElement.GetString();

            if (type == "ai-title" && root.TryGetProperty("aiTitle", out var title))
            {
                facts.Title = title.GetString();
                return;
            }

            if (type != "assistant") return;

            if (root.TryGetProperty("timestamp", out var ts) &&
                ts.ValueKind == JsonValueKind.String &&
                DateTime.TryParse(ts.GetString(), out var parsed))
            {
                facts.LastActivityUtc = parsed.ToUniversalTime();
            }

            if (!root.TryGetProperty("message", out var message)) return;

            if (message.TryGetProperty("model", out var model)) facts.Model = model.GetString();
            if (!message.TryGetProperty("usage", out var usage)) return;

            // Last one wins: this is the context the model is currently carrying,
            // cache reads included, which is what the number means.
            facts.ContextTokens =
                Long(usage, "input_tokens") +
                Long(usage, "cache_creation_input_tokens") +
                Long(usage, "cache_read_input_tokens");
        }
        catch (JsonException)
        {
            // One malformed line never aborts the scan.
        }
    }

    private static long Long(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.TryGetInt64(out var result) ? result : 0;

    /// <summary>Recent projects, newest first, from history.jsonl.</summary>
    public static List<RecentProject> ReadRecentProjects(string configDir, int limit)
    {
        var seen = new Dictionary<string, RecentProject>(StringComparer.OrdinalIgnoreCase);
        var path = ClaudePaths.HistoryFile(configDir);

        try
        {
            if (!File.Exists(path)) return new List<RecentProject>();

            // Small file (~180 KB) but read shared: Claude appends to it live.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (line.Length < 2) continue;

                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    if (!root.TryGetProperty("project", out var project)) continue;

                    var dir = project.GetString();
                    if (string.IsNullOrWhiteSpace(dir)) continue;

                    var when = root.TryGetProperty("timestamp", out var ts) && ts.TryGetInt64(out var ms)
                        ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime
                        : DateTime.MinValue;

                    // Later lines win: the file is chronological.
                    seen[dir] = new RecentProject
                    {
                        Name = Path.GetFileName(dir.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : dir,
                        Path = dir,
                        LastUsedUtc = when
                    };
                }
                catch (JsonException)
                {
                }
            }
        }
        catch
        {
            return new List<RecentProject>();
        }

        return seen.Values.OrderByDescending(p => p.LastUsedUtc).Take(limit).ToList();
    }
}
