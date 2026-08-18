using System.Text;
using ClaudeLauncher.Tui;

namespace ClaudeLauncher.Screens;

/// <summary>Creates a new profile, or edits an existing one, in profiles.json.</summary>
public sealed class AddProfileScreen : ScreenBase
{
    private const int FieldCount = 4;

    private readonly ProfileEntry? _existing;
    private readonly string _originalName;

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
        _originalName = string.Empty;
    }

    /// <summary>Edit mode: fields start from the profile and nothing is auto-derived.</summary>
    public AddProfileScreen(App app, ProfileEntry existing) : base(app)
    {
        _existing = existing;
        _originalName = existing.Name;
        _label = existing.DisplayLabel;
        _directory = existing.ConfigDir;
        _icon = existing.DisplayIcon;
        _description = existing.Description ?? string.Empty;
        _directoryTouched = true;
        _iconTouched = true;
    }

    private bool IsEdit => _existing is not null;

    /// <summary>An icon no other profile is using, from this label.</summary>
    private string Suggested() => ProfileLook.Suggest(_label, App.State.Profiles
        .Where(p => !ReferenceEquals(p, _existing))
        .Select(p => p.DisplayIcon));

    /// <summary>Steps through the icons on offer, so one can be picked without typing it.</summary>
    private void Cycle(int by)
    {
        var choices = ProfileLook.Choices(_label);
        if (choices.Count == 0) return;

        var at = choices.ToList().FindIndex(c => c == _icon);
        var next = at < 0 ? (by > 0 ? 0 : choices.Count - 1) : (at + by + choices.Count) % choices.Count;

        _icon = choices[next];
        _iconTouched = true;
        _error = null;
    }

    private string Slug => Slugify(_label);

    public override void Render(ScreenBuffer buffer)
    {
        var y = Widgets.Chrome(buffer, 0);
        var margin = Widgets.Margin(buffer);

        if (IsEdit) Widgets.SectionTitle(buffer, y, "Edit profile", $"Update '{_existing!.DisplayLabel}'");
        else Widgets.SectionTitle(buffer, y, "Add profile", "Create a new Claude profile");
        y += 2;

        var width = buffer.Width - margin * 2;
        var formWidth = Math.Min(width, Math.Max(52, width * 3 / 4));

        const int formHeight = 14;
        Widgets.TitledBox(buffer, margin, y, formWidth, formHeight, IsEdit ? "Edit profile" : "New profile", Theme.VioletSoft);

        Field(buffer, margin + 3, y + 1, formWidth - 6, "Label", _label, "e.g. Client A", 0);
        Field(buffer, margin + 3, y + 4, formWidth - 6, "Config directory", _directory, "$HOME/.claude-client-a", 1);
        Field(buffer, margin + 3, y + 7, formWidth - 6, "Icon", _icon, "single character", 2);

        // The icon in the colour the wall will paint it, since that pairing is
        // what makes a pane recognisable.
        var swatch = _icon.Length > 0 ? _icon : "?";
        var key = Slug.Length > 0 ? Slug : _label;
        buffer.Write(margin + formWidth - 12, y + 8, $" {swatch} ",
            new Sty(ProfileLook.Color(key), Theme.PanelSelected, bold: true));
        buffer.Write(margin + 3, y + 9, "← → to pick another", new Sty(Theme.Dim, Theme.Bg, italic: true));
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

            case ConsoleKey.LeftArrow:
            case ConsoleKey.RightArrow:
                if (_field != 2) return ScreenAction.None;
                Cycle(key.Key == ConsoleKey.RightArrow ? 1 : -1);
                return ScreenAction.None;
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
                if (!_iconTouched) _icon = Suggested();
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

        if (App.State.Profiles.Any(p => !ReferenceEquals(p, _existing) &&
                                        string.Equals(p.Name, slug, StringComparison.OrdinalIgnoreCase)))
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

        // A profile always ends up with an icon: it is the only thing that tells
        // one pane from another at a glance, so an empty field takes the
        // suggestion rather than saving nothing.
        var icon = _icon.Trim().Length > 0 ? _icon.Trim()[..1] : Suggested();
        var configDir = StateStore.CollapseHome(directory);
        var description = _description.Trim().Length > 0 ? _description.Trim() : null;

        // Edit mode mutates the entry in place so anything already holding a
        // reference to it (App.Profile) keeps pointing at the same profile.
        var profile = _existing ?? new ProfileEntry();
        profile.Name = slug;
        profile.Label = label;
        profile.Icon = icon;
        profile.ConfigDir = configDir;
        profile.Description = description;

        try
        {
            if (IsEdit) StateStore.UpdateProfile(_originalName, profile);
            else StateStore.AppendProfile(profile);

            Directory.CreateDirectory(StateStore.ExpandHome(profile.ConfigDir));
        }
        catch (Exception ex)
        {
            _error = "Could not save: " + ex.Message;
            return ScreenAction.None;
        }

        if (IsEdit) return ScreenAction.Replace(new ProfileScreen(App, App.State.Profiles.IndexOf(profile)));

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
