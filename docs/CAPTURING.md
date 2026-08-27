# Capturing the screenshots

The README ships text previews so the front page is never broken, but real captures look
better. Five PNGs replace them. Drop each one into `docs/assets/` under the exact filename
below, then uncomment the two `<img>` blocks in `README.md` (marked with
`Screenshots go here` and `Uncomment once docs/assets/ has the captures`) and delete the
text preview underneath each.

[&larr; Back to the README](../README.md)

## Setting up the terminal

Consistency between the shots matters more than any one of them:

- **Windows Terminal**, one tab, no split panes, and no other tab visible.
- **Cascadia Mono** or **Cascadia Code**, 12–14pt. The UI is block-drawing characters
  throughout, so a font without them renders `?` boxes.
- **Colour scheme: One Half Dark** or any near-black scheme. The launcher paints its own
  background (`#0B0E14`), so the scheme mostly shows in the window chrome — but a light
  scheme makes the title bar clash with every screenshot.
- **Window size 132x44** for the wide shots, which is the size CI asserts on and the size
  every screen has room to draw at. `Alt+Enter` for fullscreen, then drag the edge until the
  status line in the launcher's footer stops changing.
- **Acrylic and background images off** — transparency picks up whatever is behind the
  window and makes the PNGs look grubby.
- Hide the tab bar for a cleaner frame if you like: Settings → Appearance → *Always show
  the tab bar* off, *Hide the title bar* on.

Use a profile with **two accounts configured and real usage on both**, so the band has
something to show. `Win+Shift+S` (Snipping Tool) → *Window* mode captures the terminal
without the desktop behind it.

## The five captures

| File | Screen | How to get there |
| ---- | ------ | ---------------- |
| `hero.png` | The profile picker, full banner | `claude-launcher` with nothing running. This is the front-page image, so it wants the widest, tallest window you have. |
| `home.png` | Home | `claude-launcher` with two or three sessions running. Worth starting a couple first so the list is not one row. |
| `wall.png` | The terminal wall | `t` on Home, with three or four tiles and one of them mid-answer. Tiles that are actually working look alive; idle ones look empty. |
| `usage.png` | Usage per account | `Alt+U` from anywhere. |
| `dashboard.png` | The dashboard | `d` on Home. |

Keep them **under ~400 KB each**; a 132x44 terminal at 12pt is around 1400x900, which PNG
compresses well. Nothing needs a retina capture.

## After adding them

```powershell
git add docs/assets
```

Then uncomment the `<img>` blocks in `README.md`, delete the text preview blocks they
replace, and check the result renders — GitHub's preview tab on the pull request is enough.
Relative paths (`docs/assets/hero.png`) work in the README on GitHub and in the repo itself;
absolute `raw.githubusercontent.com` URLs are only needed if the image has to appear
somewhere outside the repo.

## Re-capturing after a UI change

Any change to a screen dates its screenshot. The cheap check is the text render, which is
always current:

```powershell
dotnet run --project src/ClaudeLauncher.csproj -- --selftest 132 44
```

If that output no longer matches what a screenshot shows, the screenshot is stale.
