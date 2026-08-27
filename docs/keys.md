# Keys

Every binding, and how to change one.

[&larr; Back to the README](../README.md)

## Every binding

**`F1` on any screen lists every key that screen answers to**, grouped and scrollable. The footer
only has room for four or five, so it shows the ones worth a permanent line and `F1` has the rest —
including the chords that depend on which pane has the keyboard. `?` does the same on screens with
nothing to type into.

| Screen  | Keys |
| ------- | ---- |
| Everywhere, including a focused terminal tile | `F1` keys · `Alt+U` usage detail · `Alt+R` refresh the usage band, as does clicking its `↻ usage` button · `Alt+K` change these keys |
| Home | `↑↓` navigate · `Tab` next · `Home/End` first / last · `Enter` open on the wall · `a` attach to its Windows Terminal pane · `n` / `p` new session · `r` reopen last session's terminals · `t` tile · `k` stop · `d` dashboard · `Alt+U` usage · `s` settings · `u` check for updates · `Esc` / `q` quit the launcher |
| New terminal (`t` on the wall) | `↑↓` navigate · `Enter` pick the project, then choose new / continue / resume · `a` add a folder · `d` forget an added folder · `/` filter · `Esc` back |
| Adding a folder (`a`) | type a path · `↑↓` pick from the folders below · `Tab` complete into one · `Enter` use this path, then name it · `Esc` cancel |
| Dashboard (`d`) | `p` period · `r` read again · `↑↓` pick a project · `↵` its sessions · `Esc` back |
| Usage (`Alt+U`, from anywhere) | `p` period · `r` read again, band included · `↑↓` pick an account · `Esc` back · `q` quit |
| Terminals (the wall — no tile holding the keyboard) | `1..9` focus · `↑↓←→` / `Tab` move · `Enter` attach · `Ctrl+Shift+←→↑↓` move the focused pane along the wall · `Alt+Shift+←→↑↓` resize the panes · `Alt+Shift+0` even them up · `z` zoom · `Space` layout · `t` new terminal · `n` new session · `w` close a terminal, or hide a session that is not ours · `Esc` back · `q` quit — plus `v`/`s` to split into Windows Terminal panes, only with terminal tiles **off** |
| Terminals (terminal tile released) | `Enter` or `Ctrl+]` start typing into it again · every wall key above also works · `Ctrl+F` find · `Shift+PgUp/PgDn` scroll its history |
| History (`Tab` from the Terminals find bar) | `↑↓` `PgUp/PgDn` `Home/End` move · `/`, `f` or `Ctrl+F` search again · `Esc` / `q` back |
| Terminals (terminal tile focused) | every key goes to Claude — its own UI, prompts and pickers, `Esc` and `Tab` included · `Ctrl+T` new terminal · `Ctrl+W` close this terminal · `Alt+Z` zoom this pane · `Ctrl+F` find text, `Enter` searches back through the screen history, `Tab` searches the whole session · `Alt+S` select text · `Alt+1..9` jump to a pane · `Alt+←→↑↓` step between panes · `Ctrl+Shift+←→↑↓` move this pane · `Alt+Shift+←→↑↓` resize them · `Alt+Shift+0` even them up · `Shift+PgUp/PgDn` scroll Claude’s history · `F1` keys · `Ctrl+]` or a click off the tiles releases the keyboard · `Ctrl+]`, `Enter` or a click on a tile resumes typing. All six of those chords are rebindable |
| Terminals (chat tile focused) | type to message · `Enter` send · `/` commands · `↑↓←→` / `Tab` move between tiles · `y`/`a`/`n` answer a permission · `Ctrl+T` new terminal · `Ctrl+Z` zoom · `Ctrl+L` layout · `Ctrl+W` hide this tile · `Ctrl+Shift+←→↑↓` move it · `Esc` clear, stop, back |
| Terminals (find bar) | type the query · `Enter` next hit · `Shift+Enter` previous · `↑↓` step · `Tab` search the whole session · `Ctrl+F` / `Esc` close |
| Stop session | `←→` / `Tab` choose · `Enter` confirm · `y` stop · `n` / `Esc` cancel |
| Profile | `↑↓←→` / `Tab` navigate · `Home/End` first / last · `Enter` / `Space` select · `1..9` jump · `a` add · `e` edit · `x` / `Del` remove · `d` dashboard · `s` settings · `u` check for updates · `Esc` back · `q` quit |
| Project | `↑↓` navigate · `PgUp/PgDn` `Home/End` · `Enter` select · `a` add a folder · `d` forget one · `/` filter · `Esc` / `Backspace` back · `q` quit |
| Session | `↑↓` / `Tab` navigate · `Enter` / `Space` launch · `p` change profile · `o` / `←→` open in · `n` new · `c` continue · `r` resume · `h` chat view (only with terminal tiles off) · `Esc` / `Backspace` back · `q` quit |
| Chat | type · `Enter` send · `/` commands (`↑↓` pick, `Tab` complete) · `y`/`a`/`n` answer a permission request · `Ctrl+D` detach to a pane · `Esc` clear, then stop the turn, then Home · `↑↓` `PgUp/PgDn` scroll · `End` follow. Keystrokes are ignored while Claude is working |
| Resume | `↑↓` navigate · `Enter` resume (a terminal tile, or a real terminal when tiles are off) · `t` force a terminal tile · `c` resume in the chat view · `/` filter · `l` logs · `d` delete · `Esc` back |
| Session detail | `↑↓` scroll · `PgUp/PgDn` page · `Home/End` jump · `Esc` / `Backspace` back · `q` quit |
| Delete session | `←→` / `Tab` choose · `Enter` confirm · `y` delete · `n` / `Esc` cancel |
| Add / Edit profile | `Tab` / `↑↓` next field · `←→` cycle the icon, on the icon field · `Enter` save · `Esc` cancel |
| Remove profile | `←→` / `Tab` choose · `Enter` confirm · `y` remove · `n` / `Esc` cancel |
| Settings | `↑↓` / `Tab` navigate · `Enter` / `Space` / `←→` change · `u` check for updates now · `Esc`, `q` or `s` back — `q` does not quit here |
| Update available | `Enter` update now · `n` release notes · `s` stop asking · `Esc` / `Backspace` later · `q` quit |
| Dashboard (`d`) | `p` period · `r` read again · `↑↓` pick a project · `Enter` its sessions · `Esc` / `Backspace` back · `q` quit |

The window can be resized at any time; the UI redraws itself.

## Changing the keys

**`Alt+K` opens the key editor**, from the key list or from Settings. Pick a command, press
the key you want, and it is saved. `Del` unbinds a command, `r` puts it back to its
default, and a `•` marks anything you have changed.

Only what differs from the defaults is written, to
`$HOME\.claude-launcher\keys.json`:

```json
{
  "bindings": {
    "Dashboard": "alt+d",
    "Zoom": "none"
  }
}
```

Edit it by hand if you prefer — comments and trailing commas are allowed, `"none"`
unbinds a command, and an unreadable file is kept as `keys.json.bak` and ignored rather
than losing your layout silently. `CLAUDE_LAUNCHER_KEYS` points somewhere else for a run.

Chords are written `ctrl+`, `alt+` and `shift+` in any order before a key name: a letter
or digit, `f1`-`f12`, `enter`, `escape`, `space`, `tab`, `backspace`, `delete`, `insert`,
`home`, `end`, `pageup`, `pagedown`, `left`, `right`, `up`, `down`, or a punctuation key.
Shift is part of the chord for punctuation, where it changes what the key means, and
ignored on letters and digits.

**Commands are shared between screens**, so rebinding *Quit* changes it everywhere it
exists. Two commands on the same key *within one screen* is a clash, and the editor says
so at the bottom — nothing checked that before, which is how removing a profile came to
shadow the dashboard on the screen that advertised both.

**The chords a focused tile reserves can be rebound too**, under `tile` in the editor:
release the keyboard, find, close, select, zoom and new terminal. Every key *not* on that
list is forwarded straight to Claude, so binding one of these is really saying "withhold
this key from Claude" — pick a chord Claude itself wants and it will stop working inside
the pane, which is the trade and worth knowing before you make it.

**What still cannot be rebound.** `Enter`, `Esc`, `Tab`, the arrows, `Backspace` and
anything that types a character are how a screen works rather than what it does. Nor are
the ranged chords — `Alt+1..9`, `Alt+arrows`, `Alt+Shift+arrows`, `Ctrl+Shift+arrows` —
which are a family of keys rather than one.
