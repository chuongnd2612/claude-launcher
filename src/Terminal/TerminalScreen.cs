using System.Text;
using ClaudeLauncher.Tui;

namespace ClaudeLauncher.Terminal;

/// <summary>
/// An in-memory terminal grid. Consumes the actions a VT stream decodes to and
/// exposes the result as cells a renderer can blit.
///
/// The sequence set is deliberately narrow: it covers what Claude Code actually
/// emits under a pseudo console (cursor motion, SGR, erase, alternate screen)
/// and nothing else. Scroll regions, insert/delete line and charset selection
/// never appear in a capture, so they are parsed away rather than implemented.
/// </summary>
public sealed class TerminalScreen : IVtSink
{
    private sealed class Buffer
    {
        public TerminalCell[] Cells;
        public int Cols;
        public int Rows;

        public Buffer(int cols, int rows, TerminalCell fill)
        {
            Cols = cols;
            Rows = rows;
            Cells = new TerminalCell[cols * rows];
            Cells.AsSpan().Fill(fill);
        }
    }

    private Buffer _primary;
    private Buffer _alt;
    private Buffer _active;

    /// <summary>Rows evicted from the top of the primary buffer, oldest first.</summary>
    private readonly TerminalCell[]?[] _scrollback = new TerminalCell[MaxScrollback][];
    private int _scrollbackStart;
    private int _scrollbackCount;
    private int _scrollOffset;

    private const int MaxScrollback = 2000;

    private int _cursorX;
    private int _cursorY;

    // Deferred wrap: after printing into the last column the cursor stays put
    // and only moves to the next row when another printable arrives. Without
    // this a character written exactly at the edge scrolls the screen early.
    private bool _pendingWrap;

    private int _savedX;
    private int _savedY;

    private Rgb _fg;
    private Rgb _bg;
    private CellAttrs _attrs;

    public TerminalScreen(int cols, int rows)
    {
        if (cols < 1) cols = 1;
        if (rows < 1) rows = 1;
        _primary = new Buffer(cols, rows, TerminalCell.Blank);
        _alt = new Buffer(cols, rows, TerminalCell.Blank);
        _active = _primary;
        CursorVisible = true;
    }

    public int Cols => _active.Cols;

    public int Rows => _active.Rows;

    public int CursorX => _cursorX;

    public int CursorY => _cursorY;

    public bool CursorVisible { get; private set; }

    public string? Title { get; private set; }

    /// <summary>Bumped on every mutation so a renderer can skip an idle pane.</summary>
    public long Revision { get; private set; }

    /// <summary>True while the alternate screen buffer is active (?1049h).</summary>
    public bool IsAlternate => ReferenceEquals(_active, _alt);

    /// <summary>Recorded only; the tile never sends paste markers back.</summary>
    public bool BracketedPaste { get; private set; }

    /// <summary>Recorded only; the tile never sends mouse events back.</summary>
    public bool MouseTracking { get; private set; }

    public TerminalCell this[int x, int y]
    {
        get
        {
            if ((uint)x >= (uint)_active.Cols) throw new ArgumentOutOfRangeException(nameof(x));
            if ((uint)y >= (uint)_active.Rows) throw new ArgumentOutOfRangeException(nameof(y));
            return _active.Cells[y * _active.Cols + x];
        }
    }

    /// <summary>Lines retained above the primary buffer, capped at 2000.</summary>
    public int ScrollbackCount => _scrollbackCount;

    /// <summary>How many lines the view is scrolled back; 0 is the live bottom.</summary>
    public int ScrollOffset => _scrollOffset;

    public bool IsScrolled => _scrollOffset > 0;

    /// <summary>Positive moves back into history, negative back toward live.</summary>
    public void ScrollBy(int lines)
    {
        // The alternate screen is a full-screen view with no history of its own.
        if (IsAlternate || lines == 0) return;
        var next = Math.Clamp(_scrollOffset + lines, 0, _scrollbackCount);
        if (next == _scrollOffset) return;
        _scrollOffset = next;
        Revision++;
    }

    public void ScrollToBottom()
    {
        if (_scrollOffset == 0) return;
        _scrollOffset = 0;
        Revision++;
    }

    /// <summary>
    /// The cell at a viewport position, reading scrollback for the rows the
    /// current offset has pushed the live region past. Total: out of range
    /// reads a blank rather than throwing, so a renderer can paint any rect.
    /// </summary>
    public TerminalCell CellAt(int x, int y)
    {
        if (x < 0 || y < 0 || x >= _active.Cols || y >= _active.Rows) return TerminalCell.Blank;
        if (y >= _scrollOffset) return _active.Cells[(y - _scrollOffset) * _active.Cols + x];

        var line = _scrollback[(_scrollbackStart + _scrollbackCount - _scrollOffset + y) % MaxScrollback];
        if (line is null || x >= line.Length) return TerminalCell.Blank;
        return line[x];
    }

    public void Resize(int cols, int rows)
    {
        if (cols < 1) cols = 1;
        if (rows < 1) rows = 1;
        if (cols == _active.Cols && rows == _active.Rows) return;

        var wasAlt = IsAlternate;
        _primary = Reflow(_primary, cols, rows);
        _alt = Reflow(_alt, cols, rows);
        _active = wasAlt ? _alt : _primary;

        _cursorX = Math.Min(_cursorX, cols - 1);
        _cursorY = Math.Min(_cursorY, rows - 1);
        _savedX = Math.Min(_savedX, cols - 1);
        _savedY = Math.Min(_savedY, rows - 1);
        _pendingWrap = false;
        _scrollOffset = Math.Clamp(_scrollOffset, 0, _scrollbackCount);
        Revision++;
    }

    private Buffer Reflow(Buffer old, int cols, int rows)
    {
        var next = new Buffer(cols, rows, Blank());
        var copyCols = Math.Min(cols, old.Cols);
        var copyRows = Math.Min(rows, old.Rows);
        for (var y = 0; y < copyRows; y++)
        {
            old.Cells.AsSpan(y * old.Cols, copyCols).CopyTo(next.Cells.AsSpan(y * cols, copyCols));
        }

        return next;
    }

    public string ToPlainText()
    {
        var lines = new List<string>(_active.Rows);
        var sb = new StringBuilder(_active.Cols);
        for (var y = 0; y < _active.Rows; y++)
        {
            sb.Clear();
            for (var x = 0; x < _active.Cols; x++)
            {
                var ch = _active.Cells[y * _active.Cols + x].Ch;
                sb.Append(ch == '\0' ? ' ' : ch);
            }

            lines.Add(sb.ToString().TrimEnd());
        }

        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return string.Join("\n", lines);
    }

    // ---- IVtSink ---------------------------------------------------------

    public void Print(char ch)
    {
        if (_pendingWrap)
        {
            _cursorX = 0;
            LineFeed();
            _pendingWrap = false;
        }

        _active.Cells[_cursorY * _active.Cols + _cursorX] = new TerminalCell
        {
            Ch = ch,
            Fg = _fg,
            Bg = _bg,
            Attrs = _attrs
        };

        if (_cursorX >= _active.Cols - 1) _pendingWrap = true;
        else _cursorX++;
        Revision++;
    }

    public void Execute(char control)
    {
        switch (control)
        {
            case '\r':
                _cursorX = 0;
                _pendingWrap = false;
                Revision++;
                break;
            case '\n':
            case '\v':
            case '\f':
                LineFeed();
                _pendingWrap = false;
                Revision++;
                break;
            case '\b':
                if (_pendingWrap) _pendingWrap = false;
                else if (_cursorX > 0) _cursorX--;
                Revision++;
                break;
            case '\t':
                _cursorX = Math.Min(((_cursorX / 8) + 1) * 8, _active.Cols - 1);
                _pendingWrap = false;
                Revision++;
                break;
            case '\a':
                break;
        }
    }

    public void Csi(char final, ReadOnlySpan<int> parameters, bool question)
    {
        if (question)
        {
            SetPrivateModes(parameters, final == 'h');
            return;
        }

        switch (final)
        {
            case 'A':
                MoveCursor(0, -Math.Max(1, Param(parameters, 0, 1)));
                break;
            case 'B':
                MoveCursor(0, Math.Max(1, Param(parameters, 0, 1)));
                break;
            case 'C':
                MoveCursor(Math.Max(1, Param(parameters, 0, 1)), 0);
                break;
            case 'D':
                MoveCursor(-Math.Max(1, Param(parameters, 0, 1)), 0);
                break;
            case 'H':
            case 'f':
                SetCursor(Param(parameters, 1, 1) - 1, Param(parameters, 0, 1) - 1);
                break;
            case 'J':
                EraseInDisplay(Param(parameters, 0, 0));
                break;
            case 'K':
                EraseInLine(Param(parameters, 0, 0));
                break;
            case 'X':
                EraseChars(Math.Max(1, Param(parameters, 0, 1)));
                break;
            case 'm':
                ApplySgr(parameters);
                break;
        }
    }

    public void Osc(int command, string text)
    {
        if (command != 0 && command != 2) return;
        Title = text;
        Revision++;
    }

    public void EscapeSequence(char final, char intermediate)
    {
        if (intermediate != '\0') return;
        switch (final)
        {
            case 'c':
                Reset();
                break;
            case '7':
                _savedX = _cursorX;
                _savedY = _cursorY;
                break;
            case '8':
                SetCursor(_savedX, _savedY);
                break;
        }
    }

    // ---- internals -------------------------------------------------------

    private static int Param(ReadOnlySpan<int> parameters, int index, int fallback)
    {
        if (index >= parameters.Length) return fallback;
        var value = parameters[index];
        return value < 0 ? fallback : value;
    }

    private TerminalCell Blank() => new()
    {
        Ch = ' ',
        Bg = _bg,
        Attrs = _attrs & CellAttrs.HasBg
    };

    private void LineFeed()
    {
        if (_cursorY >= _active.Rows - 1) ScrollUp();
        else _cursorY++;
    }

    private void ScrollUp()
    {
        var cols = _active.Cols;
        if (!IsAlternate) PushScrollback(_active.Cells.AsSpan(0, cols));
        Array.Copy(_active.Cells, cols, _active.Cells, 0, cols * (_active.Rows - 1));
        _active.Cells.AsSpan(cols * (_active.Rows - 1), cols).Fill(Blank());
    }

    private void PushScrollback(ReadOnlySpan<TerminalCell> row)
    {
        var slot = (_scrollbackStart + _scrollbackCount) % MaxScrollback;

        // At the cap the oldest line's array is recycled rather than dropped, so
        // a long-running pane stops allocating once the ring has filled.
        var line = _scrollback[slot];
        if (line is null || line.Length != row.Length) line = new TerminalCell[row.Length];
        row.CopyTo(line);
        _scrollback[slot] = line;

        if (_scrollbackCount == MaxScrollback) _scrollbackStart = (_scrollbackStart + 1) % MaxScrollback;
        else _scrollbackCount++;

        // A reader stays on the text they are looking at: the offset counts back
        // from the live bottom, so it has to grow as the bottom moves away. Once
        // the ring is full the oldest line is gone and the view has to slide.
        if (_scrollOffset > 0 && _scrollOffset < _scrollbackCount) _scrollOffset++;
    }

    private void ClearScrollback()
    {
        _scrollbackStart = 0;
        _scrollbackCount = 0;
        _scrollOffset = 0;
    }

    private void MoveCursor(int dx, int dy)
    {
        SetCursor(_cursorX + dx, _cursorY + dy);
    }

    private void SetCursor(int x, int y)
    {
        _cursorX = Math.Clamp(x, 0, _active.Cols - 1);
        _cursorY = Math.Clamp(y, 0, _active.Rows - 1);
        _pendingWrap = false;
        Revision++;
    }

    private void EraseInDisplay(int mode)
    {
        var cols = _active.Cols;
        var start = _cursorY * cols + _cursorX;
        var total = _active.Cells.Length;
        switch (mode)
        {
            case 0:
                _active.Cells.AsSpan(start, total - start).Fill(Blank());
                break;
            case 1:
                _active.Cells.AsSpan(0, start + 1).Fill(Blank());
                break;
            default: // 2 (screen) and 3 (screen plus scrollback)
                _active.Cells.AsSpan().Fill(Blank());
                if (mode == 3 && !IsAlternate) ClearScrollback();
                break;
        }

        _pendingWrap = false;
        Revision++;
    }

    private void EraseInLine(int mode)
    {
        var cols = _active.Cols;
        var row = _cursorY * cols;
        switch (mode)
        {
            case 0:
                _active.Cells.AsSpan(row + _cursorX, cols - _cursorX).Fill(Blank());
                break;
            case 1:
                _active.Cells.AsSpan(row, _cursorX + 1).Fill(Blank());
                break;
            default:
                _active.Cells.AsSpan(row, cols).Fill(Blank());
                break;
        }

        _pendingWrap = false;
        Revision++;
    }

    private void EraseChars(int count)
    {
        var cols = _active.Cols;
        var n = Math.Min(count, cols - _cursorX);
        _active.Cells.AsSpan(_cursorY * cols + _cursorX, n).Fill(Blank());
        _pendingWrap = false;
        Revision++;
    }

    private void SetPrivateModes(ReadOnlySpan<int> parameters, bool set)
    {
        for (var i = 0; i < parameters.Length; i++)
        {
            switch (parameters[i])
            {
                case 25:
                    CursorVisible = set;
                    Revision++;
                    break;
                case 1049:
                    SetAlternate(set);
                    break;
                case 2004:
                    BracketedPaste = set;
                    break;
                case 1000:
                case 1002:
                case 1003:
                case 1006:
                    MouseTracking = set;
                    break;
            }
        }
    }

    private void SetAlternate(bool enter)
    {
        if (enter == IsAlternate) return;
        if (enter)
        {
            _savedX = _cursorX;
            _savedY = _cursorY;
            _scrollOffset = 0;
            _alt.Cells.AsSpan().Fill(Blank());
            _active = _alt;
            _cursorX = 0;
            _cursorY = 0;
        }
        else
        {
            _active = _primary;
            _cursorX = Math.Min(_savedX, _active.Cols - 1);
            _cursorY = Math.Min(_savedY, _active.Rows - 1);
        }

        _pendingWrap = false;
        Revision++;
    }

    private void Reset()
    {
        _active = _primary;
        _primary.Cells.AsSpan().Fill(TerminalCell.Blank);
        _alt.Cells.AsSpan().Fill(TerminalCell.Blank);
        _cursorX = 0;
        _cursorY = 0;
        _savedX = 0;
        _savedY = 0;
        _pendingWrap = false;
        CursorVisible = true;
        ClearScrollback();
        ResetSgr();
        Revision++;
    }

    private void ResetSgr()
    {
        _attrs = CellAttrs.None;
        _fg = default;
        _bg = default;
    }

    private void ApplySgr(ReadOnlySpan<int> parameters)
    {
        if (parameters.Length == 0)
        {
            ResetSgr();
            Revision++;
            return;
        }

        for (var i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i] < 0 ? 0 : parameters[i];
            switch (p)
            {
                case 0: ResetSgr(); break;
                case 1: _attrs |= CellAttrs.Bold; break;
                case 2: _attrs |= CellAttrs.Dim; break;
                case 3: _attrs |= CellAttrs.Italic; break;
                case 4: _attrs |= CellAttrs.Underline; break;
                case 7: _attrs |= CellAttrs.Inverse; break;
                case 22: _attrs &= ~(CellAttrs.Bold | CellAttrs.Dim); break;
                case 23: _attrs &= ~CellAttrs.Italic; break;
                case 24: _attrs &= ~CellAttrs.Underline; break;
                case 27: _attrs &= ~CellAttrs.Inverse; break;
                case 39: _fg = default; _attrs &= ~CellAttrs.HasFg; break;
                case 49: _bg = default; _attrs &= ~CellAttrs.HasBg; break;
                case 38:
                case 48:
                    if (TryReadExtendedColor(parameters, ref i, out var extended)) SetColor(p == 38, extended);
                    break;
                default:
                    if (p >= 30 && p <= 37) SetColor(true, Ansi16(p - 30));
                    else if (p >= 40 && p <= 47) SetColor(false, Ansi16(p - 40));
                    else if (p >= 90 && p <= 97) SetColor(true, Ansi16(p - 90 + 8));
                    else if (p >= 100 && p <= 107) SetColor(false, Ansi16(p - 100 + 8));
                    break;
            }
        }

        Revision++;
    }

    private void SetColor(bool foreground, Rgb color)
    {
        if (foreground)
        {
            _fg = color;
            _attrs |= CellAttrs.HasFg;
        }
        else
        {
            _bg = color;
            _attrs |= CellAttrs.HasBg;
        }
    }

    /// <summary>
    /// Reads the tail of a 38/48 selector, advancing <paramref name="i"/> past
    /// the arguments it consumed. Claude only ever emits the ;2;r;g;b form.
    /// </summary>
    private static bool TryReadExtendedColor(ReadOnlySpan<int> parameters, ref int i, out Rgb color)
    {
        color = default;
        if (i + 1 >= parameters.Length) return false;
        var kind = parameters[i + 1];
        if (kind == 2)
        {
            if (i + 4 >= parameters.Length) { i = parameters.Length - 1; return false; }
            color = new Rgb(Channel(parameters[i + 2]), Channel(parameters[i + 3]), Channel(parameters[i + 4]));
            i += 4;
            return true;
        }

        if (kind == 5)
        {
            if (i + 2 >= parameters.Length) { i = parameters.Length - 1; return false; }
            color = Xterm256(parameters[i + 2]);
            i += 2;
            return true;
        }

        i++;
        return false;
    }

    private static byte Channel(int value) => (byte)Math.Clamp(value, 0, 255);

    private static Rgb Ansi16(int index) => index switch
    {
        0 => new Rgb(0, 0, 0),
        1 => new Rgb(205, 49, 49),
        2 => new Rgb(13, 188, 121),
        3 => new Rgb(229, 229, 16),
        4 => new Rgb(36, 114, 200),
        5 => new Rgb(188, 63, 188),
        6 => new Rgb(17, 168, 205),
        7 => new Rgb(229, 229, 229),
        8 => new Rgb(102, 102, 102),
        9 => new Rgb(241, 76, 76),
        10 => new Rgb(35, 209, 139),
        11 => new Rgb(245, 245, 67),
        12 => new Rgb(59, 142, 234),
        13 => new Rgb(214, 112, 214),
        14 => new Rgb(41, 184, 219),
        _ => new Rgb(255, 255, 255)
    };

    private static Rgb Xterm256(int index)
    {
        if (index < 0) return default;
        if (index < 16) return Ansi16(index);
        if (index < 232)
        {
            var n = index - 16;
            return new Rgb(CubeStep(n / 36), CubeStep((n / 6) % 6), CubeStep(n % 6));
        }

        if (index < 256)
        {
            var level = (byte)(8 + (index - 232) * 10);
            return new Rgb(level, level, level);
        }

        return default;
    }

    private static byte CubeStep(int step) => step == 0 ? (byte)0 : (byte)(55 + step * 40);
}
