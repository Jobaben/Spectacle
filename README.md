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
Spectacle.exe <file> --check-sections ["A,B,C"] [--config=<cfg>] [--json]  Report required sections (by heading) missing from the spec, then exit (non-zero if any)
Spectacle.exe <file> --check-duplication [--json]  Report blocks repeated verbatim elsewhere in the spec, then exit (non-zero if any)
Spectacle.exe <file> --check-alt-text [--json]  Report images missing alt text, then exit (non-zero if any)
Spectacle.exe <file> --check-link-text [--json]  Report links whose text names no destination, then exit (non-zero if any)
Spectacle.exe <file> --check-emphasis-heading [--json]  Report emphasized lines used as fake headings, then exit (non-zero if any)
Spectacle.exe <file> --check-prose [--json]    Report vague/hedging language, then exit (advisory — always exits 0)
Spectacle.exe <file> --check-toc [--json]      Report a table of contents out of sync with the headings, then exit (non-zero if any)
Spectacle.exe <file> --check-numbering [--json]  Report ordered lists whose numbering is out of sequence, then exit (non-zero if any)
Spectacle.exe <file> --check-bare-urls [--json]  Report bare (auto-linked) URLs that should be descriptive links, then exit (non-zero if any)
Spectacle.exe <file> --check-heading-numbering [--json]  Report manually numbered headings out of sequence, then exit (non-zero if any)
Spectacle.exe <file> --review [--json|--sarif|--md] [--only=a,b|--skip=a,b]  Run all checks at once, then exit (non-zero if any issues)
Spectacle.exe <dir> --review [--json|--sarif|--md]  Review every spec under a folder at once, then exit (non-zero if any issues)
Spectacle.exe <file> --review --baseline <old> [--json]  Show what a revision fixed/introduced vs an older version, then exit
Spectacle.exe --init-config [path] [--force]   Scaffold a documented .spectacle.json (refuses to overwrite without --force), then exit
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

The list is optional. Omit it and Spectacle reads the required sections from a
**`.spectacle.json`** config, so a team declares its spec template once instead of retyping
it on every invocation. The config is a JSON object with a `requiredSections` string array:

```json
{ "requiredSections": ["Overview", "Acceptance Criteria", "Non-Goals"] }
```

The same config also declares the team's **gate** — which checks `--review` runs (see
"Tuning the gate" below) — via a `disabledChecks` array:

```json
{ "requiredSections": ["Overview"], "disabledChecks": ["duplication", "alt-text"] }
```

Discovery walks up from the spec's own directory and takes the nearest `.spectacle.json`
(the "closest config wins" rule editors and linters use), so a spec inherits the settings of
its enclosing project automatically. Point at a specific file with `--config=<path>`. An
inline list always wins over config; a malformed or missing config never crashes the check
(it resolves to no required sections, and `--check-sections` with nothing to enforce exits
non-zero with a hint rather than silently passing).

`--init-config` scaffolds that file so a team can adopt the project gate in one step instead
of authoring JSON by hand. It writes a documented `.spectacle.json` — a starter
`requiredSections` template, an empty `disabledChecks`, and a `"//"` note that explains each
field and names every valid check id (sourced from the live check set, so the scaffold can't
advertise a stale one) — to the current directory, to a directory you name (`--init-config
specs`), or to an explicit path. Editing it is the point: trim the required sections to your
template and list any checks you want off. Writing over an existing config would discard a
team's tuning, so it **refuses to overwrite** unless you pass `--force`; it prints the full
path it wrote and exits 0 (2 when it refused).

`--check-emphasis-heading` flags a paragraph that is nothing but a single bold or italic run
on its own line — `**Overview**` or `_Goals_` where the agent meant `## Overview`. It looks
like a heading but is not one, so it is invisible to every heading-based command here:
`--outline` never lists it, `--check-sections` never counts it as a present section, and
`--check-structure` cannot reason about its level. Catching it keeps the rest of the heading
toolchain trustworthy. It mirrors markdownlint's MD036: only a single-line paragraph whose
*entire* content is one emphasis run is flagged, and one ending in sentence punctuation
(`. , ; : ! ?`) is left alone (an emphasized *sentence* is not a heading). Only top-level
paragraphs count — an emphasized list item (`- **Term**`) or blockquote line is a legitimate
construct. It exits non-zero when any are found, so it can gate a pipeline; add `--json` for
structured findings.

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

`--check-link-text` reports links whose visible text says nothing about where they go —
the `[click here](…)` / `[link](…)` / `[read more](…)` boilerplate AI agents reach for
instead of naming the destination. Link text is what a screen reader announces out of
context (a user tabbing through links hears only the text) and what a reader scans, so
`here` or `this` is a genuine accessibility and clarity defect — the link analogue of the
missing alt text `--check-alt-text` catches, which nothing else looks at (`--check-links`
validates only that a link's *target* resolves, never its text). Two rules: `non-descriptive`
(the text is one of a tight, curated set of generic phrases — `click here`, `here`, `link`,
`more`, `read more`, …, matching markdownlint's MD059 defaults) and `empty` (the text between
`[` and `]` is blank, distinct from `--check-links`' empty-*target* rule). The phrase list is
deliberately conservative — only wording that is non-descriptive in essentially every context —
to keep the false-positive rate low, the same stance `--check-prose` takes. Images are skipped
(their text is alt text). It exits non-zero when any link is uninformative, so it can gate a
pipeline; add `--json` for structured findings.

`--check-prose` flags the hedging and vague filler language that is the signature defect
of AI-authored specs — wording that *looks* like a requirement but commits to nothing, so
neither a reader nor the next agent can tell what to build. It reports three rules: `hedge`
(uncertainty that signals an undecided spec — `should probably`, `may need to`, `perhaps`),
`weasel` (open-ended fillers with no concrete meaning — `etc.`, `and so on`, `various`,
`a number of`), and `vague-directive` (instructions that defer the real decision — `as
appropriate`, `where applicable`, `to be determined`). The word list is deliberately tight
(multi-word phrases and unambiguous fillers, not common words like "many" or "often" that
have legitimate uses), and fenced code is skipped. Because hedging is a judgement call,
this check is **advisory**: it prints findings but always exits 0, never gating a pipeline —
the same report-don't-fail stance as `--check-fences`' `no-language` rule. Add `--json` for
structured findings.

`--check-toc` validates a spec's **table of contents** against its actual headings — the
drift an AI agent introduces when it adds, renames, or removes a section but forgets to
update the TOC. It recognizes a TOC by a heading named `Table of Contents`, `Contents`, or
`TOC` (case-insensitive) followed by a list of in-document anchor links, and reports two
defects: `stale-toc-entry` (an entry pointing at `#anchor` that matches no heading — the TOC
references a section that was removed or renamed) and `missing-from-toc` (a body section the
TOC omits). The depth the TOC is expected to cover is inferred from the entries that do
resolve, so a deeper subsection the TOC never meant to list is left alone, and only headings
*after* the TOC count as entries it should carry. The check is a **no-op when the spec has no
TOC**, so a spec that never declared one is unaffected. It uses the same Markdig
auto-identifier slugs as `--check-links`, so the anchors matched here are the ones the viewer
emits. It exits non-zero when the TOC is out of sync, so it can gate a pipeline; add `--json`
for structured findings.

`--check-numbering` validates the numbering of **ordered lists** — the broken step or
requirement sequences an AI agent emits when it drops, duplicates, or reorders an item
(`1. 2. 2. 4.`). A reviewer skims a numbered spec by its numbers, so a gap or a repeat reads
as a missing step even when the prose is intact. Following markdownlint's MD029
`one_or_ordered` spirit, a list passes when its source markers are either *all the same* (the
lazy `1. 1. 1.` style every renderer numbers sequentially) or *strictly consecutive* from
whatever the first item starts at (`1. 2. 3.`, `0. 1. 2.`, `3. 4. 5.`); anything else is one
`out-of-sequence` finding, anchored at the first item that breaks the run. Each list —
including a nested one — is judged on its own, and code fences are ignored. Keeping both
legitimate styles clean holds the false-positive rate low enough to gate, so it exits
non-zero when a list is out of sequence; add `--json` for structured findings.

`--check-bare-urls` reports bare URLs pasted straight into the prose — `https://example.com`
sitting in a sentence rather than a descriptive Markdown link. GFM auto-links such text, so it
renders as a link whose *visible text is the raw URL*: a screen reader reads the whole address
aloud and a reader scanning the page learns nothing about where it goes. It is the link analogue
of the missing alt text `--check-alt-text` catches and the worst case of the non-descriptive text
`--check-link-text` flags — the text *is* the URL — which is why neither of those looks at it (a
bare URL has no authored text to inspect). Only the bare, undelimited form is flagged; the two
legitimate ways to write a URL verbatim are deliberately left alone, so the rule keeps a clean
escape hatch: an explicit autolink (`<https://example.com>`, the CommonMark "render this as a link
on purpose" syntax) and a code span (`` `https://example.com` ``, when the URL is a literal value
like an API endpoint — Markdig never auto-links inside code). A proper `[text](url)` link is never
flagged, and URLs inside fenced or indented code are skipped for the same reason a code span is. It
exits non-zero when any bare URL is found, so it can gate a pipeline; add `--json` for structured findings.

`--check-heading-numbering` validates the numbering of *manually numbered headings* — the broken
section sequences an AI agent emits when it drops, duplicates, or reorders a section (`## 1. Goals`,
`## 2. Design`, `## 4. Rollout` — where did 3 go?). It is the heading analogue of `--check-numbering`,
which judges ordered *lists* only; a reviewer skims a numbered spec by its section numbers exactly as
they skim a numbered list, so a gap or repeat reads as a missing section even when the prose is intact.
Only flat, single-integer prefixes participate — a heading whose text begins with an integer then `.`
or `)` then whitespace (`1. `, `2) `, `10. `). Dotted hierarchical numbering (`1.2 Detail`) is
deliberately ignored: detecting it reliably and validating a full outline is a far more
false-positive-prone problem, and a spec that never numbers its headings is wholly unaffected (the
same "enforced only when present" stance the TOC and section-template checks take). Numbered headings
are grouped into runs by heading level, and a run is closed whenever a *shallower* heading intervenes
— so sub-section numbering that legitimately restarts under each new parent (`### 1.`, `### 2.` under
one `##`, then `### 1.` again under the next) is never flagged. Following markdownlint's MD029
`one_or_ordered` spirit, each run passes when its numbers are either *all the same* (the lazy `1. 1. 1.`
style) or *strictly consecutive* from whatever the first heading starts at; anything else is one
`out-of-sequence` finding, anchored at the first heading that breaks the run. It exits non-zero when a
run is out of sequence, so it can gate a pipeline; add `--json` for structured findings.

`--review` is the one-shot verdict: it runs the whole gating battery together — `--lint`,
`--check-structure`, `--check-links`, `--check-tables`, `--check-fences` (unclosed fences only —
the advisory missing-tag rule is surfaced separately, see below), `--check-paths`,
`--check-duplication`, `--check-alt-text`, `--check-link-text`, `--check-emphasis-heading`,
`--check-sections`, `--check-toc` (a no-op unless the spec has a TOC), `--check-numbering`,
`--check-bare-urls`, and `--check-heading-numbering` —
groups the findings by category with a combined issue count, and includes the checklist
completion tally. It exits non-zero if any check found an issue — so an agent or CI step can call a
single command to decide whether a spec is ready. Add `--json` for a structured report with one
array per check.

**Advisories.** `--review` also surfaces an `advisories` section — the guidance the gate
deliberately does not fail on, so it no longer requires a separate run to see. It carries the
`--check-prose` findings (hedging / vague language) and the fence `no-language` rule (a closed
but untagged code block). Advisories are reported in the text, `--json` (an `advisories` object
plus an `advisoryCount`), and `--md` outputs, but are **never counted in the issue total and
never change the exit code** — hedging and a missing language tag are judgement calls, not
pass/fail defects, the same report-don't-fail stance `--check-prose` and the dedicated
`--check-fences` take. They are guidance for the agent revising the spec, gathered into the one
command it already runs. (Advisories are independent of the `--only` / `--skip` gate selection,
since their rules never gate; they are not emitted in the `--sarif` log, which carries only the
gating defects, nor in the `--baseline` delta.)

The required-section check participates only when a spec template is declared: `--review` reads
`requiredSections` from the nearest **`.spectacle.json`** (the same config and "closest config
wins" discovery `--check-sections` uses) and reports any the spec omits. With no config the
section check is a no-op, so a spec reviewed without a template is unaffected. This makes
`.spectacle.json` the single place a team declares its template, enforced automatically by the
one-shot verdict — for a single file, a `--baseline` delta, and every spec in a folder review alike.

`--review --sarif` emits the same verdict as a **SARIF 2.1.0** log — the static-analysis
interchange format GitHub code scanning, Azure DevOps, and other CI dashboards ingest natively.
Where `--json` is Spectacle's own shape, `--sarif` is the lingua franca, so the whole check
battery becomes a first-class CI analyzer (inline PR annotations, the code-scanning tab) with no
bespoke glue. Each finding is one SARIF result with a `category/rule` rule id (e.g.
`structure/multiple-h1`, `fences/unclosed-fence`), an `error` level, a message, and a one-based
line location (a missing section, which has no line, is anchored at line 1); the tool driver lists
the full rule catalogue up front. It works for a single file
and, naturally, for a whole folder (`<dir> --review --sarif` writes results across every spec's
URI in one log). The exit code is unchanged — non-zero when any issue is found. `--sarif` takes
precedence over `--json`, and applies to the plain verdict (not the `--baseline` delta).

`--review --md` emits the verdict as a **Markdown report** — the artifact in the AI write →
review → revise loop a human reads or pastes straight into a pull request, and the most legible
form to hand back to the agent that authored the spec. Where `--json` and `--sarif` are for
machines, `--md` is for people and prose-native agents: a `# Review: <file>` heading, a one-line
summary (issue count, plus an honest note of anything suppressed or skipped), then one Markdown
subsection per check that found something — checks with nothing to report are omitted so the
report stays readable, and a clean spec simply says `No issues found.` A folder review
(`<dir> --review --md`) emits a roll-up heading followed by one section per spec. The exit code is
unchanged — non-zero when any issue is found. Precedence among the output formats is
`--sarif` > `--md` > `--json`, and `--md` applies to the plain verdict (not the `--baseline` delta).

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

### Tuning the gate

The one-shot verdict is otherwise all-or-nothing. Two controls let a team adopt it without
fighting checks that don't fit their style — the same file-level and line-level tuning every
linter offers, here serving the AI write → review → revise loop.

**Project gate (`disabledChecks` / `--only` / `--skip`).** Turn a gating check off for a whole
project by listing it in `.spectacle.json`'s `disabledChecks`, or for a single run with
`--review --skip=duplication,alt-text` (run everything except those) or
`--review --only=structure,links` (run only those). Precedence: `--only` chooses the universe,
then `disabledChecks` and `--skip` are both subtracted from it. The valid check ids are `lint`,
`structure`, `links`, `tables`, `fences`, `paths`, `duplication`, `alt-text`, `link-text`,
`emphasis-heading`, `sections`, `toc`, `numbering`, `bare-urls`, and `heading-numbering`; an unrecognized id is ignored with a warning. A disabled check is never silently
treated as passing — the verdict lists it under `skipped` (text) / `skippedChecks` (JSON) so a
clean result can't be confused with one that simply ran fewer checks. The selection applies
uniformly to a single file, a folder batch (each spec honours its own nearest config), and a
`--baseline` delta (off on both sides, so a skipped check never reads as fixed or new).

**Inline suppression (`spectacle-disable-line` / `spectacle-disable-next-line`).** Silence one
finding at one place — a paragraph an agent repeated on purpose, an intentionally decorative
image — by annotating the spec itself, the line-level companion to the project gate (the
`eslint-disable-next-line` / `# noqa` mechanism). Write an HTML comment (invisible in the
rendered preview) on the finding's line or the line before it:

```markdown
<!-- spectacle-disable-next-line duplication -->
The quick brown fox jumps over the lazy dog.

![logo](logo.png) <!-- spectacle-disable-line alt-text -->
```

List one or more check ids after the keyword (comma- or space-separated), or omit them to
suppress every check on that line. Directives inside fenced code are ignored, so a spec can
document the syntax without disarming its own gate. A suppressed finding stops gating but is
counted, not hidden: the verdict reports `N suppressed` (text) / `suppressedCount` (JSON), again
keeping a clean result honest.

`--outline`, `--checklist`, `--check-links`, `--diff`, `--check-structure`, `--check-tables`,
`--check-fences`, `--check-paths`, `--check-sections`, `--check-duplication`, `--check-alt-text`,
`--check-link-text`, `--check-emphasis-heading`, `--check-prose`, `--check-toc`,
`--check-numbering`, `--check-bare-urls`, `--check-heading-numbering`, and `--review` all run
headless and write to stdout.

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
