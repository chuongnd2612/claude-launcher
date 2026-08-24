using System.Text;
using ClaudeLauncher.Terminal;
using ClaudeLauncher.Tui;

namespace ClaudeLauncher.Screens;

/// <summary>
/// A terminal tile driven by a recorded escape sequence instead of a live child.
///
/// This is the only way the parser, the grid and the renderer can be checked
/// without spawning Claude: a pseudo console cannot run under CI, but replayed
/// bytes are deterministic and exercise the same path a real tile takes.
/// </summary>
public sealed class TerminalPreviewScreen : ScreenBase
{
    private const int Cols = 62;
    private const int Rows = 11;

    private readonly TerminalScreen _screen = new(Cols, Rows);

    public TerminalPreviewScreen(App app) : base(app)
    {
        new VtParser().Feed(Encoding.UTF8.GetBytes(Fixture()), _screen);
    }

    /// <summary>The same replayed grid, for any screen that needs one without a child.</summary>
    public static TerminalScreen Demo()
    {
        var screen = new TerminalScreen(Cols, Rows);
        new VtParser().Feed(Encoding.UTF8.GetBytes(Fixture()), screen);
        return screen;
    }

    /// <summary>
    /// The sequences a real capture of Claude showed it uses, in the same
    /// proportions: absolute addressing, truecolor SGR, cursor-forward instead
    /// of spaces, erase-in-line, and box drawing.
    /// </summary>
    private static string Fixture()
    {
        var esc = ((char)27).ToString();
        var text = new StringBuilder();

        void At(int row, int col) => text.Append(esc).Append('[').Append(row).Append(';').Append(col).Append('H');
        void Fg(int r, int g, int b) => text.Append(esc).Append("[38;2;").Append(r).Append(';').Append(g).Append(';').Append(b).Append('m');
        void Reset() => text.Append(esc).Append("[m");
        void Forward(int n) => text.Append(esc).Append('[').Append(n).Append('C');

        text.Append(esc).Append("[2J");

        At(1, 1);
        Fg(90, 160, 255);
        text.Append(new string('─', Cols));

        At(2, 3);
        Fg(230, 237, 246);
        text.Append(esc).Append("[1m").Append("Usage");
        Reset();
        Forward(4);
        Fg(124, 135, 152);
        text.Append("current session");

        At(4, 3);
        Fg(230, 237, 246);
        text.Append("Tokens");
        Forward(8);
        Fg(63, 208, 126);
        text.Append("38,412");
        Forward(3);
        Fg(124, 135, 152);
        text.Append("in");

        At(5, 3);
        Fg(230, 237, 246);
        text.Append("Context");
        Forward(7);
        Fg(227, 179, 65);
        text.Append("61%");
        Forward(6);
        Fg(124, 135, 152);
        text.Append("of 200K");

        At(7, 3);
        Fg(192, 132, 252);
        text.Append(new string('█', 18));
        Fg(78, 87, 102);
        text.Append(new string('░', 12));

        At(9, 3);
        Fg(124, 135, 152);
        text.Append("erased tail should not appear");
        At(9, 3);
        text.Append(esc).Append("[K");
        Fg(230, 237, 246);
        text.Append("> ");
        Reset();
        text.Append("try /model next");

        At(11, 1);
        Fg(90, 160, 255);
        text.Append(new string('─', Cols));
        Reset();

        return text.ToString();
    }

    public override void Render(ScreenBuffer buffer)
    {
        var y = Widgets.CompactChrome(buffer);
        var margin = Widgets.Margin(buffer);
        var width = buffer.Width - margin * 2;
        var x = margin;

        Widgets.SectionTitle(buffer, y, "Home", "Terminal preview · replayed capture");
        y += 2;

        var height = Math.Min(Rows + 2, Math.Max(4, buffer.Height - y - 5));
        var boxWidth = Math.Min(Cols + 4, width);

        buffer.Box(x, y, boxWidth, height, new Sty(Theme.Blue, Theme.PanelSelected),
            BoxStyle.Rounded, Theme.PanelSelected);

        buffer.WriteClipped(x + 2, y, " 1 · preview ", boxWidth - 4,
            new Sty(Theme.Blue, Theme.PanelSelected, bold: true));

        TerminalRender.Draw(buffer, _screen, x + 2, y + 1,
            Math.Max(0, boxWidth - 4), Math.Max(0, height - 2), Theme.PanelSelected, focused: true);

        // This is a replayed capture with no child behind it. The footer used
        // to be copied from the live wall and offered typing and Ctrl+], neither
        // of which does anything here.
        Widgets.Footer(buffer, new[]
        {
            new KeyHint("esc", "Back")
        }, KeyMap.Help);
    }

    public override ScreenAction HandleKey(ConsoleKeyInfo key) => key.Key switch
    {
        ConsoleKey.Escape => ScreenAction.Back,
        ConsoleKey.F1 => ScreenAction.Push(new KeysScreen(App, "Preview", KeyMap.Preview())),
        _ => ScreenAction.None
    };
}
