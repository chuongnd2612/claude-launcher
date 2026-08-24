using System.Diagnostics;
using ClaudeLauncher.Sessions;
using ClaudeLauncher.Tui;

namespace ClaudeLauncher.Screens;

/// <summary>Confirms stopping a running Claude session. Killing loses unsaved work, so it always asks.</summary>
public sealed class KillSessionScreen : ScreenBase
{
    private readonly SessionRow _row;
    private readonly Action _onKilled;
    private bool _confirm;
    private string? _error;

    public KillSessionScreen(App app, SessionRow row, Action onKilled) : base(app)
    {
        _row = row;
        _onKilled = onKilled;
    }

    public override void Render(ScreenBuffer buffer)
    {
        var y = Widgets.CompactChrome(buffer);
        var margin = Widgets.Margin(buffer);

        Widgets.SectionTitle(buffer, y, "Stop session", $"'{_row.ProjectName}' will be terminated");
        y += 2;

        var width = buffer.Width - margin * 2;
        var panelWidth = Math.Min(width, Math.Max(52, width * 3 / 4));

        const int panelHeight = 8;
        Widgets.TitledBox(buffer, margin, y, panelWidth, panelHeight, "Confirm", Theme.Red);

        var textWidth = panelWidth - 6;
        buffer.WriteClipped(margin + 3, y + 1, _row.Task, textWidth, new Sty(Theme.Text, Theme.Panel, bold: true));
        buffer.WriteClipped(margin + 3, y + 2, _row.ProjectPath, textWidth, new Sty(Theme.TextSoft, Theme.Panel));
        buffer.WriteClipped(margin + 3, y + 3, $"pid {_row.Pid} · {Format.State(_row.State, _row.StateAge)}",
            textWidth, new Sty(Theme.Muted, Theme.Panel));
        buffer.WriteClipped(margin + 3, y + 5, "Claude is stopped immediately; anything it has not written is lost.",
            textWidth, new Sty(Theme.Muted, Theme.Panel, italic: true));

        Choice(buffer, margin + 3, y + panelHeight + 1, "Cancel", !_confirm, Theme.Blue);
        Choice(buffer, margin + 18, y + panelHeight + 1, "Stop it", _confirm, Theme.Red);

        if (_error is not null)
            buffer.Write(margin + 1, y + panelHeight + 3, "✗ " + _error, new Sty(Theme.Red, Theme.Bg, bold: true));

        Widgets.Footer(buffer, new[]
        {
            new KeyHint("y", "Yes"),
            new KeyHint("n", "No"),
            new KeyHint("←→", "Choose"),
            new KeyHint("esc", "Cancel")
        }, KeyMap.Help);
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
            case ConsoleKey.F1:
                return ScreenAction.Push(new KeysScreen(App, "Stop session", KeyMap.Confirm()));
            case ConsoleKey.Escape:
                return ScreenAction.Back;
            case ConsoleKey.Enter:
                return _confirm ? Stop() : ScreenAction.Back;
        }

        var ch = char.ToLowerInvariant(key.KeyChar);
        if (ch == 'y') return Stop();
        if (ch == 'n') return ScreenAction.Back;

        return ScreenAction.None;
    }

    private ScreenAction Stop()
    {
        try
        {
            using var process = Process.GetProcessById(_row.Pid);
            process.Kill(entireProcessTree: true);
        }
        catch (ArgumentException)
        {
            // Already gone: the outcome the user asked for.
        }
        catch (Exception ex)
        {
            _error = "Could not stop it: " + ex.Message;
            return ScreenAction.None;
        }

        _onKilled();
        return ScreenAction.Back;
    }
}
