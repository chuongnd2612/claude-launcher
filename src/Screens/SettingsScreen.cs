using ClaudeLauncher.Tui;

namespace ClaudeLauncher.Screens;

/// <summary>UI preferences, persisted to ~/.claude-launcher/ui.json.</summary>
public sealed class SettingsScreen : ScreenBase
{
    private const int ItemCount = 5;

    private int _index;

    public SettingsScreen(App app) : base(app)
    {
    }

    public override void Render(ScreenBuffer buffer)
    {
        var y = Widgets.Chrome(buffer, 0);
        var margin = Widgets.Margin(buffer);

        Widgets.SectionTitle(buffer, y, "Settings", "Launcher preferences");
        y += 2;

        var width = buffer.Width - margin * 2;
        var panelWidth = Math.Min(width, Math.Max(52, width * 3 / 4));

        Widgets.TitledBox(buffer, margin, y, panelWidth, ItemCount + 2, "Appearance", Theme.VioletSoft);

        Toggle(buffer, margin + 2, y + 1, panelWidth - 4, 0, "Paint background",
            App.Settings.PaintBackground ? "on" : "off",
            "Use the launcher canvas color instead of the terminal's");

        Toggle(buffer, margin + 2, y + 2, panelWidth - 4, 1, "Show tips",
            App.Settings.ShowTips ? "on" : "off",
            "Tips box on the profile screen");

        Toggle(buffer, margin + 2, y + 3, panelWidth - 4, 2, "Default session mode",
            App.Settings.DefaultMode,
            "Pre-selected option on step 3");

        Toggle(buffer, margin + 2, y + 4, panelWidth - 4, 3, "Default open in",
            LaunchTarget.Label(App.Settings.DefaultOpenIn),
            "Where Enter launches Claude");

        Toggle(buffer, margin + 2, y + 5, panelWidth - 4, 4, "Remote control",
            App.Settings.RemoteControl ? "on" : "off",
            "New sessions accept input from claude.ai");

        var infoY = y + ItemCount + 3;
        if (infoY + 6 <= buffer.Height - 4)
        {
            Widgets.TitledBox(buffer, margin, infoY, panelWidth, 6, "Paths", Theme.Blue);
            Info(buffer, margin + 3, infoY + 1, "profiles", StateStore.ProfilesFilePath, panelWidth);
            Info(buffer, margin + 3, infoY + 2, "settings", StateStore.SettingsFile, panelWidth);
            Info(buffer, margin + 3, infoY + 3, "state", StateStore.StateFile, panelWidth);
            Info(buffer, margin + 3, infoY + 4, "terminal", $"{buffer.Width} x {buffer.Height}", panelWidth);
        }

        Widgets.Footer(buffer, new[]
        {
            new KeyHint("↑↓", "Navigate"),
            new KeyHint("↵/←→", "Change"),
            new KeyHint("esc", "Back")
        });
    }

    private void Toggle(ScreenBuffer buffer, int x, int y, int width, int index, string label, string value, string detail)
    {
        var active = _index == index;
        var bg = active ? Theme.PanelSelected : Theme.Panel;
        buffer.Fill(x, y, width, 1, bg);

        buffer.Write(x + 1, y, active ? "▸" : " ", new Sty(Theme.Blue, bg, bold: true));
        buffer.WriteClipped(x + 3, y, label, 22, new Sty(active ? Theme.Blue : Theme.Text, bg, bold: active));
        buffer.WriteClipped(x + 26, y, value, 12, new Sty(Theme.VioletSoft, bg, bold: true));
        buffer.WriteClipped(x + 39, y, detail, Math.Max(0, width - 40), new Sty(Theme.Dim, bg, italic: true));
    }

    private static void Info(ScreenBuffer buffer, int x, int y, string label, string value, int width)
    {
        buffer.Write(x, y, label.PadRight(9), new Sty(Theme.Dim, Theme.Panel));
        buffer.WriteClipped(x + 10, y, value, width - 14, new Sty(Theme.TextSoft, Theme.Panel));
    }

    public override ScreenAction HandleKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                _index = (_index + ItemCount - 1) % ItemCount;
                return ScreenAction.None;
            case ConsoleKey.DownArrow:
            case ConsoleKey.Tab:
                _index = (_index + 1) % ItemCount;
                return ScreenAction.None;
            case ConsoleKey.Enter:
            case ConsoleKey.Spacebar:
            case ConsoleKey.RightArrow:
                Change(1);
                return ScreenAction.None;
            case ConsoleKey.LeftArrow:
                Change(-1);
                return ScreenAction.None;
            case ConsoleKey.Escape:
                return ScreenAction.Back;
        }

        var ch = char.ToLowerInvariant(key.KeyChar);
        if (ch == 's' || ch == 'q') return ScreenAction.Back;

        return ScreenAction.None;
    }

    private void Change(int direction)
    {
        switch (_index)
        {
            case 0:
                App.Settings.PaintBackground = !App.Settings.PaintBackground;
                break;
            case 1:
                App.Settings.ShowTips = !App.Settings.ShowTips;
                break;
            case 2:
                var modes = UiSettings.Modes;
                var current = Array.IndexOf(modes, App.Settings.DefaultMode);
                if (current < 0) current = 0;
                var next = (current + direction + modes.Length) % modes.Length;
                App.Settings.DefaultMode = modes[next];
                break;
            case 3:
                App.Settings.DefaultOpenIn = LaunchTarget.Next(App.Settings.DefaultOpenIn, direction);
                break;
            default:
                App.Settings.RemoteControl = !App.Settings.RemoteControl;
                break;
        }

        StateStore.SaveSettings(App.Settings);
    }
}
