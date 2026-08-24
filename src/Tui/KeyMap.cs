namespace ClaudeLauncher.Tui;

/// <summary>A titled run of key hints, as the keys screen groups them.</summary>
public readonly struct KeyGroup
{
    public readonly string Title;
    public readonly KeyHint[] Hints;

    public KeyGroup(string title, params KeyHint[] hints)
    {
        Title = title;
        Hints = hints;
    }
}

/// <summary>
/// Every shortcut the launcher answers to, in one place.
///
/// The footers used to carry their own hint lists, which is how keys ended up
/// handled but never advertised - and, on the profile screen, advertised but not
/// handled. A footer shows the few keys worth a permanent line; the keys screen
/// shows all of them; both read from here, so they cannot drift apart again.
///
/// Grouped by *context* rather than by screen, because the terminal wall has
/// five: a focused terminal takes almost every key, and a released one takes
/// almost none. Which chords are live is the thing that was impossible to see.
/// </summary>
public static class KeyMap
{
    /// <summary>
    /// The way in to the full list. F1 rather than '?', which is a character a
    /// chat draft or a filter box is entitled to keep.
    /// </summary>
    public static readonly KeyHint Help = new("f1", "Keys");

    /// <summary>
    /// Usage detail. An Alt chord rather than a letter because it has to work on
    /// the wall too, where a focused terminal takes every plain key - Alt is the
    /// half of the keyboard Claude's own UI leaves alone. Plain 'u' is already
    /// the update check.
    /// </summary>
    public static readonly KeyHint UsageKey = new("alt+u", "Usage");

    private static KeyGroup[] With(params KeyGroup[] groups) => groups;

    // ---- Usage ---------------------------------------------------------------

    public static KeyHint[] UsageFooter() => new[]
    {
        new KeyHint("p", "Period"),
        new KeyHint("r", "Refresh"),
        new KeyHint("↑↓", "Account"),
        new KeyHint("esc", "Back")
    };

    public static KeyGroup[] Usage() => With(
        new KeyGroup("Reading it",
            new KeyHint("p", "Today, this week, all time"),
            new KeyHint("r", "Read it again"),
            new KeyHint("↑↓", "Between accounts")),
        new KeyGroup("What the numbers mean",
            new KeyHint("", "Sessions and prompts follow the period."),
            new KeyHint("", "Cost and tokens are Claude's running"),
            new KeyHint("", "totals - they carry no dates.")),
        new KeyGroup("Leaving",
            new KeyHint("esc bksp", "Back"),
            new KeyHint("q", "Quit"),
            Help));

    // ---- Home ----------------------------------------------------------------

    public static KeyHint[] HomeFooter(bool restorable) => restorable
        ? new[] { new KeyHint("↑↓", "Navigate"), new KeyHint("↵", "Open"), new KeyHint("r", "Reopen"), new KeyHint("n", "New") }
        : new[] { new KeyHint("↑↓", "Navigate"), new KeyHint("↵", "Open"), new KeyHint("n", "New") };

    public static KeyGroup[] Home(bool restorable) => With(
        new KeyGroup("Move around",
            new KeyHint("↑↓", "Previous / next session"),
            new KeyHint("tab", "Next session"),
            new KeyHint("home end", "First / last"),
            new KeyHint("↵", "Open the wall on this session")),
        new KeyGroup("Sessions",
            new KeyHint("n / p", "New session (profile picker)"),
            new KeyHint("a", "Attach to this session"),
            new KeyHint("t", "Show the terminal wall"),
            restorable
                ? new KeyHint("r", "Reopen the terminals from last time")
                : new KeyHint("r", "Reopen last terminals (none saved)"),
            new KeyHint("k", "Stop a session")),
        new KeyGroup("Elsewhere",
            new KeyHint("d", "Dashboard"),
            UsageKey,
            new KeyHint("s", "Settings"),
            new KeyHint("u", "Check for updates"),
            new KeyHint("q / esc", "Quit the launcher")));

    // ---- Terminal wall -------------------------------------------------------

    /// <summary>
    /// Zoom earns its place over a new terminal: the narrower the window, the
    /// less any one tile shows, and the more often you want one of them whole.
    /// </summary>
    public static KeyHint[] WallFooter(int panes) => new[]
    {
        new KeyHint(panes > 4 ? "1-9" : "1-4", "Focus"),
        new KeyHint("↵", "Attach"),
        new KeyHint("z", "Zoom"),
        new KeyHint("space", "Layout")
    };

    /// <summary>The wall itself: no tile is holding the keyboard.</summary>
    public static KeyGroup[] Wall(bool splitting) => With(
        new KeyGroup("Panes",
            new KeyHint("1-9", "Focus that pane"),
            new KeyHint("←→↑↓ tab", "Step between panes"),
            new KeyHint("↵", "Attach in Windows Terminal"),
            new KeyHint("w", "Close this pane"),
            new KeyHint("t", "New terminal"),
            new KeyHint("n", "New session")),
        new KeyGroup("Arranging",
            new KeyHint("^⇧←→", "Move this pane earlier / later"),
            new KeyHint("^⇧↑↓", "Same, one step at a time"),
            new KeyHint("drag a tile", "Move it onto another"),
            new KeyHint("drag a gutter", "Resize"),
            new KeyHint("alt+⇧←→↑↓", "Resize with the keyboard"),
            new KeyHint("alt+⇧0", "Even the shares out")),
        new KeyGroup("Layout",
            new KeyHint("space / ^l", "Tiled, stacked, focus"),
            new KeyHint("z / alt+z", "Zoom this pane"),
            splitting
                ? new KeyHint("v / s", "Split right / down")
                : new KeyHint("v / s", "Split (off while tiles are on)")),
        new KeyGroup("Leaving",
            UsageKey,
            new KeyHint("esc", "Back to Home"),
            new KeyHint("q", "Quit"),
            Help));

    public static KeyHint[] TerminalFooter(bool zoomed) => new[]
    {
        new KeyHint("type", "Claude's own UI"),
        new KeyHint("alt+z", zoomed ? "Wall" : "Zoom"),
        new KeyHint("alt+1-9", "Pane"),
        new KeyHint("^]", "Release")
    };

    /// <summary>
    /// A focused terminal. Everything not listed goes to Claude, Esc and Tab
    /// included - which is why the release key is the one that matters here.
    /// </summary>
    public static KeyGroup[] Terminal() => With(
        new KeyGroup("This pane",
            new KeyHint("type", "Goes straight to Claude"),
            new KeyHint("^]", "Take the keyboard back"),
            new KeyHint("^f / alt+f", "Find in this pane"),
            new KeyHint("^w / alt+w", "Close this pane"),
            new KeyHint("alt+s", "Select text with the mouse"),
            new KeyHint("⇧pgup/pgdn", "Scroll this pane's history")),
        new KeyGroup("Panes",
            new KeyHint("alt+1-9", "Focus that pane"),
            new KeyHint("alt+←→↑↓", "Step between panes"),
            new KeyHint("alt+z", "Zoom this pane"),
            new KeyHint("^t / alt+t", "New terminal")),
        new KeyGroup("Arranging",
            new KeyHint("^⇧←→↑↓", "Move this pane"),
            new KeyHint("alt+⇧←→↑↓", "Resize"),
            new KeyHint("alt+⇧0", "Even the shares out"),
            new KeyHint("drag a tile", "Move it onto another")),
        new KeyGroup("Note",
            new KeyHint("esc", "Goes to Claude, not back"),
            UsageKey,
            Help));

    public static KeyHint[] ReleasedFooter() => new[]
    {
        new KeyHint("↵", "Type again"),
        new KeyHint("1-9", "Focus"),
        new KeyHint("space", "Layout"),
        new KeyHint("esc", "Back")
    };

    /// <summary>A terminal tile that has handed the keyboard back to the wall.</summary>
    public static KeyGroup[] Released() => With(
        new KeyGroup("This pane",
            new KeyHint("↵", "Start typing into it again"),
            new KeyHint("^]", "Same, the other way round"),
            new KeyHint("^f / alt+f", "Find in this pane"),
            new KeyHint("⇧pgup/pgdn", "Scroll its history")),
        new KeyGroup("The wall",
            new KeyHint("1-9", "Focus that pane"),
            new KeyHint("←→↑↓ tab", "Step between panes"),
            new KeyHint("space / ^l", "Layout"),
            new KeyHint("z", "Zoom"),
            new KeyHint("w", "Close this pane"),
            new KeyHint("t", "New terminal")),
        new KeyGroup("Arranging",
            new KeyHint("^⇧←→↑↓", "Move this pane"),
            new KeyHint("alt+⇧←→↑↓", "Resize"),
            new KeyHint("drag a tile", "Move it onto another")),
        new KeyGroup("Leaving",
            new KeyHint("esc", "Back to Home"),
            new KeyHint("q", "Quit"),
            Help));

    public static KeyHint[] ChatFooter(bool pending) => pending
        ? new[] { new KeyHint("y", "Allow"), new KeyHint("a", "Always"), new KeyHint("n", "Deny"), new KeyHint("esc", "Back") }
        : new[] { new KeyHint("type", "Message"), new KeyHint("↵", "Send"), new KeyHint("↑↓ tab", "Tile"), new KeyHint("esc", "Back") };

    /// <summary>A chat tile on the wall.</summary>
    public static KeyGroup[] ChatTile(bool pending) => With(
        pending
            ? new KeyGroup("This permission",
                new KeyHint("y", "Allow once"),
                new KeyHint("a", "Always allow"),
                new KeyHint("n", "Deny"),
                new KeyHint("esc", "Back to Home"))
            : new KeyGroup("Writing",
                new KeyHint("type", "Compose a message"),
                new KeyHint("/", "Slash commands"),
                new KeyHint("↵", "Send, or accept a command"),
                new KeyHint("tab", "Complete a command"),
                new KeyHint("esc", "Clear, then stop, then Home")),
        new KeyGroup("Panes",
            new KeyHint("↑↓ ←→ tab", "Step between tiles"),
            new KeyHint("^t", "New terminal"),
            new KeyHint("^z", "Zoom"),
            new KeyHint("^l", "Layout"),
            new KeyHint("^w", "Hide this tile")),
        new KeyGroup("Arranging",
            new KeyHint("^⇧←→↑↓", "Move this tile"),
            new KeyHint("alt+⇧←→↑↓", "Resize"),
            new KeyHint("drag a tile", "Move it onto another")),
        new KeyGroup("Note", Help));

    public static KeyHint[] FindFooter() => new[]
    {
        new KeyHint("↵", "Next"),
        new KeyHint("tab", "Whole session"),
        new KeyHint("↑↓", "Hit"),
        new KeyHint("esc", "Close")
    };

    public static KeyGroup[] Find() => With(
        new KeyGroup("Finding",
            new KeyHint("type", "What to look for"),
            new KeyHint("↵", "Next hit"),
            new KeyHint("⇧↵", "Previous hit"),
            new KeyHint("↑↓", "Step through hits"),
            new KeyHint("tab", "Search the whole session"),
            new KeyHint("^f / esc", "Close the find bar")));

    /// <summary>The full chat view, which scrolls and detaches where a tile cannot.</summary>
    public static KeyGroup[] Chat(bool pending, bool working) => With(
        pending
            ? new KeyGroup("This permission",
                new KeyHint("y", "Allow once"),
                new KeyHint("a", "Always allow"),
                new KeyHint("n", "Deny"),
                new KeyHint("esc", "Back"))
            : new KeyGroup("Writing",
                new KeyHint("type", working ? "Ignored while Claude is working" : "Compose a message"),
                new KeyHint("/", "Slash commands"),
                new KeyHint("↵", "Send, or accept a command"),
                new KeyHint("tab", "Complete a command"),
                new KeyHint("bksp", "Delete a character")),
        new KeyGroup("Reading",
            new KeyHint("↑↓", "Scroll a line"),
            new KeyHint("pgup pgdn", "By eight"),
            new KeyHint("end", "Follow the newest again")),
        new KeyGroup("Leaving",
            new KeyHint("^d", "Detach into a real pane"),
            new KeyHint("esc", working ? "Stop the turn, then Home" : "Clear, then Home"),
            Help));

    // ---- Profiles and projects ----------------------------------------------

    public static KeyHint[] ProfileFooter() => new[]
    {
        new KeyHint("↑↓←→", "Navigate"),
        new KeyHint("↵", "Select"),
        new KeyHint("a", "Add"),
        new KeyHint("e", "Edit")
    };

    public static KeyGroup[] Profile() => With(
        new KeyGroup("Choosing",
            new KeyHint("↑↓←→ tab", "Move between tiles"),
            new KeyHint("home end", "First / last"),
            new KeyHint("1-9", "Pick that profile"),
            new KeyHint("↵ space", "Use this profile")),
        new KeyGroup("Editing",
            new KeyHint("a", "Add a profile"),
            new KeyHint("e", "Edit this one"),
            new KeyHint("x / del", "Remove this one")),
        new KeyGroup("Elsewhere",
            new KeyHint("d", "Dashboard"),
            UsageKey,
            new KeyHint("s", "Settings"),
            new KeyHint("u", "Check for updates"),
            new KeyHint("esc", "Back"),
            new KeyHint("q", "Quit"),
            Help));

    public static KeyHint[] ProjectFooter() => new[]
    {
        new KeyHint("↑↓", "Navigate"),
        new KeyHint("↵", "Select"),
        new KeyHint("a", "Add"),
        new KeyHint("/", "Filter")
    };

    public static KeyGroup[] Project() => With(
        new KeyGroup("Choosing",
            new KeyHint("↑↓", "Previous / next"),
            new KeyHint("pgup pgdn", "By eight"),
            new KeyHint("home end", "First / last"),
            new KeyHint("↵", "Use this folder")),
        new KeyGroup("The list",
            new KeyHint("/", "Filter by name"),
            new KeyHint("a", "Add a folder"),
            new KeyHint("d", "Forget a folder")),
        new KeyGroup("Leaving",
            new KeyHint("esc bksp", "Back"),
            new KeyHint("q", "Quit"),
            Help));

    public static KeyHint[] SessionFooter(bool many) => many
        ? new[] { new KeyHint("↑↓", "Navigate"), new KeyHint("↵", "Launch"), new KeyHint("p", "Profile"), new KeyHint("o", "Open in") }
        : new[] { new KeyHint("↑↓", "Navigate"), new KeyHint("↵", "Launch"), new KeyHint("o", "Open in") };

    public static KeyGroup[] Session() => With(
        new KeyGroup("Choosing",
            new KeyHint("↑↓ tab", "Between modes"),
            new KeyHint("↵ space", "Launch this mode")),
        new KeyGroup("Quick modes",
            new KeyHint("n", "New session"),
            new KeyHint("c", "Continue the last one"),
            new KeyHint("r", "Resume a specific one"),
            new KeyHint("h", "Open in the chat view")),
        new KeyGroup("Where it opens",
            new KeyHint("o / ←→", "This console, a tab, or a pane"),
            new KeyHint("p", "Switch profile")),
        new KeyGroup("Leaving",
            new KeyHint("esc bksp", "Back"),
            new KeyHint("q", "Quit"),
            Help));

    // ---- The rest ------------------------------------------------------------

    public static KeyHint[] DashboardFooter() => new[]
    {
        new KeyHint("p", "Period"),
        new KeyHint("r", "Refresh"),
        new KeyHint("↑↓", "Project"),
        new KeyHint("↵", "Sessions")
    };

    public static KeyGroup[] Dashboard() => With(
        new KeyGroup("Reading it",
            new KeyHint("p", "Today, this week, all time"),
            new KeyHint("r", "Recount from disk"),
            new KeyHint("↑↓", "Between projects"),
            new KeyHint("↵", "That project's sessions"),
            UsageKey),
        new KeyGroup("Leaving",
            new KeyHint("esc bksp", "Back"),
            new KeyHint("q", "Quit"),
            Help));

    public static KeyHint[] SettingsFooter() => new[]
    {
        new KeyHint("↑↓", "Navigate"),
        new KeyHint("↵/←→", "Change"),
        new KeyHint("u", "Check now")
    };

    public static KeyGroup[] Settings() => With(
        new KeyGroup("Changing",
            new KeyHint("↑↓ tab", "Between settings"),
            new KeyHint("↵ space →", "Next value"),
            new KeyHint("←", "Previous value")),
        new KeyGroup("Leaving",
            new KeyHint("u", "Check for updates now"),
            new KeyHint("esc / q / s", "Back - q does not quit here"),
            Help));

    public static KeyHint[] ResumeFooter() => new[]
    {
        new KeyHint("↑↓", "Navigate"),
        new KeyHint("↵", "Resume"),
        new KeyHint("c", "Chat view"),
        new KeyHint("/", "Filter")
    };

    public static KeyGroup[] Resume() => With(
        new KeyGroup("Choosing",
            new KeyHint("↑↓", "Previous / next"),
            new KeyHint("pgup pgdn", "By eight"),
            new KeyHint("home end", "First / last"),
            new KeyHint("/", "Filter")),
        new KeyGroup("Opening one",
            new KeyHint("↵", "Resume it"),
            new KeyHint("t", "Force a terminal tile"),
            new KeyHint("c", "Open in the chat view"),
            new KeyHint("l", "Read the log"),
            new KeyHint("d", "Delete it")),
        new KeyGroup("Leaving",
            new KeyHint("esc bksp", "Back"),
            new KeyHint("q", "Quit"),
            Help));

    public static KeyHint[] DetailFooter() => new[]
    {
        new KeyHint("↑↓", "Scroll"),
        new KeyHint("pgup/pgdn", "Page")
    };

    public static KeyGroup[] Detail() => With(
        new KeyGroup("Reading",
            new KeyHint("↑↓", "Line by line"),
            new KeyHint("pgup pgdn", "By ten"),
            new KeyHint("home end", "Top / bottom")),
        new KeyGroup("Leaving",
            new KeyHint("esc bksp", "Back"),
            new KeyHint("q", "Quit"),
            Help));

    public static KeyHint[] HistoryFooter() => new[]
    {
        new KeyHint("↑↓", "Move"),
        new KeyHint("/", "Search again")
    };

    public static KeyGroup[] History() => With(
        new KeyGroup("Reading",
            new KeyHint("↑↓", "Previous / next hit"),
            new KeyHint("pgup pgdn", "By ten"),
            new KeyHint("home end", "First / last")),
        new KeyGroup("Searching",
            new KeyHint("/ f ^f", "Search again"),
            new KeyHint("↵", "Run the search"),
            new KeyHint("esc", "Keep the old query")),
        new KeyGroup("Leaving",
            new KeyHint("esc bksp q", "Back - q does not quit here"),
            Help));

    public static KeyHint[] NewTerminalFooter() => new[]
    {
        new KeyHint("↑↓", "Navigate"),
        new KeyHint("↵", "Start"),
        new KeyHint("a", "Add folder"),
        new KeyHint("/", "Filter")
    };

    public static KeyGroup[] NewTerminal() => With(
        new KeyGroup("Choosing",
            new KeyHint("↑↓ tab", "Previous / next"),
            new KeyHint("pgup pgdn", "By five"),
            new KeyHint("↵", "New, continue or resume here")),
        new KeyGroup("The list",
            new KeyHint("/", "Filter by name"),
            new KeyHint("a", "Add a folder"),
            new KeyHint("d", "Forget a folder")),
        new KeyGroup("Leaving",
            new KeyHint("esc", "Back"),
            Help));

    public static KeyGroup[] Confirm() => With(
        new KeyGroup("Answering",
            new KeyHint("y", "Yes, do it"),
            new KeyHint("n", "No"),
            new KeyHint("←→ tab", "Move between the buttons"),
            new KeyHint("↵", "The button that is selected"),
            new KeyHint("esc", "Cancel")));

    public static KeyGroup[] AddProfile(bool onIcon) => With(
        new KeyGroup("Filling it in",
            new KeyHint("↑↓ tab", "Between fields"),
            new KeyHint("type", "Into this field"),
            new KeyHint("bksp", "Delete a character"),
            onIcon
                ? new KeyHint("←→", "Cycle the icon")
                : new KeyHint("←→", "Cycle the icon, on the icon field")),
        new KeyGroup("Finishing",
            new KeyHint("↵", "Save"),
            new KeyHint("esc", "Cancel"),
            Help));

    public static KeyGroup[] Update() => With(
        new KeyGroup("This release",
            new KeyHint("↵", "Update now"),
            new KeyHint("n", "Read the release notes"),
            new KeyHint("s", "Stop asking about updates")),
        new KeyGroup("Leaving",
            new KeyHint("esc bksp", "Later"),
            new KeyHint("q", "Quit"),
            Help));

    public static KeyGroup[] Preview() => With(
        new KeyGroup("This preview",
            new KeyHint("esc", "Back")),
        new KeyGroup("Note",
            new KeyHint("", "A replayed capture, not a live session -"),
            new KeyHint("", "there is nothing here to type into."),
            Help));
}
