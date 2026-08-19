using ClaudeLauncher.Tui;

namespace ClaudeLauncher.Screens;

/// <summary>Step 1 - pick a Claude profile (config dir).</summary>
public sealed class ProfileScreen : ScreenBase
{
    private const int CardHeight = 5;
    private const int CompactCardHeight = 4;
    private const int Gap = 3;

    private int _index;
    private int _scrollRow;

    public ProfileScreen(App app, int index = 0) : base(app) =>
        _index = Math.Clamp(index, 0, Math.Max(0, app.State.Profiles.Count));

    private int TileCount => App.State.Profiles.Count + 1; // + "add new profile"

    private int AddTileIndex => App.State.Profiles.Count;

    public override void Render(ScreenBuffer buffer)
    {
        var y = Widgets.Chrome(buffer, 0);
        var margin = Widgets.Margin(buffer);

        Widgets.SectionTitle(buffer, y, "Select a Claude profile");

        var inner = buffer.Width - margin * 2;
        var columns = inner >= 84 ? 2 : 1;
        var cardWidth = columns == 2 ? (inner - Gap) / 2 : inner;
        var compact = buffer.Height < 30;
        var cardHeight = compact ? CompactCardHeight : CardHeight;

        var gridTop = y + 2;
        var footerTop = buffer.Height - 3;
        var rowStride = cardHeight + 1;
        var totalRows = (TileCount + columns - 1) / columns;
        var rowsFit = Math.Max(1, (footerTop - 1 - gridTop) / rowStride);

        // Keep the highlighted tile inside the viewport on short windows.
        var selectedRow = _index / columns;
        if (selectedRow < _scrollRow) _scrollRow = selectedRow;
        if (selectedRow >= _scrollRow + rowsFit) _scrollRow = selectedRow - rowsFit + 1;
        _scrollRow = Math.Clamp(_scrollRow, 0, Math.Max(0, totalRows - rowsFit));

        if (totalRows > rowsFit)
        {
            var indicator = $"{selectedRow + 1}/{totalRows}  {(_scrollRow > 0 ? "▴" : " ")}{(_scrollRow + rowsFit < totalRows ? "▾" : " ")}";
            buffer.WriteRight(margin + inner - 1, y, indicator, new Sty(Theme.Dim, Theme.Bg));
        }

        for (var i = 0; i < TileCount; i++)
        {
            var row = i / columns;
            if (row < _scrollRow || row >= _scrollRow + rowsFit) continue;

            var column = i % columns;
            var x = margin + column * (cardWidth + Gap);
            var cardY = gridTop + (row - _scrollRow) * rowStride;

            if (i == AddTileIndex) DrawAddTile(buffer, x, cardY, cardWidth, cardHeight, i == _index);
            else DrawProfileTile(buffer, x, cardY, cardWidth, cardHeight, App.State.Profiles[i], i, i == _index);
        }

        var afterCards = gridTop + Math.Min(totalRows - _scrollRow, rowsFit) * rowStride;

        // Keep the tips box pinned above the footer so tall windows do not
        // leave a hole in the middle of the layout.
        var tipsY = Math.Max(afterCards, buffer.Height - 4 - 6);

        if (App.Settings.ShowTips)
        {
            Widgets.Tips(buffer, tipsY, new[]
            {
                "Profiles keep work and personal Claude sessions apart (CLAUDE_CONFIG_DIR)",
                "Each profile has its own settings, history and MCP servers",
                "Press e to edit or d to remove the highlighted profile",
                "Projects come from your existing QuickPaths registry"
            });
        }

        // Most runs start here, not on Home, so this is where an update has to
        // be able to say so - on the row above the tips, which is the only free
        // one when they are shown.
        var update = UpdateBanner.Line();
        var updateY = App.Settings.ShowTips ? tipsY - 1 : buffer.Height - 6;

        if (update is not null && updateY > afterCards - 1 && updateY < buffer.Height - 4)
        {
            buffer.WriteClipped(Widgets.Margin(buffer) + 1, updateY, update.Value.Text,
                buffer.Width - Widgets.Margin(buffer) * 2 - 2, new Sty(update.Value.Color, Theme.Bg));
        }

        Widgets.Footer(buffer, new[]
        {
            new KeyHint("↑↓←→", "Navigate"),
            new KeyHint("↵", "Select"),
            new KeyHint("a", "Add"),
            new KeyHint("e", "Edit"),
            new KeyHint("d", "Remove"),
            new KeyHint("s", "Settings"),
            new KeyHint("u", "Updates"),
            new KeyHint("q", "Quit")
        });
    }

    private static void DrawProfileTile(ScreenBuffer buffer, int x, int y, int width, int height, ProfileEntry profile, int index, bool selected)
    {
        Widgets.Panel(buffer, x, y, width, height, selected);

        var bg = selected ? Theme.PanelSelected : Theme.Panel;
        var full = height >= CardHeight;
        var textX = x + (full ? 9 : 5);
        var textWidth = width - (textX - x) - 4;

        if (full)
            Widgets.IconBadge(buffer, x + 2, y + 1, profile.DisplayIcon,
                selected ? ProfileLook.Color(profile.Name) : Theme.Muted, bg, selected);
        else
            buffer.Write(x + 2, y + 1, profile.DisplayIcon,
                new Sty(selected ? ProfileLook.Color(profile.Name) : Theme.Muted, bg, bold: true));

        buffer.WriteClipped(textX, y + 1, profile.DisplayLabel, textWidth,
            new Sty(selected ? Theme.Blue : Theme.Text, bg, bold: true));

        buffer.WriteClipped(textX, y + 2, StateStore.ExpandHome(profile.ConfigDir), textWidth,
            new Sty(Theme.TextSoft, bg));

        if (full)
            buffer.WriteClipped(textX, y + 3, profile.DescriptionOr(index == 0), textWidth,
                new Sty(Theme.Muted, bg, italic: true));

        if (selected) buffer.Write(x + width - 3, y + 1, "✓", new Sty(Theme.Blue, bg, bold: true));
    }

    private static void DrawAddTile(ScreenBuffer buffer, int x, int y, int width, int height, bool selected)
    {
        var bg = selected ? Theme.PanelSelected : Theme.Panel;
        var border = selected ? Theme.BorderAccent : Theme.Border;
        buffer.Box(x, y, width, height, new Sty(border, bg), BoxStyle.Dashed, bg);

        var full = height >= CardHeight;
        var textX = x + (full ? 9 : 5);
        var textWidth = width - (textX - x) - 4;

        if (full)
            Widgets.IconBadge(buffer, x + 2, y + 1, "+", selected ? Theme.VioletSoft : Theme.Muted, bg, false);
        else
            buffer.Write(x + 2, y + 1, "+", new Sty(selected ? Theme.VioletSoft : Theme.Muted, bg, bold: true));

        buffer.WriteClipped(textX, y + 1, "Add new profile", textWidth,
            new Sty(selected ? Theme.VioletSoft : Theme.TextSoft, bg, bold: true));
        buffer.WriteClipped(textX, y + 2, "Create a new Claude profile", textWidth, new Sty(Theme.Muted, bg, italic: true));

        if (full)
            buffer.WriteClipped(textX, y + 3, $"Saved to {StateStore.CollapseHome(StateStore.ProfilesFilePath)}", textWidth,
                new Sty(Theme.Dim, bg));
    }

    public override ScreenAction HandleKey(ConsoleKeyInfo key)
    {
        var columns = App.Buffer.Width - Widgets.Margin(App.Buffer) * 2 >= 84 ? 2 : 1;

        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                Move(-columns);
                return ScreenAction.None;
            case ConsoleKey.DownArrow:
                Move(columns);
                return ScreenAction.None;
            case ConsoleKey.LeftArrow:
                Move(-1);
                return ScreenAction.None;
            case ConsoleKey.RightArrow:
            case ConsoleKey.Tab:
                Move(1);
                return ScreenAction.None;
            case ConsoleKey.Home:
                _index = 0;
                return ScreenAction.None;
            case ConsoleKey.End:
                _index = TileCount - 1;
                return ScreenAction.None;
            case ConsoleKey.Enter:
            case ConsoleKey.Spacebar:
                return Choose();
            case ConsoleKey.Delete:
                return Remove();
            case ConsoleKey.Escape:
                // Back, not Exit: this screen is the root when nothing is
                // running (so Back still quits), but sits above Home when
                // something is - and then Esc should return there.
                return ScreenAction.Back;
        }

        var ch = char.ToLowerInvariant(key.KeyChar);
        if (ch == 'q') return ScreenAction.Exit;
        if (ch == 'a') return ScreenAction.Push(new AddProfileScreen(App));
        if (ch == 'e') return Edit();
        if (ch == 'd') return Remove();
        if (ch == 's') return ScreenAction.Push(new SettingsScreen(App));
        if (ch == 'u') return UpdateBanner.Pressed(App);

        if (ch >= '1' && ch <= '9')
        {
            var target = ch - '1';
            if (target < App.State.Profiles.Count)
            {
                _index = target;
                return Choose();
            }
        }

        return ScreenAction.None;
    }

    private ScreenAction Choose()
    {
        if (_index == AddTileIndex) return ScreenAction.Push(new AddProfileScreen(App));

        App.Profile = App.State.Profiles[_index];
        App.Project = null;
        return ScreenAction.Push(new ProjectScreen(App));
    }

    /// <summary>Edit and remove only apply to real profiles, never the "add" tile.</summary>
    private ScreenAction Edit()
    {
        if (_index == AddTileIndex) return ScreenAction.None;
        return ScreenAction.Push(new AddProfileScreen(App, App.State.Profiles[_index]));
    }

    private ScreenAction Remove()
    {
        if (_index == AddTileIndex) return ScreenAction.None;
        return ScreenAction.Push(new DeleteProfileScreen(App, App.State.Profiles[_index]));
    }

    private void Move(int delta)
    {
        var next = _index + delta;
        if (next < 0 || next >= TileCount) return;
        _index = next;
    }
}
