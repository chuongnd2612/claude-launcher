namespace ClaudeLauncher;

public sealed class ProfileEntry
{
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string ConfigDir { get; set; } = string.Empty;
    public string? Description { get; set; }

    public string DisplayIcon => string.IsNullOrWhiteSpace(Icon)
        ? (string.IsNullOrEmpty(Label) ? "?" : Label.Substring(0, 1).ToUpperInvariant())
        : Icon.Trim();

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

/// <summary>Persisted UI preferences (~/.claude-launcher/ui.json).</summary>
public sealed class UiSettings
{
    public bool PaintBackground { get; set; } = true;
    public bool ShowTips { get; set; } = true;
    public string DefaultMode { get; set; } = "new";

    public static readonly string[] Modes = { "new", "continue", "resume" };
}
