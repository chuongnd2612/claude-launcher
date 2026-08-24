using System.Diagnostics;
using ClaudeLauncher.Tui;

namespace ClaudeLauncher.Screens;

/// <summary>
/// Offers the newer release, and hands the update itself to the wrapper.
///
/// The launcher cannot update itself while it is running - the installer copies
/// over the very exe holding the file open, which is why the offline installer
/// retries a locked file five times. So "update now" writes the request and
/// quits; the wrapper runs the installer once the process is gone. Nothing is
/// downloaded from here.
/// </summary>
public sealed class UpdateScreen : ScreenBase
{
    private readonly UpdateInfo _info;
    private string? _notice;

    public UpdateScreen(App app, UpdateInfo info) : base(app)
    {
        _info = info;
    }

    private static string Command =>
        "irm https://raw.githubusercontent.com/chuongnd2612/claude-launcher/main/install-online.ps1 | iex";

    public override void Render(ScreenBuffer buffer)
    {
        var y = Widgets.CompactChrome(buffer);
        var margin = Widgets.Margin(buffer);
        var width = buffer.Width - margin * 2;
        var room = Math.Max(20, width - 6);

        Widgets.SectionTitle(buffer, y, "Home", "Update available");
        y += 2;

        // Built as a list first, so a short window can drop the explanation and
        // still show the two things that matter: the versions and the command.
        var body = new List<(string Text, Sty Style)>
        {
            ($"Installed    v{Program.Version}", new Sty(Theme.TextSoft, Theme.Panel)),
            ($"Available    {_info.Latest}" + (Released().Length > 0 ? "  " + Released() : ""),
                new Sty(Theme.Green, Theme.Panel, bold: true)),
            (string.Empty, new Sty(Theme.Panel, Theme.Panel))
        };

        var essential = body.Count;

        foreach (var part in Words(
            "Enter closes the launcher and runs the installer, which replaces the exe this one " +
            "is running from - then start it again with claude-launcher.", room))
        {
            body.Add((part, new Sty(Theme.TextSoft, Theme.Panel)));
        }

        body.Add((string.Empty, new Sty(Theme.Panel, Theme.Panel)));
        body.Add(("Or run it yourself:", new Sty(Theme.Muted, Theme.Panel)));

        var command = new List<(string, Sty)>();
        foreach (var part in Wrap(Command, room))
        {
            command.Add((part, new Sty(Theme.VioletSoft, Theme.Panel)));
        }

        var available = Math.Max(6, buffer.Height - y - 5);

        // Too short for everything: keep the versions and the command, drop the
        // paragraph in between - the command is the part you would retype.
        if (body.Count + command.Count + 2 > available)
        {
            body = body.Take(essential).ToList();
            body.Add(("Run this to update:", new Sty(Theme.Muted, Theme.Panel)));
        }

        body.AddRange(command);

        var boxHeight = Math.Min(available, body.Count + 2);
        Widgets.TitledBox(buffer, margin, y, width, boxHeight, " Claude Launcher ", Theme.Green);

        for (var i = 0; i < body.Count && i < boxHeight - 2; i++)
        {
            if (body[i].Text.Length == 0) continue;
            buffer.WriteClipped(margin + 3, y + 1 + i, body[i].Text, room, body[i].Style);
        }

        if (_notice is not null)
            buffer.WriteClipped(margin + 1, buffer.Height - 5, _notice, width - 2, new Sty(Theme.Amber, Theme.Bg));

        Widgets.Footer(buffer, new[]
        {
            new KeyHint("↵", "Update now"),
            new KeyHint("n", "Release notes"),
            new KeyHint("s", "Stop asking"),
            new KeyHint("esc", "Later")
        }, KeyMap.Help);
    }

    private string Released() =>
        DateTime.TryParse(_info.PublishedUtc, out var when)
            ? "released " + Sessions.Format.Ago(when.ToUniversalTime())
            : string.Empty;

    /// <summary>
    /// Breaks a command line to fit. A URL is broken after a slash rather than
    /// at a space: wrapping "irm" onto a line of its own and splitting the
    /// address mid-word is harder to read than either half of a path.
    /// </summary>
    private static List<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        var rest = text;

        while (rest.Length > width && lines.Count < 3)
        {
            var slash = rest.LastIndexOf('/', Math.Min(width - 1, rest.Length - 1));
            var cut = slash > width / 2 ? slash + 1 : width;

            lines.Add(rest[..cut]);
            rest = rest[cut..];
        }

        if (rest.Length > 0) lines.Add(rest);
        return lines;
    }

    /// <summary>Ordinary prose wrapping, at spaces.</summary>
    private static List<string> Words(string text, int width)
    {
        var lines = new List<string>();
        var line = string.Empty;

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                lines.Add(line);
                line = word;
                continue;
            }

            line = line.Length == 0 ? word : line + " " + word;
        }

        if (line.Length > 0) lines.Add(line);
        return lines;
    }

    public override ScreenAction HandleKey(ConsoleKeyInfo key)
    {
        _notice = null;

        switch (key.Key)
        {
            case ConsoleKey.F1:
                return ScreenAction.Push(new KeysScreen(App, "Update", KeyMap.Update()));
            case ConsoleKey.Escape:
            case ConsoleKey.Backspace:
                return ScreenAction.Back;

            case ConsoleKey.Enter:
                StateStore.WriteUpdateRequest(_info.Latest);
                return ScreenAction.Exit;
        }

        if (KeyBindings.Is(KeyAction.ReleaseNotes, key))
        {
            Open(_info.Url);
            return ScreenAction.None;
        }

        if (KeyBindings.Is(KeyAction.StopAsking, key))
        {
            App.Settings.CheckForUpdates = false;
            StateStore.SaveSettings(App.Settings);
            UpdateCheck.Forget();
            _notice = "update checks are off · turn them back on in settings";
            return ScreenAction.None;
        }

        if (KeyBindings.Is(KeyAction.Quit, key)) return ScreenAction.Exit;

        return ScreenAction.None;
    }

    private void Open(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            _notice = "this release has no page to open";
            return;
        }

        try
        {
            // UseShellExecute is what hands a URL to the default browser; without
            // it this tries to execute the address as a program.
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            _notice = "opened the release notes in your browser";
        }
        catch (Exception ex)
        {
            _notice = "could not open the page: " + ex.Message;
        }
    }
}
