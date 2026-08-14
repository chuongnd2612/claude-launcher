# Claude Launcher

A Windows-first Claude CLI launcher for PowerShell 5.1+.

Version **1.5.0** — redesigned TUI, no NuGet dependencies.

## Features

- Interactive terminal UI: 24-bit colors, gradient banner, rounded profile cards, step badges.
- Claude profiles: `work`, `personal`, and any profile you add via `profiles.json` — or from the
  built-in **Add profile** screen (`a`).
- Edit (`e`) and remove (`d`) profiles straight from the profile screen; removal asks for
  confirmation and leaves the profile's config directory on disk.
- Settings screen (`s`) with preferences persisted to `ui.json`.
- Projects are sourced from the existing `$QuickPaths` registry; no hardcoded project root.
- Project list with live filter (`/`), scrolling, and a "Current directory" entry.
- New / Continue / Resume sessions, with `n` / `c` / `r` quick keys.
- `CLAUDE_CONFIG_DIR` is switched per profile and restored after Claude exits.
- `claude-work qagent`, `claude-work qagent --resume`, and `claude-personal ...` shortcuts.
- Layout adapts to the window: full banner on wide terminals, compact chrome at 80x24.
- Installer does not change ExecutionPolicy globally.
- Installer stops on build failure and does not report a failed build as a successful install.

## Requirements

End users:

- Windows PowerShell 5.1 or PowerShell 7+
- `claude` available on PATH
- A terminal with VT / truecolor support — **Windows Terminal recommended** (WezTerm, Alacritty and
  the VS Code terminal work too). Legacy `conhost.exe` renders a degraded but usable UI.
- No .NET installation required when using the released standalone exe.

Contributors (building from source):

- .NET 8 SDK

## Install

**For everyone (recommended) — no .NET needed.** The release ships a standalone
`ClaudeLauncher.exe`, so nothing has to be installed beyond the launcher itself:

```powershell
irm https://raw.githubusercontent.com/chuongnd2612/claude-launcher/main/install-online.ps1 | iex
```

Pin a version, or install from a fork:

```powershell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/chuongnd2612/claude-launcher/main/install-online.ps1))) -Version v1.5.0
```

The installer downloads the exe and the wrapper, verifies the published SHA256, unblocks both files,
removes leftovers from older versions, and registers the wrapper in your `$PROFILE`.

**Or download the zip** from the [Releases page](https://github.com/chuongnd2612/claude-launcher/releases),
extract it, and run `install.cmd`. Same result, no network calls during install.

**From source (contributors).** Run `install.cmd` in a clone. It uses a process-scoped ExecutionPolicy
Bypass only for the installer, unblocks package files, builds the TUI with the .NET 8 SDK, and
registers the wrapper. If a prebuilt `ClaudeLauncher.exe` sits next to `install.cmd`, that binary is
used and the SDK is not required.

After installation:

```powershell
. $PROFILE
claude-launcher
```

Upgrading from 1.4.x: re-run the installer. It removes the stale `Terminal.Gui.dll` and `NStack.dll`
left by the previous version. `profiles.json` is untouched and stays compatible.

> First run may show a Windows SmartScreen warning, because the binary is not code-signed. Choose
> **More info → Run anyway**, or verify the SHA256 from the release page yourself.

## Commands

```powershell
claude-launcher
claude-work
claude-work qagent
claude-work qagent --resume
claude-work qagent --continue
claude-personal
claude-personal my-project
```

`claude-launcher` opens the full interactive flow:

```text
Profile -> Project -> Session -> Launch
```

The profile shortcuts preselect the profile. If a project is supplied, the launcher can launch
directly without showing the selection screens.

## Keys

| Screen  | Keys |
| ------- | ---- |
| Profile | `↑↓←→` navigate · `Enter` select · `1..9` jump · `a` add · `e` edit · `d` / `Del` remove · `s` settings · `q` quit |
| Project | `↑↓` navigate · `PgUp/PgDn` `Home/End` · `Enter` select · `/` filter · `Esc` back · `q` quit |
| Session | `↑↓` navigate · `Enter` launch · `n` new · `c` continue · `r` resume · `Esc` back |
| Add / Edit profile | `Tab` / `↑↓` next field · `Enter` save · `Esc` cancel |
| Remove profile | `←→` / `Tab` choose · `Enter` confirm · `y` remove · `n` / `Esc` cancel |
| Settings | `↑↓` navigate · `Enter` / `←→` change · `Esc` back |

The window can be resized at any time; the UI redraws itself.

## Profiles

Profiles live at:

```text
$HOME\.claude-launcher\profiles.json
```

Example:

```json
{
  "profiles": [
    {
      "name": "work",
      "label": "Work",
      "icon": "W",
      "configDir": "$HOME/.claude-work",
      "description": "Default profile for work projects"
    },
    {
      "name": "personal",
      "label": "Personal",
      "icon": "P",
      "configDir": "$HOME/.claude-personal",
      "description": "Personal profile"
    }
  ]
}
```

`description` is new in 1.5.0 and optional — it is the italic line on the profile card. `icon` should
stay a single, single-width character (a letter or a symbol such as `◆`); wide emoji break the grid
alignment. Profiles created from the **Add profile** screen are appended to this same file, with the
path stored back as `$HOME/...` when it sits under your user profile. **Edit profile** rewrites the
matching entry in place (renaming the key is allowed), and **Remove profile** deletes the entry only —
the `configDir` folder and its Claude history are left untouched. The last remaining profile cannot
be removed.

## Settings

`s` on the profile screen opens preferences, saved to `$HOME\.claude-launcher\ui.json`:

| Setting | Effect |
| ------- | ------ |
| Paint background | Use the launcher canvas color instead of your terminal's background |
| Show tips | Show or hide the tips box |
| Default session mode | Which option is preselected on step 3 (`new` / `continue` / `resume`) |

## Releasing

Cutting a release is one tag push:

```powershell
git tag v1.5.1
git push origin v1.5.1
```

`.github/workflows/release.yml` then, on `windows-latest`:

1. Publishes a self-contained, single-file `win-x64` exe, stamped with the version from the tag.
2. Runs `--selftest` at 80x24 and 132x44 as a smoke test, failing the release if a screen breaks.
3. Assembles `claude-launcher-<version>-win-x64.zip` with the exe, the wrapper, an offline
   `install.cmd`/`install.ps1`, the README and the example config.
4. Computes SHA256 for the exe and the zip.
5. Publishes a GitHub release with auto-generated notes and attaches everything, including the
   loose `ClaudeLauncher.exe`, `ClaudeLauncher.exe.sha256` and `claude-launcher.ps1` that
   `install-online.ps1` downloads.

`.github/workflows/ci.yml` runs on every push and PR: build with `-warnaserror`, render every screen
at four terminal sizes, then parse all PowerShell scripts under **Windows PowerShell 5.1** and assert
`scripts/claude-launcher.ps1` still has its UTF-8 BOM. That last check matters — without the BOM,
PowerShell 5.1 reads the file as ANSI and the box-drawing characters turn to mojibake.

Build the standalone exe locally the same way CI does:

```powershell
.\scripts\publish-standalone.ps1
.\scripts\publish-standalone.ps1 -Version 1.5.1 -Output C:\temp\out
```

Expect roughly 35-45 MB, since a self-contained build embeds the .NET runtime. For a ~2 MB binary use
`--self-contained false`, but then each machine needs the .NET 8 **Runtime** (not the SDK).

Do not add `PublishTrimmed`: `StateStore` serializes through reflection, so the trimmer strips the
metadata it needs and JSON silently breaks at runtime. Trimming would require converting to a
source-generated `JsonSerializerContext` first.

Before the first public release, remember to add a `LICENSE` file — GitHub shows the repository as
"all rights reserved" without one, which discourages the colleagues you want to share it with.

## Uninstall

If you installed from the zip or a clone, run it from that folder:

```powershell
.\uninstall.ps1
```

If you used the one-line installer and have no local copy:

```powershell
irm https://raw.githubusercontent.com/chuongnd2612/claude-launcher/main/uninstall.ps1 | iex
```

By default it removes the binary, the wrapper and the dot-source line from every PowerShell profile,
after copying `profiles.json` and `ui.json` to `$HOME\claude-launcher-backup-<timestamp>` and backing
up each profile it edits to `<profile>.claude-launcher.bak`.

| Switch | Effect |
| ------ | ------ |
| `-KeepConfig` | Leave `profiles.json` and `ui.json` in place, ready for a reinstall |
| `-Purge` | Delete `$HOME\.claude-launcher` entirely, no backup |
| `-IncludeClaudeConfigDirs` | Also delete the folders listed as `configDir`, after showing their size and asking for confirmation |
| `-Force` | Skip the confirmation prompts |
| `-WhatIf` | Print what would happen and change nothing |

Switches need a local copy or the scriptblock form — `irm ... | iex` has nowhere to put them:

```powershell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/chuongnd2612/claude-launcher/main/uninstall.ps1))) -WhatIf
```

Your Claude Code data is **not** removed unless you pass `-IncludeClaudeConfigDirs`. Folders like
`$HOME\.claude-work` hold conversation history, settings and MCP servers; the launcher only points
`CLAUDE_CONFIG_DIR` at them, it does not own them.

The uninstaller also flags two things older manual installs left behind: a `CLAUDE_CONFIG_DIR`
variable set at User scope (v1.5.x only ever sets it per-process and restores it afterwards), and
wrapper functions pasted directly into a profile instead of dot-sourced — it reports the file and line
number rather than guessing where the block ends.

The `claude-launcher`, `claude-work` and `claude-personal` functions stay defined in the current
session until you open a new terminal.

## Architecture

```text
PowerShell
   |
   +-- QuickPaths ---------> project registry
   |
   +-- Claude Launcher ----> profile registry + TUI
                                |
                                +--> profile
                                +--> project
                                +--> session mode
                                |
                                +--> Claude CLI
```

`quick-set` remains the source of truth for project locations. The launcher only orchestrates the
profile and Claude session.

Source layout:

```text
src/
  Program.cs          entry point, env pre-selection, --selftest
  App.cs              screen stack, render loop, key routing
  Models.cs           profile / project / settings models
  StateStore.cs       state.json, profiles.json, ui.json, result.json
  Tui/
    Term.cs           VT enable, alt screen, raw output
    Theme.cs          palette
    ScreenBuffer.cs   cell grid + single-write flush
    BlockFont.cs      5x5 block font for the banner
    Widgets.cs        chrome, step badges, cards, tips, footer
  Screens/            Profile, Project, Session, AddProfile, DeleteProfile, Settings
```

## Runtime state

The PowerShell wrapper writes state/result files under `$HOME\.claude-launcher` and passes their
paths to the native TUI via `CLAUDE_LAUNCHER_STATE` and `CLAUDE_LAUNCHER_RESULT`. Since 1.5.0 it also
passes `CLAUDE_LAUNCHER_PROFILES` so the TUI can add, edit and remove profiles. The native app falls back to
the same directory, so it never relies on the Windows Temp directory for launcher state.

Environment variables read by the executable:

| Variable | Meaning |
| -------- | ------- |
| `CLAUDE_LAUNCHER_STATE` | `state.json` prepared by the wrapper |
| `CLAUDE_LAUNCHER_RESULT` | `result.json` written on launch |
| `CLAUDE_LAUNCHER_PROFILES` | `profiles.json` location |
| `CLAUDE_LAUNCHER_PROFILE` | preselect a profile (name or label) |
| `CLAUDE_LAUNCHER_PROJECT` | preselect a project |
| `CLAUDE_LAUNCHER_MODE` | `new` \| `continue` \| `resume` |

The `result.json` contract is unchanged from 1.4.x: `profile`, `label`, `icon`, `configDir`,
`project`, `path`, `mode`.

## UI

The renderer is hand-written (`src/Tui/`) rather than a widget toolkit, because 24-bit gradients,
rounded cards and filled step badges are not expressible with the 16-color model of Terminal.Gui 1.x.
Every frame is composed into a cell grid and flushed with one write, with runs of identical style
sharing a single escape sequence — so there is no flicker and no partial repaint.

Layout tiers:

| Terminal | Chrome |
| -------- | ------ |
| ≥ 96x30 | Full block-font banner, 5x3 step badges, full cards, tips box |
| ~100x30 | Full banner, two-column cards, tips hidden when space runs out |
| 80x24 | One-line spaced banner, single-line step markers, compact cards with scroll indicator |

## Troubleshooting

**Escape sequences printed as text** — the terminal has no VT support. Use Windows Terminal, or
disable "Paint background" in settings for a lighter look.

**Box characters show as `?`** — the console code page is not UTF-8. Windows Terminal handles this
automatically; in legacy hosts run `chcp 65001`.

**Layout check without a terminal** — render every screen as plain text:

```powershell
$env:CLAUDE_LAUNCHER_STATE="$HOME\.claude-launcher\state.json"
& "$HOME\.claude-launcher\ClaudeLauncher.exe" --selftest 132 44
```

**`irm ... | iex` fails with `Unexpected attribute 'CmdletBinding'`** — you are running a cached copy
of the installer from when the file still carried a UTF-8 BOM. `irm` passes the BOM to
`iex` as a literal character, so `param()` is no longer the first statement. Strip it yourself:

```powershell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/chuongnd2612/claude-launcher/main/install-online.ps1).TrimStart([char]0xFEFF)))
```

**Build fails on restore** — `src/NuGet.config` clears all package sources on purpose (the project
has no dependencies, so the build stays offline-friendly). Delete that file if you add a
`PackageReference`.

## Changelog

### 1.5.0

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
