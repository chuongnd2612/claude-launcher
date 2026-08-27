<div align="center">

# ✦ Claude Launcher

**Run every Claude account, project and session from one Windows terminal.**

Pick a profile, pick a project, and Claude opens with the right `CLAUDE_CONFIG_DIR`. Then
watch every session you have running side by side, with each account's plan usage — and the
time until it resets — in the header of every screen.

[![ci](https://github.com/chuongnd2612/claude-launcher/actions/workflows/ci.yml/badge.svg)](https://github.com/chuongnd2612/claude-launcher/actions/workflows/ci.yml)
[![release](https://img.shields.io/github/v/release/chuongnd2612/claude-launcher?label=release)](https://github.com/chuongnd2612/claude-launcher/releases/latest)
[![downloads](https://img.shields.io/github/downloads/chuongnd2612/claude-launcher/total?label=downloads)](https://github.com/chuongnd2612/claude-launcher/releases)
[![platform](https://img.shields.io/badge/platform-Windows-0078D4)](https://github.com/chuongnd2612/claude-launcher/releases/latest)
[![powershell](https://img.shields.io/badge/PowerShell-5.1%2B-5391FE)](https://learn.microsoft.com/powershell/)
[![dependencies](https://img.shields.io/badge/NuGet%20dependencies-0-3FD07E)](src/ClaudeLauncher.csproj)

<!-- Screenshots go here: capture them as described in docs/CAPTURING.md, drop the
     PNGs into docs/assets/, then uncomment this and delete the text preview below.
<img src="docs/assets/hero.png" alt="Claude Launcher" width="900">
-->

</div>

```text
             ████ █      ███  █   █ ████  █████   █      ███  █   █ █   █  ████ █   █ █████ ████
            █     █     █   █ █   █ █   █ █       █     █   █ █   █ ██  █ █     █   █ █     █   █
        ✦   █     █     █████ █   █ █   █ ████    █     █████ █   █ █ █ █ █     █████ ████  ████
            █     █     █   █ █   █ █   █ █       █     █   █ █   █ █  ██ █     █   █ █     █  █
             ████ █████ █   █  ████ ████  █████   █████ █   █  ████ █   █  ████ █   █ █████ █   █

                                      Your intelligent CLI companion

                               ╭───╮              ╭───╮              ╭───╮
                               │ 1 │ ──────────── │ 2 │ ──────────── │ 3 │
                               ╰───╯              ╰───╯              ╰───╯
                              Profile            Project            Session

  ─── ↻ usage │ W Work 5h █░░░░░ 3% →2h11m 7d ██░░░░ 28% →4d │ P Personal 5h █████░ 91% →37m ───

  Select a Claude profile

  ╭──────────────────────────────────────────╮   ╭──────────────────────────────────────────╮
  │ ╭───╮  Work                            ✓ │   │ ╭───╮  Personal                          │
  │ │ W │  $HOME/.claude-work                │   │ │ P │  $HOME/.claude-personal            │
  │ ╰───╯  Default profile                   │   │ ╰───╯  Personal profile                  │
  ╰──────────────────────────────────────────╯   ╰──────────────────────────────────────────╯
```

## 🚀 Why Claude Launcher?

- **🔑 One terminal, every account.** Work and personal Claude accounts live in separate
  config dirs. The launcher switches `CLAUDE_CONFIG_DIR` per profile and puts it back when
  Claude exits — no environment variables to juggle by hand.
- **📊 Plan usage always in sight.** Every screen carries how much of each account's 5-hour
  and weekly allowance is gone **and how long until each window resets** — read from the
  utilisation Claude itself caches, so it is a real percentage, not a guess from token counts.
- **🧱 A wall of running sessions.** Everything you have open, tiled in one view, each tile
  running Claude's own interface under a pseudo console — `/usage`, the model picker and plan
  mode all render exactly. A border turns amber when that session is waiting on you.
- **🗂️ The projects you already have.** The project list comes from your existing PowerShell
  `$QuickPaths`. No new registry to maintain, no hardcoded project root.
- **⌨️ Keys you can change.** Every binding is rebindable from inside the app (`Alt+K`),
  chords included, and `F1` lists whatever the current screen answers to.
- **📦 Nothing to install but this.** One self-contained `.exe`: no .NET runtime, no NuGet
  packages, no Node — and the installer never touches your global ExecutionPolicy.

## 📦 Install

```powershell
irm https://raw.githubusercontent.com/chuongnd2612/claude-launcher/main/install-online.ps1 | iex
```

Then reload your profile and go:

```powershell
. $PROFILE
claude-launcher
```

That fetches the standalone exe and the PowerShell wrapper, verifies the published SHA256,
unblocks both files, and registers the wrapper in your `$PROFILE`. Prefer the zip, a pinned
version, or a build from source? See **[Install, update, uninstall](docs/install.md)**.

**You need:** Windows, PowerShell 5.1 or 7+, `claude` on your `PATH`, and a terminal with
truecolor — [Windows Terminal](https://aka.ms/terminal) recommended.

## ⚡ Getting started

```powershell
claude-launcher              # the full flow: profile -> project -> session
claude-work                  # jump in with the "work" profile preselected
claude-work qagent           # ...and a project: launches straight away
claude-work qagent --resume  # ...resuming that project's last conversation
claude-personal my-project   # any profile you have configured
```

The first run walks you through three steps:

| Step | What you do | Keys worth knowing |
| ---- | ----------- | ------------------ |
| **1 · Profile** | Pick the Claude account to run as | `a` add · `e` edit · `x` remove · `s` settings |
| **2 · Project** | Pick from your `$QuickPaths`, or the current directory | `/` filter · `a` add a folder |
| **3 · Session** | New, Continue, or Resume a specific conversation | `n` · `c` · `r` · `o` where it opens |

With a session already running, `claude-launcher` opens on **Home** instead of the wizard —
what is running, what you did today, and one key each to the wall (`t`), the dashboard (`d`)
and the usage detail (`Alt+U`).

## 🖼️ What it looks like

<!-- Uncomment once docs/assets/ has the captures - see docs/CAPTURING.md.
| Home — every session at once | The terminal wall (`t`) |
| --- | --- |
| <img src="docs/assets/home.png" alt="Home" width="440"> | <img src="docs/assets/wall.png" alt="Terminal wall" width="440"> |
| **Usage per account (`Alt+U`)** | **The dashboard (`d`)** |
| <img src="docs/assets/usage.png" alt="Usage" width="440"> | <img src="docs/assets/dashboard.png" alt="Dashboard" width="440"> |
-->

**Home — what is running, and what you did today:**

```text
  Home  /  4 sessions running · 11 started today

  ╭─ Running sessions · 4 ────────────────────────────────────────────────────────────────────╮
  │    project         task                              state                 context  model │
  │ ▸  qagent          Refactor runner into stages       running 12m 04s          184k  sonnet │
  │    api-gateway     Add rate limiting                 waiting? 46s              97k  sonnet │
  │    web-dash        Fix chart tooltips                idle 4m                   41k  haiku  │
  │    notes-cli       Write test suite                  running 2m 00s            63k  sonnet │
  ╰───────────────────────────────────────────────────────────────────────────────────────────╯

  ╭─ Recent projects · 2 ─────────────────────────────────────────────────────────────────────╮
  │  nauxoi                  D:\demo\nauxoi                                           2h ago │
  │  qagent                  D:\demo\q-agent                                             now │
  ╰───────────────────────────────────────────────────────────────────────────────────────────╯
```

**The terminal wall (`t`) — Claude's own interface, tiled:**

```text
  Home  /  Terminals · 4 panes                                     layout tiled · space to cycle

  ● 1 qagent  W   ○ 2 api-gateway  W   ○ 3 web-dash  W   ○ 4 notes-cli  P

  ╭─ 1 · qagent · Refacto… W ─── running 12m 04s ─╮  ╭─ 2 · api-gateway · Add r… W ── waiting? 46s ─╮
  │ feat/qagent-refactor                          │  │ main                                         │
  │ › split the runner into plan/execute/verify   │  │ › add a redis token bucket to the gateway    │
  │ stages                                        │  │ ◆ Read api/router.ts                         │
  │ I'll restructure runner.ts into three stages  │  │ ◆ Write api/limiter.ts                       │
  │ and keep the public run() signature intact.   │  │ Mount the limiter before the auth middleware?│
  │ ◆ Read agent/runner.ts                        │  │                                              │
  │ ◆ Bash pnpm typecheck                         │  │                                              │
  ╰───────────────────────────────────────────────╯  ╰──────────────────────────────────────────────╯
```

`1`–`9` focus a tile, `Enter` starts typing into it, `z` zooms it to the whole window, and
`Ctrl+]` gives the keyboard back to the launcher.

**The usage band — in the rule under the header, on every screen:**

```text
─── ↻ usage │ W Work 5h █░░░░░ 3% →2h11m 7d ██░░░░ 28% →4d │ P Personal 5h █████░ 91% →37m ───
```

Each account is its own group, coloured by its own reading: green while there is room, amber
past 60%, red past 85%. `5h` is the rolling session allowance, `7d` the weekly one, and
`→37m` is how long that window has left before it rolls over. `↻ usage` is a button — click
it, or press `Alt+R`. `Alt+U` opens the full breakdown per account.

## ⌨️ Keys worth knowing

| Where | Keys |
| ----- | ---- |
| Anywhere | `F1` every key on this screen · `Alt+U` usage detail · `Alt+R` refresh the usage band · `Alt+K` rebind keys · `q` quit |
| Home | `t` the wall · `d` dashboard · `n` new session · `a` attach · `r` reopen last terminals · `k` stop a session · `s` settings |
| The wall | `1`–`9` focus · `Tab`/arrows move · `Enter` attach · `z` zoom · `t` new terminal · `w` close · `Space` layout |
| A focused tile | every key goes to Claude · `Ctrl+]` release the keyboard · `Ctrl+T` new terminal · `Ctrl+F` find · `Alt+Z` zoom |
| Lists | `↑`/`↓` move · `/` filter · `Enter` choose · `Esc` back |

All of them are rebindable, and `Alt+K` is the editor — the full table is in
**[Keys](docs/keys.md)**.

## 📚 Documentation

- **[Install, update, uninstall](docs/install.md)** — every install route, updating in place,
  removing it cleanly.
- **[The guide](docs/guide.md)** — every screen in the order you meet them: Home, the
  dashboard, the usage band, chatting inside the launcher, resuming a conversation, the
  terminal wall, tabs and split panes.
- **[Keys](docs/keys.md)** — every binding, and the editor that changes them.
- **[Configuration](docs/configuration.md)** — the `profiles.json` schema, the settings
  screen, and every file kept under `$HOME\.claude-launcher`.
- **[Troubleshooting](docs/troubleshooting.md)** — mojibake, missing colours, checking layout
  without a terminal.
- **[Development](docs/development.md)** — how the two halves talk, and how a release is cut.
- **[Changelog](CHANGELOG.md)** — every release, newest first.

## 🏗️ How it works

Two halves that talk through JSON files under `$HOME\.claude-launcher`:

```text
  claude-launcher.ps1                      ClaudeLauncher.exe
  (PowerShell 5.1+)                        (.NET 8, zero dependencies)
        │                                          │
        │   state.json — profiles, projects  ────▶ │
        │ ◀──── result.json — what you picked      │
        │                                          │
        └──▶ claude, with CLAUDE_CONFIG_DIR set for the chosen profile
```

The wrapper owns the project list (`$QuickPaths`) and launching Claude; the TUI owns every
screen. That split is why a session can go to a new Windows Terminal tab, a split pane, or a
tile the launcher hosts itself, without any of them knowing about the others. Details in
**[Development](docs/development.md)**.

## 🤝 Contributing

Issues and pull requests are welcome.

```powershell
dotnet build src/ClaudeLauncher.csproj -c Release --nologo -warnaserror
dotnet run --project src/ClaudeLauncher.csproj -- --selftest 132 44
```

`--selftest <width> <height>` renders every screen as plain text — the only way to check
layout without an interactive terminal, and what CI asserts on at 80x24, 100x30, 132x44 and
200x60. Conventions and the release process are in **[Development](docs/development.md)**;
repo rules for AI assistants are in [CLAUDE.md](CLAUDE.md).

---

<div align="center">

Built by **Andrew Nguyen** · [@chuongnd2612](https://github.com/chuongnd2612)

</div>
