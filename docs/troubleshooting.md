# Troubleshooting

The failures worth a note, and what to do about them.

[&larr; Back to the README](../README.md)

**`claude-launcher` is not recognized after installing** — it is a PowerShell function, so it only
exists where its wrapper has been dot-sourced. Since 1.40.1 the installers register both the
Windows PowerShell 5.1 and the PowerShell 7 profile and drop a `claude-launcher.cmd` on `PATH`; an
older install registered only the profile of the host that ran `install.cmd` — Windows PowerShell —
which is why a fresh machine that lives in pwsh 7 saw nothing. Re-run the installer, then open a new
terminal. What to check if it still does not resolve:

```powershell
$PROFILE                                            # which profile is this host reading?
Get-Content $PROFILE                                # is the dot-source line in it?
Get-ExecutionPolicy -List                           # Restricted means no profile is loaded at all
Get-Command claude-launcher -All                    # the function, and the .cmd shim
```

A `Restricted` or `AllSigned` policy blocks the profile itself. Allow local scripts with
`Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`. An already-open cmd.exe or Explorer-spawned
terminal keeps the old `PATH` until it is restarted.

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
