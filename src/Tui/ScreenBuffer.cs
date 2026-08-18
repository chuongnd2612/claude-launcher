using System.Text;

namespace ClaudeLauncher.Tui;

public enum BoxStyle
{
    Rounded,
    Sharp,
    Dashed,
    Double
}

/// <summary>
/// A cell grid that is painted in full each frame and flushed with a single
/// write. Runs of identical style share one escape sequence, so a full redraw
/// of a 160x50 screen stays a few kilobytes.
/// </summary>
public sealed class ScreenBuffer
{
    private struct Cell
    {
        public char Ch;
        public Sty Style;
    }

    private Cell[] _cells = Array.Empty<Cell>();
    private readonly StringBuilder _sb = new(1 << 16);

    public int Width { get; private set; }

    public int Height { get; private set; }

    public bool PaintBackground { get; set; } = true;

    public Sty Base => new(Theme.Text, Theme.Bg);

    public void Resize(int width, int height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        _cells = new Cell[Width * Height];
    }

    public void Clear()
    {
        var style = new Sty(Theme.Text, Theme.Bg);
        for (var i = 0; i < _cells.Length; i++)
        {
            _cells[i].Ch = ' ';
            _cells[i].Style = style;
        }
    }

    public Rgb BgAt(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return Theme.Bg;
        return _cells[y * Width + x].Style.Bg;
    }

    public Rgb FgAt(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return Theme.Text;
        return _cells[y * Width + x].Style.Fg;
    }

    public void Set(int x, int y, char ch, Sty style)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return;
        var index = y * Width + x;
        _cells[index].Ch = ch;
        _cells[index].Style = style;
    }

    /// <summary>Writes text and returns the x position just past the last glyph.</summary>
    public int Write(int x, int y, string text, Sty style)
    {
        if (y < 0 || y >= Height) return x;
        var cursor = x;
        foreach (var ch in text)
        {
            if (cursor >= Width) break;
            if (cursor >= 0) Set(cursor, y, ch, style);
            cursor++;
        }

        return cursor;
    }

    /// <summary>Writes text truncated to <paramref name="maxWidth"/>, adding an ellipsis when clipped.</summary>
    public int WriteClipped(int x, int y, string text, int maxWidth, Sty style)
    {
        if (maxWidth <= 0) return x;
        var value = Truncate(text, maxWidth);
        return Write(x, y, value, style);
    }

    public void WriteCentered(int y, string text, Sty style)
    {
        var x = Math.Max(0, (Width - text.Length) / 2);
        Write(x, y, text, style);
    }

    public void WriteRight(int right, int y, string text, Sty style) => Write(right - text.Length + 1, y, text, style);

    public void Fill(int x, int y, int width, int height, Rgb bg)
    {
        for (var row = y; row < y + height; row++)
        {
            for (var col = x; col < x + width; col++)
            {
                if (col < 0 || row < 0 || col >= Width || row >= Height) continue;
                var index = row * Width + col;
                _cells[index].Ch = ' ';
                _cells[index].Style = _cells[index].Style.OnBg(bg);
            }
        }
    }

    public void HLine(int x, int y, int width, char ch, Sty style)
    {
        for (var col = x; col < x + width; col++) Set(col, y, ch, style);
    }

    /// <summary>Draws a box; when <paramref name="fill"/> is set the interior is painted too.</summary>
    public void Box(int x, int y, int width, int height, Sty border, BoxStyle style = BoxStyle.Rounded, Rgb? fill = null)
    {
        if (width < 2 || height < 2) return;

        char tl, tr, bl, br, h, v;
        switch (style)
        {
            case BoxStyle.Sharp:
                tl = '┌'; tr = '┐'; bl = '└'; br = '┘'; h = '─'; v = '│';
                break;
            case BoxStyle.Dashed:
                tl = '╭'; tr = '╮'; bl = '╰'; br = '╯'; h = '╌'; v = '╎';
                break;
            case BoxStyle.Double:
                tl = '╔'; tr = '╗'; bl = '╚'; br = '╝'; h = '═'; v = '║';
                break;
            default:
                tl = '╭'; tr = '╮'; bl = '╰'; br = '╯'; h = '─'; v = '│';
                break;
        }

        if (fill.HasValue) Fill(x, y, width, height, fill.Value);

        var borderStyle = fill.HasValue ? border.OnBg(fill.Value) : border;

        Set(x, y, tl, borderStyle);
        Set(x + width - 1, y, tr, borderStyle);
        Set(x, y + height - 1, bl, borderStyle);
        Set(x + width - 1, y + height - 1, br, borderStyle);

        for (var col = x + 1; col < x + width - 1; col++)
        {
            Set(col, y, h, borderStyle);
            Set(col, y + height - 1, h, borderStyle);
        }

        for (var row = y + 1; row < y + height - 1; row++)
        {
            Set(x, row, v, borderStyle);
            Set(x + width - 1, row, v, borderStyle);
        }
    }

    public static string Truncate(string text, int maxWidth)
    {
        if (maxWidth <= 0) return string.Empty;
        if (text.Length <= maxWidth) return text;
        if (maxWidth == 1) return "…";
        return string.Concat(text.AsSpan(0, maxWidth - 1), "…");
    }

    /// <summary>Returns the grid as plain rows, without any escape sequences.</summary>
    public string ToPlainText()
    {
        var builder = new StringBuilder(Width * Height + Height);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++) builder.Append(_cells[y * Width + x].Ch);
            builder.Append('\n');
        }

        return builder.ToString().TrimEnd('\n');
    }

    /// <summary>Serializes the grid and writes it out in one go.</summary>
    public void Flush()
    {
        _sb.Clear();
        _sb.Append(Term.Esc).Append("[H");

        for (var y = 0; y < Height; y++)
        {
            _sb.Append(Term.Esc).Append('[').Append(y + 1).Append(";1H");

            // Skip the very last cell of the last row: writing it makes some
            // terminals scroll the whole buffer up by one line.
            var lineWidth = y == Height - 1 ? Width - 1 : Width;

            var runStart = 0;
            while (runStart < lineWidth)
            {
                var style = _cells[y * Width + runStart].Style;
                var runEnd = runStart + 1;
                while (runEnd < lineWidth && _cells[y * Width + runEnd].Style.SameAttrs(style)) runEnd++;

                AppendStyle(style);
                for (var col = runStart; col < runEnd; col++) _sb.Append(_cells[y * Width + col].Ch);

                runStart = runEnd;
            }

            _sb.Append(Term.Esc).Append("[0m");
        }

        Term.Raw(_sb.ToString());
        Term.Flush();
    }

    private void AppendStyle(Sty style)
    {
        _sb.Append(Term.Esc).Append("[0");
        if (style.Bold) _sb.Append(";1");
        if (style.Dim) _sb.Append(";2");
        if (style.Italic) _sb.Append(";3");
        _sb.Append(";38;2;").Append(style.Fg.R).Append(';').Append(style.Fg.G).Append(';').Append(style.Fg.B);

        if (PaintBackground)
        {
            _sb.Append(";48;2;").Append(style.Bg.R).Append(';').Append(style.Bg.G).Append(';').Append(style.Bg.B);
        }
        else if (style.Bg != Theme.Bg)
        {
            // Keep highlighted surfaces visible even when the canvas is left to
            // the terminal's own background.
            _sb.Append(";48;2;").Append(style.Bg.R).Append(';').Append(style.Bg.G).Append(';').Append(style.Bg.B);
        }

        _sb.Append('m');
    }
}
