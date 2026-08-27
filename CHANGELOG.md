# Changelog

Every release, newest first. The tags are on the
[releases page](https://github.com/chuongnd2612/claude-launcher/releases).

## Unreleased

- **Fixed: `claude-launcher` was missing after a first install on a fresh machine.** The command is
  a PowerShell function, and the installers only ever wrote the dot-source line into `$PROFILE` —
  the profile of the host running the installer. `install.cmd` runs Windows PowerShell, so a machine
  whose shell is PowerShell 7 got the line in a file nothing reads. All three installers now
  register both hosts' profiles, taking the Documents folder from the running host's `$PROFILE` so a
  OneDrive-redirected Documents is followed rather than guessed.
- **cmd.exe can start it too.** A `claude-launcher.cmd` shim goes into `$HOME\.claude-launcher`,
  which is added to the user `PATH`: it re-enters pwsh where there is one, Windows PowerShell
  otherwise, and calls the function there. It goes through `-File` and a small entry script rather
  than `-Command`, so an argument with spaces keeps its quotes. The `PATH` edit appends only our own
  entry and writes the raw registry value back, leaving `%USERPROFILE%`-style entries expandable.
- **The installer says when the execution policy is the problem.** A fresh Windows defaults to
  `Restricted`, which stops the profile from loading at all — the installer runs under `Bypass` and
  so could never see it. It now reports the effective policy and the one-line fix.
- `uninstall.ps1` removes the shim and its `PATH` entry along with everything else.

## 1.40.0

- **The usage band separates the accounts.** Each profile is now its own group behind a
  `│`, with its name in its own colour — a dot used to separate the windows and the
  accounts alike, so the band read as one run of numbers and finding one account in it
  meant reading all of them.
- **The band shows how long each window has left**: `5h 61% →2h11m`, so a percentage says
  not just how much is gone but how long until it comes back. Claude records a reset time
  for both windows and nothing was showing it. Minutes and up, dimmed and last so it never
  competes with the number, and it is the first thing after the gauges to go as the window
  narrows — but it outranks the gauge itself, which only draws the percentage twice.
- **The refresh is visibly a button**: the band now starts with `↻ usage`. Clicking the
  label already refreshed it and `Alt+R` still does, but nothing on screen said so.

## 1.39.0

- **The usage band can be refreshed by hand**: `Alt+R`, or a click on the word `usage` in
  the rule. It reads `usage…` while it is reading. The key is handled before any screen
  sees it, so it works from a focused terminal tile as well, and `r` on the `Alt+U` detail
  screen now refreshes the band with it — the two showed the same percentages off two
  separate clocks and could disagree.
- **Fixed: the band could sit on old figures indefinitely.** It goes looking again once a
  minute, but only when something else woke the render loop — so on a screen that sits
  still, profiles or settings, the numbers held whatever they said when you arrived. The
  loop now comes round for the band's own deadline.
- **Fixed: a tile you closed came back, and could not be got rid of.** The closed set lived
  on the wall screen, and every route back to the wall builds a new one — starting a
  terminal lands on a fresh wall by itself — so a session running in someone else's
  terminal reappeared on the next visit. Closings are kept for the run now. A tile closed
  before Claude assigned it an id also stays closed once the id arrives: the hiding moves
  onto the id with it, and frees the project key so the next tile opened there is not
  hidden by a closing that was never about it. Opening a session from Home puts its tile
  back, so a closing is undoable without restarting.

## 1.38.1

- **Fixed: the key editor could not record a chord with a modifier.** Holding Alt is a key
  press of its own and arrives before the combination, so `Alt+Z` reaches the editor as
  the modifier and *then* as `Alt+Z`. The capture ended on the first of those, and since a
  modifier alone has no name it reported a key that could not be bound. Modifier presses
  are ignored while capturing now, and an unnameable key no longer closes the capture.

## 1.38.0

- **Fixed: a tile you asked to close could stay while others vanished.** Hiding was keyed
  by session id alone, and a chat has no id until Claude's first reply — so closing one
  added an empty key that matched every other id-less tile. It is keyed the way the pane
  order already is now.
- **The focused tile's footer says how to close it.** A tile takes every plain key, so
  pressing `w` sends a `w` to Claude; the footer never mentioned `Ctrl+W`, which made a
  working command look like a missing one.
- **The chords a focused tile reserves are rebindable** — release, find, close, select,
  zoom and new terminal, under `tile` in the key editor. `Alt+F`, `Alt+W` and `Alt+T` were
  duplicates of the `Ctrl` chords and are gone; bind them back if you want them.
- **The band shows both windows for every account** — the five-hour session allowance and
  the weekly one, each with its own gauge and colour. It used to show whichever single
  window Claude had marked as live, which put one account's 5h figure beside another's
  weekly one and made the two look comparable when they were not.

## 1.37.0

- **Keys can be rebound.** `Alt+K` opens an editor: pick a command, press a key, done.
  Saved to `keys.json`, which you can also edit by hand. Clashes within a screen are
  detected and reported — the first thing the check found was a clash in the new defaults
  themselves. What a key *says* in the footers and the `F1` list now comes from the
  binding, so a rebound command cannot be advertised wrongly.
- **The usage band now shows a real percentage.** It reads the utilisation Claude caches
  per account under `cachedUsageUtilization`, so the number is a share of the actual plan
  limit rather than a share of whatever the other account happened to do. The gauge is
  coloured by the reading, the marker says whether it is the 5-hour or the weekly window,
  and a `~` marks a figure older than the window it describes.
- **`Alt+U` shows both windows per account** with time until each resets and when each
  figure was last refreshed.

## 1.36.0

- **A usage band in the header on every screen**: today's session count per account, with
  a bar scaled against the busiest one. Written into the rule under the header so it
  costs no rows, degrading to counts-only and then to a single total on narrow windows.
- **`Alt+U` opens a Usage screen** — per-account sessions, prompts, cost and output
  tokens, with a period toggle. It states which figures follow the period and which are
  running totals, because cost and tokens in `.claude.json` carry no dates and cannot
  honestly be called "today".
- Per-profile session and prompt counts are new on the dashboard's data, filled from the
  history read that was already happening per profile.

## 1.35.0

- **Reorder the wall's tiles**: drag one onto another, or `Ctrl+Shift+←→↑↓` to move the focused pane.
  The arrangement is saved to `ui.json` as `terminalOrder` and comes back next launch.
- **`F1` lists every key the current screen answers to**, grouped and scrollable, with `?` as an alias
  where nothing is expecting text. Footers now show four or five hints and always keep the way in to
  the full list — previously a hint that did not fit was dropped silently, so on narrow windows keys
  like `Quit` and `Back` simply vanished. Around forty working shortcuts had never been advertised
  anywhere; they are all listed now, including which pane has to have the keyboard for each to work.
- Fixed: `d` on the profile screen removed a profile instead of opening the dashboard, which the
  footer advertised and no key could reach. `d` is now the dashboard, matching Home, and **remove
  moved to `x`** (`Del` still works).
- Fixed: the terminal preview screen offered `type` and `Ctrl+]`, neither of which does anything on a
  replayed capture.
- Fixed: a released terminal tile showed the wall's footer, which promised `Enter` would attach when
  it actually starts typing again. It has its own hints now.
- Home gained `s` for settings, which was previously reachable only from the profile picker, and now
  advertises `u` for the update check.
- `y` / `n` are advertised on the stop, delete and remove confirmations, where they always worked.

## 1.5.0

- Redesigned TUI: gradient block banner, numbered step badges, rounded cards with icon badges,
  pinned footer key bar, tips box.
- Removed the `Terminal.Gui` dependency; added a custom truecolor renderer. Build is now offline.
- New **Add profile** screen writing to `profiles.json`, and a **Settings** screen (`ui.json`).
- Project screen: live filter, scrollbar, keyboard paging.
- Session screen: `n` / `c` / `r` quick keys and a launch summary.
- Responsive layout with graceful degradation down to 80x24, live resize handling.
- Optional `description` field on profiles.
- Redesigned post-launch banner in the PowerShell wrapper, with a fallback for non-VT hosts.
- `Invoke-ClaudeLauncher -Profile` is now `-ProfileName`, with `-Profile` kept as an alias.
- `uninstall.ps1` with `-KeepConfig` / `-Purge` / `-IncludeClaudeConfigDirs` / `-WhatIf`, shipped as a
  release asset so it can be run straight from a URL.
- Mouse selection was dropped along with Terminal.Gui; the flow is keyboard-driven.
