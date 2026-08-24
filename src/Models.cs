using System.Text.Json.Serialization;

namespace ClaudeLauncher;

public sealed class ProfileEntry
{
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string ConfigDir { get; set; } = string.Empty;
    public string? Description { get; set; }

    [JsonIgnore]
    public string DisplayIcon => string.IsNullOrWhiteSpace(Icon)
        ? (string.IsNullOrEmpty(Label) ? "?" : Label.Substring(0, 1).ToUpperInvariant())
        : Icon.Trim();

    [JsonIgnore]
    public string DisplayLabel => string.IsNullOrWhiteSpace(Label) ? Name : Label;

    public string DescriptionOr(bool isFirst) =>
        !string.IsNullOrWhiteSpace(Description)
            ? Description!
            : isFirst
                ? "Default profile"
                : $"{DisplayLabel} profile";
}

public sealed class ProjectEntry
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}

public sealed class LauncherState
{
    public List<ProfileEntry> Profiles { get; set; } = new();
    public List<ProjectEntry> Projects { get; set; } = new();
}

public sealed class ProfilesFile
{
    public List<ProfileEntry> Profiles { get; set; } = new();
}

/// <summary>Projects added from inside the launcher, kept beside the wrapper's own list.</summary>
public sealed class ProjectsFileModel
{
    public List<ProjectEntry> Projects { get; set; } = new();
}

/// <summary>Where a launch lands: this console, or a Windows Terminal tab / pane.</summary>
public static class LaunchTarget
{
    public const string Current = "current";
    public const string Tab = "tab";
    public const string Right = "right";
    public const string Down = "down";

    /// <summary>Cycle order for the "o" key on the session screen.</summary>
    public static readonly string[] All = { Current, Tab, Right, Down };

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Current;
        var trimmed = value.Trim().ToLowerInvariant();
        return Array.IndexOf(All, trimmed) >= 0 ? trimmed : Current;
    }

    public static string Next(string current, int direction = 1)
    {
        var index = Array.IndexOf(All, Normalize(current));
        if (index < 0) index = 0;
        return All[(index + direction + All.Length) % All.Length];
    }

    /// <summary>Short label for the settings screen; its value column is 12 cells.</summary>
    public static string Label(string target) => Normalize(target) switch
    {
        Tab => "new tab",
        Right => "split right",
        Down => "split down",
        _ => "current"
    };

    /// <summary>The "Opens in" row on the launch summary.</summary>
    public static string Describe(string target) => Normalize(target) switch
    {
        Tab => "New tab in this Windows Terminal window · press o to change",
        Right => "New pane · split right · press o to change",
        Down => "New pane · split down · press o to change",
        _ => "This terminal · press o to change"
    };
}

/// <summary>Persisted UI preferences (~/.claude-launcher/ui.json).</summary>
public sealed class UiSettings
{
    public bool PaintBackground { get; set; } = true;
    public bool ShowTips { get; set; } = true;
    public string DefaultMode { get; set; } = "new";
    public string DefaultOpenIn { get; set; } = LaunchTarget.Current;

    /// <summary>
    /// Ask GitHub whether a newer release exists, at most once every six hours,
    /// and say so when there is. On by default; the check is a single request to
    /// the public releases API, sends nothing about you, and never blocks the
    /// launcher. CLAUDE_LAUNCHER_NO_UPDATE_CHECK=1 turns it off for one run.
    /// </summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>
    /// Show what Claude has cost on the dashboard. On by default - it is the
    /// number people open that screen for - but a single switch hides every
    /// figure for anyone who shares their screen.
    /// </summary>
    public bool ShowCosts { get; set; } = true;

    /// <summary>Dashboard period: today, week or all.</summary>
    public string DashboardPeriod { get; set; } = "today";

    /// <summary>Terminal wall layout: tiled, stacked or focus.</summary>
    public string TerminalLayout { get; set; } = "tiled";

    /// <summary>
    /// Where the wall's dividers sit, per number of columns and rows, as
    /// "2:0.62,0.38". Empty means equal shares, which is what it was before the
    /// dividers could be moved.
    /// </summary>
    public string TerminalSplits { get; set; } = string.Empty;

    /// <summary>
    /// The order tiles sit in, as session ids - or a project path, for a chat
    /// that has no id yet - joined by '|', which neither can contain. Ids that
    /// are not on the wall are kept anyway, so a session closed today keeps its
    /// slot when it is resumed tomorrow. Empty means the order tiles were first
    /// seen in, which is what it was before they could be moved.
    /// </summary>
    public string TerminalOrder { get; set; } = string.Empty;

    /// <summary>
    /// Start new sessions with claude --remote-control, so they accept input
    /// from claude.ai and the phone app. Off by default: it opens a relay
    /// through Anthropic's servers, which is the user's call to make.
    /// </summary>
    public bool RemoteControl { get; set; }

    /// <summary>
    /// Run in-launcher sessions under a pseudo console, showing Claude's own
    /// interface, instead of the launcher's styled chat view. On by default:
    /// exact rendering beats our own styling for anything Claude draws itself.
    /// </summary>
    public bool TerminalTiles { get; set; } = true;

    public static readonly string[] Modes = { "new", "continue", "resume" };
}
