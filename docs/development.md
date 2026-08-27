# Development

How the two halves fit together, and how a release is cut.

[&larr; Back to the README](../README.md)

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
  Screens/            Home, Terminals, Chat, Resume, SessionDetail, Profile,
                      Project, Session, AddProfile, DeleteProfile, KillSession,
                      DeleteSession, Settings
  Sessions/           reads Claude's session registry and transcripts; owns
                      stream-json sessions for the chat screen
```

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
   `install.cmd`/`install.ps1`, the README, the changelog, the whole of `docs/` and the example
   config.
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
