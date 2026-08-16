using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ClaudeLauncher.Sessions;

public enum ChatState
{
    Starting,
    Idle,
    Working,
    AwaitingPermission,
    Ended
}

public enum ChatLineKind
{
    UserPrompt,
    AssistantText,
    ToolCall,
    ToolResult,
    Thinking,
    Notice,
    Error
}

public sealed class ChatLine
{
    public ChatLineKind Kind { get; init; }
    public string Text { get; init; } = string.Empty;
    public string? Detail { get; init; }
}

/// <summary>A slash command the session offers, as reported at startup.</summary>
public sealed class SlashCommand
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ArgumentHint { get; init; } = string.Empty;
}

/// <summary>A tool that has started and not yet reported back.</summary>
public sealed class RunningTool
{
    public string Description { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public DateTime StartedUtc { get; init; } = DateTime.UtcNow;

    public TimeSpan Elapsed => DateTime.UtcNow - StartedUtc;
}

/// <summary>A tool waiting on the user's answer.</summary>
public sealed class PermissionAsk
{
    public string RequestId { get; init; } = string.Empty;
    public string Tool { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    /// <summary>The tool's input, echoed back verbatim when allowing.</summary>
    public string InputJson { get; init; } = "{}";
}

/// <summary>
/// A Claude session the launcher owns and can type into.
///
/// Claude runs with --input-format/--output-format stream-json, so the launcher
/// exchanges structured messages instead of emulating a terminal. The JSON is
/// only the envelope - Claude reads and writes ordinary text, uses the same
/// tools, hooks and settings, and writes the same transcript to disk, so the
/// terminal wall and Home keep seeing these sessions like any other.
///
/// --permission-prompt-tool stdio routes permission requests to us over the
/// control protocol, which is what makes an approve/deny prompt possible.
/// </summary>
public sealed class StreamSession : IDisposable
{
    private readonly object _gate = new();
    private readonly List<ChatLine> _lines = new();
    private readonly HashSet<string> _alwaysAllow = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _outbox = new();
    private readonly StringBuilder _streaming = new();

    private Process? _process;
    private Thread? _reader;
    private int _revision;

    public string ProjectPath { get; }
    public string ProjectName { get; }
    public ProfileEntry Profile { get; }

    public StreamSession(ProfileEntry profile, string projectPath)
    {
        Profile = profile;
        ProjectPath = projectPath;
        var name = Path.GetFileName(projectPath.TrimEnd('\\', '/'));
        ProjectName = string.IsNullOrEmpty(name) ? projectPath : name;
    }

    public ChatState State { get; private set; } = ChatState.Starting;

    public string? SessionId { get; private set; }

    public string? Model { get; private set; }

    public PermissionAsk? Pending { get; private set; }

    /// <summary>Non-null while a tool is running, so the screen can show what and for how long.</summary>
    public RunningTool? ActiveTool { get; private set; }

    /// <summary>Slash commands this session accepts, reported during startup.</summary>
    public IReadOnlyList<SlashCommand> Commands { get; private set; } = Array.Empty<SlashCommand>();

    /// <summary>Bumped whenever anything changes, so the screen can redraw only when needed.</summary>
    public int Revision => Volatile.Read(ref _revision);

    /// <summary>Committed lines plus whatever is still streaming in.</summary>
    public IReadOnlyList<ChatLine> Snapshot()
    {
        lock (_gate)
        {
            if (_streaming.Length == 0) return _lines.ToArray();

            var copy = new List<ChatLine>(_lines.Count + 1);
            copy.AddRange(_lines);
            copy.Add(new ChatLine { Kind = ChatLineKind.AssistantText, Text = _streaming.ToString() });
            return copy;
        }
    }

    public void Start(string? resumeSessionId = null)
    {
        var info = new ProcessStartInfo(ClaudeExecutable())
        {
            WorkingDirectory = ProjectPath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardInputEncoding = new UTF8Encoding(false)
        };

        foreach (var arg in new[]
                 {
                     "-p", "--verbose",
                     "--input-format", "stream-json",
                     "--output-format", "stream-json",
                     "--include-partial-messages",
                     // Permission requests come to us instead of being auto-denied.
                     "--permission-mode", "manual",
                     "--permission-prompt-tool", "stdio"
                 })
        {
            info.ArgumentList.Add(arg);
        }

        if (!string.IsNullOrWhiteSpace(resumeSessionId))
        {
            info.ArgumentList.Add("--resume");
            info.ArgumentList.Add(resumeSessionId!);
        }

        info.EnvironmentVariables["CLAUDE_CONFIG_DIR"] = StateStore.ExpandHome(Profile.ConfigDir);

        try
        {
            _process = Process.Start(info);
        }
        catch (Exception ex)
        {
            Add(ChatLineKind.Error, "Could not start Claude: " + ex.Message);
            State = ChatState.Ended;
            return;
        }

        if (_process is null)
        {
            Add(ChatLineKind.Error, "Could not start Claude.");
            State = ChatState.Ended;
            return;
        }

        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "claude-stream" };
        _reader.Start();

        // Announcing ourselves is what makes the CLI talk the control protocol.
        SendRaw("{\"type\":\"control_request\",\"request_id\":\"init\",\"request\":{\"subtype\":\"initialize\"}}");

        // Ready as soon as the process is up. Claude only emits its init event
        // after the first message arrives, so waiting for that would leave the
        // screen saying "starting" with no way to type the message that ends it.
        lock (_gate) State = ChatState.Idle;
        Add(ChatLineKind.Notice, $"Claude is ready in {ProjectName}. Type a message.");
    }

    public void Send(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (State == ChatState.Ended) return;

        Add(ChatLineKind.UserPrompt, text);

        var message = new
        {
            type = "user",
            message = new { role = "user", content = new[] { new { type = "text", text } } }
        };

        SendRaw(JsonSerializer.Serialize(message));

        lock (_gate) State = ChatState.Working;
        Touch();
    }

    /// <summary>Answers the pending permission request.</summary>
    public void Answer(bool allow, bool always = false)
    {
        PermissionAsk? ask;
        lock (_gate) ask = Pending;
        if (ask is null) return;

        if (allow && always)
        {
            lock (_gate) _alwaysAllow.Add(ask.Tool);
            Add(ChatLineKind.Notice, $"Allowing {ask.Tool} for the rest of this session.");
        }

        Respond(ask, allow, allow ? null : "The user declined this in Claude Launcher.");

        lock (_gate)
        {
            Pending = null;
            State = ChatState.Working;
        }

        Touch();
    }

    /// <summary>Stops the current turn. Claude reports it as interrupted and waits for the next prompt.</summary>
    public void Interrupt()
    {
        if (State is ChatState.Idle or ChatState.Ended) return;

        SendRaw("{\"type\":\"control_request\",\"request_id\":\"interrupt\",\"request\":{\"subtype\":\"interrupt\"}}");
        Add(ChatLineKind.Notice, "Interrupting…");
    }

    private void Respond(PermissionAsk ask, bool allow, string? message)
    {
        // updatedInput must echo the tool's own input back, unchanged.
        var response = allow
            ? $"{{\"type\":\"control_response\",\"response\":{{\"subtype\":\"success\",\"request_id\":{JsonSerializer.Serialize(ask.RequestId)},\"response\":{{\"behavior\":\"allow\",\"updatedInput\":{ask.InputJson}}}}}}}"
            : $"{{\"type\":\"control_response\",\"response\":{{\"subtype\":\"success\",\"request_id\":{JsonSerializer.Serialize(ask.RequestId)},\"response\":{{\"behavior\":\"deny\",\"message\":{JsonSerializer.Serialize(message ?? "Denied.")}}}}}}}";

        SendRaw(response);
    }

    private void SendRaw(string json)
    {
        var process = _process;
        if (process is null || process.HasExited) return;

        try
        {
            process.StandardInput.WriteLine(json);
            process.StandardInput.Flush();
        }
        catch (IOException)
        {
            // The child went away between the check and the write.
        }
    }

    private void ReadLoop()
    {
        var process = _process!;

        try
        {
            string? line;
            while ((line = process.StandardOutput.ReadLine()) is not null)
            {
                if (line.Length < 2) continue;
                try { Handle(line); }
                catch (JsonException) { /* one bad line never stops the session */ }
            }
        }
        catch (Exception)
        {
            // Pipe closed underneath us; treated as the session ending.
        }

        lock (_gate)
        {
            State = ChatState.Ended;
            _lines.Add(new ChatLine { Kind = ChatLineKind.Notice, Text = "Claude exited." });
        }

        Touch();
    }

    private void Handle(string line)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        if (!root.TryGetProperty("type", out var typeElement)) return;

        switch (typeElement.GetString())
        {
            case "control_request":
                HandleControlRequest(root);
                break;

            case "control_response":
                HandleControlResponse(root);
                break;

            case "stream_event":
                HandleDelta(root);
                break;

            case "assistant":
                HandleAssistant(root);
                break;

            case "user":
                // Tool results come back as user messages; the tool line already
                // says what ran, so only failures are worth a line of their own.
                HandleToolResult(root);
                break;

            case "system":
                HandleSystem(root);
                break;

            case "result":
                lock (_gate)
                {
                    Commit();
                    ActiveTool = null;
                    State = Pending is null ? ChatState.Idle : ChatState.AwaitingPermission;
                }

                Touch();
                break;
        }
    }

    private void HandleControlRequest(JsonElement root)
    {
        if (!root.TryGetProperty("request", out var request)) return;
        if (!request.TryGetProperty("subtype", out var subtype)) return;
        if (subtype.GetString() != "can_use_tool") return;

        var requestId = root.TryGetProperty("request_id", out var id) ? id.GetString() ?? string.Empty : string.Empty;
        var tool = request.TryGetProperty("tool_name", out var name) ? name.GetString() ?? "tool" : "tool";
        var description = request.TryGetProperty("description", out var desc) ? desc.GetString() ?? string.Empty : string.Empty;
        var inputJson = request.TryGetProperty("input", out var input) ? input.GetRawText() : "{}";

        var ask = new PermissionAsk
        {
            RequestId = requestId,
            Tool = tool,
            Description = description,
            InputJson = inputJson
        };

        bool auto;
        lock (_gate) auto = _alwaysAllow.Contains(tool);

        if (auto)
        {
            Respond(ask, allow: true, message: null);
            return;
        }

        lock (_gate)
        {
            Commit();
            Pending = ask;
            State = ChatState.AwaitingPermission;
        }

        Touch();
    }

    /// <summary>The reply to our initialize carries the session's slash commands.</summary>
    private void HandleControlResponse(JsonElement root)
    {
        if (!root.TryGetProperty("response", out var outer)) return;
        if (!outer.TryGetProperty("response", out var inner)) return;
        if (!inner.TryGetProperty("commands", out var commands)) return;
        if (commands.ValueKind != JsonValueKind.Array) return;

        var parsed = new List<SlashCommand>();

        foreach (var command in commands.EnumerateArray())
        {
            var name = command.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (string.IsNullOrWhiteSpace(name)) continue;

            var description = command.TryGetProperty("description", out var d) ? d.GetString() ?? string.Empty : string.Empty;
            var hint = command.TryGetProperty("argumentHint", out var a) ? a.GetString() ?? string.Empty : string.Empty;

            // Descriptions run to paragraphs; the menu has one line per command.
            var cut = description.IndexOf(". ", StringComparison.Ordinal);
            if (cut > 0) description = description.Substring(0, cut);

            parsed.Add(new SlashCommand
            {
                Name = name!,
                Description = Clean(description) ?? string.Empty,
                ArgumentHint = hint
            });
        }

        Commands = parsed.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        Touch();
    }

    private void HandleDelta(JsonElement root)
    {
        if (!root.TryGetProperty("event", out var ev)) return;
        if (!ev.TryGetProperty("type", out var type)) return;
        if (type.GetString() != "content_block_delta") return;
        if (!ev.TryGetProperty("delta", out var delta)) return;
        if (!delta.TryGetProperty("text", out var text)) return;

        var value = text.GetString();
        if (string.IsNullOrEmpty(value)) return;

        lock (_gate) _streaming.Append(value);
        Touch();
    }

    private void HandleAssistant(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var message)) return;
        if (!message.TryGetProperty("content", out var content)) return;
        if (content.ValueKind != JsonValueKind.Array) return;

        if (message.TryGetProperty("model", out var model))
        {
            var value = model.GetString();
            if (!string.IsNullOrWhiteSpace(value)) Model = value;
        }

        lock (_gate)
        {
            // The final message supersedes whatever streamed in for it.
            _streaming.Clear();

            foreach (var block in content.EnumerateArray())
            {
                if (!block.TryGetProperty("type", out var kind)) continue;

                switch (kind.GetString())
                {
                    case "text":
                        if (block.TryGetProperty("text", out var text))
                        {
                            var value = Clean(text.GetString());
                            if (value is not null) _lines.Add(new ChatLine { Kind = ChatLineKind.AssistantText, Text = value });
                        }

                        break;

                    case "thinking":
                        _lines.Add(new ChatLine { Kind = ChatLineKind.Thinking, Text = "thinking" });
                        break;

                    case "tool_use":
                        var tool = block.TryGetProperty("name", out var n) ? n.GetString() ?? "tool" : "tool";
                        _lines.Add(new ChatLine
                        {
                            Kind = ChatLineKind.ToolCall,
                            Text = tool,
                            Detail = ToolTarget(block)
                        });
                        break;
                }
            }
        }

        Touch();
    }

    private void HandleToolResult(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var message)) return;
        if (!message.TryGetProperty("content", out var content)) return;
        if (content.ValueKind != JsonValueKind.Array) return;

        foreach (var block in content.EnumerateArray())
        {
            if (!block.TryGetProperty("type", out var kind)) continue;
            if (kind.GetString() != "tool_result") continue;
            if (!block.TryGetProperty("is_error", out var isError)) continue;
            if (isError.ValueKind != JsonValueKind.True) continue;

            Add(ChatLineKind.Error, "That tool call failed.");
        }
    }

    private void HandleSystem(JsonElement root)
    {
        if (!root.TryGetProperty("subtype", out var subtype)) return;

        switch (subtype.GetString())
        {
            case "init":
                if (root.TryGetProperty("session_id", out var id)) SessionId = id.GetString();
                Touch();
                break;

            case "permission_denied":
                Add(ChatLineKind.Notice, "Claude was denied that tool.");
                break;

            // Tool output only arrives when the tool finishes, but these two say
            // what started and when it ended - enough to show a live "running".
            case "task_started":
                var description = root.TryGetProperty("description", out var desc) ? desc.GetString() : null;
                var kind = root.TryGetProperty("task_type", out var type) ? type.GetString() : null;

                lock (_gate)
                {
                    ActiveTool = new RunningTool
                    {
                        Description = Clean(description) ?? "working",
                        Kind = kind ?? string.Empty
                    };
                }

                Touch();
                break;

            case "task_notification":
                lock (_gate) ActiveTool = null;
                Touch();
                break;
        }
    }

    private static string? ToolTarget(JsonElement block)
    {
        if (!block.TryGetProperty("input", out var input) || input.ValueKind != JsonValueKind.Object) return null;

        foreach (var name in new[] { "file_path", "path", "command", "pattern", "url", "query" })
        {
            if (!input.TryGetProperty(name, out var value)) continue;
            if (value.ValueKind != JsonValueKind.String) continue;

            var text = Clean(value.GetString());
            if (text is null) continue;

            if (name is not ("file_path" or "path")) return text;

            var parts = text.Split('\\', '/');
            return parts.Length <= 2 ? text : string.Join('/', parts[^2..]);
        }

        return null;
    }

    /// <summary>Collapses whitespace; the screen wraps, so embedded newlines only confuse it.</summary>
    private static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var builder = new StringBuilder(text!.Length);
        var space = false;

        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch) || char.IsControl(ch)) { space = true; continue; }
            if (space && builder.Length > 0) builder.Append(' ');
            space = false;
            builder.Append(ch);
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    /// <summary>Moves whatever streamed in into a committed line.</summary>
    private void Commit()
    {
        if (_streaming.Length == 0) return;

        var text = Clean(_streaming.ToString());
        _streaming.Clear();
        if (text is not null) _lines.Add(new ChatLine { Kind = ChatLineKind.AssistantText, Text = text });
    }

    private void Add(ChatLineKind kind, string text)
    {
        lock (_gate) _lines.Add(new ChatLine { Kind = kind, Text = text });
        Touch();
    }

    private void Touch() => Interlocked.Increment(ref _revision);

    private static string ClaudeExecutable()
    {
        // Same resolution the wrapper relies on: claude on PATH.
        return "claude";
    }

    public void Dispose()
    {
        try
        {
            var process = _process;
            if (process is not null && !process.HasExited) process.Kill(entireProcessTree: true);
            process?.Dispose();
        }
        catch
        {
            // Nothing useful to do while tearing down.
        }
    }
}
