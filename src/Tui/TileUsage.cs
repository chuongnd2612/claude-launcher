namespace ClaudeLauncher.Tui;

/// <summary>
/// The usage panel drawn over a tile's top-right corner.
///
/// The header band answers "how much of each account is gone" for the whole
/// launcher at once; this answers it for the account behind the tile you are
/// looking at, which is the one you are about to spend. It is painted over the
/// tile rather than notched into its border because the figures need four rows
/// - two windows and when each one rolls over - and a border has one.
///
/// Off by default for that reason: it covers whatever the tile was drawing.
/// </summary>
public static class TileUsage
{
    /// <summary>Cells in the drawer's gauge. Wider than the band's six - there is room here.</summary>
    private const int Cells = 10;

    /// <summary>Inner width: " 5h " + gauge + a space + "~100%" + a trailing space.</summary>
    private const int Inner = 4 + Cells + 1 + 5 + 1;

    public const int Width = Inner + 2;

    public const int Height = 6;

    /// <summary>
    /// Draws the drawer inside the given tile: the boxed form when it fits, a
    /// single line of both readings when it does not, and the readings alone
    /// when even that is too wide. Nothing at all below that.
    ///
    /// <paramref name="x"/> and <paramref name="y"/> are the tile's own top-left
    /// corner, borders included; the drawer places itself inside them.
    /// </summary>
    public static void Draw(ScreenBuffer buffer, int x, int y, int width, int height, UsageChip chip)
    {
        // Flush against the tile's right border rather than inset from it: the
        // column between the two would keep showing whatever the tile drew
        // there, which reads as the panel having leaked.
        var right = x + width - 2;

        if (width >= Width + 4 && height >= Height + 2)
        {
            Box(buffer, right - Width + 1, y + 1, chip);
            return;
        }

        if (height < 3) return;

        // The labels are what goes as the room does, never the readings: a pane
        // narrow enough to lose "5h" is still a pane you can spend an account in.
        var line = Parts(chip, labelled: true);
        var tight = Parts(chip, labelled: false);

        if (Length(line) <= width - 2) Write(buffer, right, y + 1, line);
        else if (Length(tight) <= width - 2) Write(buffer, right, y + 1, tight);
    }

    private static void Box(ScreenBuffer buffer, int x, int y, UsageChip chip)
    {
        var fill = Theme.BgSoft;
        buffer.Box(x, y, Width, Height, new Sty(Theme.Border, fill), BoxStyle.Rounded, fill);

        // The account leads, in its own colour: two tiles side by side under
        // different profiles are the case this whole panel exists for.
        var title = $" {chip.Icon} {chip.Label} ";
        buffer.WriteClipped(x + 2, y, title, Width - 4, new Sty(chip.Color, fill, bold: true));

        if (!chip.Known)
        {
            buffer.WriteClipped(x + 2, y + 2, "no reading yet", Inner - 2, new Sty(Theme.Dim, fill, italic: true));
            return;
        }

        Window(buffer, x + 1, y + 1, "5h", chip.Session, chip.SessionResetsUtc, chip.Stale, fill);
        Window(buffer, x + 1, y + 3, "wk", chip.Weekly, chip.WeeklyResetsUtc, chip.Stale, fill);
    }

    /// <summary>One window: its gauge and number, with when it rolls over under it.</summary>
    private static void Window(ScreenBuffer buffer, int x, int y, string name, int percent,
        DateTime? resetsUtc, bool stale, Rgb fill)
    {
        var at = buffer.Write(x, y, " " + name + " ", new Sty(Theme.Dim, fill));

        if (percent < 0)
        {
            buffer.Write(at, y, "—", new Sty(Theme.Dim, fill));
            return;
        }

        at = Widgets.Meter(buffer, at, y, percent, fill, Cells);
        buffer.Write(at, y, Widgets.Reading(percent, stale), new Sty(Widgets.Heat(percent), fill, bold: !stale));

        // Dim and on its own row: the number is the alarming part, and when it
        // comes back must not compete with it.
        var countdown = Widgets.Countdown(resetsUtc, DateTime.UtcNow);
        if (countdown.Length > 0)
            buffer.WriteClipped(x + 4, y + 1, "resets " + countdown, Inner - 4, new Sty(Theme.Dim, fill));
    }

    /// <summary>
    /// The one-line forms, as coloured pieces rather than a string: the two
    /// readings are heat-coloured and the labels are not, and the whole thing
    /// has to be measured before it can be placed flush right.
    /// </summary>
    private static List<(string Text, Rgb Color, bool Bold)> Parts(UsageChip chip, bool labelled)
    {
        var parts = new List<(string, Rgb, bool)> { (" ", Theme.Dim, false) };

        if (labelled) parts.Add(("5h ", Theme.Dim, false));
        parts.Add(Reading(chip.Session, chip.Stale));

        parts.Add((labelled ? " · " : "/", Theme.Dim, false));

        if (labelled) parts.Add(("wk ", Theme.Dim, false));
        parts.Add(Reading(chip.Weekly, chip.Stale));

        parts.Add((" ", Theme.Dim, false));
        return parts;
    }

    private static (string Text, Rgb Color, bool Bold) Reading(int percent, bool stale) => percent < 0
        ? ("—", Theme.Dim, false)
        : (Widgets.Reading(percent, stale), Widgets.Heat(percent), !stale);

    private static int Length(List<(string Text, Rgb Color, bool Bold)> parts)
    {
        var width = 0;
        foreach (var (text, _, _) in parts) width += text.Length;

        return width;
    }

    /// <summary>Writes the pieces so the last of them lands on <paramref name="right"/>.</summary>
    private static void Write(ScreenBuffer buffer, int right, int y,
        List<(string Text, Rgb Color, bool Bold)> parts)
    {
        var at = right - Length(parts) + 1;

        foreach (var (text, color, bold) in parts)
            at = buffer.Write(at, y, text, new Sty(color, Theme.BgSoft, bold: bold));
    }
}
