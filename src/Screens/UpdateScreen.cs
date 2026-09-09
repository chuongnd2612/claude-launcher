using System.Diagnostics;
using ClaudeLauncher.Tui;

namespace ClaudeLauncher.Screens;

/// <summary>
/// Says what has happened about the newer release, in one of two states.
///
/// Installed is the ordinary one: <see cref="UpdateInstall"/> has already put
/// the new build on disk in the background, and all that is left is to start
/// the launcher again - so this screen only says so, and quits.
///
/// Available is the fallback for when that could not happen: auto-install is
/// off, this is not an installed launcher, or the download failed. The launcher
/// cannot overwrite the exe it is running from, so "update now" writes the
/// request and quits, and the wrapper runs the installer once the process is
/// gone.
/// </summary>
public sealed class UpdateScreen : ScreenBase
{
    private readonly UpdateInfo _info;

    /// <summary>
    /// Passed in rather than read off <see cref="UpdateInstall"/> here, so what
    /// this screen draws is decided once, by whoever opened it - a render check
    /// can then ask for either state without a download having happened.
    /// </summary>
    private readonly bool _installed;

    private string? _notice;

    public UpdateScreen(App app, UpdateInfo info, bool installed = false) : base(app)
    {
        _info = info;
        _installed = installed;
    }

    /// <summary>Column the two version numbers line up in, whichever words label them.</summary>
    private const int Label = 13;

    private static string Command =>
        "irm https://raw.githubusercontent.com/chuongnd2612/claude-launcher/main/install-online.ps1 | iex";

    public override void Render(ScreenBuffer buffer)
    {
        var y = Widgets.CompactChrome(buffer);
        var margin = Widgets.Margin(buffer);
        var width = buffer.Width - margin * 2;
        var room = Math.Max(20, width - 6);

        Widgets.SectionTitle(buffer, y, "Home", _installed ? "Update installed" : "Update available");
        y += 2;

        // Built as a list first, so a short window can drop the explanation and
        // still show the two things that matter: the versions and the command.
        var body = new List<(string Text, Sty Style)>
        {
            ((_installed ? "Running" : "Installed").PadRight(Label) + $"v{Program.Version}",
                new Sty(Theme.TextSoft, Theme.Panel)),
            ((_installed ? "Installed" : "Available").PadRight(Label) + _info.Latest +
             (Released().Length > 0 ? "  " + Released() : ""),
                new Sty(Theme.Green, Theme.Panel, bold: true)),
            (string.Empty, new Sty(Theme.Panel, Theme.Panel))
        };

        var essential = body.Count;

        foreach (var part in Words(_installed
            ? "The new build is already on disk, downloaded and checked while you worked. " +
              "Enter closes the launcher; start it again with claude-launcher and it runs the new version."
            : "Enter closes the launcher and runs the installer, which replaces the exe this one " +
              "is running from - then start it again with claude-launcher.", room))
        {
            body.Add((part, new Sty(Theme.TextSoft, Theme.Panel)));
        }

        var command = new List<(string, Sty)>();

        // Nothing left to type once it is installed, so the command that would
        // do it by hand is not offered - it would only re-download the same zip.
        if (!_installed)
        {
            body.Add((string.Empty, new Sty(Theme.Panel, Theme.Panel)));
            body.Add(("Or run it yourself:", new Sty(Theme.Muted, Theme.Panel)));

            foreach (var part in Wrap(Command, room))
            {
                command.Add((part, new Sty(Theme.VioletSoft, Theme.Panel)));
            }
        }

        var available = Math.Max(6, buffer.Height - y - 5);

        // Too short for everything: keep the versions and the command, drop the
        // paragraph in between - the command is the part you would retype.
        if (body.Count + command.Count + 2 > available)
        {
            body = body.Take(essential).ToList();
            if (!_installed) body.Add(("Run this to update:", new Sty(Theme.Muted, Theme.Panel)));
        }

        body.AddRange(command);

        var boxHeight = Math.Min(available, body.Count + 2);
        Widgets.TitledBox(buffer, margin, y, width, boxHeight, " Claude Launcher ", Theme.Green);

        for (var i = 0; i < body.Count && i < boxHeight - 2; i++)
        {
            if (body[i].Text.Length == 0) continue;
            buffer.WriteClipped(margin + 3, y + 1 + i, body[i].Text, room, body[i].Style);
        }

        // A failed background install is the reason this screen is offering a
        // command at all, so say what went wrong rather than leaving it a
        // mystery why the update did not just happen.
        var notice = _notice ?? (!_installed && UpdateInstall.Stage == InstallStage.Failed
            ? "could not install it in the background: " + UpdateInstall.Error
            : null);

        if (notice is not null)
            buffer.WriteClipped(margin + 1, buffer.Height - 5, notice, width - 2, new Sty(Theme.Amber, Theme.Bg));

        Widgets.Footer(buffer, new[]
        {
            new KeyHint("↵", _installed ? "Quit and restart" : "Update now"),
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
                // Already on disk: quitting is the whole of the update, and
                // asking the wrapper to install it again would only re-download
                // the build that is sitting there.
                if (_installed) return ScreenAction.Exit;

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
