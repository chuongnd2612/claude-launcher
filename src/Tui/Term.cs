using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace ClaudeLauncher.Tui;

/// <summary>24-bit color value.</summary>
public readonly struct Rgb : IEquatable<Rgb>
{
    public readonly byte R;
    public readonly byte G;
    public readonly byte B;

    public Rgb(byte r, byte g, byte b)
    {
        R = r;
        G = g;
        B = b;
    }

    /// <summary>Parses "#rrggbb" (or "rrggbb").</summary>
    public static Rgb Hex(string hex)
    {
        var value = hex.StartsWith("#", StringComparison.Ordinal) ? hex.Substring(1) : hex;
        var packed = int.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new Rgb((byte)((packed >> 16) & 0xFF), (byte)((packed >> 8) & 0xFF), (byte)(packed & 0xFF));
    }

    public static Rgb Lerp(Rgb a, Rgb b, double t)
    {
        if (t < 0) t = 0;
        if (t > 1) t = 1;
        return new Rgb(
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));
    }

    /// <summary>Blends <paramref name="fg"/> over <paramref name="bg"/> at the given alpha.</summary>
    public static Rgb Mix(Rgb bg, Rgb fg, double alpha) => Lerp(bg, fg, alpha);

    public bool Equals(Rgb other) => R == other.R && G == other.G && B == other.B;

    public override bool Equals(object? obj) => obj is Rgb other && Equals(other);

    public override int GetHashCode() => (R << 16) | (G << 8) | B;

    public static bool operator ==(Rgb left, Rgb right) => left.Equals(right);

    public static bool operator !=(Rgb left, Rgb right) => !left.Equals(right);
}

/// <summary>Low level terminal setup / teardown and raw output.</summary>
public static class Term
{
    public const string Esc = "\u001b";

    private const int StdOutputHandle = -11;
    private const uint EnableVirtualTerminalProcessing = 0x0004;

    private static StreamWriter? _out;
    private static bool _altScreen;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr handle, out uint mode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr handle, uint mode);

    public static StreamWriter Out
    {
        get
        {
            if (_out is null)
            {
                _out = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false), 1 << 16)
                {
                    AutoFlush = false
                };
            }

            return _out;
        }
    }

    public static int Width => Math.Max(40, SafeWidth());

    public static int Height => Math.Max(12, SafeHeight());

    private static int SafeWidth()
    {
        try { return Console.WindowWidth; }
        catch { return 120; }
    }

    private static int SafeHeight()
    {
        try { return Console.WindowHeight; }
        catch { return 40; }
    }

    /// <summary>Enables VT sequences, UTF-8 output, the alternate screen buffer and hides the caret.</summary>
    public static void Setup(string title)
    {
        EnableVirtualTerminal();

        try { Console.OutputEncoding = new UTF8Encoding(false); }
        catch { /* redirected output */ }

        Raw($"{Esc}[?1049h");   // alternate screen
        Raw($"{Esc}[?25l");     // hide cursor
        SetTitle(title);
        Raw($"{Esc}[2J");
        Flush();
        _altScreen = true;
        IsSetup = true;
    }

    /// <summary>True once <see cref="Setup"/> has run, so error paths do not leak escape codes.</summary>
    public static bool IsSetup { get; private set; }

    public static void Restore()
    {
        if (!IsSetup) return;
        IsSetup = false;

        Raw($"{Esc}[0m");
        Raw($"{Esc}[?25h");
        if (_altScreen)
        {
            Raw($"{Esc}[?1049l");
            _altScreen = false;
        }

        Flush();
    }

    public static void SetTitle(string title) => Raw($"{Esc}]0;{title}\u0007");

    public static void Raw(string text) => Out.Write(text);

    public static void Flush() => Out.Flush();

    public static void MoveTo(int x, int y) => Raw($"{Esc}[{y + 1};{x + 1}H");

    private static void EnableVirtualTerminal()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        try
        {
            var handle = GetStdHandle(StdOutputHandle);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return;
            if (!GetConsoleMode(handle, out var mode)) return;
            SetConsoleMode(handle, mode | EnableVirtualTerminalProcessing);
        }
        catch
        {
            // Older hosts without VT support: the UI degrades but still runs.
        }
    }
}
