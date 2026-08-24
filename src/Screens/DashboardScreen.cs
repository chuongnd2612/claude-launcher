using ClaudeLauncher.Sessions;
using ClaudeLauncher.Tui;

namespace ClaudeLauncher.Screens;

/// <summary>
/// What Claude has been doing, and what it has cost, per profile.
///
/// Two kinds of number sit here and the screen keeps them apart, because they
/// cannot both be labelled the same way. Cost and tokens are Claude's own record
/// and carry no dates, so they are totals however the period is set. Everything
/// else - sessions, prompts, edits, pull requests - is counted from lines with a
/// timestamp on them, so those follow the period.
///
/// It is built on a background task: reading a config dir and today's
/// transcripts is fast, but not so fast that a screen should wait for it.
/// </summary>
public sealed class DashboardScreen : ScreenBase
{
    private readonly SessionService? _service;
    private readonly bool _fixture;

    private DashboardData? _data;
    private bool _building;
    private Period _period;
    private int _index;
    private string? _notice;

    public DashboardScreen(App app, SessionService service) : base(app)
    {
        _service = service;
        _period = Parse(app.Settings.DashboardPeriod);
        Rebuild();
    }

    /// <summary>Fixture constructor for --selftest.</summary>
    public DashboardScreen(App app, DashboardData data) : base(app)
    {
        _service = null;
        _fixture = true;
        _data = data;
        _period = data.Period;
    }

    public override TimeSpan? RefreshInterval =>
        _fixture ? null : TimeSpan.FromMilliseconds(_building ? 120 : 2000);

    public override bool NeedsRedraw() => true;

    private void Rebuild()
    {
        if (_fixture || _service is null || _building) return;

        _building = true;
        var snapshot = _service.Build();
        var state = App.State;
        var period = _period;

        Task.Run(() =>
        {
            try
            {
                _data = Metrics.Build(state, snapshot, period);
            }
            catch (Exception)
            {
                _data = DashboardData.Empty;
            }
            finally
            {
                _building = false;
                ConsoleInput.Wake();
            }
        });
    }

    public override void Render(ScreenBuffer buffer)
    {
        var y = Widgets.CompactChrome(buffer);
        var margin = Widgets.Margin(buffer);
        var width = buffer.Width - margin * 2;

        Widgets.SectionTitle(buffer, y, "Home", "Dashboard");

        var right = _building && _data is null
            ? "reading…"
            : $"{Metrics.Describe(_period)} · p to change";

        buffer.WriteRight(margin + width - 1, y, right, new Sty(Theme.Dim, Theme.Bg));
        y += 2;

        if (_data is null)
        {
            buffer.WriteClipped(margin + 1, y, "Adding up what Claude has recorded…", width - 2,
                new Sty(Theme.Muted, Theme.Bg, italic: true));

            Footer(buffer);
            return;
        }

        var data = _data;
        var bottom = buffer.Height - 5;

        // Side by side while there is room for two readable columns, stacked
        // when there is not - the same ladder the wall uses.
        var columns = width >= 108 ? 2 : 1;
        var columnWidth = columns == 2 ? (width - 2) / 2 : width;

        if (columns == 1)
        {
            // Stacked, usage leads: it is what the screen is for, and whatever
            // runs out of room at the bottom should not be that.
            var stacked = DashboardPanels.Usage(buffer, margin, y, columnWidth, data, bottom, App.Settings);
            stacked = DashboardPanels.Activity(buffer, margin, stacked + 1, columnWidth, data, bottom, App.Settings);
            stacked = DashboardPanels.Work(buffer, margin, stacked + 1, columnWidth, data, bottom, App.Settings);
            DashboardPanels.Projects(buffer, margin, stacked + 1, columnWidth, data, bottom, App.Settings, _index);
        }
        else
        {
            var leftY = DashboardPanels.Activity(buffer, margin, y, columnWidth, data, bottom, App.Settings);
            DashboardPanels.Work(buffer, margin, leftY + 1, columnWidth, data, bottom, App.Settings);

            var secondX = margin + columnWidth + 2;
            var rightY = DashboardPanels.Usage(buffer, secondX, y, columnWidth, data, bottom, App.Settings);
            DashboardPanels.Projects(buffer, secondX, rightY + 1, columnWidth, data, bottom, App.Settings, _index);
        }

        if (_notice is not null)
        {
            buffer.WriteClipped(margin + 1, buffer.Height - 6, _notice, width - 2,
                new Sty(Theme.Amber, Theme.Bg));
        }
        else if (data.Milliseconds > 0)
        {
            var read = data.BytesScanned / 1024.0 / 1024.0;
            var note = $"read {read:0.#} MB in {data.Milliseconds} ms" +
                       (data.Capped ? " · capped, so counts are a floor" : "");

            buffer.WriteClipped(margin + 1, buffer.Height - 6, note, width - 2, new Sty(Theme.Dim, Theme.Bg));
        }

        Footer(buffer);
    }

    private void Footer(ScreenBuffer buffer) =>
        Widgets.Footer(buffer, KeyMap.DashboardFooter(), KeyMap.Help);

    public override ScreenAction HandleKey(ConsoleKeyInfo key)
    {
        _notice = null;

        switch (key.Key)
        {
            case ConsoleKey.F1:
                return ScreenAction.Push(new KeysScreen(App, "Dashboard", KeyMap.Dashboard()));
            case ConsoleKey.U when (key.Modifiers & ConsoleModifiers.Alt) != 0:
                return _service is null
                    ? ScreenAction.None
                    : ScreenAction.Push(new UsageScreen(App, _service));
            case ConsoleKey.Escape:
            case ConsoleKey.Backspace:
                return ScreenAction.Back;

            case ConsoleKey.UpArrow:
                _index = Math.Max(0, _index - 1);
                return ScreenAction.None;

            case ConsoleKey.DownArrow:
                _index = Math.Min(Math.Max(0, (_data?.Projects.Count ?? 1) - 1), _index + 1);
                return ScreenAction.None;

            case ConsoleKey.Enter:
                return Open();
        }

        switch (char.ToLowerInvariant(key.KeyChar))
        {
            case 'p':
                _period = _period switch
                {
                    Period.Today => Period.Week,
                    Period.Week => Period.All,
                    _ => Period.Today
                };

                App.Settings.DashboardPeriod = _period.ToString().ToLowerInvariant();
                StateStore.SaveSettings(App.Settings);

                _data = null;
                Rebuild();
                return ScreenAction.None;

            case 'r':
                _data = null;
                Rebuild();
                return ScreenAction.None;

            case 'q':
                return ScreenAction.Exit;
        }

        return ScreenAction.None;
    }

    /// <summary>Opens the selected project's earlier sessions.</summary>
    private ScreenAction Open()
    {
        if (_data is null || _data.Projects.Count == 0) return ScreenAction.None;
        if (App.Profile is null) App.Profile = App.State.Profiles.FirstOrDefault();
        if (App.Profile is null) return ScreenAction.None;

        var project = _data.Projects[Math.Clamp(_index, 0, _data.Projects.Count - 1)];

        if (!Directory.Exists(project.Path))
        {
            _notice = $"{project.Name} is not on disk any more";
            return ScreenAction.None;
        }

        App.Project = new ProjectEntry { Name = project.Name, Path = project.Path };
        return ScreenAction.Push(new ResumeScreen(App, LaunchTarget.Current));
    }

    private static Period Parse(string? value) => value?.ToLowerInvariant() switch
    {
        "week" => Period.Week,
        "all" => Period.All,
        _ => Period.Today
    };
}
