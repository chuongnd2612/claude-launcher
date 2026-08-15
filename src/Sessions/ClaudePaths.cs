namespace ClaudeLauncher.Sessions;

/// <summary>Locations inside a Claude config dir (CLAUDE_CONFIG_DIR).</summary>
public static class ClaudePaths
{
    public static string SessionsDir(string configDir) => Path.Combine(configDir, "sessions");

    public static string ProjectsDir(string configDir) => Path.Combine(configDir, "projects");

    public static string HistoryFile(string configDir) => Path.Combine(configDir, "history.jsonl");

    public static string TranscriptFile(string configDir, string cwd, string sessionId) =>
        Path.Combine(ProjectsDir(configDir), EncodeProjectDir(cwd), sessionId + ".jsonl");

    /// <summary>
    /// Claude's project folder key: every separator, drive colon, dot, underscore
    /// and space becomes '-', case preserved. Verified against every folder in a
    /// real config dir.
    ///
    /// Lossy on purpose - "emehub\api" and "emehub-api" both encode to the same
    /// key - so only ever go path to folder. Recover a real path from the "cwd"
    /// recorded inside the transcript instead.
    /// </summary>
    public static string EncodeProjectDir(string absolutePath)
    {
        var trimmed = absolutePath.TrimEnd('\\', '/');
        var chars = new char[trimmed.Length];

        for (var i = 0; i < trimmed.Length; i++)
        {
            var ch = trimmed[i];
            chars[i] = ch is ':' or '\\' or '/' or '.' or '_' or ' ' ? '-' : ch;
        }

        return new string(chars);
    }
}
