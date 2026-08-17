using System.Text.Json;

namespace ClaudeLauncher;

/// <summary>One terminal that was open, and everything needed to open it again.</summary>
public sealed class WorkspaceEntry
{
    public string SessionId { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>Stored per entry: two tiles can belong to different profiles.</summary>
    public string ConfigDir { get; set; } = string.Empty;
}

public sealed class WorkspaceFile
{
    public List<WorkspaceEntry> Terminals { get; set; } = new();
}

/// <summary>
/// The set of terminals that was open last time.
///
/// Terminal tiles are children of the launcher and die with it, which is what
/// keeps them from being orphaned - but it also means an afternoon's worth of
/// open sessions goes away on exit. The conversations survive on disk, so what
/// is missing is only the list of which ones were up; that is what this keeps,
/// so they can all be resumed in one go.
/// </summary>
public static class Workspace
{
    public static string File => Path.Combine(StateStore.DataDir, "workspace.json");

    public static List<WorkspaceEntry> Load()
    {
        try
        {
            if (!System.IO.File.Exists(File)) return new List<WorkspaceEntry>();

            var loaded = JsonSerializer.Deserialize<WorkspaceFile>(
                System.IO.File.ReadAllText(File),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return loaded?.Terminals
                       .Where(t => !string.IsNullOrWhiteSpace(t.SessionId) &&
                                   !string.IsNullOrWhiteSpace(t.ProjectPath))
                       .ToList()
                   ?? new List<WorkspaceEntry>();
        }
        catch (Exception)
        {
            return new List<WorkspaceEntry>();
        }
    }

    public static void Save(IEnumerable<WorkspaceEntry> terminals)
    {
        try
        {
            Directory.CreateDirectory(StateStore.DataDir);
            System.IO.File.WriteAllText(File, JsonSerializer.Serialize(
                new WorkspaceFile { Terminals = terminals.ToList() },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
            // Losing the list costs a few keystrokes next time, not the work.
        }
    }

    /// <summary>
    /// Entries still worth offering: the conversation has to exist on disk, or
    /// --resume would open a tile that immediately fails. A project folder that
    /// has been moved or deleted is dropped for the same reason.
    /// </summary>
    public static List<WorkspaceEntry> Restorable()
    {
        return Load()
            .Where(entry =>
            {
                if (!Directory.Exists(entry.ProjectPath)) return false;
                if (string.IsNullOrWhiteSpace(entry.ConfigDir)) return false;

                var transcript = Sessions.ClaudePaths.TranscriptFile(
                    StateStore.ExpandHome(entry.ConfigDir), entry.ProjectPath, entry.SessionId);

                return System.IO.File.Exists(transcript);
            })
            .ToList();
    }
}
