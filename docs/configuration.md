# Configuration

Profiles, settings, and every file the launcher keeps.

[&larr; Back to the README](../README.md)

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

`description` is new in 1.5.0 and optional — it is the italic line on the profile card.

**Every profile gets an icon and a colour of its own.** The **Add profile** screen fills the icon in
as you type the label: its initial, or — when another profile already shows that letter — the first
free shape from `◆ ● ■ ▲ ★ ◇ ○ □ △ ☆ ✦ ✱`. `←` `→` on the icon field steps through the same set, and
the preview beside it shows the icon in the colour the wall will paint it. Saving without touching the
field keeps the suggestion, so a profile is never left with nothing to tell it apart.

The colour is derived from the profile key, so it stays with a profile rather than following its
position in the file — except when two keys would land on the same colour, in which case the second
takes the next free one. Eight colours are available; past that they repeat.

A profile written before this, or by hand with no `icon`, is given one when the file is read. Your
`profiles.json` is not rewritten for it.

`icon` should stay a single, single-width character (a letter or a symbol such as `◆`); wide emoji
break the grid alignment. Profiles created from the **Add profile** screen are appended to this same file, with the
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
| Check for updates | On: ask GitHub once every six hours whether a newer release exists, and say so on Home |
| Show costs | On: show what Claude has cost on the dashboard. Off hides every figure and keeps the token counts |
| Terminal splits | Not on the settings screen: where the wall's dividers sit, written as you drag them. Delete the line to go back to equal panes |
| Terminal order | Not on the settings screen: the order the wall's tiles sit in, written when you move one. Delete the line to go back to the order sessions were opened in |
| Terminal tiles | On (default): a session opened inside the launcher runs under a pseudo console and shows Claude's own interface, so `/usage`, the model picker and plan mode render exactly. Off: the launcher's own styled chat view, easier to watch several sessions at once, but rich screens arrive as plain text |

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
| `CLAUDE_LAUNCHER_NO_UPDATE_CHECK` | `1` skips the update check for that run |
| `CLAUDE_LAUNCHER_REPO` | `owner/repo` to check for releases instead of the default |

The `result.json` contract is unchanged from 1.4.x: `profile`, `label`, `icon`, `configDir`,
`project`, `path`, `mode`.
