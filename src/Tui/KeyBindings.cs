namespace ClaudeLauncher.Tui;

/// <summary>
/// The commands a key can be bound to.
///
/// Only discrete commands are here. Enter, Esc, Tab, the arrows, Backspace and
/// anything that types a character are deliberately absent: they are how a screen
/// works rather than what it does, several screens read them in ways that cannot
/// be expressed as one action, and every key not listed here still reaches a
/// focused terminal untouched.
///
/// Actions are shared across screens on purpose. Rebinding Quit changes it
/// everywhere it exists, which is the behaviour someone rebinding it wants;
/// clashes are checked per screen instead, because 't' meaning "show the wall" on
/// Home and "new terminal" on the wall was never a conflict.
/// </summary>
public enum KeyAction
{
    None = 0,

    Keys,
    Usage,
    Quit,
    Settings,
    Updates,
    Dashboard,

    NewSession,
    Attach,
    ReopenLast,
    ShowWall,
    StopSession,

    Zoom,
    CloseTile,
    SplitRight,
    SplitDown,
    NewTerminal,

    AddProfile,
    EditProfile,
    RemoveProfile,

    Filter,
    AddFolder,
    ForgetFolder,

    OpenIn,
    SwitchProfile,
    ModeNew,
    ModeContinue,
    ModeResume,
    ModeChat,

    Period,
    Refresh,

    ResumeChat,
    ResumeLog,
    ResumeTile,
    DeleteSession,

    ReleaseNotes,
    StopAsking,
    Search,

    EditKeys
}

/// <summary>Which screen a binding belongs to, for clash checking and the editor.</summary>
public enum KeyScope
{
    Everywhere,
    Home,
    Wall,
    Profiles,
    Projects,
    Session,
    Dashboard,
    Usage,
    Resume,
    Settings,
    Update,
    History
}

/// <summary>One rebindable command: what it is, where it applies, how to say it.</summary>
public sealed class KeyBinding
{
    public KeyAction Action { get; init; }
    public KeyScope Scope { get; init; }
    public string Label { get; init; } = string.Empty;
    public Chord Default { get; init; }
}

/// <summary>
/// Which key runs which command, with the user's own choices layered over the
/// defaults.
///
/// Screens ask "is this key my Dashboard command" rather than "what command is
/// this key", which keeps each screen's existing order of checks intact. That
/// order is load-bearing in places - the wall has to test its chords before the
/// blocks that match arrows without looking at modifiers - and a central
/// dispatcher would have quietly flattened it.
/// </summary>
public static class KeyBindings
{
    private static readonly Dictionary<KeyAction, Chord> Bound = new();

    public static IReadOnlyList<KeyBinding> All { get; } = Catalogue();

    /// <summary>Loads the user's overrides. Anything unparsed keeps its default.</summary>
    public static void Load(IReadOnlyDictionary<string, string>? saved)
    {
        Bound.Clear();

        foreach (var binding in All) Bound[binding.Action] = binding.Default;

        if (saved is null) return;

        foreach (var (name, text) in saved)
        {
            if (!Enum.TryParse<KeyAction>(name, ignoreCase: true, out var action)) continue;
            if (action == KeyAction.None) continue;

            // "" or "none" unbinds; anything unreadable is left alone rather than
            // silently turning into a key nobody asked for.
            if (string.IsNullOrWhiteSpace(text) || text.Trim().ToLowerInvariant() == "none")
            {
                Bound[action] = default;
                continue;
            }

            if (Chord.TryParse(text, out var chord)) Bound[action] = chord;
        }
    }

    public static Chord Of(KeyAction action) =>
        Bound.TryGetValue(action, out var chord) ? chord : Default(action);

    private static Chord Default(KeyAction action)
    {
        foreach (var binding in All)
        {
            if (binding.Action == action) return binding.Default;
        }

        return default;
    }

    /// <summary>The one question screens ask.</summary>
    public static bool Is(KeyAction action, ConsoleKeyInfo key)
    {
        var chord = Of(action);
        return !chord.None && chord.Matches(key);
    }

    public static string Describe(KeyAction action) => Of(action).Compact();

    /// <summary>What to write to keys.json: only what differs from the defaults.</summary>
    public static Dictionary<string, string> Changed()
    {
        var changed = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var binding in All)
        {
            var chord = Of(binding.Action);
            if (chord.Equals(binding.Default)) continue;

            changed[binding.Action.ToString()] = chord.None ? "none" : chord.Describe();
        }

        return changed;
    }

    public static void Set(KeyAction action, Chord chord) => Bound[action] = chord;

    /// <summary>
    /// What each screen actually answers to, listed rather than derived.
    ///
    /// A binding's Scope says where it belongs for the editor's grouping, which
    /// is not the same as where it is live: Filter is a projects command that the
    /// resume and new-terminal lists also use, and Period and Refresh are shared
    /// by the dashboard and the usage screen. Deriving the clash check from Scope
    /// alone quietly missed those, so the screens say for themselves.
    /// </summary>
    private static readonly (KeyScope Scope, KeyAction[] Actions)[] Screens =
    {
        (KeyScope.Home, new[]
        {
            KeyAction.Settings, KeyAction.Updates, KeyAction.Dashboard, KeyAction.NewSession,
            KeyAction.SwitchProfile, KeyAction.Attach, KeyAction.ReopenLast, KeyAction.ShowWall,
            KeyAction.StopSession
        }),
        (KeyScope.Wall, new[]
        {
            KeyAction.Zoom, KeyAction.CloseTile, KeyAction.SplitRight, KeyAction.SplitDown,
            KeyAction.NewSession, KeyAction.NewTerminal
        }),
        (KeyScope.Profiles, new[]
        {
            KeyAction.AddProfile, KeyAction.EditProfile, KeyAction.RemoveProfile,
            KeyAction.Settings, KeyAction.Updates, KeyAction.Dashboard
        }),
        (KeyScope.Projects, new[] { KeyAction.Filter, KeyAction.AddFolder, KeyAction.ForgetFolder }),
        (KeyScope.Session, new[]
        {
            KeyAction.OpenIn, KeyAction.SwitchProfile, KeyAction.ModeNew,
            KeyAction.ModeContinue, KeyAction.ModeResume, KeyAction.ModeChat
        }),
        (KeyScope.Dashboard, new[] { KeyAction.Period, KeyAction.Refresh }),
        (KeyScope.Usage, new[] { KeyAction.Period, KeyAction.Refresh }),
        (KeyScope.Resume, new[]
        {
            KeyAction.Filter, KeyAction.ResumeChat, KeyAction.ResumeLog,
            KeyAction.ResumeTile, KeyAction.DeleteSession
        }),
        (KeyScope.Settings, new[] { KeyAction.Updates, KeyAction.Settings }),
        (KeyScope.Update, new[] { KeyAction.ReleaseNotes, KeyAction.StopAsking }),
        (KeyScope.History, new[] { KeyAction.Search })
    };

    /// <summary>Live on every screen, so they clash with all of the above.</summary>
    private static readonly KeyAction[] Global =
    {
        KeyAction.Keys, KeyAction.Usage, KeyAction.Quit, KeyAction.EditKeys
    };

    /// <summary>
    /// Two commands on the same key on the same screen. Nothing checked this
    /// before, which is how removing a profile came to shadow the dashboard on
    /// the screen that advertised both.
    /// </summary>
    public static List<string> Clashes()
    {
        var found = new List<string>();

        foreach (var (scope, actions) in Screens) Check(scope, actions, found);
        Check(KeyScope.Everywhere, Array.Empty<KeyAction>(), found);

        return found;
    }

    private static void Check(KeyScope scope, KeyAction[] actions, List<string> found)
    {
        var seen = new Dictionary<Chord, KeyAction>();

        foreach (var action in Global.Concat(actions))
        {
            var chord = Of(action);
            if (chord.None) continue;

            if (seen.TryGetValue(chord, out var other) && other != action)
            {
                var note = $"{Where(scope)}: {chord.Compact()} is both {Name(other)} and {Name(action)}";
                if (!found.Contains(note)) found.Add(note);

                continue;
            }

            seen[chord] = action;
        }
    }

    private static string Where(KeyScope scope) =>
        scope == KeyScope.Everywhere ? "everywhere" : scope.ToString().ToLowerInvariant();

    public static string Name(KeyAction action)
    {
        foreach (var binding in All)
        {
            if (binding.Action == action) return binding.Label;
        }

        return action.ToString();
    }

    public static KeyScope Scope(KeyAction action)
    {
        foreach (var binding in All)
        {
            if (binding.Action == action) return binding.Scope;
        }

        return KeyScope.Everywhere;
    }

    private static KeyBinding Row(KeyAction action, KeyScope scope, string label, string chord)
    {
        Chord.TryParse(chord, out var parsed);
        return new KeyBinding { Action = action, Scope = scope, Label = label, Default = parsed };
    }

    /// <summary>The defaults, which are exactly the keys these commands had before.</summary>
    private static IReadOnlyList<KeyBinding> Catalogue() => new[]
    {
        Row(KeyAction.Keys, KeyScope.Everywhere, "Show the key list", "f1"),
        Row(KeyAction.Usage, KeyScope.Everywhere, "Usage per account", "alt+u"),
        Row(KeyAction.Quit, KeyScope.Everywhere, "Quit the launcher", "q"),
        // alt+k, not 'e': 'e' is edit-a-profile, and a command that applies
        // everywhere cannot take a letter another screen already spends. The
        // clash check found this one before it shipped.
        Row(KeyAction.EditKeys, KeyScope.Everywhere, "Change these keys", "alt+k"),

        Row(KeyAction.Settings, KeyScope.Home, "Settings", "s"),
        Row(KeyAction.Updates, KeyScope.Home, "Check for updates", "u"),
        Row(KeyAction.Dashboard, KeyScope.Home, "Dashboard", "d"),
        Row(KeyAction.NewSession, KeyScope.Home, "New session", "n"),
        Row(KeyAction.SwitchProfile, KeyScope.Home, "Profiles", "p"),
        Row(KeyAction.Attach, KeyScope.Home, "Attach to this session", "a"),
        Row(KeyAction.ReopenLast, KeyScope.Home, "Reopen last terminals", "r"),
        Row(KeyAction.ShowWall, KeyScope.Home, "Show the terminal wall", "t"),
        Row(KeyAction.StopSession, KeyScope.Home, "Stop a session", "k"),

        Row(KeyAction.Zoom, KeyScope.Wall, "Zoom this pane", "z"),
        Row(KeyAction.CloseTile, KeyScope.Wall, "Close this pane", "w"),
        Row(KeyAction.SplitRight, KeyScope.Wall, "Split right", "v"),
        Row(KeyAction.SplitDown, KeyScope.Wall, "Split down", "s"),
        Row(KeyAction.NewTerminal, KeyScope.Wall, "New terminal", "t"),

        Row(KeyAction.AddProfile, KeyScope.Profiles, "Add a profile", "a"),
        Row(KeyAction.EditProfile, KeyScope.Profiles, "Edit this profile", "e"),
        Row(KeyAction.RemoveProfile, KeyScope.Profiles, "Remove this profile", "x"),

        Row(KeyAction.Filter, KeyScope.Projects, "Filter the list", "/"),
        Row(KeyAction.AddFolder, KeyScope.Projects, "Add a folder", "a"),
        Row(KeyAction.ForgetFolder, KeyScope.Projects, "Forget a folder", "d"),

        Row(KeyAction.OpenIn, KeyScope.Session, "Where it opens", "o"),
        Row(KeyAction.ModeNew, KeyScope.Session, "New session", "n"),
        Row(KeyAction.ModeContinue, KeyScope.Session, "Continue the last one", "c"),
        Row(KeyAction.ModeResume, KeyScope.Session, "Resume a specific one", "r"),
        Row(KeyAction.ModeChat, KeyScope.Session, "Open the chat view", "h"),

        Row(KeyAction.Period, KeyScope.Dashboard, "Change the period", "p"),
        Row(KeyAction.Refresh, KeyScope.Dashboard, "Read it again", "r"),

        Row(KeyAction.ResumeChat, KeyScope.Resume, "Resume in the chat view", "c"),
        Row(KeyAction.ResumeLog, KeyScope.Resume, "Read the log", "l"),
        Row(KeyAction.ResumeTile, KeyScope.Resume, "Force a terminal tile", "t"),
        Row(KeyAction.DeleteSession, KeyScope.Resume, "Delete this session", "d"),

        Row(KeyAction.ReleaseNotes, KeyScope.Update, "Read the release notes", "n"),
        Row(KeyAction.StopAsking, KeyScope.Update, "Stop asking about updates", "s"),

        Row(KeyAction.Search, KeyScope.History, "Search again", "/")
    };
}
