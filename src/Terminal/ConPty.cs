using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ClaudeLauncher.Terminal;

/// <summary>
/// A child process running under a Windows pseudo console: its output is raised
/// as raw VT bytes, its input is fed back as text, and it follows the tile when
/// the tile is resized.
/// </summary>
public sealed class ConPtySession : IDisposable
{
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private static readonly IntPtr AttributePseudoConsole = 0x00020016;
    private const int StillActive = 259;

    private readonly object _gate = new();
    private readonly Thread _pump;
    private readonly FileStream _reader;
    private readonly FileStream _writer;

    private IntPtr _pseudoConsole;
    private IntPtr _attributeList;
    private IntPtr _process;
    private IntPtr _thread;
    private bool _disposed;

    private ConPtySession(IntPtr pseudoConsole, IntPtr attributeList, IntPtr process, IntPtr thread,
        int processId, FileStream reader, FileStream writer)
    {
        _pseudoConsole = pseudoConsole;
        _attributeList = attributeList;
        _process = process;
        _thread = thread;
        ProcessId = processId;
        _reader = reader;
        _writer = writer;

        _pump = new Thread(Pump) { IsBackground = true, Name = "conpty-read" };
        _pump.Start();
    }

    /// <summary>Raised on the read thread with a fresh array holding one chunk of output.</summary>
    public event Action<byte[]>? Output;

    public int ProcessId { get; }

    public bool HasExited
    {
        get
        {
            var handle = _process;
            if (handle == IntPtr.Zero) return true;
            return !GetExitCodeProcess(handle, out var code) || code != StillActive;
        }
    }

    /// <param name="env">
    /// Applied last over the scrubbed environment; a null value removes the variable.
    /// The launcher passes CLAUDE_CONFIG_DIR this way, since it differs per profile.
    /// </param>
    public static ConPtySession Start(string commandLine, string workingDirectory, int cols, int rows,
        IReadOnlyDictionary<string, string?>? env = null)
    {
        var size = new Coord { X = Clamp(cols), Y = Clamp(rows) };

        if (!CreatePipe(out var inRead, out var inWrite, IntPtr.Zero, 0))
            throw new IOException("CreatePipe (input): " + Marshal.GetLastWin32Error());
        if (!CreatePipe(out var outRead, out var outWrite, IntPtr.Zero, 0))
            throw new IOException("CreatePipe (output): " + Marshal.GetLastWin32Error());

        var hr = CreatePseudoConsole(size, inRead, outWrite, 0, out var pseudoConsole);
        if (hr != 0) throw new IOException($"CreatePseudoConsole failed: 0x{hr:x8}");

        nint attrSize = 0;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attrSize);
        if (attrSize <= 0) throw new IOException("InitializeProcThreadAttributeList sized 0");

        var attrList = Marshal.AllocHGlobal(attrSize);
        var environment = IntPtr.Zero;
        try
        {
            if (!InitializeProcThreadAttributeList(attrList, 1, 0, ref attrSize))
                throw new IOException("InitializeProcThreadAttributeList: " + Marshal.GetLastWin32Error());
            if (!UpdateProcThreadAttribute(attrList, 0, AttributePseudoConsole, pseudoConsole,
                    IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                throw new IOException("UpdateProcThreadAttribute: " + Marshal.GetLastWin32Error());

            var si = new StartupInfoEx();
            si.StartupInfo.Cb = Marshal.SizeOf<StartupInfoEx>();
            si.AttributeList = attrList;

            environment = BuildEnvironmentBlock(env);

            if (!CreateProcess(null, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                    ExtendedStartupInfoPresent | CreateUnicodeEnvironment, environment,
                    string.IsNullOrEmpty(workingDirectory) ? null : workingDirectory, ref si, out var pi))
            {
                throw new IOException("CreateProcess failed: " + Marshal.GetLastWin32Error());
            }

            // The pty owns these ends now; holding them open would prevent EOF.
            CloseHandle(inRead);
            CloseHandle(outWrite);

            var reader = new FileStream(new SafeFileHandle(outRead, true), FileAccess.Read, 1);
            var writer = new FileStream(new SafeFileHandle(inWrite, true), FileAccess.Write, 1);

            return new ConPtySession(pseudoConsole, attrList, pi.Process, pi.Thread, pi.ProcessId, reader, writer);
        }
        catch
        {
            ClosePseudoConsole(pseudoConsole);
            DeleteProcThreadAttributeList(attrList);
            Marshal.FreeHGlobal(attrList);
            CloseHandle(inRead);
            CloseHandle(inWrite);
            CloseHandle(outRead);
            CloseHandle(outWrite);
            throw;
        }
        finally
        {
            if (environment != IntPtr.Zero) Marshal.FreeHGlobal(environment);
        }
    }

    public void Write(string text) => Write(Encoding.UTF8.GetBytes(text));

    public void Write(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0) return;

        lock (_gate)
        {
            if (_disposed) return;
            try
            {
                _writer.Write(bytes);
                _writer.Flush();
            }
            catch (IOException)
            {
                // The child closed its input; keystrokes for a dead tile are dropped.
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    public void Resize(int cols, int rows)
    {
        lock (_gate)
        {
            if (_disposed || _pseudoConsole == IntPtr.Zero) return;
            ResizePseudoConsole(_pseudoConsole, new Coord { X = Clamp(cols), Y = Clamp(rows) });
        }
    }

    public void Dispose()
    {
        IntPtr console, attrList, process, thread;

        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            console = _pseudoConsole;
            attrList = _attributeList;
            process = _process;
            thread = _thread;
            _pseudoConsole = IntPtr.Zero;
            _attributeList = IntPtr.Zero;
            _process = IntPtr.Zero;
            _thread = IntPtr.Zero;
        }

        // Closing the console signals the child and drops the write end of the
        // output pipe, which is what lets the blocked read thread see EOF.
        if (console != IntPtr.Zero) ClosePseudoConsole(console);
        _pump.Join(TimeSpan.FromSeconds(2));

        if (process != IntPtr.Zero)
        {
            if (GetExitCodeProcess(process, out var code) && code == StillActive)
                TerminateProcess(process, 0);
            CloseHandle(process);
        }

        if (thread != IntPtr.Zero) CloseHandle(thread);

        if (attrList != IntPtr.Zero)
        {
            DeleteProcThreadAttributeList(attrList);
            Marshal.FreeHGlobal(attrList);
        }

        try { _writer.Dispose(); } catch (IOException) { }
        try { _reader.Dispose(); } catch (IOException) { }
    }

    private void Pump()
    {
        var chunk = new byte[16 * 1024];
        try
        {
            int read;
            while ((read = _reader.Read(chunk, 0, chunk.Length)) > 0)
            {
                var copy = new byte[read];
                Buffer.BlockCopy(chunk, 0, copy, 0, read);
                Output?.Invoke(copy);
            }
        }
        catch (IOException)
        {
            // Pipe broken when the console goes away; expected on Dispose.
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>
    /// A child <c>claude</c> that inherits CLAUDECODE or CLAUDE_CODE_ENTRYPOINT from a
    /// parent session silently runs non-interactive, so every CLAUDE/ANTHROPIC variable
    /// but the config dir is dropped. TERM and friends are set because without them the
    /// child decides the terminal has no colour and renders nearly monochrome.
    /// </summary>
    private static IntPtr BuildEnvironmentBlock(IReadOnlyDictionary<string, string?>? overrides)
    {
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var name = (string)entry.Key;
            if (name.Length == 0) continue;
            if (!name.Equals("CLAUDE_CONFIG_DIR", StringComparison.OrdinalIgnoreCase) &&
                (name.StartsWith("CLAUDE", StringComparison.OrdinalIgnoreCase) ||
                 name.StartsWith("ANTHROPIC", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            vars[name] = entry.Value as string ?? string.Empty;
        }

        vars["TERM"] = "xterm-256color";
        vars["COLORTERM"] = "truecolor";
        vars["FORCE_COLOR"] = "3";

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                if (string.IsNullOrEmpty(key)) continue;
                if (value is null) vars.Remove(key);
                else vars[key] = value;
            }
        }

        var block = new StringBuilder();
        foreach (var name in vars.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            block.Append(name).Append('=').Append(vars[name]).Append('\0');
        }

        block.Append('\0');
        return Marshal.StringToHGlobalUni(block.ToString());
    }

    private static short Clamp(int value) => (short)Math.Clamp(value, 1, short.MaxValue);

#pragma warning disable CS0649 // Filled in by the OS or left at zero on purpose.
    [StructLayout(LayoutKind.Sequential)]
    private struct Coord { public short X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx { public StartupInfo StartupInfo; public IntPtr AttributeList; }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public int Cb; public IntPtr Reserved, Desktop, Title;
        public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public short ShowWindow, Reserved2; public IntPtr Reserved2Ptr, StdInput, StdOutput, StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation { public IntPtr Process, Thread; public int ProcessId, ThreadId; }
#pragma warning restore CS0649

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out IntPtr read, out IntPtr write, IntPtr attrs, int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int CreatePseudoConsole(Coord size, IntPtr input, IntPtr output, uint flags, out IntPtr pc);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int ResizePseudoConsole(IntPtr pc, Coord size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void ClosePseudoConsole(IntPtr pc);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref nint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute,
        IntPtr value, nint size, IntPtr previous, IntPtr returnSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void DeleteProcThreadAttributeList(IntPtr list);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(string? app, string command, IntPtr processAttrs, IntPtr threadAttrs,
        bool inherit, uint flags, IntPtr environment, string? cwd, ref StartupInfoEx si, out ProcessInformation pi);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr handle, out int code);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr handle, uint code);
}
