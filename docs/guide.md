# The guide

Every screen, in the order you meet them.

[&larr; Back to the README](../README.md)

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

## Dashboard

Home carries the dashboard's own panels underneath its sections, in the space the session list and
recent projects leave — usage per profile, today's counts, the work Claude did, and the projects it
did it in. `d` opens the same panels as a screen of their own, where the period can be changed and a
project's sessions opened.

Below 34 rows there is no room for boxes, so Home falls back to one dim line under the breadcrumb:

```text
  Home  /  2 sessions running · 4 started today
  today · 4 sessions · 60 prompts · 46 files · 54 edits · 3 PRs        W Work $83.55   P Personal $37.96
```

The profiles move to a second line when the window is too narrow for both, and the whole band is
absent until the first answer arrives — zeroes that are about to change are worse than nothing. It is
cached and refreshed in the background at most once a minute, so drawing it costs nothing per frame.

`d` opens the full screen: what Claude has been doing, and what it has cost, per profile.

```text
  Home  /  Dashboard                                                       today · p to change

  ╭─ Today ───────────────────────╮  ╭─ Usage · recorded by Claude ────────╮
  │  Sessions                    17 │  │  profile            cost      output │
  │  Running now                  3 │  │  W Work · alex     $83.55        214k │
  │  Waiting on you              2? │  │  P Personal · sam  $37.96         32k │
  │  Prompts sent               164 │  │  total            $121.51        246k │
  │  Busiest hour             14:00 │  ╰─────────────────────────────────╯
  ╰───────────────────────────────╯  ╭─ Projects · today ──────────────╮
                                      │ ▸ ddks_surency  ▪▪▪▪  8 ses   96 pr │
  ╭─ Work ─────────────────────────╮  │   qagent        ▪▪    5 ses   41 pr │
  │  Files touched              143 │  ╰─────────────────────────────────╯
  │  Edits written               67 │
  │  Commands run               312 │
  │  Pull requests                4 │
  ╰───────────────────────────────╯
```

`p` cycles the period (today → last 7 days → all time, remembered), `r` reads again, `↑↓` picks a
project and `↵` opens its earlier sessions, `Esc` goes back.

**Two kinds of number, kept apart.** The usage panel is Claude's own record from each profile's
`.claude.json` — exact cost and tokens, and it carries **no dates**, so it is a total whichever period
is showing. That is why it says "recorded by Claude" and not "this month". Everything else is counted
from lines that have a timestamp on them, so those do follow the period.

| Row | Where it comes from |
| --- | ------------------- |
| Cost, output tokens | `projects[<cwd>].lastModelUsage[<model>]` per profile. Cache reads are counted but never folded into the token figure — they run to hundreds of millions against a million output tokens |
| Sessions, prompts, busiest hour | `history.jsonl`, which records a prompt with its project, session and time |
| Running now, waiting on you | the session registry, the same source the wall uses. `2?` keeps the question mark: Claude publishes busy/idle only, so a pane needing attention is a heuristic, not a count |
| Files touched, edits, commands | tool calls on assistant lines inside the period. Files are distinct paths, so ten edits to one file is one file |
| Pull requests | distinct pull requests **referenced** in the period, from Claude's own `pr-link` lines |
| Projects | prompts and sessions in the period, with that project's share of the recorded cost |

**Speed.** Nothing happens on the startup path; the screen builds on a background task and says
`reading…` until it is there. Measured on 254 transcripts totalling 395 MB: **today reads 34 MB in
137 ms**, last 7 days 89 MB in 193 ms. The whole-history case is capped at 512 MB, and says so on
screen rather than quietly under-counting.

Not shown, because nothing records it: whether a session finished, failed or was blocked, and how
long a task took. Test counts are absent for the same reason — guessing them from console output
would be a number that is wrong once and never trusted again.

## The usage band

Every screen carries **how much of each account's plan is gone** in the rule under the
header:

```text
✦ CLAUDE LAUNCHER
─── ↻ usage │ W Work 5h █░░░░░ 3% →2h11m 7d ██░░░░ 28% →4d │ P Personal 5h █░░░░░ ~4% →37m 7d █████░ 91% →19h ───
```

Every configured profile appears, in the order they are configured, so the positions stay
where you learned them, **each in its own group behind a `│`** and named in its own colour,
so one account can be found in the band without reading all of them.

**Both windows are shown for each**: `5h` is the rolling session allowance, `7d` the weekly
one. An account can be comfortable on one and nearly out on the other — Claude reports them
separately and so does this.

Each gauge is coloured by its own reading: green while there is room, amber past 60%, red
past 85%. A `~` marks a figure older than the window it describes, so treat it as the
last known answer rather than the current one.

**`→2h11m` is how long that window has left** before it rolls over and the percentage drops
back — `→37m`, `→19h`, `→4d`, or `→due` for a window whose reset time has passed. 91% of the
week gone is a reason to stop if the week turns over on Friday and hardly one if it turns
over tonight, which is why the number alone was only half an answer.

As the window narrows the band drops the gauges, then the countdowns, then the account
names and window markers, and finally falls back to whichever single number across every
account and window is closest to its limit — that being the one worth the last of the room.
The countdown outranks the gauge on purpose: the gauge only draws the percentage a second
time, while the countdown is the part that says something the number cannot.

**Where the percentage comes from.** Claude works out its own utilisation when it talks
to the API and caches it under `cachedUsageUtilization` in each config dir — which makes
it per account. It reports both windows, marks which one is currently counting, and gives
a reset time for each. That cache is the only place a real percentage exists: the cost and
token figures elsewhere in `.claude.json` are running totals with no ceiling recorded
beside them, so they can tell you what you have spent but never how close you are to the
limit.

It is drawn *into* the rule rather than on a row of its own, so it costs no space: at
80x24 a wizard screen has only twelve content rows, and the band is not worth one of them.
Whatever the width, the percentage is the last thing to go, because it is the only part
that answers the question. On the compact header the author byline gives way to it.

**It reads itself again every minute**, and the **`↻ usage` button reads it now** — click it,
or press `Alt+R`. The band shows `↻ usage…` while it does. That re-reads what Claude
cached; it cannot ask the API itself, so a figure only moves once Claude has talked to the API again, and the
`~` is how you tell a stale one. `Alt+R` is handled before any screen sees it, so it works
from a focused terminal tile too, and `r` on the `Alt+U` detail screen now refreshes the
band along with the screen. Both keys are rebindable under `everywhere` in the key editor.

**`Alt+U` opens the detail** — both windows per account side by side with how long until
each resets, when each account's figure was last refreshed, and then sessions, prompts,
cost and output tokens with a line saying which of those follow the period and which are
running totals. Alt rather than a plain letter because a focused terminal takes every
ordinary key, so `Alt+U` works from the wall mid-sentence too.

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

  ╭─ 1 · qagent  W Work · alex ─ running 12m ─╮  ╭─ 2 · api-gateway  P Personal ───────╮
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
the wall is as often picking up yesterday's work as starting something new.

**`p` changes the profile there too.** A terminal opened this way never passes through the profile
step, so that is the place to say which account it runs under; the summary shows the config directory
it resolves to, which is the part worth being sure about before it starts. It does not go through the
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

**Every tile says which session it is.** After the project name comes what Claude calls this session:
the name you gave it with `/rename`, else the title Claude derived for the conversation, else the
short id. The name follows the session everywhere — the wall, Home, and the resume picker — because
Claude writes it into the transcript as well as its registry, and the launcher reads both. A rename
made long ago is found by scanning back through the transcript for it, which is bounded at 8 MB so a
list of sessions never stalls. A rename shows on the tile within a tick — Claude records it immediately, and a name someone
typed wins over one that was guessed. The exception is the name Claude builds out of the folder
(`ddks-surency-fd` for `ddks_surency`), which says less than the title and is skipped. Several panes of one
project is the normal way to work, and without it they are the same header three times over. A long
name is cut; below about ninety columns the border has nothing left after the project and the state,
and the name gives way.

**Every tile says whose it is.** The header carries the profile the session runs under and the
Claude account signed in there — `W Work · alex` — in that profile's own colour, with the icon leading.
The pane strip above the tiles marks each pane the same way. With two profiles open the panes are
otherwise identical, and the whole point of a profile is that the session behind it is a different
account doing different work.

The name comes from Claude's own `oauthAccount` block in `<configDir>\.claude.json`: its display
name, or the part of the email address before the `@` when there is no name. Nothing is written, and
the file is only re-read every few minutes — it is rewritten on nearly every turn, but who is signed
in changes about once a month. A profile that has never been signed in simply shows no name.

On a narrow pane the header gives up the account first, then the profile label, keeping the icon
last: `W Work · alex` → `W Work` → `W`.

**Resizing the panes.** The wall no longer insists on equal shares. Drag a divider with the mouse —
each gutter carries a small grip, and the divider follows the pointer until you let go — or move it
from the keyboard with `Alt+Shift+←→` for columns and `Alt+Shift+↑↓` for rows. `Alt+Shift+0` makes
them even again. Plain `Alt+arrow` still steps between panes; the Shift is what separates the two.

**Each row resizes on its own.** The divider between panes 1 and 2 belongs to that row: dragging it
does not move the panes below. So a wall of four can be one wide terminal beside a narrow one on top,
and an even pair underneath. Row heights still apply across a row — panes side by side share a height,
which is what keeps the grid readable rather than becoming a pile of loose boxes.

A pane can never be squeezed out of existence: the divider stops while both sides still have room to
be read, and the far edge stays flush whatever the fractions come to. Positions are remembered in
`ui.json` per row and per number of columns — a two-pane wall and a three-pane wall are different
arrangements and keep their own splits, written as `0#2:0.68,0.32|1#2:0.39,0.61`. A layout saved
before rows had their own splits still loads. Row heights are not saved, because they follow a window
height that changes on its own.

**Reordering panes: drag a tile, or `Ctrl+Shift+←→↑↓`.** Press on a tile and drag it onto another to
move it there; the rest shift up or down to make room, so the pane numbers stay a sequence you can
read. The tile you are carrying is marked `moving` and the one you would drop it on `drop here`, and
letting go over the tile you started on cancels. `Ctrl+Shift` and an arrow does the same from the
keyboard and works even while a terminal has the keyboard, which is the only way to rearrange the
wall in a host with no mouse.

The order is remembered in `ui.json` as `terminalOrder`, a list of session ids. Ids that are not on
the wall are kept, so a session you close today comes back to the same slot when you resume it
tomorrow; the most recent two dozen survive.

**Finding text: `Ctrl+F`.** Opens a search bar under the wall and searches the focused terminal.
Typing narrows as you go and jumps to the first hit, `Enter` walks to the next, `Shift+Enter` back,
`Esc` closes and returns to the bottom. The current hit is amber, the others blue.

The bar searches the *screen* first, which is instant and highlights in place. One limit is
inherent: a match cannot span a line break — what reads as one sentence may be a wrapped line, and the
grid no longer records where the wrap fell. A shell or any program that stays on the primary screen
is also searched across its full 2000-line scrollback. `Alt+F` does the same thing, for when Claude
wants `Ctrl+F`.

**What scrolled off the screen: `Enter`.** When the query is not on screen the bar offers
`enter searches back`, and pressing it walks Claude's own history a screenful at a time until it
finds the text or reaches the top — the same thing you would do with the wheel, minus the reading.
The bar counts as it goes (`searching back · 12 screens`) and stops on the first match, leaving it
highlighted where it was found. `Enter` again keeps going further back; `Esc` scrolls back down and
closes.

This works because Claude scrolls its own view for a wheel report, which is all the launcher can send
it: there is no command for "show me line 400". So two things follow. Searching back is paced by how
fast Claude repaints — measured at roughly a screenful every 120 ms, so a long sweep takes seconds and
shows its progress. And coming back down is a scroll like any other: a deep sweep can leave the view
part-way and one flick of the wheel finishes the job.

**The whole session: `Tab` from the search bar.** Claude runs on the *alternate screen* and keeps its
own history to itself, so the grid only ever holds one screenful — but Claude writes every turn to a
transcript as it goes, and that is what `Tab` searches. It opens a **History** screen listing every
mention in the conversation, oldest first, with the time, who said it, and the line it appeared in;
the selected match is shown in full underneath.

| Key | On the History screen |
| --- | --- |
| `↑` `↓` `PgUp` `PgDn` `Home` `End` | move through the matches |
| `/` | search this session for something else — typing replaces the old query |
| `Esc` | back to the wall |

Searching an 18 MB transcript takes about 120 ms, and the list stops at the first 300 matches (the
header says so when it does). Only this session's own transcript is searched — not every session on
the machine.

**Zooming without letting go: `Alt+Z`.** Fills the wall with the focused terminal and keeps the
keyboard in it, so a pane can be read closely mid-sentence rather than after releasing the keyboard,
pressing `z`, and taking it back. `Alt+Z` again returns to the wall. Plain `z` still zooms from the
wall itself when the keyboard is not in a terminal.

**Selecting text: `Alt+S`.** Reading the mouse means turning the console's *quick edit* off, and
quick edit is exactly what drags a selection — the two cannot both be on. `Alt+S` borrows it back:
dragging selects and copies as it does anywhere else, and the mouse stops focusing tiles until you
press `Alt+S` again. The launcher restores whatever your console had when it exits.

**The mouse works too.** Click any tile to focus it and start typing into it. **Clicking off the
tiles hands the keyboard back**, the same as `Ctrl+]` — so the next key is a wall command rather than
another character in whichever terminal had focus. The wheel scrolls whichever tile is under the
pointer, focused or not, and never changes focus, so reading one pane cannot take the keyboard off
another. This needs
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

**`Ctrl+W` (or `Alt+W`) closes the focused terminal** and stops that Claude. It works while you are
typing, because a terminal tile owns every printable key. `w` does the same from the wall's own keys;
for a session running in someone else's terminal it only hides the pane, since that one is not ours
to end. Nothing is lost either way — the conversation is on disk and `r` or **Resume** brings it back.

**`Ctrl+C` goes to Claude**, which uses it to interrupt a turn. It never closes the launcher and never
stops other sessions.

Terminal tiles stop with the launcher, so an afternoon's worth of open sessions goes away on exit.
The conversations do not — only the list of which ones were open. That list is kept, so **`r` on Home
reopens them all at once**, each resumed rather than started fresh.

The offer only counts sessions that can actually come back: the project folder still has to exist and
the conversation has to be on disk. A terminal you opened but never typed into has nothing to resume
and is not offered. Home is also the landing screen whenever there is something to reopen, so the
first thing you see after starting the launcher is the way back to yesterday's work.

**The set accumulates rather than being replaced.** Opening one terminal today does not discard the
five you had open yesterday — they are all still offered. A terminal leaves the list when you
**close** it (`Ctrl+W`), not when a later run happens not to have it open, and entries whose project
or conversation has since gone are dropped. At most 20 are kept, newest first.

Quitting shows what it is doing: each session takes about a second to stop, so `q` draws a
**Closing n sessions** panel with a progress bar rather than sitting on a frozen screen. They stop in
parallel, so the wait is about the same whether one terminal is open or six.

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
