using ClaudeLauncher.Tui;

namespace ClaudeLauncher.Screens;

/// <summary>Confirms removing a profile from profiles.json.</summary>
public sealed class DeleteProfileScreen : ScreenBase
{
    private readonly ProfileEntry _profile;
    private bool _confirm;
    private string? _error;

    public DeleteProfileScreen(App app, ProfileEntry profile) : base(app) => _profile = profile;

    public override void Render(ScreenBuffer buffer)
    {
        var y = Widgets.Chrome(buffer, 0);
        var margin = Widgets.Margin(buffer);

        Widgets.SectionTitle(buffer, y, "Remove profile", $"'{_profile.DisplayLabel}' will be deleted");
        y += 2;

        var width = buffer.Width - margin * 2;
        var panelWidth = Math.Min(width, Math.Max(52, width * 3 / 4));

        const int panelHeight = 8;
        Widgets.TitledBox(buffer, margin, y, panelWidth, panelHeight, "Confirm", Theme.Red);

        var textWidth = panelWidth - 6;
        buffer.WriteClipped(margin + 3, y + 1, _profile.DisplayLabel, textWidth, new Sty(Theme.Text, Theme.Panel, bold: true));
        buffer.WriteClipped(margin + 3, y + 2, StateStore.ExpandHome(_profile.ConfigDir), textWidth, new Sty(Theme.TextSoft, Theme.Panel));
        buffer.WriteClipped(margin + 3, y + 4, "The entry is removed from profiles.json.", textWidth, new Sty(Theme.Muted, Theme.Panel));
        buffer.WriteClipped(margin + 3, y + 5, "Its config directory and Claude history stay on disk.", textWidth,
            new Sty(Theme.Muted, Theme.Panel, italic: true));

        Choice(buffer, margin + 3, y + panelHeight + 1, "Cancel", !_confirm, Theme.Blue);
        Choice(buffer, margin + 18, y + panelHeight + 1, "Remove", _confirm, Theme.Red);

        if (_error is not null)
            buffer.Write(margin + 1, y + panelHeight + 3, "✗ " + _error, new Sty(Theme.Red, Theme.Bg, bold: true));

        Widgets.Footer(buffer, new[]
        {
            new KeyHint("←→", "Choose"),
            new KeyHint("↵", "Confirm"),
            new KeyHint("esc", "Cancel")
        });
    }

    private static void Choice(ScreenBuffer buffer, int x, int y, string label, bool active, Rgb color)
    {
        var bg = active ? Theme.PanelSelected : Theme.BgSoft;
        var text = $" {label} ";
        buffer.Fill(x, y, text.Length + 2, 1, bg);
        buffer.Write(x + 1, y, text, new Sty(active ? color : Theme.Muted, bg, bold: active));
    }

    public override ScreenAction HandleKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
            case ConsoleKey.RightArrow:
            case ConsoleKey.Tab:
                _confirm = !_confirm;
                return ScreenAction.None;
            case ConsoleKey.Escape:
                return ScreenAction.Back;
            case ConsoleKey.Enter:
                return _confirm ? Remove() : ScreenAction.Back;
        }

        var ch = char.ToLowerInvariant(key.KeyChar);
        if (ch == 'y') return Remove();
        if (ch == 'n') return ScreenAction.Back;

        return ScreenAction.None;
    }

    private ScreenAction Remove()
    {
        if (App.State.Profiles.Count <= 1)
        {
            _error = "At least one profile must remain.";
            return ScreenAction.None;
        }

        try
        {
            StateStore.RemoveProfile(_profile.Name);
        }
        catch (Exception ex)
        {
            _error = "Could not remove: " + ex.Message;
            return ScreenAction.None;
        }

        var index = App.State.Profiles.IndexOf(_profile);
        App.State.Profiles.Remove(_profile);
        if (ReferenceEquals(App.Profile, _profile)) App.Profile = null;

        // Land on a real profile rather than the "add" tile when the last one goes.
        return ScreenAction.Replace(new ProfileScreen(App, Math.Min(index, App.State.Profiles.Count - 1)));
    }
}
