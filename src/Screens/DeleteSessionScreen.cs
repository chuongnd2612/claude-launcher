using ClaudeLauncher.Sessions;
using ClaudeLauncher.Tui;

namespace ClaudeLauncher.Screens;

/// <summary>Confirms deleting a transcript. The conversation is not recoverable afterwards.</summary>
public sealed class DeleteSessionScreen : ScreenBase
{
    private readonly PastSession _session;
    private readonly Action _onDeleted;
    private bool _confirm;
    private string? _error;

    public DeleteSessionScreen(App app, PastSession session, Action onDeleted) : base(app)
    {
        _session = session;
        _onDeleted = onDeleted;
    }

    public override void Render(ScreenBuffer buffer)
    {
        var y = Widgets.CompactChrome(buffer);
        var margin = Widgets.Margin(buffer);

        Widgets.SectionTitle(buffer, y, "Delete session", $"{_session.ShortId} will be removed");
        y += 2;

        var width = buffer.Width - margin * 2;
        var panelWidth = Math.Min(width, Math.Max(52, width * 3 / 4));

        const int panelHeight = 8;
        Widgets.TitledBox(buffer, margin, y, panelWidth, panelHeight, "Confirm", Theme.Red);

        var textWidth = panelWidth - 6;
        buffer.WriteClipped(margin + 3, y + 1, _session.DisplayTitle, textWidth,
            new Sty(Theme.Text, Theme.Panel, bold: true));
        buffer.WriteClipped(margin + 3, y + 2, _session.Path, textWidth, new Sty(Theme.TextSoft, Theme.Panel));
        buffer.WriteClipped(margin + 3, y + 3,
            $"{Format.Ago(_session.LastActivityUtc)} · {_session.SizeBytes / 1024:N0} KB",
            textWidth, new Sty(Theme.Muted, Theme.Panel));
        buffer.WriteClipped(margin + 3, y + 5,
            "The conversation is deleted from disk and cannot be resumed afterwards.",
            textWidth, new Sty(Theme.Muted, Theme.Panel, italic: true));

        Choice(buffer, margin + 3, y + panelHeight + 1, "Cancel", !_confirm, Theme.Blue);
        Choice(buffer, margin + 18, y + panelHeight + 1, "Delete", _confirm, Theme.Red);

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
                return _confirm ? Delete() : ScreenAction.Back;
        }

        var ch = char.ToLowerInvariant(key.KeyChar);
        if (ch == 'y') return Delete();
        if (ch == 'n') return ScreenAction.Back;

        return ScreenAction.None;
    }

    private ScreenAction Delete()
    {
        try
        {
            File.Delete(_session.Path);
        }
        catch (FileNotFoundException)
        {
            // Already gone: the outcome that was asked for.
        }
        catch (Exception ex)
        {
            _error = "Could not delete: " + ex.Message;
            return ScreenAction.None;
        }

        _onDeleted();
        return ScreenAction.Back;
    }
}
