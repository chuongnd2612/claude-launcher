using System.Text.Json;

namespace ClaudeLauncher;

/// <summary>
/// File plumbing. The PowerShell wrapper stays the source of truth for
/// projects (QuickPaths); this class only reads what it prepares and writes
/// back the launch result plus any newly added profile.
/// </summary>
public static class StateStore
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true
    };

    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string DataDir => Path.Combine(Home, ".claude-launcher");

    public static string StateFile => Environment.GetEnvironmentVariable("CLAUDE_LAUNCHER_STATE")
        ?? Path.Combine(DataDir, "state.json");

    public static string ResultFile => Environment.GetEnvironmentVariable("CLAUDE_LAUNCHER_RESULT")
        ?? Path.Combine(DataDir, "result.json");

    public static string ProfilesFilePath => Environment.GetEnvironmentVariable("CLAUDE_LAUNCHER_PROFILES")
        ?? Path.Combine(DataDir, "profiles.json");

    public static string SettingsFile => Path.Combine(DataDir, "ui.json");

    public static LauncherState LoadState()
    {
        if (!File.Exists(StateFile))
        {
            throw new FileNotFoundException(
                "Launcher state not found. Run the PowerShell wrapper (claude-launcher) so it can prepare the state file.",
                StateFile);
        }

        var json = File.ReadAllText(StateFile);
        var state = new LauncherState();

        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        var root = document.RootElement;
        state.Profiles = ReadList<ProfileEntry>(root, "profiles");
        state.Projects = ReadList<ProjectEntry>(root, "projects");

        state.Profiles = state.Profiles
            .Where(p => !string.IsNullOrWhiteSpace(p.Name) || !string.IsNullOrWhiteSpace(p.Label))
            .ToList();

        if (state.Profiles.Count == 0)
            throw new InvalidOperationException("No Claude profiles configured in " + ProfilesFilePath);

        return state;
    }

    /// <summary>
    /// Reads a property that should hold an array. PowerShell 5.1 sometimes
    /// collapses single element arrays into a bare object, so both shapes are
    /// accepted.
    /// </summary>
    private static List<T> ReadList<T>(JsonElement root, string propertyName) where T : class
    {
        var results = new List<T>();
        if (root.ValueKind != JsonValueKind.Object) return results;
        if (!root.TryGetProperty(propertyName, out var element))
        {
            foreach (var property in root.EnumerateObject())
            {
                if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)) continue;
                element = property.Value;
                break;
            }
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var value = Deserialize<T>(item);
                    if (value is not null) results.Add(value);
                }

                break;
            case JsonValueKind.Object:
                var single = Deserialize<T>(element);
                if (single is not null) results.Add(single);
                break;
        }

        return results;
    }

    private static T? Deserialize<T>(JsonElement element) where T : class
    {
        try { return element.Deserialize<T>(ReadOptions); }
        catch (JsonException) { return null; }
    }

    public static UiSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return new UiSettings();
            return JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(SettingsFile), ReadOptions) ?? new UiSettings();
        }
        catch
        {
            return new UiSettings();
        }
    }

    public static void SaveSettings(UiSettings settings)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(settings, WriteOptions));
        }
        catch
        {
            // Preferences are best effort; never block a launch on them.
        }
    }

    /// <summary>Appends a profile to profiles.json, creating the file when needed.</summary>
    public static void AppendProfile(ProfileEntry profile)
    {
        var file = ReadProfilesFile();
        file.Profiles.Add(profile);
        WriteProfilesFile(file);
    }

    /// <summary>
    /// Replaces the stored profile whose key matches <paramref name="originalName"/>.
    /// Falls back to appending when the file does not know about it yet.
    /// </summary>
    public static void UpdateProfile(string originalName, ProfileEntry profile)
    {
        var file = ReadProfilesFile();
        var index = file.Profiles.FindIndex(p => string.Equals(p.Name, originalName, StringComparison.OrdinalIgnoreCase));

        if (index >= 0) file.Profiles[index] = profile;
        else file.Profiles.Add(profile);

        WriteProfilesFile(file);
    }

    /// <summary>Removes a profile from profiles.json. The config directory is left untouched.</summary>
    public static void RemoveProfile(string name)
    {
        var file = ReadProfilesFile();
        file.Profiles.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        WriteProfilesFile(file);
    }

    /// <summary>Reads profiles.json, backing up and starting fresh when it is not valid JSON.</summary>
    private static ProfilesFile ReadProfilesFile()
    {
        var file = new ProfilesFile();
        if (!File.Exists(ProfilesFilePath)) return file;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(ProfilesFilePath), new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
            file.Profiles = ReadList<ProfileEntry>(document.RootElement, "profiles");
        }
        catch (JsonException)
        {
            File.Copy(ProfilesFilePath, ProfilesFilePath + ".bak", overwrite: true);
            file.Profiles = new List<ProfileEntry>();
        }

        return file;
    }

    private static void WriteProfilesFile(ProfilesFile file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ProfilesFilePath)!);
        File.WriteAllText(ProfilesFilePath, JsonSerializer.Serialize(file, WriteOptions));
    }

    public static void WriteResult(ProfileEntry profile, ProjectEntry project, string mode) =>
        WriteResult(profile, project, mode, LaunchTarget.Current);

    /// <summary>
    /// Writes result.json. <paramref name="openIn"/> is additive: an older wrapper
    /// ignores it, and a missing value means the current console.
    /// </summary>
    public static void WriteResult(ProfileEntry profile, ProjectEntry project, string mode, string openIn)
    {
        var result = new
        {
            profile = profile.Name,
            label = profile.DisplayLabel,
            icon = profile.DisplayIcon,
            configDir = ExpandHome(profile.ConfigDir),
            project = project.Name,
            path = project.Path,
            mode,
            openIn = LaunchTarget.Normalize(openIn)
        };

        var directory = Path.GetDirectoryName(ResultFile);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(ResultFile, JsonSerializer.Serialize(result, WriteOptions));
    }

    public static string ExpandHome(string path) =>
        string.IsNullOrEmpty(path) ? path : path.Replace("$HOME", Home, StringComparison.OrdinalIgnoreCase);

    /// <summary>Stores paths under the user profile with the portable $HOME token.</summary>
    public static string CollapseHome(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        if (path.StartsWith(Home, StringComparison.OrdinalIgnoreCase))
            return "$HOME" + path.Substring(Home.Length).Replace('\\', '/');
        return path;
    }
}
