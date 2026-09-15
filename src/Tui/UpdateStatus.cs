namespace ClaudeLauncher.Tui;

/// <summary>
/// What to say about a pending update right now, or null when there is
/// nothing to say.
///
/// Lives here rather than in Screens because <see cref="Widgets.Footer"/>
/// draws it directly, in the same bottom-right corner every screen's footer
/// already reserves for the version number - that is what makes it visible
/// everywhere rather than only on the screens that used to remember to draw
/// it. <see cref="Screens.UpdateBanner"/> still answers the key that opens
/// the detail screen; this is only the line.
/// </summary>
public static class UpdateStatus
{
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
}
