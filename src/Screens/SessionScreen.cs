using ClaudeLauncher.Tui;

namespace ClaudeLauncher.Screens;

/// <summary>Step 3 - choose how Claude Code should start.</summary>
public sealed class SessionScreen : ScreenBase
{
    private sealed record Option(string Mode, string Glyph, string Title, string Detail, string Hint);

    private static readonly Option[] Options =
    {
        new("new", "▶", "New session", "Start a fresh conversation in this project", "claude"),
        new("continue", "→", "Continue", "Pick up the most recent conversation", "claude --continue"),
        new("resume", "↻", "Resume", "Choose from earlier sessions to resume", "claude --resume")
    };

    private int _index;
    private string _openIn;

    public SessionScreen(App app) : base(app)
    {
        var preferred = Array.FindIndex(Options, o => o.Mode == app.Settings.DefaultMode);
        _index = preferred >= 0 ? preferred : 0;
        _openIn = LaunchTarget.Normalize(app.Settings.DefaultOpenIn);
    }

    public override void Render(ScreenBuffer buffer)
    {
        var y = Widgets.Chrome(buffer, 2);
        var margin = Widgets.Margin(buffer);
        var profile = App.Profile!;
        var project = App.Project!;

        Widgets.SectionTitle(buffer, y, $"{profile.DisplayIcon}  {profile.DisplayLabel}  ·  {project.Name}", "How should Claude start?");
        y += 2;

        var width = buffer.Width - margin * 2;
        var cardWidth = Math.Min(width, Math.Max(44, width * 2 / 3));

        for (var i = 0; i < Options.Length; i++)
        {
            var option = Options[i];
            var selected = i == _index;
            var cardY = y + i * 4;
            if (cardY + 3 > buffer.Height - 4) break;

            Widgets.Panel(buffer, margin, cardY, cardWidth, 3, selected);
            var bg = selected ? Theme.PanelSelected : Theme.Panel;

            buffer.Write(margin + 2, cardY + 1, option.Glyph,
                new Sty(selected ? Theme.Blue : Theme.Muted, bg, bold: true));

            var textX = margin + 5;
            buffer.WriteClipped(textX, cardY + 1, option.Title, 16,
                new Sty(selected ? Theme.Blue : Theme.Text, bg, bold: true));

            buffer.WriteClipped(textX + 16, cardY + 1, option.Detail, cardWidth - (textX - margin) - 18 - option.Hint.Length,
                new Sty(Theme.Muted, bg));

            buffer.WriteRight(margin + cardWidth - 3, cardY + 1, option.Hint,
                new Sty(selected ? Theme.VioletSoft : Theme.Dim, bg, italic: true));
        }

        var summaryY = y + Options.Length * 4;
        if (summaryY + 7 <= buffer.Height - 4)
        {
            Widgets.TitledBox(buffer, margin, summaryY, width, 7, "Launch summary", Theme.VioletSoft);
            Row(buffer, margin + 3, summaryY + 1, "Profile", profile.DisplayLabel, width);
            Row(buffer, margin + 3, summaryY + 2, "Config", StateStore.ExpandHome(profile.ConfigDir), width);
            Row(buffer, margin + 3, summaryY + 3, "Project", project.Name, width);
            Row(buffer, margin + 3, summaryY + 4, "Path", project.Path, width);
            Row(buffer, margin + 3, summaryY + 5, "Opens in", LaunchTarget.Describe(_openIn), width);
        }

        Widgets.Footer(buffer, new[]
        {
            new KeyHint("↑↓", "Navigate"),
            new KeyHint("↵", "Launch"),
            new KeyHint("o", "Open in"),
            new KeyHint("n/c/r", "Quick mode"),
            new KeyHint("esc", "Back"),
            new KeyHint("q", "Quit")
        });
    }

    private static void Row(ScreenBuffer buffer, int x, int y, string label, string value, int width)
    {
        buffer.Write(x, y, label.PadRight(9), new Sty(Theme.Dim, Theme.Panel));
        buffer.WriteClipped(x + 10, y, value, width - 14, new Sty(Theme.TextSoft, Theme.Panel));
    }

    public override ScreenAction HandleKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                _index = Math.Max(0, _index - 1);
                return ScreenAction.None;
            case ConsoleKey.DownArrow:
            case ConsoleKey.Tab:
                _index = Math.Min(Options.Length - 1, _index + 1);
                return ScreenAction.None;
            case ConsoleKey.Enter:
            case ConsoleKey.Spacebar:
                return ScreenAction.Finish(Options[_index].Mode, _openIn);
            case ConsoleKey.LeftArrow:
                _openIn = LaunchTarget.Next(_openIn, -1);
                return ScreenAction.None;
            case ConsoleKey.RightArrow:
                _openIn = LaunchTarget.Next(_openIn);
                return ScreenAction.None;
            case ConsoleKey.Escape:
            case ConsoleKey.Backspace:
                return ScreenAction.Back;
        }

        switch (char.ToLowerInvariant(key.KeyChar))
        {
            case 'o': _openIn = LaunchTarget.Next(_openIn); return ScreenAction.None;
            case 'n': return ScreenAction.Finish("new", _openIn);
            case 'c': return ScreenAction.Finish("continue", _openIn);
            case 'r': return ScreenAction.Finish("resume", _openIn);
            case 'q': return ScreenAction.Exit;
        }

        return ScreenAction.None;
    }
}
