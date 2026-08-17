using System.Text;
using System.Text.Json;

namespace ClaudeLauncher;

/// <summary>
/// The shell's own quick-path registry: a flat name to path map that
/// <c>quick-set</c> writes, <c>cd &lt;name&gt;</c> reads, and the wrapper turns
/// into the launcher's project list.
///
/// Writing here rather than to a file of our own means a project added in the
/// launcher is a real quick path - it works with <c>cd</c> and shows up in
/// <c>quick-list</c> - instead of being known only to this program.
/// </summary>
public static class QuickPaths
{
    /// <summary>
    /// Where quick-path.ps1 keeps the file. The profile builds this from its own
    /// data directory, so the candidates cover both PowerShell editions and a
    /// Documents folder redirected into OneDrive.
    /// </summary>
    public static string? File()
    {
        var explicitPath = Environment.GetEnvironmentVariable("CLAUDE_LAUNCHER_QUICKPATHS");
        if (!string.IsNullOrWhiteSpace(explicitPath)) return explicitPath;

        var home = StateStore.Home;
        var oneDrive = Environment.GetEnvironmentVariable("OneDrive");

        var candidates = new List<string>
        {
            Path.Combine(home, "Documents", "WindowsPowerShell", "data", "quickpaths.json"),
            Path.Combine(home, "Documents", "PowerShell", "data", "quickpaths.json")
        };

        if (!string.IsNullOrWhiteSpace(oneDrive))
        {
            candidates.Add(Path.Combine(oneDrive, "Documents", "WindowsPowerShell", "data", "quickpaths.json"));
            candidates.Add(Path.Combine(oneDrive, "Documents", "PowerShell", "data", "quickpaths.json"));
        }

        foreach (var candidate in candidates)
        {
            if (System.IO.File.Exists(candidate)) return candidate;
        }

        // Nothing on disk yet: offer the first location whose folder exists, so a
        // first quick path can still be written where the shell will look.
        foreach (var candidate in candidates)
        {
            var directory = Path.GetDirectoryName(candidate);
            if (directory is not null && Directory.Exists(Path.GetDirectoryName(directory))) return candidate;
        }

        return null;
    }

    public static Dictionary<string, string> Load()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var path = File();

        if (path is null || !System.IO.File.Exists(path)) return map;

        try
        {
            using var document = JsonDocument.Parse(System.IO.File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object) return map;

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String) continue;
                var value = property.Value.GetString();
                if (!string.IsNullOrWhiteSpace(value)) map[property.Name] = value!;
            }
        }
        catch (Exception)
        {
            // A hand-edited file that no longer parses is the shell's problem,
            // not a reason for the launcher to fall over.
        }

        return map;
    }

    /// <summary>Adds or updates one entry. False when there is nowhere to write.</summary>
    public static bool Save(string name, string path)
    {
        var file = File();
        if (file is null || string.IsNullOrWhiteSpace(name)) return false;

        var map = Load();
        map[name] = path;

        return Write(file, map);
    }

    public static bool Remove(string name)
    {
        var file = File();
        if (file is null) return false;

        var map = Load();
        if (!map.Remove(name)) return false;

        return Write(file, map);
    }

    /// <summary>The name an entry is stored under, or null when the path is not registered.</summary>
    public static string? NameFor(string path)
    {
        foreach (var (name, value) in Load())
        {
            if (Same(value, path)) return name;
        }

        return null;
    }

    public static bool Contains(string path) => NameFor(path) is not null;

    /// <summary>
    /// A quick-path name is typed after <c>cd</c>, so it is squeezed to something
    /// worth typing: lower case, no spaces or separators.
    /// </summary>
    public static string SuggestName(string path)
    {
        var leaf = new DirectoryInfo(path.TrimEnd('\\', '/')).Name;
        var name = new string(leaf.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

        return name.Length == 0 ? "project" : name;
    }

    private static bool Write(string file, Dictionary<string, string> map)
    {
        try
        {
            var directory = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(
                map.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(e => e.Key, e => e.Value),
                new JsonSerializerOptions { WriteIndented = true });

            // Windows PowerShell wrote this file with a BOM, and that is what it
            // expects to read back; keep it rather than quietly changing the
            // encoding of a file the shell owns.
            System.IO.File.WriteAllText(file, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool Same(string a, string b) =>
        string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
}
