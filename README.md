# Spectacle

A Windows-only Markdown viewer. Renders `.md` / `.markdown` files with VS Code-preview fidelity.
Dark theme. WCAG-accessible. No editing.

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
Spectacle.exe <file.md|file.markdown>   Open and render
Spectacle.exe --register                Register file association
Spectacle.exe --unregister              Remove file association
Spectacle.exe --help                    Show help
Spectacle.exe --version                 Show version
```

## Keyboard

Spectacle can be operated entirely without a mouse. Press `?` inside the preview to see the full cheatsheet.

### Window-level (anywhere)

| Keys | Action |
|---|---|
| Ctrl+R / F5 | Reload from disk |
| Ctrl+= / Ctrl+- / Ctrl+0 | Zoom in / out / reset |
| F11 | Fullscreen |
| Esc | Close window (when no overlay / composer / re-anchor active) |
| Ctrl+Shift+C | Copy revision plan |
| Ctrl+Shift+E | Export revision plan… |

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
