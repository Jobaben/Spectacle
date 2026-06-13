# Spectacle

A Windows-only Markdown viewer. Renders `.md` / `.markdown` files with VS Code-preview fidelity.
Dark theme. WCAG-accessible. No editing. Export any document to a self-contained HTML file,
and see live word count / reading time in the status bar.

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
Spectacle.exe <file> --revision-plan [out] [--json] [--unresolved]  Export the review's revision plan, then exit
Spectacle.exe <file> --review-summary [--json]  Print review status (open/resolved/orphaned), then exit
Spectacle.exe <file> --lint [--json]           Report spec readiness issues, then exit (non-zero if any)
Spectacle.exe <file> --outline [--json]        Print the heading outline, then exit
Spectacle.exe <file> --checklist [--json]      Report task-list/acceptance-criteria completion, then exit
Spectacle.exe <file> --check-links [--json]    Report broken internal links, then exit (non-zero if any)
Spectacle.exe <file> --diff <other> [--json]   Show block-level changes vs another spec, then exit
Spectacle.exe <file> --check-structure [--json]  Report heading-hierarchy issues, then exit (non-zero if any)
Spectacle.exe --register                       Register file association
Spectacle.exe --unregister                     Remove file association
Spectacle.exe --help                           Show help
Spectacle.exe --version                        Show version
```

`--export-html` writes a portable, single-file HTML document (theme and syntax-highlight
styling inlined, no external assets) next to the source — defaulting to `<file>.html` — or
to the optional output path. `--stats` and `--export-html` run headless and never open a window.

`--revision-plan` writes the review's revision plan — the same actionable plan you build
interactively with comments and copy/export via `Ctrl+Shift+C` / `Ctrl+Shift+E` — headlessly,
so you can pipe a review back to the AI agent that authored the spec. It re-anchors your saved
comments against the current source (dropping orphans whose blocks no longer exist) and defaults
to `<file>.revisions.md`, or the optional output path. Add `--json` for a structured
`<file>.revisions.json` an agent can apply programmatically. Add `--unresolved` to emit only
open comments, so you hand the agent just the outstanding work. Runs headless, never opens a window.

`--review-summary` prints where a review stands — total comments, how many are open vs resolved,
and how many still anchor to a current block (`Anchored`) vs point at content the agent has since
changed or removed (`Orphaned`). Add `--json` for a machine-readable summary. Like `--stats`, it
writes to stdout and never opens a window.

`--lint` reports common readiness gaps in an AI-authored spec: leftover placeholder markers
(`TODO`, `TBD`, `FIXME`, `<placeholder>`, `lorem ipsum`, … — ignoring fenced code) and empty
sections (a heading with no content of its own and no subsection beneath it). It prints each
finding with a line number and exits non-zero when any are found, so it can gate a pipeline; add
`--json` for structured findings.

`--outline` prints the document's heading tree (indented by level, with line numbers) so you can
grasp a spec's structure at a glance or feed it to tooling. Add `--json` for a structured outline.

`--checklist` tracks acceptance criteria: it finds GFM task-list items (`- [ ]` / `- [x]`, ignoring
fenced code), reports how many are complete, and lists the open ones with line numbers. Add `--json`
for structured items.

`--check-links` validates the spec's internal links — anchor links (`#section`) must resolve to a
heading slug or an explicit element id, and link targets must be non-empty (external and relative
links are left alone). It prints each broken link with a line number and exits non-zero when any are
found, so it can gate a pipeline; add `--json` for structured findings.

`--diff <other>` shows what changed between two versions of a spec — invaluable when an AI agent
revises its own output. It compares blocks structurally (a block is unchanged only if its text is
identical), reporting added (`+`) and removed (`-`) blocks with line numbers; an edit shows as one
removed plus one added. The named `<other>` is the baseline and the opened `<file>` is the revision.
Add `--json` for structured added/removed arrays.

`--check-structure` validates the heading hierarchy (distinct from `--lint`'s content checks): more
than one top-level `#` heading, skipped levels (e.g. `##` jumping straight to `####`), and duplicate
heading text (which also produces ambiguous anchors). It exits non-zero when any are found; add
`--json` for structured findings.

`--outline`, `--checklist`, `--check-links`, `--diff`, and `--check-structure` all run headless and
write to stdout.

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
