namespace ClaudeLauncher.Tui;

/// <summary>
/// Gives a project a colour of its own, so tiles sharing one read as a family
/// at a glance even when they are not next to each other.
///
/// Unlike <see cref="ProfileLook"/>, which registers a known, small set of
/// profiles up front to guarantee none of them collide, a project has no such
/// registration - there can be dozens, opened and closed all the time, and
/// guaranteeing every one a distinct colour is neither practical nor the
/// point. A hash is enough: the same path always lands on the same colour,
/// drawn from the same palette the profiles use.
/// </summary>
public static class ProjectLook
{
    public static Rgb Color(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return Theme.Muted;

        // FNV-1a, matching ProfileLook: stable across a restart, unlike the
        // runtime's own string hash, which is randomised per process.
        var hash = 2166136261u;
        foreach (var ch in path.Trim().ToLowerInvariant())
        {
            hash ^= ch;
            hash *= 16777619u;
        }

        return ProfileLook.Colors[(int)(hash % (uint)ProfileLook.Colors.Length)];
    }
}
