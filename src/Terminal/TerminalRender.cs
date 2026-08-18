using ClaudeLauncher.Tui;

namespace ClaudeLauncher.Terminal;

/// <summary>
/// Paints a terminal grid into the launcher's own buffer.
///
/// Claude emits 24-bit colour and never indexed colour, so there is no palette
/// to remap: its cells arrive as exact RGB and are drawn as sent. What is left
/// to us is what any terminal can do to a colour after the fact - fading an
/// unfocused pane so the focused one reads at a glance.
/// </summary>
/// <summary>What a search wants painted over the grid, and which hit is current.</summary>
public readonly record struct SearchHighlight(IReadOnlyList<TerminalMatch> Hits, int Current);

public static class TerminalRender
{
    /// <summary>How far an unfocused pane fades toward the panel background.</summary>
    private const double UnfocusedFade = 0.45;

    public static void Draw(ScreenBuffer buffer, TerminalScreen screen,
        int x, int y, int width, int height, Rgb fill, bool focused,
        SearchHighlight? search = null)
    {
        if (width <= 0 || height <= 0) return;

        var cols = Math.Min(width, screen.Cols);
        var rows = Math.Min(height, screen.Rows);
        var marks = Marks(screen, search, cols, rows);

        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
            {
                var cell = screen.CellAt(col, row);

                var fg = (cell.Attrs & CellAttrs.HasFg) != 0 ? cell.Fg : Theme.Text;
                var bg = (cell.Attrs & CellAttrs.HasBg) != 0 ? cell.Bg : fill;

                if ((cell.Attrs & CellAttrs.Inverse) != 0) (fg, bg) = (bg, fg);

                if (!focused)
                {
                    fg = Rgb.Lerp(fg, fill, UnfocusedFade);
                    if ((cell.Attrs & CellAttrs.HasBg) != 0) bg = Rgb.Lerp(bg, fill, UnfocusedFade);
                }

                var style = new Sty(fg, bg,
                    bold: (cell.Attrs & CellAttrs.Bold) != 0,
                    dim: (cell.Attrs & CellAttrs.Dim) != 0,
                    italic: (cell.Attrs & CellAttrs.Italic) != 0);

                // A hit has to survive whatever colour Claude painted underneath
                // it, so it replaces both halves of the style rather than tinting.
                var mark = marks is null ? (byte)0 : marks[row * cols + col];
                if (mark == Current) style = new Sty(Theme.Bg, Theme.Amber, bold: true);
                else if (mark == Other) style = new Sty(Theme.Text, Theme.BlueDeep);

                buffer.Set(x + col, y + row, cell.Ch == '\0' ? ' ' : cell.Ch, style);
            }
        }

        if (screen.IsScrolled)
        {
            DrawScrollMark(buffer, screen, x, y, width, fill);
            return;
        }

        if (!focused || !screen.CursorVisible) return;

        var cursorX = x + screen.CursorX;
        var cursorY = y + screen.CursorY;
        if (screen.CursorX >= cols || screen.CursorY >= rows) return;

        // The launcher owns the real cursor, so a tile's cursor is drawn as a
        // block rather than parked - otherwise several tiles would fight over it.
        var under = screen[screen.CursorX, screen.CursorY];
        var underFg = (under.Attrs & CellAttrs.HasFg) != 0 ? under.Fg : Theme.Text;
        buffer.Set(cursorX, cursorY, under.Ch == '\0' ? ' ' : under.Ch, new Sty(fill, underFg));
    }

    private const byte Other = 1;
    private const byte Current = 2;

    /// <summary>
    /// Paints the hit list onto a grid-shaped map once, rather than asking every
    /// cell whether any of up to four hundred matches covers it.
    /// </summary>
    private static byte[]? Marks(TerminalScreen screen, SearchHighlight? search, int cols, int rows)
    {
        if (search is null || search.Value.Hits.Count == 0 || cols <= 0 || rows <= 0) return null;

        var hits = search.Value.Hits;
        var marks = new byte[cols * rows];
        var top = screen.TopLine;

        for (var i = 0; i < hits.Count; i++)
        {
            var row = hits[i].Line - top;
            if (row < 0 || row >= rows) continue;

            var mark = i == search.Value.Current ? Current : Other;
            var end = Math.Min(cols, hits[i].Col + hits[i].Length);
            for (var col = Math.Max(0, hits[i].Col); col < end; col++) marks[row * cols + col] = mark;
        }

        return marks;
    }

    /// <summary>Narrower than this and the mark would eat the pane's own output.</summary>
    private const int MarkMinWidth = 20;

    private static void DrawScrollMark(ScreenBuffer buffer, TerminalScreen screen,
        int x, int y, int width, Rgb fill)
    {
        if (width < MarkMinWidth) return;

        var label = "↑ " + screen.ScrollOffset;
        if (label.Length > width) return;

        var style = new Sty(Theme.Muted, fill, bold: false, dim: true);
        var start = x + width - label.Length;
        for (var i = 0; i < label.Length; i++) buffer.Set(start + i, y, label[i], style);
    }
}
