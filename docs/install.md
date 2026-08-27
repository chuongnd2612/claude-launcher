# Install, update, uninstall

Getting Claude Launcher onto a machine, keeping it current, and taking it off again.

[&larr; Back to the README](../README.md)

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
removes leftovers from older versions, and registers the wrapper in every shell (see
[What the installer registers](#what-the-installer-registers)).

**Or download the zip** from the [Releases page](https://github.com/chuongnd2612/claude-launcher/releases),
extract it, and run `install.cmd`. Same result, no network calls during install.

**From source (contributors).** Run `install.cmd` in a clone. It uses a process-scoped ExecutionPolicy
Bypass only for the installer, unblocks package files, builds the TUI with the .NET 8 SDK, and
registers the wrapper. If a prebuilt `ClaudeLauncher.exe` sits next to `install.cmd`, that binary is
used and the SDK is not required.

After installation, open a new terminal and run `claude-launcher`. In the window you installed from,
reload the profile first:

```powershell
. $PROFILE
claude-launcher
```

## What the installer registers

`claude-launcher` is a PowerShell *function*, not an executable, so it exists only where its wrapper
has been dot-sourced. Every installer therefore registers three things:

| What | Where | Why |
| ---- | ----- | --- |
| The wrapper | `$HOME\Documents\WindowsPowerShell\functions\claude-launcher.ps1` | Defines `claude-launcher`, `claude-work` and `claude-personal` |
| A dot-source line | **both** `Documents\WindowsPowerShell\` and `Documents\PowerShell\Microsoft.PowerShell_profile.ps1` | Windows PowerShell 5.1 and PowerShell 7 read different profiles; `install.cmd` runs under 5.1, so registering only the running host's `$PROFILE` would leave a pwsh 7 machine with nothing |
| `claude-launcher.cmd` | `$HOME\.claude-launcher`, added to your user `PATH` | cmd.exe and anything that is not PowerShell cannot see a function. The shim re-enters pwsh (or Windows PowerShell) and calls it |

The Documents folder is taken from the running host's `$PROFILE`, so a OneDrive-redirected Documents
is followed rather than guessed. Adding to `PATH` edits only the user key, appends only our own
entry, and never rewrites `%USERPROFILE%`-style entries into fixed paths. `uninstall.ps1` takes all
three back out.

If your execution policy is `Restricted` — the Windows default on a fresh machine — no profile is
loaded at all and `claude-launcher` still looks missing. The installer says so; the fix is:

```powershell
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
```

Upgrading from 1.4.x: re-run the installer. It removes the stale `Terminal.Gui.dll` and `NStack.dll`
left by the previous version. `profiles.json` is untouched and stays compatible.

> First run may show a Windows SmartScreen warning, because the binary is not code-signed. Choose
> **More info → Run anyway**, or verify the SHA256 from the release page yourself.

## Updates

The launcher asks GitHub whether there is a newer release when it starts, and says so on whichever
screen you land on — the profile picker, Home, or Settings:

```text
update available · v1.29.0 · press u
```

**`u` asks again, any time.** With an update known it opens the update screen; with none known it
runs a check there and then and says what came back — `up to date · v1.31.0 is the newest`, or
`could not reach github`. That works from the profile picker, Home and Settings, and it works even
with the automatic check switched off: pressing the key is you asking, not the setting.

The update screen shows what is installed, what is available, and how to get it.
`Enter` closes the launcher and lets the wrapper run the installer — it has to happen in that order,
because the installer replaces the very exe the launcher is running from. `n` opens the release notes
in a browser, `s` stops the asking, `Esc` leaves it for later.

What the check does and does not do:

- **It never blocks.** The request runs in the background from the first frame; the answer arrives
  when it arrives, and the launcher is identical until then.
- **It asks rarely.** At most once every six hours, with the answer kept in `$HOME\.claude-launcher\update.json`, so a day of
  launches is a handful of requests.
- **It fails quietly.** Offline, behind a proxy, rate limited: nothing is shown and nothing is said.
- **It sends nothing about you.** One unauthenticated GET to the public releases API.
- **It never nags a build of its own.** A binary with no version stamped on it reads as `0.0.0` and is
  left alone, so working from source does not produce an update prompt.

Turn it off in **Settings → Check for updates**, or for one run with `CLAUDE_LAUNCHER_NO_UPDATE_CHECK=1`.
Point it at a fork with `CLAUDE_LAUNCHER_REPO=owner/repo`.

## Uninstall

If you installed from the zip or a clone, run it from that folder:

```powershell
.\uninstall.ps1
```

If you used the one-line installer and have no local copy:

```powershell
irm https://raw.githubusercontent.com/chuongnd2612/claude-launcher/main/uninstall.ps1 | iex
```

By default it removes the binary, the cmd shim and its `PATH` entry, the wrapper, and the dot-source
line from every PowerShell profile, after copying `profiles.json` and `ui.json` to `$HOME\claude-launcher-backup-<timestamp>` and backing
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
