using ClaudeLauncher.Tui;

namespace ClaudeLauncher.Screens;

/// <summary>
/// Adding and forgetting projects, shared by every screen that lists them.
///
/// Both the wizard's project step and the new-terminal picker manage the same
/// list, and a folder added in one belongs in the other. Keeping the rules here
/// - what a valid path is, where it is saved, what a name means - means they
/// cannot drift apart.
/// </summary>
public sealed class ProjectEditor
{
    private readonly App _app;
    private readonly List<ProjectEntry> _projects;

    private string _draft = string.Empty;
    private string? _pendingPath;

    /// <summary>
    /// True while the name still holds the suggestion nobody has touched. Typing
    /// then replaces it rather than appending, which is what a pre-filled field
    /// is expected to do - otherwise a typed name lands glued onto the guess.
    /// </summary>
    private bool _suggested;

    /// <summary>Folders matching what has been typed, recomputed as it changes.</summary>
    private readonly List<string> _matches = new();
    private string _matchedOn = "\u0001";
    private int _matchIndex;

    /// <summary>More than this and the dropdown crowds out the list behind it.</summary>
    private const int MaxMatches = 6;

    public ProjectEditor(App app, List<ProjectEntry> projects)
    {
        _app = app;
        _projects = projects;
    }

    /// <summary>True while a path or a name is being typed; the owner must let it have the keys.</summary>
    public bool Active { get; private set; }

    public string? Notice { get; set; }

    /// <summary>Index the owner should select, set when a project is added.</summary>
    public int? Select { get; private set; }

    /// <summary>Rows the editor needs below the list, dropdown included.</summary>
    public int Height => Active ? 3 + _matches.Count : 0;

    public void Begin()
    {
        Active = true;
        _draft = string.Empty;
        _pendingPath = null;
        _suggested = false;
        Notice = null;
        _matches.Clear();
        _matchedOn = "\u0001";
    }

    public void Cancel()
    {
        Active = false;
        _draft = string.Empty;
        _pendingPath = null;
        _suggested = false;
        _matches.Clear();
        _matchedOn = "\u0001";
    }

    public void Render(ScreenBuffer buffer, int x, int y, int width)
    {
        if (!Active || y + 3 > buffer.Height - 4) return;

        var naming = _pendingPath is not null;
        Widgets.TitledBox(buffer, x, y, width, 3, naming ? "Name for cd" : "Folder path", Theme.VioletSoft);

        var draftStyle = _suggested
            ? new Sty(Theme.Muted, Theme.Panel, italic: true)
            : new Sty(Theme.Text, Theme.Panel);

        var cursor = buffer.Write(x + 3, y + 1, _draft, draftStyle);
        buffer.Write(cursor, y + 1, "▏", new Sty(Theme.Blue, Theme.Panel, bold: true));

        if (naming)
        {
            buffer.WriteClipped(x + 3, y + 2, _pendingPath!, width - 6,
                new Sty(Theme.Dim, Theme.Panel, italic: true));
            return;
        }

        // Folders under what has been typed, so a path is chosen rather than
        // spelled out. Drawn as a dropdown hanging off the input, the way the
        // slash-command menu hangs off a tile's prompt.
        for (var i = 0; i < _matches.Count; i++)
        {
            var row = y + 3 + i;
            if (row > buffer.Height - 5) break;

            var selected = i == _matchIndex;
            var bg = selected ? Theme.PanelSelected : Theme.Panel;

            buffer.Fill(x + 1, row, width - 2, 1, bg);
            buffer.Write(x + 3, row, selected ? "▸ " : "  ", new Sty(Theme.Blue, bg, bold: true));
            buffer.WriteClipped(x + 5, row, Path.GetFileName(_matches[i]), width - 10,
                new Sty(selected ? Theme.Blue : Theme.TextSoft, bg, bold: selected));
        }
    }

    /// <summary>
    /// Recomputes the folder list for what has been typed. Split on the last
    /// separator: what precedes it is the folder to look in, what follows is the
    /// prefix to match - the same rule a shell completes by.
    /// </summary>
    private void RefreshMatches()
    {
        if (_pendingPath is not null || _draft == _matchedOn) return;

        _matchedOn = _draft;
        _matches.Clear();
        _matchIndex = 0;

        var text = _draft.Trim().Trim('"');
        if (text.Length == 0) return;

        try
        {
            text = Environment.ExpandEnvironmentVariables(text);
            if (text.StartsWith("~", StringComparison.Ordinal))
                text = Path.Combine(StateStore.Home, text.TrimStart('~', '\\', '/'));

            var separator = text.LastIndexOfAny(new[] { '\\', '/' });
            if (separator < 0) return;

            var parent = text.Substring(0, separator + 1);
            var prefix = text.Substring(separator + 1);

            if (!Directory.Exists(parent)) return;

            foreach (var directory in Directory.EnumerateDirectories(parent))
            {
                var leaf = Path.GetFileName(directory);
                if (prefix.Length > 0 && !leaf.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

                _matches.Add(directory);
                if (_matches.Count >= MaxMatches) break;
            }
        }
        catch (Exception)
        {
            // An unreadable or malformed path simply offers nothing.
            _matches.Clear();
        }
    }

    /// <summary>Fills the input with the highlighted folder, ready to go deeper.</summary>
    private void Complete()
    {
        if (_matches.Count == 0) return;

        _draft = _matches[Math.Clamp(_matchIndex, 0, _matches.Count - 1)] + Path.DirectorySeparatorChar;
        _suggested = false;
        RefreshMatches();
    }

    public KeyHint[] Hints
    {
        get
        {
            if (_pendingPath is not null)
                return new[] { new KeyHint("type", "Name for cd"), new KeyHint("↵", "Save"), new KeyHint("esc", "Cancel") };

            return _matches.Count > 0
                ? new[]
                {
                    new KeyHint("↑↓", "Pick folder"),
                    new KeyHint("tab", "Complete"),
                    new KeyHint("↵", "Use this path"),
                    new KeyHint("esc", "Cancel")
                }
                : new[] { new KeyHint("type", "Folder path"), new KeyHint("↵", "Next"), new KeyHint("esc", "Cancel") };
        }
    }

    /// <summary>Handles a key while adding. Returns false when the editor is not open.</summary>
    public bool HandleKey(ConsoleKeyInfo key)
    {
        if (!Active) return false;

        Select = null;

        switch (key.Key)
        {
            case ConsoleKey.Escape:
                Cancel();
                return true;
            case ConsoleKey.Tab:
                Complete();
                return true;
            case ConsoleKey.UpArrow:
                if (_matches.Count > 0) _matchIndex = (_matchIndex - 1 + _matches.Count) % _matches.Count;
                return true;
            case ConsoleKey.DownArrow:
                if (_matches.Count > 0) _matchIndex = (_matchIndex + 1) % _matches.Count;
                return true;
            case ConsoleKey.Backspace:
                if (_suggested)
                {
                    _draft = string.Empty;
                    _suggested = false;
                    return true;
                }

                if (_draft.Length > 0) _draft = _draft.Substring(0, _draft.Length - 1);
                RefreshMatches();
                return true;
            case ConsoleKey.Enter:
                if (_pendingPath is null) AcceptPath();
                else Save();
                return true;
        }

        if (!char.IsControl(key.KeyChar))
        {
            if (_suggested)
            {
                _draft = string.Empty;
                _suggested = false;
            }

            _draft += key.KeyChar;
        }

        RefreshMatches();
        return true;
    }

    /// <summary>
    /// First step: resolve what was typed to a real folder, then ask what to
    /// call it - that name is what <c>cd</c> will answer to, so it is worth a
    /// prompt rather than a silent guess.
    /// </summary>
    private void AcceptPath()
    {
        var path = _draft.Trim().Trim('"');
        if (path.Length == 0) return;

        path = Environment.ExpandEnvironmentVariables(path);
        if (path.StartsWith("~", StringComparison.Ordinal))
            path = Path.Combine(StateStore.Home, path.TrimStart('~', '\\', '/'));

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception)
        {
            Notice = "that is not a usable path";
            return;
        }

        // A folder that does not exist would start Claude in nothing, so it is
        // refused here rather than failing later with a pty error.
        if (!Directory.Exists(full))
        {
            Notice = "no such folder: " + full;
            return;
        }

        if (_projects.Any(p => Same(p.Path, full)))
        {
            Notice = "already on the list";
            Cancel();
            return;
        }

        _pendingPath = full;
        _draft = QuickPaths.SuggestName(full);
        _suggested = true;
        _matches.Clear();
        Notice = null;
    }

    /// <summary>
    /// Second step: save it. A quick path is the better home - the same registry
    /// <c>quick-set</c> writes - so the folder works with <c>cd</c> too. Only
    /// when there is nowhere to put one does this fall back to our own list.
    /// </summary>
    private void Save()
    {
        if (_pendingPath is null) return;

        var name = new string(_draft.Trim().Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (name.Length == 0) name = QuickPaths.SuggestName(_pendingPath);

        var entry = new ProjectEntry { Name = name, Path = _pendingPath };

        if (QuickPaths.Save(name, _pendingPath))
        {
            Notice = $"quick path saved · cd {name} · your shell picks it up next time it starts";
        }
        else if (StateStore.AddProject(entry))
        {
            Notice = $"added {name} (quick paths unavailable, kept in the launcher)";
        }
        else
        {
            Notice = "could not save that project";
            return;
        }

        _app.State.Projects.Add(entry);
        if (!ReferenceEquals(_projects, _app.State.Projects)) _projects.Add(entry);

        Select = _projects.Count - 1;
        Cancel();
    }

    /// <summary>Forgets a project we own. The wrapper's own entries are left alone.</summary>
    public bool Forget(ProjectEntry project)
    {
        var quickName = QuickPaths.NameFor(project.Path);

        var removed = quickName is not null
            ? QuickPaths.Remove(quickName)
            : StateStore.RemoveAddedProject(project.Path);

        if (!removed)
        {
            Notice = "that one is not ours to remove - it came from the wrapper";
            return false;
        }

        _projects.RemoveAll(p => Same(p.Path, project.Path));
        if (!ReferenceEquals(_projects, _app.State.Projects))
            _app.State.Projects.RemoveAll(p => Same(p.Path, project.Path));

        Notice = quickName is not null ? $"removed quick path {quickName}" : $"removed {project.Name}";
        return true;
    }

    private static bool Same(string a, string b) =>
        string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
}
