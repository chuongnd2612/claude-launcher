using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace ClaudeLauncher.Tui;

public enum InputKind
{
    Key,
    MouseDown,
    MouseWheel
}

/// <summary>One thing the user did: a key press, a click, or a wheel notch.</summary>
public readonly struct InputEvent
{
    public readonly InputKind Kind;
    public readonly ConsoleKeyInfo Key;

    /// <summary>Cell coordinates inside the launcher's own grid.</summary>
    public readonly int X;
    public readonly int Y;

    /// <summary>Wheel notches: positive scrolls back through history.</summary>
    public readonly int Delta;

    public InputEvent(ConsoleKeyInfo key)
    {
        Kind = InputKind.Key;
        Key = key;
        X = Y = Delta = 0;
    }

    public InputEvent(InputKind kind, int x, int y, int delta)
    {
        Kind = kind;
        Key = default;
        X = x;
        Y = y;
        Delta = delta;
    }
}

/// <summary>
/// Console input read on a thread of its own and handed over as events.
///
/// The launcher used to poll <see cref="Console.KeyAvailable"/> every 35ms,
/// which put that much delay in front of every keystroke before anything even
/// looked at it. Reading blocks here instead and signals the render loop, so a
/// key is acted on as soon as Windows has it. Reading the input records rather
/// than <see cref="Console.ReadKey"/> is also the only way to see the mouse.
/// </summary>
public static class ConsoleInput
{
    private const int StdInputHandle = -10;

    private const uint EnableProcessedInput = 0x0001;
    private const uint EnableMouseInput = 0x0010;
    private const uint EnableQuickEditMode = 0x0040;
    private const uint EnableExtendedFlags = 0x0080;
    private const uint EnableWindowInput = 0x0008;

    private const ushort KeyEventType = 0x0001;
    private const ushort MouseEventType = 0x0002;

    private const uint MouseWheeled = 0x0004;
    private const uint LeftmostPressed = 0x0001;

    private static readonly ConcurrentQueue<InputEvent> Queue = new();

    /// <summary>Set when there is something to do: input arrived, or a tile drew.</summary>
    private static readonly ManualResetEventSlim Signal = new(false);

    private static Thread? _reader;
    private static IntPtr _handle = IntPtr.Zero;
    private static uint _previousMode;
    private static bool _started;
    private static bool _mouse;

    public static bool MouseEnabled => _mouse;

    /// <summary>Wakes the render loop early - a tile with new output calls this.</summary>
    public static void Wake() => Signal.Set();

    public static void Start()
    {
        if (_started) return;
        _started = true;

        try
        {
            _handle = GetStdHandle(StdInputHandle);
            if (_handle != IntPtr.Zero && GetConsoleMode(_handle, out _previousMode))
            {
                // Quick edit has to go: with it on, a click starts a selection
                // and the console keeps the mouse events to itself.
                var mode = (_previousMode & ~EnableQuickEditMode & ~EnableProcessedInput)
                           | EnableExtendedFlags | EnableMouseInput | EnableWindowInput;

                _mouse = SetConsoleMode(_handle, mode);
            }
        }
        catch (Exception)
        {
            // Not a real console (a pipe, or another platform): keys still work
            // through the fallback reader below.
            _mouse = false;
        }

        _reader = new Thread(Pump) { IsBackground = true, Name = "console-input" };
        _reader.Start();
    }

    public static void Stop()
    {
        if (!_started) return;
        _started = false;

        try
        {
            if (_mouse && _handle != IntPtr.Zero) SetConsoleMode(_handle, _previousMode);
        }
        catch (Exception)
        {
            // Restoring the mode is best effort; the console is going away anyway.
        }
    }

    /// <summary>
    /// Waits for input or a wake-up. Returns false when it timed out, which the
    /// caller treats as "check whether anything needs redrawing".
    /// </summary>
    public static bool Wait(TimeSpan timeout, out InputEvent input)
    {
        if (Queue.TryDequeue(out input)) return true;

        Signal.Wait(timeout);
        Signal.Reset();

        return Queue.TryDequeue(out input);
    }

    private static void Pump()
    {
        while (_started)
        {
            try
            {
                if (!ReadRecords()) Fallback();
            }
            catch (Exception)
            {
                Fallback();
            }
        }
    }

    /// <summary>Console.ReadKey for hosts where the input records are unavailable.</summary>
    private static void Fallback()
    {
        try
        {
            Queue.Enqueue(new InputEvent(Console.ReadKey(intercept: true)));
            Signal.Set();
        }
        catch (InvalidOperationException)
        {
            Thread.Sleep(50);
        }
    }

    private static bool ReadRecords()
    {
        if (_handle == IntPtr.Zero) return false;

        var records = new InputRecord[16];
        if (!ReadConsoleInput(_handle, records, (uint)records.Length, out var read)) return false;

        for (var i = 0; i < read; i++)
        {
            var record = records[i];

            switch (record.EventType)
            {
                case KeyEventType when record.Key.KeyDown:
                    Queue.Enqueue(new InputEvent(ToKeyInfo(record.Key)));
                    break;

                case MouseEventType when record.Mouse.EventFlags == MouseWheeled:
                    // The high word is a signed notch count, 120 per detent.
                    var notches = (short)(record.Mouse.ButtonState >> 16) / 120;
                    if (notches != 0)
                    {
                        Queue.Enqueue(new InputEvent(InputKind.MouseWheel,
                            record.Mouse.Position.X, record.Mouse.Position.Y, notches));
                    }

                    break;

                case MouseEventType when record.Mouse.EventFlags == 0 &&
                                         (record.Mouse.ButtonState & LeftmostPressed) != 0:
                    Queue.Enqueue(new InputEvent(InputKind.MouseDown,
                        record.Mouse.Position.X, record.Mouse.Position.Y, 0));
                    break;
            }
        }

        if (read > 0) Signal.Set();
        return true;
    }

    private static ConsoleKeyInfo ToKeyInfo(KeyEventRecord key)
    {
        const int shiftPressed = 0x0010;
        const int leftAlt = 0x0002;
        const int rightAlt = 0x0001;
        const int leftCtrl = 0x0008;
        const int rightCtrl = 0x0004;

        var state = key.ControlKeyState;
        var modifiers = (ConsoleModifiers)0;

        if ((state & shiftPressed) != 0) modifiers |= ConsoleModifiers.Shift;
        if ((state & (leftAlt | rightAlt)) != 0) modifiers |= ConsoleModifiers.Alt;
        if ((state & (leftCtrl | rightCtrl)) != 0) modifiers |= ConsoleModifiers.Control;

        return new ConsoleKeyInfo(key.UnicodeChar, (ConsoleKey)key.VirtualKeyCode,
            (modifiers & ConsoleModifiers.Shift) != 0,
            (modifiers & ConsoleModifiers.Alt) != 0,
            (modifiers & ConsoleModifiers.Control) != 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Coord
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyEventRecord
    {
        [MarshalAs(UnmanagedType.Bool)] public bool KeyDown;
        public ushort RepeatCount;
        public ushort VirtualKeyCode;
        public ushort VirtualScanCode;
        public char UnicodeChar;
        public int ControlKeyState;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseEventRecord
    {
        public Coord Position;
        public uint ButtonState;
        public uint ControlKeyState;
        public uint EventFlags;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputRecord
    {
        [FieldOffset(0)] public ushort EventType;
        [FieldOffset(4)] public KeyEventRecord Key;
        [FieldOffset(4)] public MouseEventRecord Mouse;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr handle, out uint mode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr handle, uint mode);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ReadConsoleInput(IntPtr handle,
        [Out] InputRecord[] buffer, uint length, out uint read);
}
