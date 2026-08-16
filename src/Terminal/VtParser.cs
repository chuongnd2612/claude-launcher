using System.Text;

namespace ClaudeLauncher.Terminal;

/// <summary>
/// Decodes a VT/ANSI byte stream into <see cref="IVtSink"/> calls.
/// <para>
/// Holds no screen state — only enough to resume mid-sequence, because the pty
/// pipe splits escape sequences and multi-byte UTF-8 characters across reads at
/// will. Every field below survives a <see cref="Feed"/> boundary.
/// </para>
/// </summary>
public sealed class VtParser
{
    private const int MaxParams = 16;
    private const int MaxOscText = 4096;

    private enum State
    {
        Ground,
        Escape,
        Csi,
        OscCommand,
        OscText,
        OscEscape
    }

    private State _state = State.Ground;

    private readonly int[] _params = new int[MaxParams];
    private int _paramCount;
    private int _param = -1;
    private bool _sawParam;
    private bool _question;

    private char _intermediate;

    // OSC text is UTF-8 on the wire (window titles carry non-ASCII), so it is
    // buffered as bytes and decoded once at the terminator.
    private readonly byte[] _osc = new byte[MaxOscText];
    private int _oscLen;
    private int _oscCommand;
    private bool _oscHasCommand;

    // Partial UTF-8 sequence carried across a Feed boundary.
    private int _utf8Cp;
    private int _utf8Remaining;

    public void Feed(ReadOnlySpan<byte> bytes, IVtSink sink)
    {
        for (int i = 0; i < bytes.Length; i++)
        {
            byte b = bytes[i];
            switch (_state)
            {
                case State.Ground: Ground(b, sink); break;
                case State.Escape: Escape(b, sink); break;
                case State.Csi: Csi(b, sink); break;
                case State.OscCommand: OscCommand(b, sink); break;
                case State.OscText: OscText(b, sink); break;
                case State.OscEscape: OscEscape(b, sink); break;
            }
        }
    }

    public void Reset()
    {
        _state = State.Ground;
        _paramCount = 0;
        _param = -1;
        _sawParam = false;
        _question = false;
        _intermediate = '\0';
        _oscLen = 0;
        _oscCommand = 0;
        _oscHasCommand = false;
        _utf8Cp = 0;
        _utf8Remaining = 0;
    }

    private void Ground(byte b, IVtSink sink)
    {
        if (_utf8Remaining > 0)
        {
            if ((b & 0xC0) == 0x80)
            {
                _utf8Cp = (_utf8Cp << 6) | (b & 0x3F);
                if (--_utf8Remaining == 0) EmitCodepoint(_utf8Cp, sink);
                return;
            }

            // Truncated sequence: drop it and re-read this byte as a fresh start.
            _utf8Remaining = 0;
        }

        if (b < 0x20)
        {
            if (b == 0x1B) { BeginEscape(); return; }
            if (b is 0x07 or 0x08 or 0x09 or 0x0A or 0x0D) sink.Execute((char)b);
            return;
        }

        if (b < 0x7F) { sink.Print((char)b); return; }
        if (b == 0x7F) return; // DEL

        if ((b & 0xE0) == 0xC0) { _utf8Cp = b & 0x1F; _utf8Remaining = 1; }
        else if ((b & 0xF0) == 0xE0) { _utf8Cp = b & 0x0F; _utf8Remaining = 2; }
        else if ((b & 0xF8) == 0xF0) { _utf8Cp = b & 0x07; _utf8Remaining = 3; }
        // else: stray continuation or 0xF8+, not a valid lead — drop it.
    }

    /// <summary>
    /// Astral-plane codepoints are emitted as two <c>Print</c> calls, one per
    /// UTF-16 surrogate half, so the screen stores what a .NET string would.
    /// </summary>
    private static void EmitCodepoint(int cp, IVtSink sink)
    {
        if (cp > 0x10FFFF || (cp >= 0xD800 && cp <= 0xDFFF)) return;
        if (cp <= 0xFFFF) { sink.Print((char)cp); return; }

        cp -= 0x10000;
        sink.Print((char)(0xD800 + (cp >> 10)));
        sink.Print((char)(0xDC00 + (cp & 0x3FF)));
    }

    private void BeginEscape()
    {
        _state = State.Escape;
        _intermediate = '\0';
    }

    private void Escape(byte b, IVtSink sink)
    {
        switch (b)
        {
            case 0x1B:
                _intermediate = '\0';
                return;
            case (byte)'[':
                _state = State.Csi;
                _paramCount = 0;
                _param = -1;
                _sawParam = false;
                _question = false;
                _intermediate = '\0';
                return;
            case (byte)']':
                _state = State.OscCommand;
                _oscLen = 0;
                _oscCommand = 0;
                _oscHasCommand = false;
                return;
        }

        if (b >= 0x20 && b <= 0x2F) { _intermediate = (char)b; return; }

        if (b < 0x20)
        {
            if (b is 0x07 or 0x08 or 0x09 or 0x0A or 0x0D) sink.Execute((char)b);
            return;
        }

        if (b >= 0x30 && b <= 0x7E) sink.EscapeSequence((char)b, _intermediate);
        _state = State.Ground;
    }

    private void Csi(byte b, IVtSink sink)
    {
        if (b == 0x1B) { BeginEscape(); return; }

        if (b < 0x20)
        {
            if (b is 0x07 or 0x08 or 0x09 or 0x0A or 0x0D) sink.Execute((char)b);
            return;
        }

        if (b >= 0x30 && b <= 0x39)
        {
            _sawParam = true;
            if (_param < 0) _param = 0;
            _param = _param * 10 + (b - 0x30);
            return;
        }

        if (b == 0x3B)
        {
            _sawParam = true;
            PushParam();
            return;
        }

        if (b >= 0x3C && b <= 0x3F)
        {
            if (b == 0x3F) _question = true;
            return;
        }

        if (b >= 0x20 && b <= 0x2F) { _intermediate = (char)b; return; }

        if (b >= 0x40 && b <= 0x7E)
        {
            if (_sawParam) PushParam();
            sink.Csi((char)b, _params.AsSpan(0, _paramCount), _question);
        }

        _state = State.Ground;
    }

    private void PushParam()
    {
        if (_paramCount < MaxParams) _params[_paramCount++] = _param;
        _param = -1;
    }

    private void OscCommand(byte b, IVtSink sink)
    {
        if (b >= 0x30 && b <= 0x39)
        {
            _oscHasCommand = true;
            if (_oscCommand < 1_000_000) _oscCommand = _oscCommand * 10 + (b - 0x30);
            return;
        }

        if (b == 0x3B) { _state = State.OscText; return; }
        if (b == 0x07) { DispatchOsc(sink); return; }
        if (b == 0x1B) { _state = State.OscEscape; return; }

        // Anything else means this is not an OSC we understand; swallow the
        // rest as text rather than losing sync on the terminator.
        _state = State.OscText;
        OscText(b, sink);
    }

    private void OscText(byte b, IVtSink sink)
    {
        // Only BEL and ESC \ terminate: the stream is UTF-8, so a bare 0x9C is
        // a continuation byte and never a C1 ST.
        switch (b)
        {
            case 0x07:
                DispatchOsc(sink);
                return;
            case 0x1B:
                _state = State.OscEscape;
                return;
        }

        if (b < 0x20) return;
        if (_oscLen < MaxOscText) _osc[_oscLen++] = b;
    }

    private void OscEscape(byte b, IVtSink sink)
    {
        DispatchOsc(sink);
        if (b == (byte)'\\') return;

        // Not ST after all — the ESC opened a new sequence.
        BeginEscape();
        Escape(b, sink);
    }

    private void DispatchOsc(IVtSink sink)
    {
        sink.Osc(_oscHasCommand ? _oscCommand : -1, Encoding.UTF8.GetString(_osc, 0, _oscLen));
        _oscLen = 0;
        _oscCommand = 0;
        _oscHasCommand = false;
        _state = State.Ground;
    }
}
