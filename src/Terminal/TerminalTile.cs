using System.Text;

namespace ClaudeLauncher.Terminal;

/// <summary>
/// One Claude session running under a pseudo console: the child, the parser and
/// the grid, wired together and guarded for the render thread.
///
/// Where <see cref="Sessions.StreamSession"/> interprets a conversation and
/// styles it in the launcher's own language, this displays whatever Claude
/// paints. That is the whole trade - it gets /usage, the model picker and plan
/// mode exactly right, and gives up per-element styling to do it.
/// </summary>
public sealed class TerminalTile : IDisposable
{
    private readonly ConPtySession _pty;
    private readonly VtParser _parser = new();
    private readonly TerminalScreen _screen;
    private readonly object _gate = new();

    private int _cols;
    private int _rows;
    private bool _disposed;

    private TerminalTile(ConPtySession pty, TerminalScreen screen, int cols, int rows)
    {
        _pty = pty;
        _screen = screen;
        _cols = cols;
        _rows = rows;

        _pty.Output += OnOutput;
    }

    public string ProjectPath { get; private init; } = string.Empty;

    public string ProjectName { get; private init; } = string.Empty;

    public bool HasExited => _pty.HasExited;

    public int ProcessId => _pty.ProcessId;

    public string? Title
    {
        get { lock (_gate) return _screen.Title; }
    }

    public long Revision
    {
        get { lock (_gate) return _screen.Revision; }
    }

    /// <summary>
    /// The session id, known before the child starts. Generating it here rather
    /// than discovering it later is what lets a terminal tile appear on Home and
    /// resolve its own transcript from the first frame.
    /// </summary>
    public string SessionId { get; private init; } = string.Empty;

    public static TerminalTile Start(string projectPath, string projectName, string configDir,
        int cols, int rows, string? resumeSessionId = null)
    {
        cols = Math.Max(20, cols);
        rows = Math.Max(4, rows);

        var resuming = !string.IsNullOrWhiteSpace(resumeSessionId);
        var sessionId = resuming ? resumeSessionId! : Guid.NewGuid().ToString();

        var command = new StringBuilder();
        command.Append('"').Append(Executable()).Append('"');
        command.Append(resuming ? " --resume " : " --session-id ").Append(sessionId);

        var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["CLAUDE_CONFIG_DIR"] = configDir
        };

        var pty = ConPtySession.Start(command.ToString(), projectPath, cols, rows, env);

        return new TerminalTile(pty, new TerminalScreen(cols, rows), cols, rows)
        {
            ProjectPath = projectPath,
            ProjectName = projectName,
            SessionId = sessionId
        };
    }

    /// <summary>Runs <paramref name="read"/> against a stable grid.</summary>
    public void Read(Action<TerminalScreen> read)
    {
        lock (_gate) read(_screen);
    }

    public void Send(ConsoleKeyInfo key)
    {
        var encoded = KeyEncoder.Encode(key);
        if (encoded.Length > 0) Write(encoded);
    }

    public void Write(string text)
    {
        if (_disposed) return;

        try
        {
            _pty.Write(text);
        }
        catch (IOException)
        {
            // The child went away between the key press and the write.
        }
    }

    public void Resize(int cols, int rows)
    {
        cols = Math.Max(20, cols);
        rows = Math.Max(4, rows);

        lock (_gate)
        {
            if (cols == _cols && rows == _rows) return;
            _cols = cols;
            _rows = rows;
            _screen.Resize(cols, rows);
        }

        _pty.Resize(cols, rows);
    }

    private void OnOutput(byte[] chunk)
    {
        lock (_gate) _parser.Feed(chunk, _screen);
    }

    private static string Executable()
    {
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "bin", "claude.exe");

        if (File.Exists(local)) return local;

        // CreateProcess will not search PATH for a bare name the way a shell
        // does, so resolve it here and fall back to the plain name.
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), "claude.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry is not worth failing the launch over.
            }
        }

        return "claude.exe";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _pty.Output -= OnOutput;
        _pty.Dispose();
    }
}
