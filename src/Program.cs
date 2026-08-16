using System.Reflection;
using ClaudeLauncher.Screens;
using ClaudeLauncher.Sessions;
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

        Widgets.Version = Version;

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
                // Land on Home only when there is something to come home to;
                // with nothing running the wizard is still the fastest path.
                var service = new SessionService(state);
                var snapshot = service.Build();

                if (snapshot.Sessions.Count > 0) app.Run(new HomeScreen(app, service));
                else app.Run(new ProfileScreen(app));

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
                // Deliberately not Settings.DefaultOpenIn: a scripted launch must not
                // start opening windows because of a UI preference.
                var openIn = LaunchTarget.Normalize(Environment.GetEnvironmentVariable("CLAUDE_LAUNCHER_OPEN_IN"));
                StateStore.WriteResult(profile, project, mode, openIn);
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
        Console.WriteLine("  CLAUDE_LAUNCHER_OPEN_IN    current | tab | right | down");
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
            ("home", new HomeScreen(app, DemoSnapshot())),
            ("terminals", new TerminalsScreen(app, DemoSnapshot())),
            ("terminal-preview", new TerminalPreviewScreen(app)),
            ("new-terminal", new NewTerminalScreen(app)),
            ("kill-session", new KillSessionScreen(app, DemoSnapshot().Sessions[0], () => { })),
            ("chat", new ChatScreen(app, DemoChat(), ChatState.AwaitingPermission, DemoAsk())),
            ("resume", new ResumeScreen(app, DemoPastSessions())),
            ("session-detail", new SessionDetailScreen(app, DemoPastSessions()[0], DemoDetail())),
            ("delete-session", new DeleteSessionScreen(app, DemoPastSessions()[0], () => { })),
            ("profile", new ProfileScreen(app)),
            ("project", new ProjectScreen(app)),
            ("session", new SessionScreen(app)),
            ("add-profile", new AddProfileScreen(app)),
            ("edit-profile", new AddProfileScreen(app, app.State.Profiles[0])),
            ("delete-profile", new DeleteProfileScreen(app, app.State.Profiles[0])),
            ("settings", new SettingsScreen(app))
        };

        foreach (var (name, screen) in screens)
        {
            Console.WriteLine($"===== {name} ({width}x{height}) =====");
            Console.WriteLine(app.RenderToText(screen, width, height));
            Console.WriteLine();
        }
    }

    /// <summary>
    /// Fixed sessions for --selftest. Real ones would make the rendered output
    /// depend on whatever happens to be running, which CI cannot assert against.
    /// </summary>
    private static SessionSnapshot DemoSnapshot() => new()
    {
        Sessions = new[]
        {
            Row("qagent", "Refactor runner into stages", SessionState.Running, 12, 4, 184_000, 1),
            Row("api-gateway", "Add rate limiting", SessionState.Waiting, 0, 46, 97_000, 2),
            Row("web-dash", "Fix chart tooltips", SessionState.Idle, 4, 0, 41_000, 3),
            Row("notes-cli", "Write test suite", SessionState.Running, 2, 0, 63_000, 4)
        },
        Recent = new[]
        {
            new RecentProject { Name = "nauxoi", Path = @"D:\demo\nauxoi", LastUsedUtc = DateTime.UtcNow.AddHours(-2) },
            new RecentProject { Name = "qagent", Path = @"D:\demo\q-agent", LastUsedUtc = DateTime.UtcNow }
        },
        SessionsToday = 11
    };

    private static SessionRow Row(string project, string task, SessionState state,
        int minutes, int seconds, long tokens, int pane) => new()
    {
        SessionId = $"{project}-0000-0000",
        ProfileName = "Work",
        ProfileIcon = "W",
        ProjectName = project,
        ProjectPath = @"D:\demo\" + project,
        Task = task,
        State = state,
        StateAge = new TimeSpan(0, 0, minutes, seconds),
        ContextTokens = tokens,
        Model = pane == 3 ? "haiku-4.5" : "sonnet-4.5",
        Pid = 1000 + pane,
        Branch = pane == 1 ? "feat/qagent-refactor" : pane == 3 ? "fix/tooltip" : "main",
        Entries = DemoEntries(pane)
    };

    private static TranscriptEntry[] DemoEntries(int pane) => pane switch
    {
        1 => new[]
        {
            Prompt("split the runner into plan/execute/verify stages"),
            Say("I'll restructure runner.ts into three stages and keep the public run() signature intact."),
            Tool("Read", "agent/runner.ts"),
            Tool("Edit", "stages/plan.ts"),
            Tool("Bash", "pnpm typecheck"),
            Say("Stage split done. Wiring verify() next."),
            new TranscriptEntry { Kind = EntryKind.Thinking, Text = "thinking" }
        },
        2 => new[]
        {
            Prompt("add a redis token bucket to the gateway"),
            Tool("Read", "api/router.ts"),
            Tool("Write", "api/limiter.ts"),
            Say("Mount the limiter before the auth middleware?")
        },
        3 => new[]
        {
            Prompt("tooltips clip at the right edge of the chart"),
            Tool("Edit", "charts/Tooltip.tsx"),
            Say("Flipped the anchor when it overflows. Fixed in both the line and bar chart tooltips.")
        },
        _ => new[]
        {
            Prompt("write a test suite for the note parser"),
            Tool("Write", "test/parser.spec.ts"),
            Tool("Bash", "pnpm vitest run"),
            Say("17 passed, 1 failed. Patching normaliseEol().")
        }
    };

    private static ChatLine[] DemoChat() => new[]
    {
        new ChatLine { Kind = ChatLineKind.UserPrompt, Text = "add a redis token bucket to the gateway" },
        new ChatLine { Kind = ChatLineKind.AssistantText, Text = "I'll add a limiter module and mount it before the auth middleware." },
        new ChatLine { Kind = ChatLineKind.ToolCall, Text = "Read", Detail = "api/router.ts" },
        new ChatLine { Kind = ChatLineKind.ToolCall, Text = "Write", Detail = "api/limiter.ts" }
    };

    private static PermissionAsk DemoAsk() => new()
    {
        RequestId = "demo",
        Tool = "Edit",
        Description = "api/router.ts",
        InputJson = "{}"
    };

    private static List<PastSession> DemoPastSessions() => new()
    {
        new PastSession
        {
            SessionId = "8f31c2ab-0000-0000-0000-000000000000",
            Path = @"C:\Users\demo\.claude-work\projects\D--demo\8f31c2ab.jsonl",
            LastActivityUtc = DateTime.UtcNow.AddHours(-2),
            SizeBytes = 184_320,
            Loaded = true,
            Title = "Refactor QAgent runner into stages",
            FirstPrompt = "split the runner into plan/execute/verify stages and keep the CLI surface stable",
            Model = "opus-5",
            Branch = "feat/qagent-refactor",
            ContextTokens = 184_000
        },
        new PastSession
        {
            SessionId = "a7d90144-0000-0000-0000-000000000000",
            Path = @"C:\Users\demo\.claude-work\projects\D--demo\a7d90144.jsonl",
            LastActivityUtc = DateTime.UtcNow.AddDays(-1),
            SizeBytes = 76_800,
            Loaded = true,
            Title = "Add golden tests for QAgent",
            FirstPrompt = "cover the runner with golden tests before we refactor it",
            Model = "sonnet-4.5",
            ContextTokens = 76_000
        }
    };

    private static SessionDetail DemoDetail()
    {
        var detail = new SessionDetail
        {
            Turns = 48,
            ToolCalls = 61,
            StartedUtc = DateTime.UtcNow.AddHours(-3),
            LastActivityUtc = DateTime.UtcNow.AddHours(-2)
        };

        detail.Files["agent/runner.ts"] = 4;
        detail.Files["stages/plan.ts"] = 2;
        detail.Entries.AddRange(DemoEntries(1));
        return detail;
    }

    private static TranscriptEntry Prompt(string text) => new() { Kind = EntryKind.UserPrompt, Text = text };

    private static TranscriptEntry Say(string text) => new() { Kind = EntryKind.AssistantText, Text = text };

    private static TranscriptEntry Tool(string verb, string target) =>
        new() { Kind = EntryKind.ToolCall, Text = verb, Target = target };
}
