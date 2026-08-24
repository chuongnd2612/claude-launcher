using ClaudeLauncher.Sessions;
using ClaudeLauncher.Tui;

namespace ClaudeLauncher.Screens;

/// <summary>
/// Usage per account: the detail behind the band in the header.
///
/// The band can only carry a session count, so this is where the rest lives. It
/// keeps the two kinds of number apart and says which is which, because they
/// cannot honestly share a label: sessions and prompts come from lines with a
/// timestamp, so they follow the period, while cost and tokens are Claude's own
/// running total in .claude.json with no dates on them at all. A screen that
/// showed both under one "today" would be wrong about half of it.
/// </summary>
public sealed class UsageScreen : ScreenBase
{
    private readonly SessionService? _service;
    private readonly bool _fixture;

    private DashboardData? _data;
    private bool _building;
    private Period _period;
    private int _index;

    public UsageScreen(App app, SessionService service) : base(app)
    {
        _service = service;
        _period = Period.Today;
        Rebuild();
    }

    /// <summary>Fixture constructor for --selftest.</summary>
    public UsageScreen(App app, DashboardData data) : base(app)
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
                // Build, not Cached: Cached returns null on a first call and
                // starts a build of its own, so asking it here and falling back
                // would run two of them at once over the same tens of megabytes.
                // This is a screen opened on purpose, which is where the
                // dashboard pays that cost too.
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

        Widgets.SectionTitle(buffer, y, "Usage", Metrics.Describe(_period));
        y += 2;

        if (_data is null)
        {
            buffer.WriteClipped(margin + 1, y, "Reading what Claude has written down…", width - 2,
                new Sty(Theme.Muted, Theme.Bg, italic: true));

            Footer(buffer);
            return;
        }

        var profiles = _data.Profiles;
        if (profiles.Count == 0)
        {
            buffer.WriteClipped(margin + 1, y, "No profiles are configured.", width - 2,
                new Sty(Theme.Muted, Theme.Bg, italic: true));

            Footer(buffer);
            return;
        }

        if (_index >= profiles.Count) _index = profiles.Count - 1;

        var money = App.Settings.ShowCosts;
        var bottom = buffer.Height - 5;

        y = Rows(buffer, margin, y, width, profiles, money, bottom);

        // Which figures the period applies to, said plainly rather than left to
        // be guessed - the reason this screen exists in the shape it does.
        if (y < bottom)
        {
            var note = money
                ? "sessions and prompts are this period · cost and tokens are Claude's running totals"
                : "sessions and prompts are this period · tokens are Claude's running total";

            buffer.WriteClipped(margin + 1, y + 1, note, width - 2,
                new Sty(Theme.Dim, Theme.Bg, italic: true));
        }

        Footer(buffer);
    }

    /// <summary>One row per account, widest column first so the numbers line up.</summary>
    private int Rows(ScreenBuffer buffer, int margin, int y, int width,
        List<ProfileUsage> profiles, bool money, int bottom)
    {
        var head = money ? $"{"sessions",9} {"prompts",8} {"cost",9} {"tokens",8}"
            : $"{"sessions",9} {"prompts",8} {"tokens",8}";

        var right = margin + width - 2;
        var nameWidth = Math.Max(10, width - head.Length - 6);

        buffer.WriteClipped(margin + 2, y, "account", nameWidth, new Sty(Theme.Dim, Theme.Bg));
        buffer.WriteRight(right, y, head, new Sty(Theme.Dim, Theme.Bg));
        y++;

        foreach (var profile in profiles)
        {
            if (y >= bottom) break;

            var here = profiles[_index] == profile;
            var fill = here ? Theme.PanelSelected : Theme.Bg;

            if (here) buffer.Fill(margin, y, width, 1, fill);

            buffer.Write(margin + 1, y, here ? "›" : " ", new Sty(Theme.Blue, fill, bold: true));

            var name = profile.Icon + " " + profile.Label;
            if (profile.Account.Length > 0) name += " · " + profile.Account;

            buffer.WriteClipped(margin + 3, y, name, nameWidth,
                new Sty(ProfileLook.Color(profile.Key), fill, bold: here));

            var cost = profile.HasCost ? $"${profile.CostUsd:0.00}" : "—";
            var tokens = Format.Tokens(profile.OutputTokens);

            var numbers = money
                ? $"{profile.Sessions,9} {profile.Prompts,8} {cost,9} {tokens,8}"
                : $"{profile.Sessions,9} {profile.Prompts,8} {tokens,8}";

            buffer.WriteRight(right, y, numbers, new Sty(Theme.TextSoft, fill));
            y++;
        }

        if (profiles.Count < 2 || y >= bottom) return y;

        var sessions = profiles.Sum(p => p.Sessions);
        var prompts = profiles.Sum(p => p.Prompts);

        var total = money
            ? $"{sessions,9} {prompts,8} {$"${_data!.TotalCost:0.00}",9} {Format.Tokens(_data.TotalOutput),8}"
            : $"{sessions,9} {prompts,8} {Format.Tokens(_data!.TotalOutput),8}";

        buffer.WriteClipped(margin + 3, y, "all accounts", nameWidth, new Sty(Theme.Text, Theme.Bg, bold: true));
        buffer.WriteRight(right, y, total, new Sty(Theme.Text, Theme.Bg, bold: true));

        return y + 1;
    }

    private void Footer(ScreenBuffer buffer) =>
        Widgets.Footer(buffer, KeyMap.UsageFooter(), KeyMap.Help);

    public override ScreenAction HandleKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.F1:
                return ScreenAction.Push(new KeysScreen(App, "Usage", KeyMap.Usage()));
            case ConsoleKey.Escape:
            case ConsoleKey.Backspace:
                return ScreenAction.Back;
            case ConsoleKey.UpArrow:
                _index = Math.Max(0, _index - 1);
                return ScreenAction.None;
            case ConsoleKey.DownArrow:
                var last = (_data?.Profiles.Count ?? 0) - 1;
                if (_index < last) _index++;
                return ScreenAction.None;
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

                Rebuild();
                return ScreenAction.None;

            case 'r':
                Rebuild();
                return ScreenAction.None;

            case 'q':
                return ScreenAction.Exit;
        }

        return ScreenAction.None;
    }
}
