namespace ClaudeLauncher.Tui;

/// <summary>
/// Minimal 5x5 block font for the banner. Only the glyphs needed by the
/// launcher title are defined; anything else makes <see cref="TryRender"/>
/// fail so the caller can fall back to the compact one line banner.
/// </summary>
public static class BlockFont
{
    public const int GlyphHeight = 5;
    private const int GlyphWidth = 5;
    private const int Tracking = 1;   // blank columns between glyphs
    private const int WordGap = 3;    // blank columns for a space

    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['A'] = new[]
        {
            " ███ ",
            "█   █",
            "█████",
            "█   █",
            "█   █"
        },
        ['C'] = new[]
        {
            " ████",
            "█    ",
            "█    ",
            "█    ",
            " ████"
        },
        ['D'] = new[]
        {
            "████ ",
            "█   █",
            "█   █",
            "█   █",
            "████ "
        },
        ['E'] = new[]
        {
            "█████",
            "█    ",
            "████ ",
            "█    ",
            "█████"
        },
        ['H'] = new[]
        {
            "█   █",
            "█   █",
            "█████",
            "█   █",
            "█   █"
        },
        ['L'] = new[]
        {
            "█    ",
            "█    ",
            "█    ",
            "█    ",
            "█████"
        },
        ['N'] = new[]
        {
            "█   █",
            "██  █",
            "█ █ █",
            "█  ██",
            "█   █"
        },
        ['R'] = new[]
        {
            "████ ",
            "█   █",
            "████ ",
            "█  █ ",
            "█   █"
        },
        ['S'] = new[]
        {
            " ████",
            "█    ",
            " ███ ",
            "    █",
            "████ "
        },
        ['T'] = new[]
        {
            "█████",
            "  █  ",
            "  █  ",
            "  █  ",
            "  █  "
        },
        ['U'] = new[]
        {
            "█   █",
            "█   █",
            "█   █",
            "█   █",
            " ████"
        }
    };

    public static bool CanRender(string text)
    {
        foreach (var ch in text.ToUpperInvariant())
        {
            if (ch == ' ') continue;
            if (!Glyphs.ContainsKey(ch)) return false;
        }

        return true;
    }

    public static int MeasureWidth(string text)
    {
        var width = 0;
        var first = true;
        foreach (var ch in text.ToUpperInvariant())
        {
            if (ch == ' ')
            {
                width += WordGap;
                first = true;
                continue;
            }

            if (!first) width += Tracking;
            width += GlyphWidth;
            first = false;
        }

        return width;
    }

    /// <summary>
    /// Renders the text as 5 rows of block glyphs, coloring every filled cell
    /// with a horizontal gradient across the whole banner.
    /// </summary>
    public static bool TryRender(ScreenBuffer buffer, int x, int y, string text, Rgb from, Rgb to)
    {
        var upper = text.ToUpperInvariant();
        if (!CanRender(upper)) return false;

        var total = Math.Max(1, MeasureWidth(upper) - 1);
        var cursor = x;
        var first = true;

        foreach (var ch in upper)
        {
            if (ch == ' ')
            {
                cursor += WordGap;
                first = true;
                continue;
            }

            if (!first) cursor += Tracking;
            var glyph = Glyphs[ch];

            for (var row = 0; row < GlyphHeight; row++)
            {
                var line = glyph[row];
                for (var col = 0; col < line.Length; col++)
                {
                    if (line[col] == ' ') continue;
                    var px = cursor + col;
                    var t = (double)(px - x) / total;
                    var color = Rgb.Lerp(from, to, t);
                    buffer.Set(px, y + row, '█', new Sty(color, buffer.BgAt(px, y + row)));
                }
            }

            cursor += GlyphWidth;
            first = false;
        }

        return true;
    }
}
