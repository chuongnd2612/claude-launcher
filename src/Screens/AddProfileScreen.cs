using System.Text;
using ClaudeLauncher.Tui;

namespace ClaudeLauncher.Screens;

/// <summary>Creates a new profile and appends it to profiles.json.</summary>
public sealed class AddProfileScreen : ScreenBase
{
    private const int FieldCount = 4;

    private string _label = string.Empty;
    private string _directory = string.Empty;
    private string _icon = string.Empty;
    private string _description = string.Empty;

    private bool _directoryTouched;
    private bool _iconTouched;
    private int _field;
    private string? _error;

    public AddProfileScreen(App app) : base(app)
    {
    }

    private string Slug => Slugify(_label);

    public override void Render(ScreenBuffer buffer)
    {
        var y = Widgets.Chrome(buffer, 0);
        var margin = Widgets.Margin(buffer);

        Widgets.SectionTitle(buffer, y, "Add profile", "Create a new Claude profile");
        y += 2;

        var width = buffer.Width - margin * 2;
        var formWidth = Math.Min(width, Math.Max(52, width * 3 / 4));

        const int formHeight = 14;
        Widgets.TitledBox(buffer, margin, y, formWidth, formHeight, "New profile", Theme.VioletSoft);

        Field(buffer, margin + 3, y + 1, formWidth - 6, "Label", _label, "e.g. Client A", 0);
        Field(buffer, margin + 3, y + 4, formWidth - 6, "Config directory", _directory, "$HOME/.claude-client-a", 1);
        Field(buffer, margin + 3, y + 7, formWidth - 6, "Icon", _icon, "single character", 2);
        Field(buffer, margin + 3, y + 10, formWidth - 6, "Description", _description, "optional", 3);

        var infoY = y + formHeight;
        buffer.Write(margin + 1, infoY, $"Key: {(Slug.Length == 0 ? "—" : Slug)}", new Sty(Theme.Dim, Theme.Bg));
        buffer.Write(margin + 1, infoY + 1, $"File: {StateStore.ProfilesFilePath}", new Sty(Theme.Dim, Theme.Bg));

        if (_error is not null)
            buffer.Write(margin + 1, infoY + 3, "✗ " + _error, new Sty(Theme.Red, Theme.Bg, bold: true));

        Widgets.Footer(buffer, new[]
        {
            new KeyHint("↑↓/tab", "Field"),
            new KeyHint("↵", "Save"),
            new KeyHint("esc", "Cancel")
        });
    }

    private void Field(ScreenBuffer buffer, int x, int y, int width, string label, string value, string placeholder, int index)
    {
        var active = _field == index;
        buffer.Write(x, y, label, new Sty(active ? Theme.Blue : Theme.Muted, Theme.Panel, bold: active));

        var boxY = y + 1;
        var bg = active ? Theme.PanelSelected : Theme.BgSoft;
        buffer.Fill(x, boxY, width, 1, bg);

        var display = value;
        var style = new Sty(Theme.Text, bg);
        if (display.Length == 0 && !active)
        {
            display = placeholder;
            style = new Sty(Theme.Dim, bg, italic: true);
        }

        var cursorX = buffer.WriteClipped(x + 1, boxY, display, width - 3, style);
        if (active) buffer.Write(cursorX, boxY, "▏", new Sty(Theme.Blue, bg, bold: true));
    }

    public override ScreenAction HandleKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                return ScreenAction.Back;
            case ConsoleKey.Tab:
            case ConsoleKey.DownArrow:
                _field = (_field + 1) % FieldCount;
                return ScreenAction.None;
            case ConsoleKey.UpArrow:
                _field = (_field + FieldCount - 1) % FieldCount;
                return ScreenAction.None;
            case ConsoleKey.Enter:
                return Save();
            case ConsoleKey.Backspace:
                Edit(current => current.Length > 0 ? current.Substring(0, current.Length - 1) : current);
                return ScreenAction.None;
        }

        if (!char.IsControl(key.KeyChar))
        {
            var ch = key.KeyChar;
            Edit(current => _field == 2 ? ch.ToString() : current + ch);
        }

        return ScreenAction.None;
    }

    private void Edit(Func<string, string> transform)
    {
        _error = null;

        switch (_field)
        {
            case 0:
                _label = transform(_label);
                if (!_directoryTouched) _directory = Slug.Length > 0 ? $"$HOME/.claude-{Slug}" : string.Empty;
                if (!_iconTouched) _icon = _label.Length > 0 ? _label.Substring(0, 1).ToUpperInvariant() : string.Empty;
                break;
            case 1:
                _directory = transform(_directory);
                _directoryTouched = true;
                break;
            case 2:
                _icon = transform(_icon);
                _iconTouched = true;
                break;
            default:
                _description = transform(_description);
                break;
        }
    }

    private ScreenAction Save()
    {
        var label = _label.Trim();
        var slug = Slug;

        if (label.Length == 0)
        {
            _error = "Label is required.";
            _field = 0;
            return ScreenAction.None;
        }

        if (slug.Length == 0)
        {
            _error = "Label must contain at least one letter or digit.";
            _field = 0;
            return ScreenAction.None;
        }

        if (App.State.Profiles.Any(p => string.Equals(p.Name, slug, StringComparison.OrdinalIgnoreCase)))
        {
            _error = $"A profile with key '{slug}' already exists.";
            _field = 0;
            return ScreenAction.None;
        }

        var directory = _directory.Trim();
        if (directory.Length == 0)
        {
            _error = "Config directory is required.";
            _field = 1;
            return ScreenAction.None;
        }

        var profile = new ProfileEntry
        {
            Name = slug,
            Label = label,
            Icon = _icon.Trim().Length > 0 ? _icon.Trim().Substring(0, 1) : label.Substring(0, 1).ToUpperInvariant(),
            ConfigDir = StateStore.CollapseHome(directory),
            Description = _description.Trim().Length > 0 ? _description.Trim() : null
        };

        try
        {
            StateStore.AppendProfile(profile);
            Directory.CreateDirectory(StateStore.ExpandHome(profile.ConfigDir));
        }
        catch (Exception ex)
        {
            _error = "Could not save: " + ex.Message;
            return ScreenAction.None;
        }

        App.State.Profiles.Add(profile);
        return ScreenAction.Replace(new ProfileScreen(App, App.State.Profiles.Count - 1));
    }

    private static string Slugify(string value)
    {
        var builder = new StringBuilder();
        var lastDash = true;

        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                lastDash = false;
            }
            else if (!lastDash)
            {
                builder.Append('-');
                lastDash = true;
            }
        }

        return builder.ToString().Trim('-');
    }
}
