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

| Keys | Action |
|---|---|
| Ctrl+R / F5 | Reload from disk |
| Ctrl+= / Ctrl+- / Ctrl+0 | Zoom in / out / reset |
| F11 | Fullscreen |
| Esc | Close window |

## Limits (v1)

- Read-only. No editing.
- Markdown only. Will refuse other extensions with exit code 2.
- No math, no Mermaid diagrams.
- Windows 11 only. Requires the WebView2 Evergreen Runtime (preinstalled on Win11).
