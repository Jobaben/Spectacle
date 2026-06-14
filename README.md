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
Spectacle.exe <file> --check-tables [--json]   Report malformed tables, then exit (non-zero if any)
Spectacle.exe <file> --check-fences [--json]   Report fenced-code-block issues (unclosed, untagged), then exit
Spectacle.exe <file> --check-paths [--json]    Report relative link/image targets missing on disk, then exit (non-zero if any)
Spectacle.exe <file> --check-sections "A,B,C" [--json]  Report required sections (by heading) missing from the spec, then exit (non-zero if any)
Spectacle.exe <file> --check-duplication [--json]  Report blocks repeated verbatim elsewhere in the spec, then exit (non-zero if any)
Spectacle.exe <file> --check-alt-text [--json]  Report images missing alt text, then exit (non-zero if any)
Spectacle.exe <file> --review [--json|--sarif] Run all checks at once, then exit (non-zero if any issues)
Spectacle.exe <dir> --review [--json|--sarif]  Review every spec under a folder at once, then exit (non-zero if any issues)
Spectacle.exe <file> --review --baseline <old> [--json]  Show what a revision fixed/introduced vs an older version, then exit
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

`--check-tables` validates GFM pipe tables: every separator and body row must have the same number
of cells as the header. It flags mismatches with line numbers and exits non-zero when any are found;
add `--json` for structured issues.

`--check-fences` validates fenced code blocks — the kind AI agents routinely emit malformed. It
reports two rules: `unclosed-fence` (a fence opened but never closed, which swallows the rest of the
document into one code block — a real rendering defect) and `no-language` (a closed fence with no
language/info string, which renders without syntax highlighting — advisory). Closing is judged the
CommonMark way: a closing fence repeats the opener's delimiter character (`` ` `` or `~`) at least as
many times with no info string, and a run of the *other* delimiter inside a block is content, not a
toggle. It exits non-zero only when a fence is genuinely unclosed (so it can gate a pipeline without
failing on a stylistic missing tag); add `--json` for structured issues.

`--check-paths` validates the spec's *relative* link and image targets against the filesystem — the
gap `--check-links` deliberately leaves alone. AI agents frequently reference files and images that
were never created (hallucinated paths); this catches them by resolving each relative target against
the spec's own directory and reporting the ones that don't exist on disk. It strips any `#fragment`
or `?query` and percent-decodes before resolving. External targets (any URI scheme, protocol-relative
`//host`), in-document anchors (`#section`), and site-absolute paths (`/foo`) are left alone. It exits
non-zero when any relative target is missing; add `--json` for structured findings.

`--check-sections "A,B,C"` enforces a spec **template** — the one gap the other checks
leave open. Every other check validates what is *present*; none notices what an AI agent
*omitted*. Pass the sections your specs must contain as a comma-separated list (in the
second positional, like `--diff`'s file) and Spectacle reports each one with no matching
heading. Matching is by exact heading text, case-insensitive and trimmed, at any level — a
required `Acceptance Criteria` is satisfied by `## Acceptance Criteria` or `#### Acceptance
Criteria` alike, but a required `Goals` is *not* satisfied by a `Non-Goals` heading (it is a
full-text match, not a substring). Missing sections are reported in the order requested; it
exits non-zero when any are absent, so it can gate a pipeline. Add `--json` for structured
findings.

`--check-duplication` flags content an AI agent repeated verbatim — the same paragraph,
list item, code block, or table appearing twice in the spec. Agents pad output by restating
a requirement in two sections or pasting the same boilerplate into multiple places, and every
other check looks at one block in isolation, so a verbatim repeat slips through. It compares
blocks by kind and normalized text (the same whitespace-insensitive comparison `--diff` uses),
reports each repeat with its line and the line of the first occurrence it duplicates, and skips
blocks shorter than a small threshold (separators, one-word labels repeat legitimately). It
exits non-zero when any block repeats, so it can gate a pipeline; add `--json` for structured
findings.

`--check-alt-text` reports images with no alt text — the `![](image.png)` form an agent emits
when it drops a screenshot or diagram into a spec without describing it. Alt text is what a
screen reader announces and what shows when the image fails to load, so a missing description
is a genuine accessibility defect; `--check-links` deliberately skips images, so nothing else
catches it. An image is flagged when the text between `![` and `]` is empty or only whitespace;
the target is reported so the finding points at a recognizable image (whether that relative
target exists on disk is `--check-paths`' concern). It exits non-zero when any image lacks alt
text, so it can gate a pipeline; add `--json` for structured findings.

`--review` is the one-shot verdict: it runs `--lint`, `--check-structure`, `--check-links`,
`--check-tables`, `--check-fences` (unclosed fences only — the advisory missing-tag rule stays under
the dedicated command), and `--check-paths` together, groups the findings by category with a combined
issue count, and includes the checklist completion tally. It exits non-zero if any check found an
issue — so an agent or CI step can call a single command to decide whether a spec is ready. Add
`--json` for a structured report with one array per check.

`--review --sarif` emits the same verdict as a **SARIF 2.1.0** log — the static-analysis
interchange format GitHub code scanning, Azure DevOps, and other CI dashboards ingest natively.
Where `--json` is Spectacle's own shape, `--sarif` is the lingua franca, so the whole check
battery becomes a first-class CI analyzer (inline PR annotations, the code-scanning tab) with no
bespoke glue. Each finding is one SARIF result with a `category/rule` rule id (e.g.
`structure/multiple-h1`, `fences/unclosed-fence`), an `error` level, a message, and a one-based
line location; the tool driver lists the full rule catalogue up front. It works for a single file
and, naturally, for a whole folder (`<dir> --review --sarif` writes results across every spec's
URI in one log). The exit code is unchanged — non-zero when any issue is found. `--sarif` takes
precedence over `--json`, and applies to the plain verdict (not the `--baseline` delta).

`--review <dir>` reviews a **whole folder** of specs in one shot — AI agents routinely emit a
directory of them. Point `--review` at a directory and it walks it recursively, runs the full
review on every `.md` / `.markdown` file, and prints a roll-up: how many files it checked, how
many have issues, and the total issue count, followed by a per-file line. It exits non-zero if any
spec in the set has an issue, so one command gates the entire batch; add `--json` for a structured
report carrying each file's full findings. If the folder holds no specs it prints a notice and
exits 0.

`--review <file> --baseline <old>` answers the question at the heart of the write → review → revise
loop: *what did this revision actually change?* It runs the full review on both the current file and
the older `<old>` version and classifies every finding as **fixed** (gone since the baseline),
**new** (introduced by the revision) or **persisting** (present in both), and tracks checklist
progress across the two. Findings are matched by category, rule and message — not line number — so a
finding that merely moved counts as persisting, not as one fixed plus one new. It exits non-zero
while the revision still carries any issue (new or persisting), matching plain `--review`'s
"spec must be clean" gate; add `--json` for structured `fixed` / `new` / `persisting` arrays an
agent can act on.

`--outline`, `--checklist`, `--check-links`, `--diff`, `--check-structure`, `--check-tables`,
`--check-fences`, `--check-paths`, `--check-sections`, `--check-duplication`, `--check-alt-text`,
and `--review` all run headless and write to stdout.

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
