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
| Home | `↑↓` navigate · `Enter` open on the wall · `a` attach to its Windows Terminal pane · `n` new session · `r` reopen last session's terminals · `t` tile · `k` stop · `p` profiles · `q` quit |
| New terminal (`t` on the wall) | `↑↓` navigate · `Enter` pick the project, then choose new / continue / resume · `a` add a folder · `d` forget an added folder · `/` filter · `Esc` back |
| Adding a folder (`a`) | type a path · `↑↓` pick from the folders below · `Tab` complete into one · `Enter` use this path, then name it · `Esc` cancel |
| Terminals (read-only tile focused) | `1..9` focus · `↑↓←→` move · `Enter` attach · `z` zoom · `Space` layout · `t` new terminal · `w` remove tile · `Esc` back — plus `v`/`s` to split into Windows Terminal panes, only with terminal tiles **off** |
| Terminals (terminal tile focused) | every key goes to Claude — its own UI, prompts and pickers · `Ctrl+T` / `Alt+T` new terminal · `Alt+1..9` jump to a pane · `Alt+←→↑↓` step between panes · `Shift+PgUp/PgDn` scroll Claude’s history · `Ctrl+]` release the keyboard · `Ctrl+]` or `Enter` resume typing |
| Terminals (chat tile focused) | type to message · `Enter` send · `/` commands · `↑↓←→` / `Tab` move between tiles · `y`/`a`/`n` answer a permission · `Ctrl+T` new terminal · `Ctrl+Z` zoom · `Ctrl+L` layout · `Ctrl+W` remove tile · `Esc` clear, stop, back |
| Stop session | `←→` / `Tab` choose · `Enter` confirm · `y` stop · `n` / `Esc` cancel |
| Profile | `↑↓←→` navigate · `Enter` select · `1..9` jump · `a` add · `e` edit · `d` / `Del` remove · `s` settings · `q` quit |
| Project | `↑↓` navigate · `PgUp/PgDn` `Home/End` · `Enter` select · `a` add a folder · `d` forget one · `/` filter · `Esc` back · `q` quit |
| Session | `↑↓` navigate · `Enter` launch · `o` / `←→` open in · `n` new · `c` continue · `r` resume · `h` chat view (only with terminal tiles off) · `Esc` back |
| Chat | type · `Enter` send · `/` commands (`↑↓` pick, `Tab` complete) · `y`/`a`/`n` answer a permission request · `Ctrl+D` detach to a pane · `Esc` stop a turn, then back · `PgUp/PgDn` scroll · `End` follow |
| Resume | `↑↓` navigate · `Enter` resume (a terminal tile, or a real terminal when tiles are off) · `t` force a terminal tile · `c` resume in the chat view · `/` filter · `l` logs · `d` delete · `Esc` back |
| Session detail | `↑↓` scroll · `PgUp/PgDn` page · `Home/End` jump · `Esc` back |
| Delete session | `←→` / `Tab` choose · `Enter` confirm · `y` delete · `n` / `Esc` cancel |
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
| Default open in | Where `Enter` launches Claude (`current` / `new tab` / `split right` / `split down`) |
| Remote control | Start new sessions with `claude --remote-control`, so they accept input from claude.ai and the phone app |
| Terminal tiles | On (default): a session opened inside the launcher runs under a pseudo console and shows Claude's own interface, so `/usage`, the model picker and plan mode render exactly. Off: the launcher's own styled chat view, easier to watch several sessions at once, but rich screens arrive as plain text |

## Home: what is running

With at least one Claude session alive, `claude-launcher` opens on **Home** instead of the profile
picker. With nothing running you go straight into the wizard as before, so the quick path is
unchanged.

```text
Home  /  4 sessions running

  project          task                          state             context   model
▸ qagent           Refactor runner into stages    running 12m 04s      184k   sonnet-4.5
  api-gateway      Add rate limiting              waiting? 46s          97k   sonnet-4.5
  web-dash         Fix chart tooltips             idle 4m               41k   haiku-4.5
```

The list is read from what Claude Code already records — the launcher does not track sessions itself,
so sessions started anywhere (another terminal, another tool) show up too. It refreshes once a second.

| Column | Where it comes from |
| ------ | ------------------- |
| task | Claude's own generated session title, else its session name |
| state | Claude's `busy` / `idle` status, aged from when it last changed |
| context | tokens carried by the most recent assistant message |
| model | the model that message used |

Two deliberate limits, so the screen never implies more than it knows:

- **`waiting?` has a question mark** because it is inferred. Claude publishes only *busy* and *idle*;
  a session sitting at a permission prompt looks exactly like an idle one, so "recently idle" is the
  best available signal.
- **`context` is not a session total.** It is one complete number from the last message rather than a
  partial sum over the transcript. Lifetime totals need the whole file, which is not worth doing on
  every refresh.

`Enter` brings Windows Terminal to the front. It does not jump to a specific pane: Windows Terminal
assigns pane ids that cannot be read back from its CLI, so targeting one would sometimes switch you
to an unrelated pane.

`k` stops a session, always behind a confirmation — it kills the process tree, so anything Claude has
not written out is lost. Background agents (`entrypoint: sdk-cli`) are left off the list; they are not
terminals you can return to.

## Chatting inside the launcher

Step 3 has a fourth option, **Chat here** (`h`). It starts Claude as a session the launcher owns and
gives you a prompt without opening a window:

```text
╭──────────────────────────────────────────────────────────────────────────╮
│  › add a redis token bucket to the gateway                               │
│  I'll add a limiter module and mount it before the auth middleware.      │
│  ◆ Read api/router.ts                                                    │
│  ◆ Write api/limiter.ts                                                  │
╰──────────────────────────────────────────────────────────────────────────╯
╭─ Permission ─────────────────────────────────────────────────────────────╮
│  ◆ Edit  api/router.ts                                                   │
│  Claude wants to run this tool.                                          │
│  y allow    a always allow    n deny                                     │
╰──────────────────────────────────────────────────────────────────────────╯
 › _
```

Type and press `Enter`. Replies stream in as they are written. When Claude wants a tool that needs
approval, the amber box appears: `y` allows it once, `a` allows that tool for the rest of the
session, `n` refuses it and tells Claude why. `Esc` stops a turn that is running, and `Esc` again
leaves the screen.

Under the hood the launcher talks to Claude over its `stream-json` interface rather than pretending
to be a terminal, which is what makes approve and deny possible at all. It is the same Claude — same
tools, hooks, settings, MCP servers, and the same transcript on disk, so these sessions appear on
Home and in the terminal wall like any other.

**Several chats at once.** `Esc` from a chat returns to **Home**, not the wizard step behind it — the
session keeps running, and Home is where it can be found again. It is listed there marked `· chat`
and appears as a tile on the wall; `Enter` reopens the conversation where you left off. So the loop
is:

```text
Home ─ n ─→ profile ─→ project ─→ Chat here ─→ type ─ Esc ─→ Home
  ↑                                                            │
  └──────────── Enter reopens either chat ←───────────────── n again
```

A chat shows up on Home the moment it starts, before Claude has replied at all.

**Typing on the wall.** Every chat tile has its **own prompt at the bottom of the tile**. Just type —
whatever you type goes to the focused tile, no mode to enter first. Replies, tool calls, the slash
command menu and permission prompts all render inside that tile.

Typing `/` opens a command dropdown inside the tile — one command per row with its description and
argument hint, `↑↓` to pick, `Tab` or `Enter` to complete. The list comes from the session itself, so
your project and plugin commands are in it.

Because the letters belong to your message, the wall's commands move to keys a message can't contain
while a chat tile is focused: **arrows** or **Tab** move between tiles, `Ctrl+Z` zooms, `Ctrl+L`
cycles the layout, `Esc` clears the draft, then interrupts, then leaves. Focus a *terminal* tile and
the single-letter keys come back, since there is nothing to type into. The wall opens focused on a
chat tile when there is one.

**Slash commands work here.** Type `/` and the commands this session offers are listed — all of them,
read from the session itself rather than hardcoded, so your own project and plugin commands appear
too. `↑↓` picks, `Tab` or `Enter` completes, then `Enter` sends.

**A running tool says so.** While a tool is working you get a live line — `◆ Echo start, sleep 6s,
echo done · running 4s` — so a slow command never looks like a hang.

**`Ctrl+D` hands the conversation to a real terminal.** It opens a Windows Terminal pane running
`claude --resume` on that same conversation and closes the chat. Everything said so far is carried
over, and the pane is independent of the launcher.

Two limits remain:

- **It is a conversation view, not Claude's own terminal UI.** Slash commands run, but their
  interactive interfaces (plan mode, pickers) do not render here.
- **Tool *output* arrives when the tool finishes.** You see what is running and for how long, but not
  its stdout scrolling live — Claude reports the result in one piece. For watching a long build, use
  `Ctrl+D` or launch into a pane and watch the real terminal.

The session belongs to the launcher, so closing the launcher ends the *process* — but the
conversation is on disk either way, so `Resume` (or `Ctrl+D` beforehand) always picks it back up.

## Resuming a specific conversation

Choosing **Resume** on step 3 now lists that project's earlier sessions instead of handing Claude a
bare `--resume` and letting it ask:

```text
╭─ Sessions · 6 ──────────────────────────────────────────────────────────────╮
│ ⌕ press / to filter by prompt or id                                         │
│ ▸ 8f31c2ab  Refactor QAgent runner into stages          2h ago       184k   │
│   a7d90144  Add golden tests for QAgent                 1d ago        76k   │
╰─────────────────────────────────────────────────────────────────────────────╯
╭─ Opening prompt ────────────────────────────────────────────────────────────╮
│ split the runner into plan/execute/verify stages and keep the CLI stable     │
╰─────────────────────────────────────────────────────────────────────────────╯
```

Titles are Claude's own generated session titles; the panel underneath shows the prompt that started
the highlighted one, which is usually what tells similar sessions apart. `Enter` resumes it directly
(`claude --resume <id>`), honouring the **Opens in** target, so you can resume an old conversation
straight into a split pane. With no transcripts for the project, `r` falls through to plain
`--resume` as before.

Rows load lazily — only the ones on screen are read — so a project with dozens of sessions still
opens instantly.

`c` picks the conversation up in the **chat screen** instead of a terminal. Claude reloads the whole
history either way; what differs is where you type. The chat screen shows only the messages from this
sitting, with a note saying so — it is not a transcript viewer, and `l` remains the way to read back
what was said before.

`l` opens **session detail**: turns, tool calls, files touched, and a scrollable transcript. That one
is a full pass over the file, which is why it is behind an explicit keypress; a 35 MB transcript
takes about a quarter of a second. `d` deletes a transcript, behind a confirmation — the conversation
cannot be resumed afterwards.

**No cost column.** Pricing a session needs a per-model rate table that would go stale silently, and a
wrong dollar figure is worse than none. Tokens are shown instead, and those are measured.

## The terminal wall

`t` on Home tiles every running session into one view, each tile tailing that session's transcript:

```text
  ╭───╮          ╭───╮          ╭───╮          ╭───╮
  │ 1 │ ──────── │ 2 │ ──────── │ 3 │ ──────── │ 4 │
  ╰───╯          ╰───╯          ╰───╯          ╰───╯
  qagent         api-gateway    web-dash       notes-cli

  ╭─ 1 · qagent ────────── running 12m 04s ─╮  ╭─ 2 · api-gateway ──── waiting? 46s ─╮
  │ feat/qagent-refactor                    │  │ main                                │
  │ › split the runner into stages          │  │ › add a redis token bucket          │
  │ ◆ Read agent/runner.ts                  │  │ ◆ Write api/limiter.ts              │
  │ ◆ Bash pnpm typecheck                   │  │ Mount the limiter before auth?      │
  ╰─────────────────────────────────────────╯  ╰─────────────────────────────────────╯
```

A tile's border turns **amber** when that session may be waiting for you, and blue when it is the
focused one. **Tile order is fixed** — a pane keeps its number for as long as it exists, so nothing
shuffles under your hands while you type. New sessions join at the end.

`Space` cycles three layouts:

| Layout | Shape |
| ------ | ----- |
| `tiled` | a grid, squarest that fits — 2x2 for four sessions |
| `stacked` | one column, full width — best for reading prose |
| `focus` | the focused session large, the rest as a list — how more than four fit |

`z` zooms the focused tile to the whole grid. `v` and `s` start another Claude in that tile's project,
in a pane beside it. `w` removes a tile from the wall (it does not stop the session — Windows
Terminal has no CLI to close someone else's pane).

**The tiles are read-only.** They are built from the transcript on disk, not from the terminal, so
you cannot type into them — press `Enter` to jump to the real one. This is also why the design's
`b` broadcast key is absent: there is no way to send input to a pane Windows Terminal owns, and a key
that could paste a prompt into the *wrong* session is worse than no key.

### Another terminal, without leaving the wall

`t` on the wall opens a **project picker** — or **`Ctrl+T` / `Alt+T`** when a tile has the keyboard,
since a focused chat or terminal tile takes plain letters for itself.

Choosing a project then asks **how it starts**: a new conversation, continue the most recent one, or
resume a specific one from the picker. It does not assume a fresh session — a terminal opened from
the wall is as often picking up yesterday's work as starting something new. It does not go through the
wizard, and it does not use the focused tile's project — pick any project, including one the wall
has never shown.

The list is the shell's quick paths plus anything you add. **`a` adds a folder** in two steps: type
or paste a path (`~` and `%VARS%` are expanded, and it is checked for existence, so a typo fails here
rather than as a pty error later), then give it the short name you want.

**You do not have to type the whole path.** As you type, the folders under it are listed below the
field: `↑↓` picks one, `Tab` completes into it and offers what is inside, so a path is walked into
rather than spelled out. `~` and `%VARS%` expand as you go.

**That name is a real quick path.** The launcher writes the same
`Documents\WindowsPowerShell\data\quickpaths.json` that `quick-set` writes, so the folder also works
with `cd <name>` and shows up in `quick-list` — it is not a private list of the launcher's. Existing
entries and the file's encoding are preserved. The new path is usable in the launcher immediately;
your **shell** picks it up when it next loads its profile.

**`d` forgets** one — `quick-remove` by another route. If quick paths cannot be written at all, the
launcher falls back to its own `$HOME\.claude-launcher\projects.json` and says so.

**The same `a` and `d` work on step 2 of the wizard**, so projects can be managed wherever they are
listed rather than only on the way to a terminal. `"Current directory"` is this shell's own folder
rather than a saved project, so it cannot be forgotten.

### Terminal tiles — Claude's own interface, inside the wall

**Which engine runs an in-launcher session is a setting.** `Terminal tiles` in settings (`s`) is
**on by default**. With it on, step 3 drops to three options — **New session**, **Continue**,
**Resume** — and each one opens the session on the **terminal wall**, with the new tile focused and
ready to type. Everything else you have running stays in sight; `z` zooms the focused tile to the
whole window when you want one session filling the screen. There is no separate `Chat here` row,
because every option already opens here.

Turn the setting off to get the old behaviour: `New`/`Continue`/`Resume` hand the session to the
wrapper, and `Chat here` opens the launcher's styled chat view — blue prompts, muted tool lines, the
amber permission box — at the cost of rich screens arriving as plain text.

**Choosing a tab or a pane still wins.** The in-launcher path applies only when `Opens in` is this
console; picking `tab`, `right` or `down` is an explicit ask for a real window and behaves exactly
as before, setting or no setting.

Both kinds of tile can coexist on the wall regardless of the setting: `c` on the resume picker opens
a conversation in the chat view, and `t` there forces a terminal tile.

Press `t` on the wall to open a **terminal tile** — or **`Ctrl+T`** when a chat tile is focused,
because a focused chat tile takes every printable key for its own prompt and would otherwise type a
literal `t`. The session runs under a real Windows pseudo console, and the launcher draws exactly
what Claude draws. This is the tile to use when Claude's own
interface is the point — the `/usage` dashboard, the `/model` picker, plan mode — because there is
no interpretation left to get wrong. Anything Anthropic adds later works the same day.

While a terminal tile is focused it takes **every** key, including `Esc`, `Tab` and the arrows,
because Claude's own UI needs them. `Ctrl+]` hands the keyboard back to the wall; `Ctrl+]` again (or
`Enter`) starts typing into it once more. The tile header shows which mode you are in.

**Switching panes mid-sentence.** `Alt+1..9` jumps straight to a pane and `Alt+←→↑↓` steps between
them, without releasing the keyboard first — you land typing in the new pane. Alt is reserved for
this because Claude's own interface uses `Esc`, `Tab`, the arrows and `Ctrl`, which all stay its own.

**The mouse works too.** Click any tile to focus it, and the wheel scrolls whichever tile is under
the pointer — focused or not, so reading one pane never takes the keyboard off another. This needs
the console's *Quick Edit* mode off, which the launcher turns off while it runs and restores on exit.

**Scrolling back.** Claude draws itself on the *alternate screen* — the same mode `vim` or `less`
uses — for its whole run, not only for `/usage`. Programs there repaint rather than scroll, so there
is no terminal scrollback to read: **Claude keeps its own history and scrolls it itself**.

So the wheel over a tile is forwarded to Claude as a mouse report, exactly as a real terminal would,
and Claude scrolls its own conversation. `Shift+PgUp` / `Shift+PgDn` go to it for the same reason.
The pane under the pointer scrolls whether or not it has the keyboard.

The launcher's own 2000-line scrollback still exists and still works, but only for a program that
uses the **primary** screen and genuinely scrolls it. Claude is not one, so its indicator (`↑ n`)
will not appear for a Claude tile.

The trade, stated plainly:

| | Chat tile (default) | Terminal tile |
| --- | --- | --- |
| Rich screens (`/usage`, `/model`) | plain text only | exact |
| Styled in the launcher's palette | yes — `›` prompts, `◆` tools, amber thinking | no — Claude's own 24-bit colours |
| Permission prompts | the launcher's amber box, `y`/`a`/`n` | Claude's own prompt |
| Watching several at once | better — one visual language | busier |

Claude emits **24-bit colour**, so a terminal tile shows its palette as sent; a colour *scheme* is
not possible because there are no palette indices to remap. Unfocused terminal tiles are faded
toward the panel background so the focused one still reads at a glance. If you want Claude's output
in the launcher's own colours, the chat tile is the one that does that — which is why both kinds
exist rather than one replacing the other.

### Picking up where you left off

Terminal tiles stop with the launcher, so an afternoon's worth of open sessions goes away on exit.
The conversations do not — only the list of which ones were open. That list is kept, so **`r` on Home
reopens them all at once**, each resumed rather than started fresh.

The offer only counts sessions that can actually come back: the project folder still has to exist and
the conversation has to be on disk. A terminal you opened but never typed into has nothing to resume
and is not offered. Home is also the landing screen whenever there is something to reopen, so the
first thing you see after starting the launcher is the way back to yesterday's work.

Terminal tiles are children of the launcher and **stop when it exits** — including if it is killed
outright rather than closed cleanly, because every session it starts is placed in a Windows job
object that takes them (and their own subprocesses) down with it — there is no handoff to a
Windows Terminal pane from inside a terminal tile, because the tile owns every key including `Enter`.
The conversation is not lost: each terminal tile is started with its own `--session-id`, so it is
recorded like any other session and can be picked up afterwards from the resume picker (`r`) or with
`claude --resume <id>` in a real terminal. Use a chat tile and `Ctrl+D` if you want to hand a running
session to a pane without stopping it.

**Typing from your phone instead.** Turn on **Remote control** in settings and every session the
launcher starts runs `claude --remote-control <project>`. That session then accepts input from
claude.ai and the Claude phone app, named after its project — so the wall can sit on one monitor as a
live overview while you answer a waiting session from wherever you are. This is Claude's own feature;
the launcher only turns it on per session. It relays through Anthropic's servers, which is why it is
off by default, and it applies to sessions the launcher starts — never to ones already running.

## Tabs and split panes

Step 3 has an **Opens in** row. Press `o` (or `←→`) to cycle where Claude starts:

| Value | Effect |
| ----- | ------ |
| `current` | This console, exactly as before — Claude replaces the launcher in the same window |
| `tab` | A new Windows Terminal tab |
| `right` | Splits the current pane, new pane to the right |
| `down` | Splits the current pane, new pane below |

Anything other than `current` needs [Windows Terminal](https://aka.ms/terminal). If `wt.exe` is
missing, the launcher says so and falls back to this console rather than failing the launch.

Each pane gets its own `CLAUDE_CONFIG_DIR`, so you can run a **Work** session beside a **Personal**
one — that is the point of the feature. The launcher does this by writing a small startup script per
pane under `$HOME\.claude-launcher\panes`; they are disposable and pruned after two days.

Two things worth knowing:

- The pane keeps its shell open after Claude exits (`-NoExit`), so an error stays readable instead of
  the window vanishing. Close it with `exit`.
- Splitting targets the window you are already in. Run the launcher from outside Windows Terminal
  (the VS Code terminal, plain conhost) and there is no window to split, so it opens a new one and
  tells you.

Set `CLAUDE_LAUNCHER_OPEN_IN` to script it. Note that a fully specified, non-interactive launch
defaults to `current` regardless of your saved preference, so automation never starts opening windows
by surprise.

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
  Screens/            Home, Terminals, Chat, Resume, SessionDetail, Profile,
                      Project, Session, AddProfile, DeleteProfile, KillSession,
                      DeleteSession, Settings
  Sessions/           reads Claude's session registry and transcripts; owns
                      stream-json sessions for the chat screen
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
| `CLAUDE_LAUNCHER_OPEN_IN` | `current` \| `tab` \| `right` \| `down` |

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
