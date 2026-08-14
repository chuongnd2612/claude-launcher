namespace ClaudeLauncher.Tui;

/// <summary>
/// Palette for the v1.5 redesign: near-black canvas, blue to violet accents,
/// soft slate borders. Colors are 24-bit and require a VT capable terminal
/// (Windows Terminal, WezTerm, Alacritty, VS Code terminal...).
/// </summary>
public static class Theme
{
    // Canvas
    public static readonly Rgb Bg = Rgb.Hex("#0B0E14");
    public static readonly Rgb BgSoft = Rgb.Hex("#10141C");
    public static readonly Rgb Panel = Rgb.Hex("#0F131B");
    public static readonly Rgb PanelSelected = Rgb.Hex("#111B2E");

    // Borders
    public static readonly Rgb Border = Rgb.Hex("#232A36");
    public static readonly Rgb BorderMuted = Rgb.Hex("#1B212B");
    public static readonly Rgb BorderAccent = Rgb.Hex("#3D7EFF");

    // Text
    public static readonly Rgb Text = Rgb.Hex("#E6EDF6");
    public static readonly Rgb TextSoft = Rgb.Hex("#B4BECC");
    public static readonly Rgb Muted = Rgb.Hex("#7C8798");
    public static readonly Rgb Dim = Rgb.Hex("#4E5766");

    // Accents
    public static readonly Rgb Blue = Rgb.Hex("#5AA0FF");
    public static readonly Rgb BlueDeep = Rgb.Hex("#3D7EFF");
    public static readonly Rgb Violet = Rgb.Hex("#C084FC");
    public static readonly Rgb VioletSoft = Rgb.Hex("#A78BFA");
    public static readonly Rgb Green = Rgb.Hex("#3FD07E");
    public static readonly Rgb Amber = Rgb.Hex("#E3B341");
    public static readonly Rgb Red = Rgb.Hex("#F87171");

    // Banner gradient (left to right)
    public static readonly Rgb GradientStart = Rgb.Hex("#4A9EFF");
    public static readonly Rgb GradientEnd = Rgb.Hex("#C77DFF");

    public static Rgb Gradient(double t) => Rgb.Lerp(GradientStart, GradientEnd, t);
}

/// <summary>A single cell style.</summary>
public readonly struct Sty
{
    public readonly Rgb Fg;
    public readonly Rgb Bg;
    public readonly bool Bold;
    public readonly bool Dim;
    public readonly bool Italic;

    public Sty(Rgb fg, Rgb bg, bool bold = false, bool dim = false, bool italic = false)
    {
        Fg = fg;
        Bg = bg;
        Bold = bold;
        Dim = dim;
        Italic = italic;
    }

    public Sty With(Rgb fg) => new(fg, Bg, Bold, Dim, Italic);

    public Sty OnBg(Rgb bg) => new(Fg, bg, Bold, Dim, Italic);

    public Sty AsBold() => new(Fg, Bg, true, Dim, Italic);

    public Sty AsItalic() => new(Fg, Bg, Bold, Dim, true);

    public bool SameAttrs(Sty other) =>
        Fg == other.Fg && Bg == other.Bg && Bold == other.Bold && Dim == other.Dim && Italic == other.Italic;
}
