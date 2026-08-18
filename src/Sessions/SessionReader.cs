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

        public string Branch { get; set; } = string.Empty;

        /// <summary>Oldest to newest. Only filled when the caller asks for them.</summary>
        public List<TranscriptEntry> Entries { get; } = new();
    }

    /// <summary>How many decoded entries a tile keeps. A tall tile shows ~20.</summary>
    private const int MaxEntries = 60;

    /// <summary>
    /// Reads the last 64 KB of a transcript. Measured on a 3.1 MB file that
    /// Claude had open: 13 ms, with the title 538 bytes from the end.
    /// </summary>
    public static TranscriptFacts ReadTranscriptTail(string path, bool withEntries = false)
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

            for (var i = first; i < last; i++) Fold(lines[i], facts, withEntries);

            // Keep only the newest, since a tile draws from the end.
            if (facts.Entries.Count > MaxEntries)
                facts.Entries.RemoveRange(0, facts.Entries.Count - MaxEntries);
        }
        catch
        {
            // Missing, locked or unreadable: the caller renders what it has.
        }

        return facts;
    }

    private static void Fold(string line, TranscriptFacts facts, bool withEntries)
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

            if (root.TryGetProperty("gitBranch", out var branch) &&
                branch.ValueKind == JsonValueKind.String)
            {
                var value = branch.GetString();
                if (!string.IsNullOrWhiteSpace(value)) facts.Branch = value!;
            }

            if (type == "user")
            {
                if (withEntries) FoldUser(root, facts);
                return;
            }

            if (type != "assistant") return;
            if (withEntries) FoldAssistant(root, facts);

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

    /// <summary>
    /// A user line is either a typed prompt or a tool result being fed back.
    /// Only the prompt is worth a tile row; tool results and pasted images are
    /// noise at this size.
    /// </summary>
    private static void FoldUser(JsonElement root, TranscriptFacts facts)
    {
        if (!root.TryGetProperty("message", out var message)) return;
        if (!message.TryGetProperty("content", out var content)) return;

        if (content.ValueKind == JsonValueKind.String)
        {
            Add(facts, EntryKind.UserPrompt, content.GetString());
            return;
        }

        if (content.ValueKind != JsonValueKind.Array) return;

        foreach (var block in content.EnumerateArray())
        {
            if (!block.TryGetProperty("type", out var kind)) continue;
            if (kind.GetString() != "text") continue;
            if (block.TryGetProperty("text", out var text)) Add(facts, EntryKind.UserPrompt, text.GetString());
        }
    }

    private static void FoldAssistant(JsonElement root, TranscriptFacts facts)
    {
        if (!root.TryGetProperty("message", out var message)) return;
        if (!message.TryGetProperty("content", out var content)) return;
        if (content.ValueKind != JsonValueKind.Array) return;

        foreach (var block in content.EnumerateArray())
        {
            if (!block.TryGetProperty("type", out var kindElement)) continue;

            switch (kindElement.GetString())
            {
                case "text":
                    if (block.TryGetProperty("text", out var text))
                        Add(facts, EntryKind.AssistantText, text.GetString());
                    break;

                case "thinking":
                    Add(facts, EntryKind.Thinking, "thinking");
                    break;

                case "tool_use":
                    var name = block.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (string.IsNullOrEmpty(name)) break;
                    facts.Entries.Add(new TranscriptEntry
                    {
                        Kind = EntryKind.ToolCall,
                        Text = name!,
                        Target = ToolTarget(block)
                    });
                    break;
            }
        }
    }

    /// <summary>The one thing worth showing about a tool call: which file, or which command.</summary>
    private static string? ToolTarget(JsonElement block)
    {
        if (!block.TryGetProperty("input", out var input) || input.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var name in new[] { "file_path", "path", "command", "pattern", "url", "query" })
        {
            if (!input.TryGetProperty(name, out var value)) continue;
            if (value.ValueKind != JsonValueKind.String) continue;

            var text = Flatten(value.GetString());
            if (string.IsNullOrEmpty(text)) continue;

            // Paths are more legible from the tail; commands from the head.
            return name is "file_path" or "path" ? Shorten(text!) : text;
        }

        return null;
    }

    /// <summary>Keeps the last two path segments: "src/agent/runner.ts" stays readable in a narrow tile.</summary>
    private static string Shorten(string path)
    {
        var parts = path.Split('\\', '/');
        return parts.Length <= 2 ? path : string.Join('/', parts[^2..]);
    }

    private static void Add(TranscriptFacts facts, EntryKind kind, string? text)
    {
        var value = Prose(text);
        if (string.IsNullOrEmpty(value)) return;
        facts.Entries.Add(new TranscriptEntry { Kind = kind, Text = value! });
    }

    /// <summary>
    /// Collapses newlines and runs of whitespace: a tile draws single lines.
    /// Nothing else is removed - this also runs over file paths and commands,
    /// where stripping characters would corrupt what it is showing.
    /// </summary>
    private static string? Flatten(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var builder = new System.Text.StringBuilder(text!.Length);
        var space = false;

        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch) || char.IsControl(ch))
            {
                space = true;
                continue;
            }

            if (space && builder.Length > 0) builder.Append(' ');
            space = false;
            builder.Append(ch);
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    /// <summary>
    /// Prose only: drops the markdown Claude writes for a terminal that renders
    /// it. Deliberately narrow - bold markers, code fences and backticks, which
    /// are unambiguous decoration. Underscores and hashes are left alone because
    /// they carry meaning inside identifiers like grp_case_id.
    /// </summary>
    private static string? Prose(string? text)
    {
        var flat = Flatten(text);
        if (flat is null) return null;

        flat = flat.Replace("```", " ").Replace("**", string.Empty).Replace("`", string.Empty);

        // Heading marks only count at the start of what is now one line.
        flat = flat.TrimStart('#', ' ');

        flat = Flatten(flat);
        return string.IsNullOrWhiteSpace(flat) ? null : flat;
    }

    private static long Long(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.TryGetInt64(out var result) ? result : 0;

    /// <summary>
    /// Past sessions of one project, newest first. Only file metadata is read
    /// here: a project can hold dozens of transcripts and most are never drawn.
    /// </summary>
    public static List<PastSession> ListProjectSessions(string configDir, string projectPath)
    {
        var results = new List<PastSession>();
        var dir = Path.Combine(ClaudePaths.ProjectsDir(configDir), ClaudePaths.EncodeProjectDir(projectPath));

        string[] files;
        try { files = Directory.GetFiles(dir, "*.jsonl", SearchOption.TopDirectoryOnly); }
        catch { return results; }

        foreach (var file in files)
        {
            try
            {
                var info = new FileInfo(file);
                results.Add(new PastSession
                {
                    SessionId = Path.GetFileNameWithoutExtension(file),
                    Path = file,
                    LastActivityUtc = info.LastWriteTimeUtc,
                    SizeBytes = info.Length
                });
            }
            catch (IOException)
            {
            }
        }

        return results.OrderByDescending(s => s.LastActivityUtc).ToList();
    }

    /// <summary>Fills the fields the picker draws. Tail for the title, head for the opening prompt.</summary>
    public static void Load(PastSession session)
    {
        if (session.Loaded) return;
        session.Loaded = true;

        var facts = ReadTranscriptTail(session.Path);
        session.Title = facts.Title;
        session.Model = facts.Model;
        session.Branch = facts.Branch;
        session.ContextTokens = facts.ContextTokens;
        session.FirstPrompt = ReadFirstPrompt(session.Path);
    }

    /// <summary>
    /// The prompt that started a session, from the first 32 KB. Bounded on
    /// purpose: a first message carrying a pasted image can run to megabytes,
    /// and no prompt is worth reading that far for.
    /// </summary>
    public static string? ReadFirstPrompt(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 32 * 1024, FileOptions.SequentialScan);
            using var reader = new StreamReader(stream);

            var budget = 32 * 1024;
            string? line;

            while (budget > 0 && (line = reader.ReadLine()) is not null)
            {
                budget -= line.Length;
                if (line.Length < 2) continue;

                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;

                    if (!root.TryGetProperty("type", out var type)) continue;

                    if (type.GetString() == "last-prompt" &&
                        root.TryGetProperty("lastPrompt", out var last))
                    {
                        return Prose(last.GetString());
                    }

                    if (type.GetString() != "user") continue;
                    if (root.TryGetProperty("isSidechain", out var side) &&
                        side.ValueKind == JsonValueKind.True) continue;

                    var facts = new TranscriptFacts();
                    FoldUser(root, facts);
                    if (facts.Entries.Count > 0) return facts.Entries[0].Text;
                }
                catch (JsonException)
                {
                }
            }
        }
        catch
        {
        }

        return null;
    }

    /// <summary>
    /// Every mention of a query in one session's transcript, oldest first.
    ///
    /// This is the only way to search what has scrolled out of a terminal:
    /// Claude lives on the alternate screen and keeps its history to itself, but
    /// it writes every turn here as it goes.
    ///
    /// A transcript runs to tens of megabytes, so a line that cannot contain the
    /// query is rejected on the raw text before any JSON work. That shortcut is
    /// only sound while the query has nothing JSON would escape - a quote or a
    /// backslash is written differently on disk than it was typed, so those fall
    /// back to parsing every turn.
    /// </summary>
    public static List<TranscriptHit> SearchTranscript(string path, string query, int limit = 300)
    {
        var hits = new List<TranscriptHit>();
        if (string.IsNullOrWhiteSpace(query) || !File.Exists(path)) return hits;

        var escapes = query.Any(ch => ch is '"' or '\\' or '/' || char.IsControl(ch));

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 128 * 1024, FileOptions.SequentialScan);
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = reader.ReadLine()) is not null && hits.Count < limit)
            {
                if (line.Length < 2) continue;
                if (!escapes && !line.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;

                var isUser = line.Contains("\"type\":\"user\"", StringComparison.Ordinal);
                var isAssistant = line.Contains("\"type\":\"assistant\"", StringComparison.Ordinal);
                if (!isUser && !isAssistant) continue;

                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;

                    var when = root.TryGetProperty("timestamp", out var ts) &&
                               ts.ValueKind == JsonValueKind.String &&
                               DateTime.TryParse(ts.GetString(), out var parsed)
                        ? parsed.ToUniversalTime()
                        : (DateTime?)null;

                    var facts = new TranscriptFacts();

                    if (isUser)
                    {
                        // A sidechain is a subagent's own conversation, not this one.
                        if (root.TryGetProperty("isSidechain", out var side) &&
                            side.ValueKind == JsonValueKind.True) continue;

                        FoldUser(root, facts);
                    }
                    else
                    {
                        FoldAssistant(root, facts);
                    }

                    foreach (var entry in facts.Entries)
                    {
                        if (hits.Count >= limit) break;

                        var text = entry.Target is null ? entry.Text : entry.Text + "  " + entry.Target;
                        var at = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                        if (at < 0) continue;

                        hits.Add(new TranscriptHit
                        {
                            Kind = entry.Kind,
                            WhenUtc = when,
                            Text = text,
                            Column = at
                        });
                    }
                }
                catch (JsonException)
                {
                    // Claude appends while we read, so the last line is often half written.
                }
            }
        }
        catch
        {
            // Whatever was gathered before the failure is still worth showing.
        }

        return hits;
    }

    /// <summary>
    /// A full pass over one transcript, for the detail screen only. Counting
    /// turns and tool calls is the whole point, and those cannot come from a
    /// tail - but this runs for a single session the user asked to open.
    /// </summary>
    public static SessionDetail ScanSession(string path)
    {
        var detail = new SessionDetail();

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 128 * 1024, FileOptions.SequentialScan);
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (line.Length < 2) continue;

                // Cheap reject before any JSON work: the format is compact, so
                // these substrings are exact.
                var isUser = line.Contains("\"type\":\"user\"", StringComparison.Ordinal);
                var isAssistant = line.Contains("\"type\":\"assistant\"", StringComparison.Ordinal);
                if (!isUser && !isAssistant) continue;

                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;

                    var when = root.TryGetProperty("timestamp", out var ts) &&
                               ts.ValueKind == JsonValueKind.String &&
                               DateTime.TryParse(ts.GetString(), out var parsed)
                        ? parsed.ToUniversalTime()
                        : (DateTime?)null;

                    if (when is not null)
                    {
                        detail.StartedUtc ??= when;
                        detail.LastActivityUtc = when;
                    }

                    var facts = new TranscriptFacts();

                    if (isUser)
                    {
                        if (root.TryGetProperty("isSidechain", out var side) &&
                            side.ValueKind == JsonValueKind.True) continue;

                        FoldUser(root, facts);
                        if (facts.Entries.Count > 0) detail.Turns++;
                    }
                    else
                    {
                        FoldAssistant(root, facts);
                    }

                    foreach (var entry in facts.Entries)
                    {
                        if (entry.Kind == EntryKind.ToolCall)
                        {
                            detail.ToolCalls++;
                            if (entry.Target is not null && IsFileTool(entry.Text))
                                detail.Files[entry.Target] = detail.Files.GetValueOrDefault(entry.Target) + 1;
                        }

                        detail.Entries.Add(entry);
                    }
                }
                catch (JsonException)
                {
                    detail.MalformedLines++;
                }
            }
        }
        catch
        {
            // Whatever was gathered before the failure is still worth showing.
        }

        // The screen only ever draws the end of the conversation.
        const int keep = 400;
        if (detail.Entries.Count > keep) detail.Entries.RemoveRange(0, detail.Entries.Count - keep);

        return detail;
    }

    private static bool IsFileTool(string verb) =>
        verb is "Read" or "Edit" or "Write" or "NotebookEdit" or "MultiEdit";

    private static readonly Dictionary<string, (DateTime Read, long Stamp, ClaudeAccount? Account)> Accounts =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Claude rewrites .claude.json constantly; who is signed in barely changes.</summary>
    private static readonly TimeSpan AccountTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Who Claude Code is signed in as in one config dir, from the oauthAccount
    /// block of its own .claude.json.
    ///
    /// Cached behind a clock as well as a timestamp: the file is written on
    /// nearly every turn, and the wall would otherwise re-parse it for every
    /// pane on every frame to learn something that changes once a month.
    /// </summary>
    public static ClaudeAccount? ReadAccount(string configDir)
    {
        if (string.IsNullOrWhiteSpace(configDir)) return null;

        var path = Path.Combine(configDir, ".claude.json");

        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return null;

            var stamp = info.LastWriteTimeUtc.Ticks ^ info.Length;

            if (Accounts.TryGetValue(path, out var cached) &&
                (cached.Stamp == stamp || DateTime.UtcNow - cached.Read < AccountTtl))
            {
                return cached.Account;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);
            using var document = JsonDocument.Parse(stream);

            ClaudeAccount? account = null;

            if (document.RootElement.TryGetProperty("oauthAccount", out var oauth) &&
                oauth.ValueKind == JsonValueKind.Object)
            {
                account = new ClaudeAccount
                {
                    DisplayName = Text(oauth, "displayName"),
                    Email = Text(oauth, "emailAddress"),
                    Organization = Text(oauth, "organizationName")
                };

                if (account.Label.Length == 0) account = null;
            }

            Accounts[path] = (DateTime.UtcNow, stamp, account);
            return account;
        }
        catch
        {
            // A config dir we cannot read is one we say nothing about.
            Accounts[path] = (DateTime.UtcNow, 0, null);
            return null;
        }
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

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
