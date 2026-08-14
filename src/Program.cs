using System.Reflection;
using ClaudeLauncher.Screens;
using ClaudeLauncher.Tui;

namespace ClaudeLauncher;

public static class Program
{
    /// <summary>
    /// Taken from the assembly so the release workflow can stamp it from the git
    /// tag (-p:Version=...) without anyone editing source.
    /// </summary>
    public static string Version { get; } =
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";

    public static int Main(string[] args)
    {
        if (args.Any(a => a is "--version" or "-v"))
        {
            Console.WriteLine($"claude-launcher {Version}");
            return 0;
        }

        if (args.Any(a => a is "--help" or "-h"))
        {
            PrintHelp();
            return 0;
        }

        try
        {
            var settings = StateStore.LoadSettings();
            var state = StateStore.LoadState();
            var app = new App(state, settings);

            if (args.Any(a => a == "--selftest"))
            {
                SelfTest(app, args);
                return 0;
            }

            var requestedProfile = Environment.GetEnvironmentVariable("CLAUDE_LAUNCHER_PROFILE");
            var requestedProject = Environment.GetEnvironmentVariable("CLAUDE_LAUNCHER_PROJECT");
            var requestedMode = Environment.GetEnvironmentVariable("CLAUDE_LAUNCHER_MODE");

            var profile = string.IsNullOrWhiteSpace(requestedProfile)
                ? null
                : state.Profiles.FirstOrDefault(p =>
                    string.Equals(p.Name, requestedProfile, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.Label, requestedProfile, StringComparison.OrdinalIgnoreCase));

            if (profile is null)
            {
                app.Run(new ProfileScreen(app));
                return 0;
            }

            app.Profile = profile;

            var project = string.IsNullOrWhiteSpace(requestedProject)
                ? null
                : state.Projects.FirstOrDefault(p =>
                    string.Equals(p.Name, requestedProject, StringComparison.OrdinalIgnoreCase));

            if (project is null)
            {
                app.Run(new ProfileScreen(app), new ProjectScreen(app));
                return 0;
            }

            app.Project = project;

            // Fully specified launch: no UI needed, hand straight back to the shell.
            var mode = NormalizeMode(requestedMode);
            if (mode is not null)
            {
                StateStore.WriteResult(profile, project, mode);
                return 0;
            }

            app.Run(new ProfileScreen(app), new ProjectScreen(app), new SessionScreen(app));
            return 0;
        }
        catch (Exception ex)
        {
            try { Term.Restore(); } catch { /* nothing else to do */ }
            Console.Error.WriteLine("claude-launcher: " + ex.Message);
            return 1;
        }
    }

    private static string? NormalizeMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return null;
        var value = mode.Trim().ToLowerInvariant();
        return UiSettings.Modes.Contains(value) ? value : null;
    }

    private static void PrintHelp()
    {
        Console.WriteLine($"claude-launcher {Version} - interactive picker for Claude Code profiles and projects.");
        Console.WriteLine();
        Console.WriteLine("This executable is driven by the PowerShell wrapper; run 'claude-launcher' in PowerShell.");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -h, --help                 Show this help");
        Console.WriteLine("  -v, --version              Show the version");
        Console.WriteLine("      --selftest [w] [h]     Render every screen as plain text (layout check)");
        Console.WriteLine();
        Console.WriteLine("Environment:");
        Console.WriteLine("  CLAUDE_LAUNCHER_STATE      state.json prepared by the wrapper");
        Console.WriteLine("  CLAUDE_LAUNCHER_RESULT     result.json written on launch");
        Console.WriteLine("  CLAUDE_LAUNCHER_PROFILES   profiles.json (defaults to ~/.claude-launcher/profiles.json)");
        Console.WriteLine("  CLAUDE_LAUNCHER_PROFILE    pre-select a profile");
        Console.WriteLine("  CLAUDE_LAUNCHER_PROJECT    pre-select a project");
        Console.WriteLine("  CLAUDE_LAUNCHER_MODE       new | continue | resume");
    }

    /// <summary>Renders each screen to stdout as plain text - handy for checking layout over SSH or in CI.</summary>
    private static void SelfTest(App app, string[] args)
    {
        var numbers = args.Where(a => int.TryParse(a, out _)).Select(int.Parse).ToArray();
        var width = numbers.Length > 0 ? numbers[0] : 120;
        var height = numbers.Length > 1 ? numbers[1] : 44;

        app.Profile = app.State.Profiles[0];
        app.Project = app.State.Projects.Count > 0
            ? app.State.Projects[0]
            : new ProjectEntry { Name = "current", Path = Environment.CurrentDirectory };

        var screens = new (string Name, ScreenBase Screen)[]
        {
            ("profile", new ProfileScreen(app)),
            ("project", new ProjectScreen(app)),
            ("session", new SessionScreen(app)),
            ("add-profile", new AddProfileScreen(app)),
            ("settings", new SettingsScreen(app))
        };

        foreach (var (name, screen) in screens)
        {
            Console.WriteLine($"===== {name} ({width}x{height}) =====");
            Console.WriteLine(app.RenderToText(screen, width, height));
            Console.WriteLine();
        }
    }
}
