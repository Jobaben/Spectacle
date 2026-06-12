# Spectacle

A Windows-only Markdown viewer. Renders `.md` / `.markdown` files with VS Code-preview fidelity.
Dark and light themes (toggle with `Ctrl+T`). WCAG-accessible. No editing. Open files and
jump back to recent ones without leaving the keyboard. Export any document to a self-contained
HTML file, and see live word count / reading time in the status bar.

## Install

1. `dotnet publish src/Spectacle -p:PublishProfile=win-x64`
2. Copy `publish/win-x64/Spectacle.exe` to `C:\Tools\Spectacle\`.
3. Run `C:\Tools\Spectacle\Spectacle.exe --register` to set as the default handler for `.md` / `.markdown` (per-user, no admin).
4. Optional PowerShell helper, in `$PROFILE`:
   ```powershell
   function spectacle { param([string]$Path) & 'C:\Tools\Spectacle\Spectacle.exe' $Path }
   ```

## Usage

```text
Spectacle.exe <file.md|file.markdown>          Open and render
Spectacle.exe <file> --stats                   Print word count, reading time and structure, then exit
Spectacle.exe <file> --export-html [out.html]  Export a self-contained HTML file, then exit
Spectacle.exe <file> --export-html --light     Export using the light theme (defaults to dark)
Spectacle.exe --register                       Register file association
Spectacle.exe --unregister                     Remove file association
Spectacle.exe --help                           Show help
Spectacle.exe --version                        Show version
```

`--export-html` writes a portable, single-file HTML document (theme and syntax-highlight
styling inlined, no external assets) next to the source — defaulting to `<file>.html` — or
to the optional output path. Add `--light` to export the light theme instead of the default
dark one. `--stats` and `--export-html` run headless and never open a window.

## Keyboard

Spectacle can be operated entirely without a mouse. Press `?` inside the preview to see the full cheatsheet.

### Window-level (anywhere)

| Keys | Action |
|---|---|
| Ctrl+R / F5 | Reload from disk |
| Ctrl+O | Open another Markdown file… (in a new window) |
| Ctrl+Shift+O | Reopen the most recent file |
| Ctrl+T | Toggle dark / light theme |
| Ctrl+= / Ctrl+- / Ctrl+0 | Zoom in / out / reset |
| F11 | Fullscreen |
| Esc | Close window (when no overlay / composer / re-anchor active) |
| Ctrl+Shift+C | Copy revision plan |
| Ctrl+Shift+E | Export revision plan… |
| Ctrl+Shift+H | Export rendered document to a standalone HTML file… |

### Navigation (inside the preview)

| Keys | Action |
|---|---|
| ↑ / ↓ | Previous / next focusable (block, comment, orphan) |
| Home / End | First / last focusable |
| gg | Jump to first |
| G | Jump to last |
| Ctrl+F | Find in document (Enter / Shift+Enter or F3 / Shift+F3 to cycle matches, Esc to close) |
| t | Toggle the document outline (↑ / ↓ to move, Enter to jump, Esc to close) |
| ? | Show keyboard help overlay |

### On a focused block

| Keys | Action |
|---|---|
| Enter or c | Add a new comment on this block |

### On a focused comment

| Keys | Action |
|---|---|
| e | Edit the comment |
| r | Resolve / reopen |
| d | Delete |

### On a focused orphan row

| Keys | Action |
|---|---|
| d | Delete the orphan |
| a | Begin re-anchor (then arrow-pick a target block and press Enter, or Esc to cancel) |

### In the composer

| Keys | Action |
|---|---|
| Esc | Cancel and close |
| Ctrl+Enter | Save |

## Limits (v1)

- Read-only. No editing.
- Markdown only. Will refuse other extensions with exit code 2.
- No math, no Mermaid diagrams.
- Windows 11 only. Requires the WebView2 Evergreen Runtime (preinstalled on Win11).
