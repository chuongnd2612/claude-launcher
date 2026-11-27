using System.Text.Json;

namespace ClaudeLauncher.Sessions;

/// <summary>Session ids handed to Claude by the launcher.</summary>
public static class LauncherSessions
{
    private sealed class FileModel
    {
        public List<string> Sessions { get; set; } = new();
    }

    public static string File => Path.Combine(StateStore.DataDir, "sessions.json");

    public static IReadOnlyCollection<string> Load()
    {
        try
        {
            if (!System.IO.File.Exists(File)) return Array.Empty<string>();

            var model = JsonSerializer.Deserialize<FileModel>(System.IO.File.ReadAllText(File),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return model?.Sessions
                       .Where(id => !string.IsNullOrWhiteSpace(id))
                       .Distinct(StringComparer.OrdinalIgnoreCase)
                       .ToArray()
                   ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static void Remember(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;

        try
        {
            var ids = Load().Where(id => !string.Equals(id, sessionId, StringComparison.OrdinalIgnoreCase))
                .Prepend(sessionId)
                .Take(100)
                .ToList();

            Directory.CreateDirectory(StateStore.DataDir);
            System.IO.File.WriteAllText(File, JsonSerializer.Serialize(
                new FileModel { Sessions = ids },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Losing ownership history may hide a tile; it must never block a launch.
        }
    }
}
