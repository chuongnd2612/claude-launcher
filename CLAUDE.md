# CLAUDE.md

Guidance for Claude Code when working in this repository.

## What this is

Claude Launcher — an interactive Windows TUI that picks a Claude profile, a project, and a session
mode, then hands the choice back to a PowerShell wrapper which launches `claude` with the right
`CLAUDE_CONFIG_DIR`.

Two halves that talk through JSON files under `$HOME\.claude-launcher`:

| Half | Owns |
| ---- | ---- |
| `scripts/claude-launcher.ps1` (PowerShell 5.1) | projects from `$QuickPaths`, writes `state.json`, reads `result.json`, launches Claude |
| `src/` (.NET 8, C#) | the TUI; reads `state.json`, writes `result.json` and `profiles.json` |

The wrapper is the source of truth for the projects it knows about (`$QuickPaths`), and the TUI never
discovers projects on its own. It may *add* one: a folder typed into the new-terminal picker is stored
in `$HOME\.claude-launcher\projects.json` and merged in on load. That file belongs to the TUI; the
wrapper's list is never edited.

## Layout

```text
src/
  Program.cs          entry point, env pre-selection, --selftest
  App.cs              screen stack, render loop, key routing
  Models.cs           profile / project / settings models
  StateStore.cs       state.json, profiles.json, ui.json, result.json
  Tui/                Term, Theme, ScreenBuffer, BlockFont, Widgets
  Screens/            Profile, Project, Session, AddProfile, DeleteProfile, Settings
scripts/              claude-launcher.ps1 (wrapper), publish-standalone.ps1
packaging/            offline installer shipped inside the release zip
.github/workflows/    ci.yml, release.yml
```

## Build and check

```powershell
dotnet build src/ClaudeLauncher.csproj -c Release --nologo -warnaserror   # what CI runs
dotnet run --project src/ClaudeLauncher.csproj -- --selftest 120 44       # render every screen
```

`--selftest <width> <height>` renders each screen to plain text. It is the only way to verify the UI
without an interactive terminal — `Console.ReadKey` cannot be driven from a piped session, so key
handling has to be exercised by hand.

**Always run the selftest at 80x28 and at a large size after touching a screen or `Tui/`.** CI
asserts on rendered strings at 80x24, 100x30, 132x44 and 200x60, and layout regressions (overflowing
the footer bar, a box that no longer fits) only show up at the small sizes.

There is no unit test project. Store-level logic can be exercised directly:

```powershell
Add-Type -Path src\bin\Debug\net8.0\ClaudeLauncher.dll
$env:CLAUDE_LAUNCHER_PROFILES = "$env:TEMP\profiles-test.json"   # never point at the real file
[ClaudeLauncher.StateStore]::RemoveProfile('scratch')
```

## Conventions

- C# is `Nullable`/`ImplicitUsings` enabled and CI builds `-warnaserror`; a warning fails the build.
- Comments explain *why*, not *what*, and are rare. Match the surrounding density — do not narrate.
- Screens derive from `ScreenBase` and return a `ScreenAction` (`None`/`Push`/`Replace`/`Back`/
  `Exit`/`Finish`) from `HandleKey`. Never mutate the screen stack directly.
- New screens must be added to the `--selftest` list in `Program.cs`, and their keys documented in
  the README "Keys" table.
- Paths written to `profiles.json` go through `StateStore.CollapseHome` (stored as `$HOME/...`) and
  come back through `ExpandHome`.
- Icons must be a single single-width character; wide emoji break the grid.
- `scripts/claude-launcher.ps1` must keep its UTF-8 **BOM** and parse under Windows PowerShell 5.1 —
  CI enforces both. No PS7-only syntax in any shipped `.ps1`.
- `src/bin/`, `src/obj/` and `dist/` are build output and must never be committed. The repo has no
  `.gitignore`, so check `git status` before staging.

## Workflow rules

### Finishing a feature — PR, self-review, auto-merge

When a feature or fix is complete and verified, take it all the way to merged without waiting to be
asked again:

1. Branch off `main` (`feat/<slug>` or `fix/<slug>`) if the work is not already on one; never commit
   straight to `main`.
2. Build clean and run `--selftest` at a small and a large size. Do not open a PR on a red build.
3. Commit with a Conventional Commit subject (`feat:`, `fix:`, `fix(ci):`, `docs:`) and end the
   message with:
   `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`
4. `gh pr create` with a body covering what changed, why, and how it was verified — plus what was
   *not* verified (e.g. interactive key flow). End the body with:
   `🤖 Generated with [Claude Code](https://claude.com/claude-code)`
5. Self-review before merging: read the PR diff (`gh pr diff`) as a reviewer, not as the author.
   Post the review with `gh pr review --approve --body "..."` stating what was checked. If the
   review turns up a real problem, fix it and push before merging — do not merge over a known issue.
6. Wait for `ci` to pass (`gh pr checks --watch`), then merge and clean up:
   `gh pr merge --squash --delete-branch --admin`
7. Report the PR URL and the merge result. If any step fails, stop and say so — never report a merge
   that did not happen.

### "Release" — tag and publish

When the user says *release*, cut it end to end:

1. Confirm `main` is up to date, clean, and green.
2. Pick the next semver tag from `git tag --list` (tags are `vX.Y.Z`; latest wins). Patch for fixes,
   minor for features. Ask only if the bump is genuinely ambiguous.
3. `git tag vX.Y.Z && git push origin vX.Y.Z`
4. `release.yml` does the rest on the tag push: publishes the self-contained exe, smoke-tests it,
   builds the zip + SHA256, and publishes the GitHub release with generated notes. Do **not**
   `gh release create` by hand — that would race the workflow.
5. Watch it (`gh run watch`) and report the release URL, or the failure if the workflow goes red.

The version is stamped from the tag via `-p:Version=`; `<Version>` in `ClaudeLauncher.csproj` is
only a local fallback and does not need bumping per release.

## Documentation

`README.md` is user-facing and detailed — keys, `profiles.json` schema, install/uninstall switches,
env vars, changelog. Any user-visible change (new key, new screen, new config field, new installer
switch) updates the README in the same PR.
