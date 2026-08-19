using ClaudeLauncher.Tui;

namespace ClaudeLauncher.Screens;

/// <summary>
/// The one line that says whether there is a newer version, and the key that
/// asks again.
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
        if (UpdateCheck.Available is not null)
            return ($"update available · {UpdateCheck.Available.Latest} · press u", Theme.Green);

        if (UpdateCheck.Checking) return ("checking for updates…", Theme.Dim);

        return UpdateCheck.Answer is null ? null : (UpdateCheck.Answer, Theme.Dim);
    }

    /// <summary>
    /// Handles u: open the offer when there is one, and ask again when there is
    /// not - so the key does something every time it is pressed.
    /// </summary>
    public static ScreenAction Pressed(App app)
    {
        if (UpdateCheck.Available is not null)
            return ScreenAction.Push(new UpdateScreen(app, UpdateCheck.Available));

        UpdateCheck.CheckNow(Program.Version, ConsoleInput.Wake);
        return ScreenAction.None;
    }
}
