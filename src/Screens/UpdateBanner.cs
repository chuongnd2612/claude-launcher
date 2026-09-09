using ClaudeLauncher.Tui;

namespace ClaudeLauncher.Screens;

/// <summary>
/// The one line that says where the update has got to, and the key that opens
/// it.
///
/// It lives here rather than on Home because Home is not where most runs start:
/// with nothing running the launcher opens on the profile picker, and an offer
/// that only Home could draw was invisible to anyone who had just opened the
/// launcher to start something.
/// </summary>
public static class UpdateBanner
{
    /// <summary>What to say right now, or null when there is nothing to say.</summary>
    public static (string Text, Rgb Color)? Line()
    {
        switch (UpdateInstall.Stage)
        {
            // The end of the story, so it wins over the offer that started it:
            // there is nothing left to decide, only a launcher to start again.
            case InstallStage.Installed:
                return ($"✓ update installed · {UpdateInstall.Staged} · restart to update", Theme.Green);

            case InstallStage.Downloading:
                return ($"downloading {UpdateInstall.Staged}…", Theme.Dim);

            case InstallStage.Installing:
                return ($"installing {UpdateInstall.Staged}…", Theme.Dim);
        }

        if (UpdateCheck.Available is not null)
        {
            // Amber once the background install has failed: the update is still
            // there to be had, but it is now a thing to go and do by hand.
            var color = UpdateInstall.Stage == InstallStage.Failed ? Theme.Amber : Theme.Green;
            return ($"update available · {UpdateCheck.Available.Latest} · press u", color);
        }

        if (UpdateCheck.Checking) return ("checking for updates…", Theme.Dim);

        return UpdateCheck.Answer is null ? null : (UpdateCheck.Answer, Theme.Dim);
    }

    /// <summary>
    /// Handles u: open whatever there is to say about the update when there is
    /// something, and ask again when there is not - so the key does something
    /// every time it is pressed.
    /// </summary>
    public static ScreenAction Pressed(App app)
    {
        // An install can outlive the offer that started it, so the version it
        // staged stands in for a release we no longer hold the details of.
        var info = UpdateCheck.Available
                   ?? (UpdateInstall.Stage == InstallStage.Installed
                       ? new UpdateInfo { Latest = UpdateInstall.Staged }
                       : null);

        if (info is not null)
            return ScreenAction.Push(new UpdateScreen(app, info,
                UpdateInstall.Stage == InstallStage.Installed));

        UpdateCheck.CheckNow(Program.Version, ConsoleInput.Wake);
        return ScreenAction.None;
    }
}
