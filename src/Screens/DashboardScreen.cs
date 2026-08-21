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
            var stacked = Usage(buffer, margin, y, columnWidth, data, bottom);
            stacked = Activity(buffer, margin, stacked + 1, columnWidth, data, bottom);
            stacked = Work(buffer, margin, stacked + 1, columnWidth, data, bottom);
            Projects(buffer, margin, stacked + 1, columnWidth, data, bottom);
        }
        else
        {
            var leftY = Activity(buffer, margin, y, columnWidth, data, bottom);
            Work(buffer, margin, leftY + 1, columnWidth, data, bottom);

            var secondX = margin + columnWidth + 2;
            var rightY = Usage(buffer, secondX, y, columnWidth, data, bottom);
            Projects(buffer, secondX, rightY + 1, columnWidth, data, bottom);
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

    private void Footer(ScreenBuffer buffer) => Widgets.Footer(buffer, new[]
    {
        new KeyHint("p", "Period"),
        new KeyHint("r", "Refresh"),
        new KeyHint("↑↓", "Project"),
        new KeyHint("↵", "Sessions"),
        new KeyHint("esc", "Back")
    });

    private int Activity(ScreenBuffer buffer, int x, int y, int width, DashboardData data, int bottom)
    {
        var rows = new List<(string, string, Rgb)>
        {
            ("Sessions", data.Totals.Sessions.ToString(), Theme.Text),
            ("Running now", data.Totals.Live.ToString(), Theme.Green),
            ("Waiting on you", data.Totals.Waiting == 0 ? "0" : data.Totals.Waiting + "?", Theme.Amber),
            ("Prompts sent", data.Totals.Prompts.ToString(), Theme.Text),
            ("Busiest hour", data.Totals.BusiestHour is null ? "—" : $"{data.Totals.BusiestHour:00}:00", Theme.TextSoft)
        };

        return Panel(buffer, x, y, width, $" {Capitalise(Metrics.Describe(_period))} ", Theme.Blue, rows, bottom);
    }

    private int Work(ScreenBuffer buffer, int x, int y, int width, DashboardData data, int bottom)
    {
        var rows = new List<(string, string, Rgb)>
        {
            ("Files touched", data.Totals.FilesTouched.ToString(), Theme.Text),
            ("Edits written", data.Totals.Edits.ToString(), Theme.Text),
            ("Commands run", data.Totals.Commands.ToString(), Theme.Text),
            ("Pull requests", data.Totals.PullRequests.ToString(), Theme.VioletSoft)
        };

        return Panel(buffer, x, y, width, " Work ", Theme.VioletSoft, rows, bottom);
    }

    /// <summary>
    /// Per profile, from Claude's own record. Deliberately not labelled with the
    /// period: that block has no dates in it, and pretending otherwise would put
    /// a wrong number next to a right one.
    /// </summary>
    private int Usage(ScreenBuffer buffer, int x, int y, int width, DashboardData data, int bottom)
    {
        var height = Math.Min(bottom - y, data.Profiles.Count + 4);
        if (height < 4) return y;

        Widgets.TitledBox(buffer, x, y, width, height, " Usage · recorded by Claude ", Theme.Green);

        var line = y + 1;
        var money = App.Settings.ShowCosts;

        buffer.WriteClipped(x + 3, line, "profile", width - 6, new Sty(Theme.Muted, Theme.Panel));
        buffer.WriteRight(x + width - 3, line, money ? "cost      output" : "output", new Sty(Theme.Muted, Theme.Panel));
        line++;

        foreach (var profile in data.Profiles)
        {
            if (line >= y + height - 1) break;

            var label = $"{profile.Icon} {profile.Label}";
            if (profile.Account.Length > 0) label += " · " + profile.Account;

            buffer.WriteClipped(x + 3, line, label, Math.Max(8, width - 26),
                new Sty(ProfileLook.Color(profile.Key), Theme.Panel));

            var cost = !profile.HasCost ? "—" : $"${profile.CostUsd:0.00}";
            var output = Format.Tokens(profile.OutputTokens);

            buffer.WriteRight(x + width - 3, line,
                money ? $"{cost,9} {output,7}" : $"{output,7}",
                new Sty(Theme.TextSoft, Theme.Panel));

            line++;
        }

        if (line < y + height - 1 && data.Profiles.Count > 1)
        {
            var total = money ? $"${data.TotalCost:0.00}" : string.Empty;
            buffer.WriteClipped(x + 3, line, "total", width - 6, new Sty(Theme.Muted, Theme.Panel));
            buffer.WriteRight(x + width - 3, line,
                money ? $"{total,9} {Format.Tokens(data.TotalOutput),7}" : $"{Format.Tokens(data.TotalOutput),7}",
                new Sty(Theme.Text, Theme.Panel, bold: true));
        }

        return y + height;
    }

    private void Projects(ScreenBuffer buffer, int x, int y, int width, DashboardData data, int bottom)
    {
        var height = Math.Min(bottom - y, Math.Max(4, data.Projects.Count + 3));
        if (height < 4) return;

        Widgets.TitledBox(buffer, x, y, width, height,
            $" Projects · {Metrics.Describe(_period)} ", Theme.Blue);

        if (data.Projects.Count == 0)
        {
            buffer.WriteClipped(x + 3, y + 1, "Nothing recorded in this period.", width - 6,
                new Sty(Theme.Muted, Theme.Panel, italic: true));
            return;
        }

        if (_index >= data.Projects.Count) _index = data.Projects.Count - 1;

        var rows = height - 2;
        var busiest = Math.Max(1, data.Projects.Max(p => p.Prompts));

        for (var row = 0; row < rows && row < data.Projects.Count; row++)
        {
            var project = data.Projects[row];
            var selected = row == _index;
            var lineY = y + 1 + row;
            var bg = selected ? Theme.PanelSelected : Theme.Panel;

            buffer.Fill(x + 1, lineY, width - 2, 1, bg);
            buffer.Write(x + 2, lineY, selected ? "▸" : " ", new Sty(Theme.Blue, bg, bold: true));

            buffer.WriteClipped(x + 4, lineY, project.Name, Math.Max(8, width / 3),
                new Sty(selected ? Theme.Text : Theme.TextSoft, bg));

            var right = App.Settings.ShowCosts && project.HasCost
                ? $"{project.Sessions,3} ses  {project.Prompts,4} pr  ${project.CostUsd,7:0.00}"
                : $"{project.Sessions,3} ses  {project.Prompts,4} pr";

            buffer.WriteRight(x + width - 3, lineY, right, new Sty(Theme.Muted, bg));

            // A bar as wide as the busiest project, so the shape reads before
            // the digits do.
            var barRoom = Math.Max(0, width - 8 - right.Length - Math.Max(8, width / 3));
            if (barRoom >= 4)
            {
                var filled = Math.Max(1, project.Prompts * barRoom / busiest);
                var barX = x + 5 + Math.Max(8, width / 3);
                for (var i = 0; i < filled && i < barRoom; i++)
                    buffer.Set(barX + i, lineY, '▪', new Sty(Theme.BorderAccent, bg));
            }
        }
    }

    private static int Panel(ScreenBuffer buffer, int x, int y, int width, string title, Rgb color,
        List<(string Label, string Value, Rgb Color)> rows, int bottom)
    {
        var height = Math.Min(bottom - y, rows.Count + 2);
        if (height < 3) return y;

        Widgets.TitledBox(buffer, x, y, width, height, title, color);

        for (var i = 0; i < rows.Count && i < height - 2; i++)
        {
            buffer.WriteClipped(x + 3, y + 1 + i, rows[i].Label, width - 16, new Sty(Theme.Muted, Theme.Panel));
            buffer.WriteRight(x + width - 3, y + 1 + i, rows[i].Value,
                new Sty(rows[i].Color, Theme.Panel, bold: true));
        }

        return y + height;
    }

    private static string Capitalise(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    public override ScreenAction HandleKey(ConsoleKeyInfo key)
    {
        _notice = null;

        switch (key.Key)
        {
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
