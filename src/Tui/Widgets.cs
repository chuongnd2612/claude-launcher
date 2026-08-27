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

/// <summary>
/// One account's slice of the usage band: how much of its plan is gone.
///
/// A percentage, not a count. A count answers "how much did I do", which is only
/// a share of anything if you already know the ceiling - and the ceiling is the
/// thing Claude records under cachedUsageUtilization and nowhere else.
/// </summary>
public readonly struct UsageChip
{
    public readonly string Icon;
    public readonly string Label;
    public readonly Rgb Color;

    /// <summary>The five-hour session window, 0-100, or -1 when not known.</summary>
    public readonly int Session;

    /// <summary>The weekly window, 0-100, or -1 when not known.</summary>
    public readonly int Weekly;

    /// <summary>
    /// When each window rolls over, or null when Claude recorded no reset for it.
    ///
    /// A percentage without this answers half the question: 91% of the week gone
    /// is a reason to stop if the week turns over on Friday and hardly one if it
    /// turns over tonight.
    /// </summary>
    public readonly DateTime? SessionResetsUtc;

    public readonly DateTime? WeeklyResetsUtc;

    /// <summary>The cache is older than the session window it describes.</summary>
    public readonly bool Stale;

    public UsageChip(string icon, string label, Rgb color, int session, int weekly, bool stale,
        DateTime? sessionResetsUtc = null, DateTime? weeklyResetsUtc = null)
    {
        Icon = icon;
        Label = label;
        Color = color;
        Session = session;
        Weekly = weekly;
        Stale = stale;
        SessionResetsUtc = sessionResetsUtc;
        WeeklyResetsUtc = weeklyResetsUtc;
    }

    public bool Known => Session >= 0 || Weekly >= 0;
}

/// <summary>Reusable pieces of the launcher chrome.</summary>
public static class Widgets
{
    public const string LogoMark = "✦";

    public const string Author = "Andrew Nguyen";

    public const string AuthorHandle = "chuongnd2612";

    /// <summary>Stamped from the assembly at startup and shown on every footer.</summary>
    public static string Version { get; set; } = string.Empty;

    /// <summary>
    /// Today's sessions per account, drawn into the rule under the header.
    ///
    /// A static for the same reason <see cref="Version"/> is one: every screen
    /// draws the chrome, and threading this through both chrome helpers would
    /// mean changing all nineteen call sites to say the same thing. The app sets
    /// it off the render path; null means draw a plain rule, which is also what
    /// --selftest gets, so a render check stays free of live numbers.
    /// </summary>
    public static IReadOnlyList<UsageChip>? Usage { get; set; }

    /// <summary>True while the figures are being read again, so the band says so.</summary>
    public static bool UsageRefreshing { get; set; }

    /// <summary>
    /// Where the band's own label ended up, so a click on it can ask for a
    /// refresh. Set as the band draws and cleared when it does not, because the
    /// row it lands on is the header's business and differs per screen.
    /// </summary>
    public static (int Y, int From, int To)? UsageButton { get; private set; }

    /// <summary>True when a click at these cells landed on the band's label.</summary>
    public static bool OnUsageButton(int x, int y) =>
        UsageButton is { } hit && y == hit.Y && x >= hit.From && x <= hit.To;

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

        UsageRule(buffer, y);
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

        // The byline gives way to the band: a number that changes earns the room
        // over one that never does.
        var credit = $"{Author} - {AuthorHandle}";
        if (Usage is null && margin + 20 + credit.Length < buffer.Width - margin)
            buffer.WriteRight(buffer.Width - margin - 1, y, credit, new Sty(Theme.Dim, Theme.Bg));

        UsageRule(buffer, y + 1);
        return y + 3;
    }

    /// <summary>
    /// The rule under the header, with the usage band written into it - the way
    /// TitledBox lays a label over a box edge.
    ///
    /// Into the rule rather than onto a row of its own, because a row is the one
    /// thing the small sizes have none of: a Chrome screen at 80x24 has twelve
    /// content rows in total, and the band is not worth one of them.
    /// </summary>
    public static void UsageRule(ScreenBuffer buffer, int y)
    {
        var margin = Margin(buffer);
        var width = Math.Max(0, buffer.Width - margin * 2);

        buffer.HLine(margin, y, width, '─', new Sty(Theme.BorderMuted, Theme.Bg));

        UsageButton = null;

        var chips = Usage;
        if (chips is null || chips.Count == 0 || width < 22) return;

        // Room for the band, less the rule stub either side of it.
        var room = width - 8;

        // Worked out once per frame rather than once per measurement: a countdown
        // shortens as the clock moves, and a shape measured against one string
        // and drawn with another would overrun the rule.
        var resets = Countdowns(chips);

        // Most to least informative, first that fits wins. The percentage is the
        // last thing to go, because it is the only part that answers the question.
        var shape = Widest(chips, resets, room);
        if (shape == Shape.None)
        {
            Worst(buffer, margin, y, room, chips);
            return;
        }

        var x = margin + 3;
        buffer.Write(x++, y, " ", new Sty(Theme.BorderMuted, Theme.Bg));
        x = Label(buffer, x, y);

        for (var i = 0; i < chips.Count; i++)
        {
            var chip = chips[i];

            // A bar between accounts and plain spaces inside one. The old dot
            // separated both alike, so the band read as a single run of numbers
            // and finding one account in it meant reading all of them.
            x = buffer.Write(x, y, Divider, new Sty(Theme.Border, Theme.Bg));
            x = buffer.Write(x, y, chip.Icon, new Sty(chip.Color, Theme.Bg, bold: true));

            // The name in the account's own colour, so the group it heads belongs
            // to it: the icon used to be the only coloured cell in the group.
            if (Names(shape))
                x = buffer.Write(x, y, " " + chip.Label, new Sty(chip.Color, Theme.Bg));

            if (!chip.Known)
            {
                x = buffer.Write(x, y, " —", new Sty(Theme.Dim, Theme.Bg));
                continue;
            }

            // Both windows, always: the five-hour one says whether to keep going
            // now, the weekly one whether to keep going this week, and an account
            // can be comfortable on one while nearly out on the other.
            x = Window(buffer, x, y, shape, "5h", chip.Session, resets[i].Session, chip.Stale);
            x = Window(buffer, x, y, shape, "7d", chip.Weekly, resets[i].Weekly, stale: false);
        }

        buffer.Write(x, y, " ", new Sty(Theme.BorderMuted, Theme.Bg));
    }

    /// <summary>What goes between one account's group and the next.</summary>
    private const string Divider = " │ ";

    /// <summary>
    /// The word the band starts with, which doubles as its refresh button: the
    /// key does the same thing, but a figure you are looking at is the moment you
    /// want it again, and reaching for a chord to get it is a poor answer.
    ///
    /// The arrow is what makes it look like one - the click target was already
    /// here and nothing on screen said so. It reads "usage…" while the read is in
    /// flight, so a click that found nothing new is still visibly a click that
    /// did something.
    /// </summary>
    private const int LabelWidth = 8;

    private static int Label(ScreenBuffer buffer, int x, int y)
    {
        var busy = UsageRefreshing;
        var start = x;

        x = buffer.Write(x, y, "↻ ", new Sty(busy ? Theme.Blue : Theme.BlueDeep, Theme.Bg, bold: busy));
        var end = buffer.Write(x, y, busy ? "usage…" : "usage",
            new Sty(busy ? Theme.TextSoft : Theme.Muted, Theme.Bg));

        UsageButton = (y, start, end - 1);
        return end;
    }

    /// <summary>One window's slice: its name, its gauge, its number, its clock.</summary>
    private static int Window(ScreenBuffer buffer, int x, int y, Shape shape,
        string name, int percent, string countdown, bool stale)
    {
        if (percent < 0)
        {
            if (Names(shape)) x = buffer.Write(x, y, " " + name, new Sty(Theme.Dim, Theme.Bg));
            return buffer.Write(x, y, " —", new Sty(Theme.Dim, Theme.Bg));
        }

        if (Names(shape)) x = buffer.Write(x, y, " " + name, new Sty(Theme.Dim, Theme.Bg));

        x = buffer.Write(x, y, " ", new Sty(Theme.Dim, Theme.Bg));
        if (HasMeter(shape)) x = Meter(buffer, x, y, percent);

        // A stale reading keeps its colour and loses its weight: muting it would
        // take the warning off a number that may still be the one that matters.
        // The tilde is what says "and this is an old answer".
        x = buffer.Write(x, y, Reading(percent, stale),
            new Sty(Heat(percent), Theme.Bg, bold: !stale));

        // Dim and last. A window nearly gone is the alarming part; when it comes
        // back is what you read next, and it must not compete with the number.
        if (HasReset(shape) && countdown.Length > 0)
            x = buffer.Write(x, y, " " + countdown, new Sty(Theme.Dim, Theme.Bg));

        return x;
    }

    /// <summary>
    /// How much of a slice the band can afford, widest first.
    ///
    /// The clock outranks the gauge deliberately: the gauge only draws the
    /// percentage a second time, while the countdown is the one part of a slice
    /// that says something the number cannot.
    /// </summary>
    private enum Shape
    {
        None,

        /// <summary>Icon and percentage only.</summary>
        Tight,

        /// <summary>Labels and the window marker, nothing else.</summary>
        Plain,

        /// <summary>Labels and the gauge, no time to reset.</summary>
        Gauge,

        /// <summary>Labels and time to reset, no gauge.</summary>
        Clock,

        /// <summary>Everything.</summary>
        Full
    }

    private static bool Names(Shape shape) => shape != Shape.Tight;

    private static bool HasMeter(Shape shape) => shape is Shape.Gauge or Shape.Full;

    private static bool HasReset(Shape shape) => shape is Shape.Clock or Shape.Full;

    /// <summary>
    /// Six. Two of these are drawn per account now, so eight was too wide - but
    /// four could not tell 3% from 28%, which are a rounding apart at that size
    /// and both land on one filled cell. Six separates them.
    /// </summary>
    private const int MeterCells = 6;

    /// <summary>
    /// The gauge itself: filled cells coloured by how bad the number is, empty
    /// ones left dim. The colour belongs to the reading, not to the account -
    /// painting the whole bar in the account's colour was the thing that made an
    /// earlier version unreadable, because a coloured blob says nothing about
    /// magnitude.
    /// </summary>
    private static int Meter(ScreenBuffer buffer, int x, int y, int percent)
    {
        var filled = (int)Math.Round(Math.Clamp(percent, 0, 100) / 100.0 * MeterCells);
        if (percent > 0 && filled == 0) filled = 1;

        var heat = Heat(percent);

        for (var i = 0; i < MeterCells; i++)
        {
            var on = i < filled;
            buffer.Set(x + i, y, on ? '█' : '░', new Sty(on ? heat : Theme.Dim, Theme.Bg));
        }

        // Written, not skipped: an unwritten cell leaves the rule showing
        // through and the gauge reads as if it were joined to the number.
        buffer.Set(x + MeterCells, y, ' ', new Sty(Theme.Dim, Theme.Bg));
        return x + MeterCells + 1;
    }

    /// <summary>Green while there is room, amber when it is going, red near the end.</summary>
    private static Rgb Heat(int percent) => percent >= 85 ? Theme.Red
        : percent >= 60 ? Theme.Amber
        : Theme.Green;

    /// <summary>
    /// Every window's time to reset, in the exact form the band prints it, so
    /// the measuring and the drawing cannot disagree.
    /// </summary>
    private static (string Session, string Weekly)[] Countdowns(IReadOnlyList<UsageChip> chips)
    {
        var now = DateTime.UtcNow;
        var rows = new (string, string)[chips.Count];

        for (var i = 0; i < chips.Count; i++)
            rows[i] = (Countdown(chips[i].SessionResetsUtc, now),
                Countdown(chips[i].WeeklyResetsUtc, now));

        return rows;
    }

    /// <summary>
    /// "→2h11m": what is left of the window, arrow first so it reads as a
    /// destination rather than as a second quantity beside the percentage.
    /// Minutes and up - a figure that ticks every second belongs on the usage
    /// screen, not in chrome that is drawn everywhere.
    /// </summary>
    private static string Countdown(DateTime? resetsUtc, DateTime now)
    {
        if (resetsUtc is not { } at) return string.Empty;

        var left = at - now;
        if (left <= TimeSpan.Zero) return "→due";
        if (left.TotalHours < 1) return $"→{Math.Max(1, (int)left.TotalMinutes)}m";

        // The smaller unit is dropped when it is zero: "4d00h" spends two cells
        // on nothing, and the band has none to spare.
        if (left.TotalDays < 1)
            return left.Minutes == 0
                ? $"→{(int)left.TotalHours}h"
                : $"→{(int)left.TotalHours}h{left.Minutes:00}m";

        return left.Hours == 0
            ? $"→{(int)left.TotalDays}d"
            : $"→{(int)left.TotalDays}d{left.Hours:00}h";
    }

    /// <summary>The most detailed shape that fits, or None when even the tight one does not.</summary>
    private static Shape Widest(IReadOnlyList<UsageChip> chips,
        (string Session, string Weekly)[] resets, int room)
    {
        foreach (var shape in new[] { Shape.Full, Shape.Clock, Shape.Gauge, Shape.Plain, Shape.Tight })
        {
            if (Fits(chips, resets, room, shape)) return shape;
        }

        return Shape.None;
    }

    private static bool Fits(IReadOnlyList<UsageChip> chips,
        (string Session, string Weekly)[] resets, int room, Shape shape)
    {
        // The label's widest form, marker included, so the band does not have to
        // give up a meter for the one cell that appears while it is refreshing.
        var width = LabelWidth;

        for (var i = 0; i < chips.Count; i++)
        {
            var chip = chips[i];

            width += Divider.Length + chip.Icon.Length;
            if (Names(shape)) width += 1 + chip.Label.Length;

            if (!chip.Known)
            {
                width += 2;
                continue;
            }

            width += Slice(shape, chip.Session, resets[i].Session, chip.Stale)
                     + Slice(shape, chip.Weekly, resets[i].Weekly, false);
        }

        return width + 2 <= room;
    }

    private static int Slice(Shape shape, int percent, string countdown, bool stale)
    {
        var name = Names(shape) ? 3 : 0;                                    // " 5h"
        if (percent < 0) return name + 2;

        var meter = HasMeter(shape) ? MeterCells + 1 : 0;
        var reset = HasReset(shape) && countdown.Length > 0 ? 1 + countdown.Length : 0;
        return name + 1 + meter + Reading(percent, stale).Length + reset;
    }

    private static string Reading(int percent, bool stale) =>
        (stale ? "~" : string.Empty) + percent + "%";

    /// <summary>
    /// The fallback when nothing else fits: the account closest to its limit,
    /// because that is the one number worth the space.
    /// </summary>
    private static void Worst(ScreenBuffer buffer, int margin, int y, int room,
        IReadOnlyList<UsageChip> chips)
    {
        var worst = -1;
        var stale = false;

        // Whichever number across every account and both windows is closest to
        // its limit: with room for one figure, that is the one worth having.
        foreach (var chip in chips)
        {
            if (chip.Session > worst)
            {
                worst = chip.Session;
                stale = chip.Stale;
            }

            if (chip.Weekly > worst)
            {
                worst = chip.Weekly;
                stale = false;
            }
        }

        if (worst < 0) return;

        var reading = Reading(worst, stale);
        if (LabelWidth + 1 + reading.Length + 2 > room) return;

        var at = margin + 3;
        buffer.Write(at, y, " ", new Sty(Theme.BorderMuted, Theme.Bg));

        var x = buffer.Write(Label(buffer, at + 1, y), y, " ", new Sty(Theme.Muted, Theme.Bg));
        x = buffer.Write(x, y, reading, new Sty(Heat(worst), Theme.Bg, bold: !stale));
        buffer.Write(x, y, " ", new Sty(Theme.BorderMuted, Theme.Bg));
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

    /// <summary>
    /// Footer key hints inside a rounded bar pinned to the bottom.
    ///
    /// A hint that will not fit is dropped rather than squeezed, and the ones
    /// dropped are the last - so <paramref name="pinned"/> is measured out of the
    /// room first and written from the right. That is how the way in to the full
    /// key list stays on screen at eighty columns, where it would otherwise be
    /// the first thing to go.
    /// </summary>
    public static void Footer(ScreenBuffer buffer, IReadOnlyList<KeyHint> hints, KeyHint? pinned = null)
    {
        var margin = Margin(buffer);
        var width = buffer.Width - margin * 2;
        var y = buffer.Height - 3;
        if (y < 3) return;

        buffer.Box(margin, y, width, 3, new Sty(Theme.BorderMuted, Theme.BgSoft), BoxStyle.Rounded, Theme.BgSoft);

        // Claimed before anything is laid out, plus a gap to sit behind.
        var pinnedWidth = pinned is { } pin ? pin.Key.Length + 1 + pin.Label.Length : 0;
        var reserved = pinnedWidth > 0 ? pinnedWidth + 4 : 0;

        // Tighten the gaps before letting a long hint row spill out of the bar.
        var text = hints.Sum(h => h.Key.Length + 1 + h.Label.Length);
        var gaps = Math.Max(0, hints.Count - 1);
        var room = width - 4 - reserved;
        var spacing = 4;
        while (spacing > 1 && gaps > 0 && text + spacing * gaps > room) spacing--;

        var total = text + spacing * gaps;
        var x = margin + Math.Max(2, (width - reserved - total) / 2);
        var limit = margin + width - 2 - reserved;

        var right = margin + width - 2;

        if (pinnedWidth > 0)
        {
            var at = right - pinnedWidth;
            at = buffer.Write(at, y + 1, pinned!.Value.Key, new Sty(Theme.Amber, Theme.BgSoft, bold: true));
            at = buffer.Write(at, y + 1, " ", new Sty(Theme.Muted, Theme.BgSoft));
            buffer.Write(at, y + 1, pinned.Value.Label, new Sty(Theme.Muted, Theme.BgSoft));
            right -= pinnedWidth + 2;
        }

        // Version sits at the right end of the bar, and only when it cannot
        // crowd the hints - a hint that vanishes matters more than the number.
        if (Version.Length > 0)
        {
            var label = "v" + Version;
            var free = right - 1 - (x + total);
            if (free >= label.Length + 3)
                buffer.WriteRight(right - 1, y + 1, label, new Sty(Theme.Dim, Theme.BgSoft));
        }

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
