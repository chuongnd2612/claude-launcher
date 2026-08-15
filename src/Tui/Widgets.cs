namespace ClaudeLauncher.Tui;

public enum StepState
{
    Upcoming,
    Active,
    Done
}

public readonly struct KeyHint
{
    public readonly string Key;
    public readonly string Label;

    public KeyHint(string key, string label)
    {
        Key = key;
        Label = label;
    }
}

/// <summary>Reusable pieces of the launcher chrome.</summary>
public static class Widgets
{
    public const string LogoMark = "✦";

    public const string Author = "Andrew Nguyen";

    public const string AuthorHandle = "chuongnd2612";

    public static int Margin(ScreenBuffer buffer) => buffer.Width >= 110 ? 4 : 2;

    /// <summary>
    /// Draws logo, subtitle, the three step badges and the divider.
    /// Returns the first free content row.
    /// </summary>
    public static int Chrome(ScreenBuffer buffer, int activeStep)
    {
        const string title = "CLAUDE LAUNCHER";
        const string subtitle = "Your intelligent CLI companion";

        var y = 1;
        var big = buffer.Height >= 30 && buffer.Width >= BlockFont.MeasureWidth(title) + 12;

        if (big)
        {
            var bannerWidth = BlockFont.MeasureWidth(title);
            var markWidth = 4;
            var startX = Math.Max(1, (buffer.Width - (bannerWidth + markWidth)) / 2);

            buffer.Write(startX, y + 2, LogoMark, new Sty(Theme.GradientStart, Theme.Bg, bold: true));
            if (!BlockFont.TryRender(buffer, startX + markWidth, y, title, Theme.GradientStart, Theme.GradientEnd))
            {
                big = false;
            }
            else
            {
                y += BlockFont.GlyphHeight + 1;
            }
        }

        if (!big)
        {
            var spaced = Spread(title);
            var startX = Math.Max(0, (buffer.Width - (spaced.Length + 2)) / 2);
            buffer.Write(startX, y, LogoMark + " ", new Sty(Theme.GradientStart, Theme.Bg, bold: true));
            GradientText(buffer, startX + 2, y, spaced, Theme.GradientStart, Theme.GradientEnd, bold: true);
            y += 1;
        }

        buffer.WriteCentered(y, subtitle, new Sty(Theme.Muted, Theme.Bg));

        // The row under the subtitle was blank filler, so the byline is free.
        // Tall windows can spare one more row to keep it off the step badges.
        Credit(buffer, y + 1);
        y += buffer.Height >= 32 ? 3 : 2;

        y = Steps(buffer, y, activeStep);
        y += 1;

        var margin = Margin(buffer);
        buffer.HLine(margin, y, Math.Max(0, buffer.Width - margin * 2), '─', new Sty(Theme.BorderMuted, Theme.Bg));
        return y + 2;
    }

    /// <summary>
    /// One-line header for screens that need their rows for content: logo left,
    /// byline right, rule under it. Returns the first free row.
    /// </summary>
    public static int CompactChrome(ScreenBuffer buffer, int y = 1)
    {
        var margin = Margin(buffer);
        var x = buffer.Write(margin, y, LogoMark + " ", new Sty(Theme.GradientStart, Theme.Bg, bold: true));
        GradientText(buffer, x, y, "CLAUDE LAUNCHER", Theme.GradientStart, Theme.GradientEnd, bold: true);

        var credit = $"{Author} - {AuthorHandle}";
        if (margin + 20 + credit.Length < buffer.Width - margin)
            buffer.WriteRight(buffer.Width - margin - 1, y, credit, new Sty(Theme.Dim, Theme.Bg));

        buffer.HLine(margin, y + 1, Math.Max(0, buffer.Width - margin * 2), '─',
            new Sty(Theme.BorderMuted, Theme.Bg));

        return y + 3;
    }

    /// <summary>Author byline, centered under the subtitle.</summary>
    private static void Credit(ScreenBuffer buffer, int y)
    {
        const string separator = " - ";
        var width = Author.Length + separator.Length + AuthorHandle.Length;

        // Dropped rather than wrapped when the window is too narrow for it.
        if (width + 2 > buffer.Width) return;

        var x = Math.Max(0, (buffer.Width - width) / 2);
        x = buffer.Write(x, y, Author, new Sty(Theme.Muted, Theme.Bg));
        x = buffer.Write(x, y, separator, new Sty(Theme.Dim, Theme.Bg));
        buffer.Write(x, y, AuthorHandle, new Sty(Theme.Dim, Theme.Bg));
    }

    /// <summary>Renders the wizard progress. Returns the row after the block.</summary>
    private static int Steps(ScreenBuffer buffer, int y, int activeStep)
    {
        string[] labels = { "Profile", "Project", "Session" };
        var compact = buffer.Height < 28;

        if (compact)
        {
            var parts = new List<string>();
            for (var i = 0; i < labels.Length; i++)
            {
                var marker = i < activeStep ? "✓" : i == activeStep ? "●" : "○";
                parts.Add($"{marker} {labels[i]}");
            }

            var separator = "  ─────  ";
            var total = parts.Sum(p => p.Length) + separator.Length * (parts.Count - 1);
            var x = Math.Max(0, (buffer.Width - total) / 2);

            for (var i = 0; i < parts.Count; i++)
            {
                var state = i < activeStep ? StepState.Done : i == activeStep ? StepState.Active : StepState.Upcoming;
                var style = state switch
                {
                    StepState.Active => new Sty(Theme.Blue, Theme.Bg, bold: true),
                    StepState.Done => new Sty(Theme.VioletSoft, Theme.Bg),
                    _ => new Sty(Theme.Dim, Theme.Bg)
                };

                x = buffer.Write(x, y, parts[i], style);
                if (i < parts.Count - 1) x = buffer.Write(x, y, separator, new Sty(Theme.BorderMuted, Theme.Bg));
            }

            return y + 1;
        }

        const int badgeWidth = 5;
        const int badgeHeight = 3;
        var gap = buffer.Width >= 96 ? 14 : 9;
        var blockWidth = labels.Length * badgeWidth + (labels.Length - 1) * gap;
        var startX = Math.Max(0, (buffer.Width - blockWidth) / 2);

        for (var i = 0; i < labels.Length; i++)
        {
            var x = startX + i * (badgeWidth + gap);
            var state = i < activeStep ? StepState.Done : i == activeStep ? StepState.Active : StepState.Upcoming;
            var glyph = state == StepState.Done ? '✓' : (char)('1' + i);

            switch (state)
            {
                case StepState.Active:
                    buffer.Box(x, y, badgeWidth, badgeHeight, new Sty(Theme.BlueDeep, Theme.BlueDeep), BoxStyle.Rounded, Theme.BlueDeep);
                    buffer.Set(x + 2, y + 1, glyph, new Sty(Rgb.Hex("#FFFFFF"), Theme.BlueDeep, bold: true));
                    break;
                case StepState.Done:
                    buffer.Box(x, y, badgeWidth, badgeHeight, new Sty(Theme.VioletSoft, Theme.Bg));
                    buffer.Set(x + 2, y + 1, glyph, new Sty(Theme.VioletSoft, Theme.Bg, bold: true));
                    break;
                default:
                    buffer.Box(x, y, badgeWidth, badgeHeight, new Sty(Theme.Border, Theme.Bg));
                    buffer.Set(x + 2, y + 1, glyph, new Sty(Theme.Dim, Theme.Bg));
                    break;
            }

            var labelStyle = state switch
            {
                StepState.Active => new Sty(Theme.Text, Theme.Bg, bold: true),
                StepState.Done => new Sty(Theme.TextSoft, Theme.Bg),
                _ => new Sty(Theme.Dim, Theme.Bg)
            };

            var label = labels[i];
            buffer.Write(x + badgeWidth / 2 - label.Length / 2, y + badgeHeight, label, labelStyle);

            if (i < labels.Length - 1)
            {
                var lineStyle = i < activeStep ? new Sty(Theme.VioletSoft, Theme.Bg) : new Sty(Theme.BorderMuted, Theme.Bg);
                buffer.HLine(x + badgeWidth + 1, y + 1, gap - 2, '─', lineStyle);
            }
        }

        return y + badgeHeight + 1;
    }

    public static void SectionTitle(ScreenBuffer buffer, int y, string text)
    {
        buffer.Write(Margin(buffer), y, text, new Sty(Theme.VioletSoft, Theme.Bg, bold: true));
    }

    /// <summary>Section title with a highlighted breadcrumb prefix, e.g. "Work / Select a project".</summary>
    public static void SectionTitle(ScreenBuffer buffer, int y, string breadcrumb, string text)
    {
        var x = Margin(buffer);
        x = buffer.Write(x, y, breadcrumb, new Sty(Theme.Blue, Theme.Bg, bold: true));
        x = buffer.Write(x, y, "  /  ", new Sty(Theme.Dim, Theme.Bg));
        buffer.Write(x, y, text, new Sty(Theme.VioletSoft, Theme.Bg, bold: true));
    }

    public static void GradientText(ScreenBuffer buffer, int x, int y, string text, Rgb from, Rgb to, bool bold = false)
    {
        var total = Math.Max(1, text.Length - 1);
        for (var i = 0; i < text.Length; i++)
        {
            var color = Rgb.Lerp(from, to, (double)i / total);
            buffer.Set(x + i, y, text[i], new Sty(color, buffer.BgAt(x + i, y), bold));
        }
    }

    public static string Spread(string text)
    {
        var chars = new List<char>();
        foreach (var ch in text)
        {
            chars.Add(ch);
            chars.Add(' ');
        }

        if (chars.Count > 0) chars.RemoveAt(chars.Count - 1);
        return new string(chars.ToArray());
    }

    /// <summary>A rounded icon badge, 5x3, used inside cards.</summary>
    public static void IconBadge(ScreenBuffer buffer, int x, int y, string glyph, Rgb color, Rgb bg, bool filled)
    {
        if (filled)
        {
            buffer.Box(x, y, 5, 3, new Sty(color, color), BoxStyle.Rounded, color);
            buffer.Write(x + 2 - (glyph.Length - 1) / 2, y + 1, glyph, new Sty(Rgb.Hex("#FFFFFF"), color, bold: true));
        }
        else
        {
            buffer.Box(x, y, 5, 3, new Sty(color, bg), BoxStyle.Rounded, bg);
            buffer.Write(x + 2 - (glyph.Length - 1) / 2, y + 1, glyph, new Sty(color, bg, bold: true));
        }
    }

    /// <summary>Panel surface used for cards and boxes.</summary>
    public static void Panel(ScreenBuffer buffer, int x, int y, int width, int height, bool selected, bool dashed = false)
    {
        var border = selected ? Theme.BorderAccent : Theme.Border;
        var fill = selected ? Theme.PanelSelected : Theme.Panel;
        var style = dashed ? BoxStyle.Dashed : BoxStyle.Rounded;
        buffer.Box(x, y, width, height, new Sty(border, fill), style, fill);
    }

    public static void TitledBox(ScreenBuffer buffer, int x, int y, int width, int height, string title, Rgb titleColor)
    {
        buffer.Box(x, y, width, height, new Sty(Theme.Border, Theme.Panel), BoxStyle.Rounded, Theme.Panel);
        var label = $" {title} ";
        buffer.Write(x + 2, y, label, new Sty(titleColor, Theme.Panel, bold: true));
    }

    /// <summary>Footer key hints inside a rounded bar pinned to the bottom.</summary>
    public static void Footer(ScreenBuffer buffer, IReadOnlyList<KeyHint> hints)
    {
        var margin = Margin(buffer);
        var width = buffer.Width - margin * 2;
        var y = buffer.Height - 3;
        if (y < 3) return;

        buffer.Box(margin, y, width, 3, new Sty(Theme.BorderMuted, Theme.BgSoft), BoxStyle.Rounded, Theme.BgSoft);

        // Tighten the gaps before letting a long hint row spill out of the bar.
        var text = hints.Sum(h => h.Key.Length + 1 + h.Label.Length);
        var gaps = Math.Max(0, hints.Count - 1);
        var room = width - 4;
        var spacing = 4;
        while (spacing > 1 && gaps > 0 && text + spacing * gaps > room) spacing--;

        var total = text + spacing * gaps;
        var x = margin + Math.Max(2, (width - total) / 2);
        var limit = margin + width - 2;

        foreach (var hint in hints)
        {
            if (x + hint.Key.Length + 1 + hint.Label.Length > limit) break;

            x = buffer.Write(x, y + 1, hint.Key, new Sty(Theme.Blue, Theme.BgSoft, bold: true));
            x = buffer.Write(x, y + 1, " ", new Sty(Theme.Muted, Theme.BgSoft));
            x = buffer.Write(x, y + 1, hint.Label, new Sty(Theme.Muted, Theme.BgSoft));
            x += spacing;
        }
    }

    /// <summary>Tips box; returns the height it occupied (0 when it did not fit).</summary>
    public static int Tips(ScreenBuffer buffer, int y, IReadOnlyList<string> tips)
    {
        var margin = Margin(buffer);
        var width = buffer.Width - margin * 2;
        var height = tips.Count + 2;
        var footerTop = buffer.Height - 3;
        if (y + height > footerTop - 1 || width < 20) return 0;

        buffer.Box(margin, y, width, height, new Sty(Theme.BorderMuted, Theme.Panel), BoxStyle.Rounded, Theme.Panel);
        buffer.Write(margin + 2, y, " ◈ Tips ", new Sty(Theme.Amber, Theme.Panel, bold: true));

        for (var i = 0; i < tips.Count; i++)
        {
            var row = y + 1 + i;
            buffer.Write(margin + 2, row, "•", new Sty(Theme.VioletSoft, Theme.Panel));
            buffer.WriteClipped(margin + 4, row, tips[i], width - 6, new Sty(Theme.Muted, Theme.Panel));
        }

        return height;
    }
}
