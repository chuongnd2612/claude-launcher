using ClaudeLauncher.Sessions;
using ClaudeLauncher.Tui;

namespace ClaudeLauncher.Screens;

/// <summary>
/// The dashboard's panels, drawn by whoever has room for them.
///
/// They live here rather than on the dashboard screen because Home shows the
/// same four boxes below its own sections, and two copies of this drawing code
/// would drift apart the first time a column moved.
/// </summary>
public static class DashboardPanels
{
    public static int Activity(ScreenBuffer buffer, int x, int y, int width, DashboardData data, int bottom, UiSettings settings, int selected = -1)
    {
        var rows = new List<(string, string, Rgb)>
        {
            ("Sessions", data.Totals.Sessions.ToString(), Theme.Text),
            ("Running now", data.Totals.Live.ToString(), Theme.Green),
            ("Waiting on you", data.Totals.Waiting == 0 ? "0" : data.Totals.Waiting + "?", Theme.Amber),
            ("Prompts sent", data.Totals.Prompts.ToString(), Theme.Text),
            ("Busiest hour", data.Totals.BusiestHour is null ? "—" : $"{data.Totals.BusiestHour:00}:00", Theme.TextSoft)
        };

        return Box(buffer, x, y, width, $" {Capitalise(Metrics.Describe(data.Period))} ", Theme.Blue, rows, bottom);
    }

    public static int Work(ScreenBuffer buffer, int x, int y, int width, DashboardData data, int bottom, UiSettings settings, int selected = -1)
    {
        var rows = new List<(string, string, Rgb)>
        {
            ("Files touched", data.Totals.FilesTouched.ToString(), Theme.Text),
            ("Edits written", data.Totals.Edits.ToString(), Theme.Text),
            ("Commands run", data.Totals.Commands.ToString(), Theme.Text),
            ("Pull requests", data.Totals.PullRequests.ToString(), Theme.VioletSoft)
        };

        return Box(buffer, x, y, width, " Work ", Theme.VioletSoft, rows, bottom);
    }

    /// <summary>
    /// Per profile, from Claude's own record. Deliberately not labelled with the
    /// period: that block has no dates in it, and pretending otherwise would put
    /// a wrong number next to a right one.
    /// </summary>
    public static int Usage(ScreenBuffer buffer, int x, int y, int width, DashboardData data, int bottom, UiSettings settings, int selected = -1)
    {
        var height = Math.Min(bottom - y, data.Profiles.Count + 4);
        if (height < 4) return y;

        Widgets.TitledBox(buffer, x, y, width, height, " Usage · recorded by Claude ", Theme.Green);

        var line = y + 1;
        var money = settings.ShowCosts;

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

    public static void Projects(ScreenBuffer buffer, int x, int y, int width, DashboardData data, int bottom, UiSettings settings, int selected = -1)
    {
        var height = Math.Min(bottom - y, Math.Max(4, data.Projects.Count + 3));
        if (height < 4) return;

        Widgets.TitledBox(buffer, x, y, width, height,
            $" Projects · {Metrics.Describe(data.Period)} ", Theme.Blue);

        if (data.Projects.Count == 0)
        {
            buffer.WriteClipped(x + 3, y + 1, "Nothing recorded in this period.", width - 6,
                new Sty(Theme.Muted, Theme.Panel, italic: true));
            return;
        }

        if (selected >= data.Projects.Count) selected = data.Projects.Count - 1;

        var rows = height - 2;
        var busiest = Math.Max(1, data.Projects.Max(p => p.Prompts));

        for (var row = 0; row < rows && row < data.Projects.Count; row++)
        {
            var project = data.Projects[row];

            // Home passes -1: nothing is selected there, because nothing on that
            // screen moves the selection.
            var here = row == selected;
            var lineY = y + 1 + row;
            var bg = here ? Theme.PanelSelected : Theme.Panel;

            buffer.Fill(x + 1, lineY, width - 2, 1, bg);
            buffer.Write(x + 2, lineY, here ? "▸" : " ", new Sty(Theme.Blue, bg, bold: true));

            buffer.WriteClipped(x + 4, lineY, project.Name, Math.Max(8, width / 3),
                new Sty(here ? Theme.Text : Theme.TextSoft, bg));

            var right = settings.ShowCosts && project.HasCost
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

    private static int Box(ScreenBuffer buffer, int x, int y, int width, string title, Rgb color,
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
}
