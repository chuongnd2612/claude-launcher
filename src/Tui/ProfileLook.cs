namespace ClaudeLauncher.Tui;

/// <summary>
/// Gives every profile a mark you can tell apart at a glance: an icon it does
/// not share with another profile, and a colour of its own.
///
/// The first letter of the label was the old default, which reads well until two
/// profiles begin with the same one - and then a wall of panes is identical
/// except for the project name. So a taken letter falls back to a shape, and the
/// colour is derived from the profile's key rather than its position, so it does
/// not change when a profile is added above it.
/// </summary>
public static class ProfileLook
{
    /// <summary>
    /// Shapes to fall back on. All single width in the terminals this targets -
    /// the same glyphs the launcher's own chrome already draws - so the grid
    /// cannot be knocked out of alignment by an icon.
    /// </summary>
    private static readonly string[] Shapes =
    {
        "◆", "●", "■", "▲", "★", "◇", "○", "□", "△", "☆", "✦", "✱"
    };

    /// <summary>
    /// Colours far enough apart to be told apart side by side on a dark panel,
    /// and all legible against it.
    /// </summary>
    private static readonly Rgb[] Colors =
    {
        Rgb.Hex("#5AA0FF"), // blue
        Rgb.Hex("#3FD07E"), // green
        Rgb.Hex("#C084FC"), // violet
        Rgb.Hex("#E3B341"), // amber
        Rgb.Hex("#4ECDC4"), // teal
        Rgb.Hex("#F87171"), // red
        Rgb.Hex("#F0A6CA"), // pink
        Rgb.Hex("#A3BE8C")  // sage
    };

    /// <summary>
    /// An icon for a label that no other profile is already using: its initial
    /// if that is free, else the first free shape.
    /// </summary>
    public static string Suggest(string label, IEnumerable<string> taken)
    {
        var used = new HashSet<string>(taken.Where(t => t.Length > 0), StringComparer.OrdinalIgnoreCase);

        var initial = Initial(label);
        if (initial.Length > 0 && !used.Contains(initial)) return initial;

        foreach (var shape in Shapes)
        {
            if (!used.Contains(shape)) return shape;
        }

        // More profiles than shapes: the initial is still better than nothing.
        return initial.Length > 0 ? initial : Shapes[0];
    }

    /// <summary>Every icon on offer, initial first, for cycling through by hand.</summary>
    public static IReadOnlyList<string> Choices(string label)
    {
        var choices = new List<string>();
        var initial = Initial(label);
        if (initial.Length > 0) choices.Add(initial);

        choices.AddRange(Shapes);
        return choices;
    }

    private static readonly Dictionary<string, Rgb> Assigned = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Settles the colours once, for a known set of profiles.
    ///
    /// Hashing alone is stable but can put two profiles on the same colour, and
    /// telling them apart is the entire point. So each profile takes the colour
    /// its key hashes to when that is still free, and the next free one when it
    /// is not - which leaves everyone's colour where it was unless there is a
    /// real clash to resolve.
    ///
    /// Both the key and the label are registered, because a session row carries
    /// the label while the profile screen has the key.
    /// </summary>
    public static void Assign(IEnumerable<(string Key, string Label)> profiles)
    {
        Assigned.Clear();
        var used = new HashSet<Rgb>();

        foreach (var (key, label) in profiles)
        {
            var name = string.IsNullOrWhiteSpace(key) ? label : key;
            if (string.IsNullOrWhiteSpace(name)) continue;

            var color = Hashed(name);

            if (used.Contains(color))
            {
                var free = Colors.FirstOrDefault(c => !used.Contains(c), color);
                color = free;
            }

            used.Add(color);
            Assigned[name] = color;
            if (!string.IsNullOrWhiteSpace(label)) Assigned[label] = color;
        }
    }

    /// <summary>
    /// The profile's colour: the one settled by <see cref="Assign"/> where that
    /// has run, else straight from the key - so a profile being typed into the
    /// add screen still shows a colour.
    /// </summary>
    public static Rgb Color(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return Theme.Muted;
        if (Assigned.TryGetValue(key.Trim(), out var assigned)) return assigned;

        return Hashed(key);
    }

    private static Rgb Hashed(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return Theme.Muted;

        // FNV-1a: short, stable, and not the runtime's string hash, which is
        // randomised per process and would recolour every profile on restart.
        var hash = 2166136261u;
        foreach (var ch in key.Trim().ToLowerInvariant())
        {
            hash ^= ch;
            hash *= 16777619u;
        }

        return Colors[(int)(hash % (uint)Colors.Length)];
    }

    private static string Initial(string label) =>
        string.IsNullOrWhiteSpace(label) ? string.Empty : label.Trim()[..1].ToUpperInvariant();
}
