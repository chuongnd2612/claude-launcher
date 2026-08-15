namespace ClaudeLauncher.Sessions;

/// <summary>
/// What a session is doing. There is no Ended: a session whose process is gone
/// is dropped from the list rather than shown as a state.
/// </summary>
public enum SessionState
{
    Unknown,
    Running,
    Waiting,
    Idle
}

/// <summary>One file in &lt;configDir&gt;\sessions, written by Claude Code itself.</summary>
public sealed class ClaudeSessionFile
{
    public int Pid { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string Cwd { get; set; } = string.Empty;
    public long StartedAt { get; set; }

    /// <summary>Process start as a Windows FILETIME, as a string. Defeats pid reuse.</summary>
    public string? ProcStart { get; set; }

    public string? Name { get; set; }
    public string? Kind { get; set; }

    /// <summary>"cli" for a session someone started in a terminal; "sdk-cli" for a background agent.</summary>
    public string? Entrypoint { get; set; }

    /// <summary>"busy" or "idle". Absent on some sessions, so never assume it is set.</summary>
    public string? Status { get; set; }

    public long UpdatedAt { get; set; }
    public long StatusUpdatedAt { get; set; }
}

public enum EntryKind
{
    UserPrompt,
    AssistantText,
    ToolCall,
    Thinking
}

/// <summary>
/// One decoded transcript line, ready to draw. Plain text only: no markdown,
/// no ANSI, no newlines - the screen wraps it to whatever the tile allows.
/// </summary>
public sealed class TranscriptEntry
{
    public EntryKind Kind { get; init; }

    /// <summary>Prose, the prompt, or the tool verb ("Read", "Bash").</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Tool calls only: the file or command the tool acted on.</summary>
    public string? Target { get; init; }
}

/// <summary>What the Home screen draws for one running session.</summary>
public sealed class SessionRow
{
    public string SessionId { get; set; } = string.Empty;
    public string ProfileName { get; set; } = string.Empty;
    public string ProfileIcon { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>Claude's own ai-title, else its derived session name, else the short id.</summary>
    public string Task { get; set; } = string.Empty;

    public SessionState State { get; set; } = SessionState.Unknown;
    public TimeSpan StateAge { get; set; }

    /// <summary>Context carried by the last assistant message, not a session total.</summary>
    public long ContextTokens { get; set; }
    public string? Model { get; set; }
    public int Pid { get; set; }
    public string Branch { get; set; } = string.Empty;

    /// <summary>Oldest to newest, capped by the reader. Empty on the Home screen.</summary>
    public IReadOnlyList<TranscriptEntry> Entries { get; set; } = Array.Empty<TranscriptEntry>();
}

/// <summary>A past session of one project, for the resume picker.</summary>
public sealed class PastSession
{
    public string SessionId { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public DateTime LastActivityUtc { get; set; }
    public long SizeBytes { get; set; }

    /// <summary>Filled lazily: the list only reads what it is about to draw.</summary>
    public bool Loaded { get; set; }

    public string? Title { get; set; }
    public string? FirstPrompt { get; set; }
    public string? Model { get; set; }
    public string Branch { get; set; } = string.Empty;
    public long ContextTokens { get; set; }

    public string ShortId => SessionId.Length >= 8 ? SessionId.Substring(0, 8) : SessionId;

    public string DisplayTitle =>
        !string.IsNullOrWhiteSpace(Title) ? Title!
        : !string.IsNullOrWhiteSpace(FirstPrompt) ? FirstPrompt!
        : ShortId;
}

/// <summary>Counted by a full pass over one transcript, for the detail screen.</summary>
public sealed class SessionDetail
{
    public int Turns { get; set; }
    public int ToolCalls { get; set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? LastActivityUtc { get; set; }

    /// <summary>File path to the number of times a tool touched it.</summary>
    public Dictionary<string, int> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<TranscriptEntry> Entries { get; } = new();
    public int MalformedLines { get; set; }
}

/// <summary>A row of the "Recent projects" panel, from history.jsonl.</summary>
public sealed class RecentProject
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public DateTime LastUsedUtc { get; set; }
}

/// <summary>Everything the Home screen renders, built off the render thread.</summary>
public sealed class SessionSnapshot
{
    public static readonly SessionSnapshot Empty = new();

    public IReadOnlyList<SessionRow> Sessions { get; init; } = Array.Empty<SessionRow>();
    public IReadOnlyList<RecentProject> Recent { get; init; } = Array.Empty<RecentProject>();

    /// <summary>Sessions started today, live or not - counted from the registry's startedAt.</summary>
    public int SessionsToday { get; init; }
}
