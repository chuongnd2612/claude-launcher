using ClaudeLauncher.Tui;

namespace ClaudeLauncher.Screens;

/// <summary>
/// The key that opens whatever the footer's corner is currently saying about
/// the update.
///
/// The line itself moved to <see cref="UpdateStatus"/>, which the footer
/// draws in its own bottom-right corner on every screen; this is what is left
/// once that line no longer needs a screen to remember to draw it.
/// </summary>
public static class UpdateBanner
{
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
