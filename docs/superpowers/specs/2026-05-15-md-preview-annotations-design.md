# Spectacle — Preview Annotations for LLM-Targeted Revisions (Design Spec)

**Status:** Proposed 2026-05-15
**Scope:** Add a block-level annotation layer to the preview that captures revision instructions and exports them as an LLM-targeted "revision plan." The source `.md` is never modified by Spectacle.

## 1. Problem and Goal

Reviewing a rendered Markdown document and capturing revision intent today means switching to an editor, jumping back to the preview, and re-explaining context to whatever tool will do the rewrite. Spectacle renders the document beautifully; the gap is between "I saw something I want changed" and "I can hand an LLM clear instructions to change it."

**Goal:** While viewing a `.md` in Spectacle, let the user click any block in the preview, write a revision instruction against that block, and export the collected instructions as an unambiguous LLM-targeted **revision plan**.

### 1.1 Reconciliation with v1's "Non-Goals: Editing"

The v1 design spec lists editing as a non-goal. This feature does **not** violate that constraint:

- Spectacle does not write to the source `.md`. Ever.
- Annotations live in a sidecar file under `%LOCALAPPDATA%`.
- The output is an LLM-targeted prompt that the user (or an external tool) applies to the source. Spectacle remains a read-only viewer for the markdown itself.

The `BufferDocument` editor seam reserved by the v1 spec is untouched and unused by this feature.

## 2. The Load-Bearing Artifact: The Revision Plan

Every other decision in this spec serves the quality of one output. When the user copies/exports, an LLM receives this:

````markdown
# Revision plan for README.md

Source file: C:\path\to\README.md (SHA-256: abc123…)
Generated: 2026-05-15T10:00:00Z

Apply each revision below to the source file. Quote each "Original" block
verbatim from the source before replacing it; leave all other content
unchanged. If an "Original" no longer matches the source exactly, stop and
report which revision could not be applied.

---

## Revision 1 — paragraph at line 42

**Original (verbatim from source):**

> Spectacle is a Windows-only Markdown viewer.

**Instruction:**

Reword for clarity: open with what the user gets, not what the tool is.
Suggest: "View Markdown files with VS Code-preview fidelity, from Explorer
or PowerShell."

---

## Revision 2 — heading at line 7 ("## Install")

**Original (verbatim from source):**

> ## Install

**Instruction:**

Rename to "## Quick start" — "Install" implies an installer; this is copy-and-run.
````

Properties this format earns:

- LLM gets the exact original to match against → unambiguous targeting.
- Numbered, independent revisions → can be applied one at a time.
- Block kind + line number for the human reader and as a sanity check.
- File SHA in the header → LLM can refuse if the source has changed underneath.
- "Stop and report if Original doesn't match" instruction → bounds hallucination.

**Primary export path:** clipboard copy via a top-bar button. **Secondary:** save to `<file>.revisions.md` next to the source.

## 3. Approach

**Inline-after-block composer + per-file sidecar JSON.**

| Option | Decision |
|---|---|
| **A. Inline-after-block + sidecar JSON** | **Chosen.** Matches "click a section which pops a comment block." Composer renders in document flow directly under the clicked block, pushing the rest down. Pure WebView2 work, no new WPF panels. Visual separation comes from styling. |
| B. Right-rail comment cards (Google-Docs style) | Rejected for v1. More polished review UX but heavier: scroll-sync, narrow-window collapse, connector geometry on resize. Revisit if review-density warrants it. |
| C. Inline source-comment markers (`<!-- spectacle: … -->` in the .md) | Rejected. Writes to the source `.md` — contradicts the read-only principle. |

## 4. Block Identity

Block identity is the load-bearing technical decision. If a comment binds to the wrong block on reload, the feature has betrayed the user.

Each rendered block carries:

- `kind`: one of `paragraph | heading | list-item | code | blockquote | table | hr`.
- `line`: 1-based source line of the block (Markdig exposes this via `Block.Line`).
- `textHash`: SHA-256 of the block's source markdown text (the slice from `Block.Span`).
- `leadingText`: first 80 chars of the block's source text, for debugging / orphan display only — **never** used to silently re-attach.

Raw HTML blocks (`HtmlBlock` in Markdig) are not commentable in v1. Markdig renders them verbatim and does not accept attribute injection, so they cannot be anchored from the rendered DOM. They are skipped by `BlockTagger`.

### 4.1 Matching Policy on Reload (Strict)

The matcher walks current blocks and saved comments in source order, keying each on `(kind, textHash)`. For each saved comment whose anchor was the *N*th occurrence of `(kind, textHash)` in the previously rendered document, the matcher binds it to the *N*th occurrence of the same `(kind, textHash)` in the current document.

1. If a current block exists with that `(kind, textHash, occurrence-index)` → re-attach. Line may have shifted; that's fine.
2. No match → comment is **orphaned**. It is **not** auto-reattached by fuzzy match.
3. Orphans appear in a collapsed "Orphaned (n)" panel at the top of the preview, showing each orphan's `leadingText` and offering **Delete** or **Re-anchor manually** (click a block to bind).

The occurrence-index tiebreak handles the "two identical empty paragraphs" case deterministically. Strict matching trades occasional manual re-anchoring for the guarantee that a comment is never silently bound to the wrong block.

`BlockAnchor` therefore also records the occurrence-index at write time:

```
BlockAnchor { Kind, Line, TextHash, OccurrenceIndex, LeadingText }
```

## 5. UI Behavior

- **Hover indicator.** A 3 px accent left-border appears on the block under the cursor. CSS-only:
  ```css
  .md-block { border-left: 3px solid transparent; padding-left: 0.5rem; }
  .md-block:hover { border-left-color: var(--accent); cursor: pointer; }
  ```
  No animation (consistent with the v1 spec's "no motion sensitivity hazards"). Hover lives always when in the preview — there is **no review-mode toggle**.
- **Click target.** The block itself is the click target. Text selection inside a block uses normal selection-drag behavior; a single click without drag opens the composer. Implementation: on `mouseup`, treat as click if `mousedown→mouseup` displacement < 4 px and no text is selected.
- **Composer.** Renders in document flow directly under the clicked block. Distinct background, accent left-border, "Revision request" header, a multi-line `<textarea>`, and buttons: **Save**, **Cancel**, and (when editing) **Delete**. Escape cancels. Save is disabled while the body is empty/whitespace.
- **Saved comments.** Same accent styling, but read mode (no textarea). Clicking a saved comment opens an editor. A discrete "•••" menu offers **Edit**, **Delete**, **Resolve**.
- **Multiple comments per block.** Stack chronologically below the block, each with `Revision request #N` header.
- **Resolved comments.** Greyed out, collapsed by default, with a small toggle to expand.
- **Comment count badge.** Blocks with existing comments display a small `💬 N` glyph at the start of the line in addition to the accent border. Provides a non-color signal for orientation and screen readers.
- **Top-bar actions.** A new compact top bar appears above the WebView when at least one comment exists, with:
  - **Copy revision plan** (clipboard)
  - **Export revision plan…** (saves `<file>.revisions.md`)
  - **N comment(s) • M orphaned** status text

When there are zero comments and zero orphans, the top bar is hidden — the document looks identical to the read-only viewer.

## 6. Architecture Additions

```
┌────────────────────────────────────────────────────────────────┐
│ PreviewPipeline (existing)                                     │
│   ▶ MdRenderer ──▶ BlockTagger (NEW Markdig renderer override) │
│         emits  <div class="md-block" data-block-id="b0"        │
│                     data-kind="paragraph" data-line="42"       │
│                     data-text-hash="…">…</div>                 │
│   ▶ AnnotationStore.Load(filePath)                             │
│   ▶ AnnotationMatcher.Match(blocks, savedComments)             │
│        ─▶ { matched, orphaned }                                │
│   ▶ PreviewHtml.Build(bodyHtml, baseHref, theme, annotations)  │
│        injects PreviewAnnotations.js + frozen payload          │
│   ▶ WebViewHost.NavigateToString(html)                         │
│                                                                 │
│ WebView2 ◀─ postMessage ─▶ WebViewHost                          │
│   commentSave / commentDelete / commentResolve / orphanReanchor│
│        ─▶ AnnotationStore.Save(updated)                        │
│        ─▶ PreviewPipeline.Refresh()                            │
└────────────────────────────────────────────────────────────────┘
```

### 6.1 Units

| Unit | Responsibility |
|---|---|
| `Render/BlockTagger` | Markdig renderer override that wraps each block-level element with `data-block-id`, `data-kind`, `data-line`, `data-text-hash`. Source-line and text-span come from Markdig's `Block.Line` / `Block.Span`. |
| `Render/PreviewHtml` *(extended)* | Existing builder gains an `IReadOnlyList<MatchedComment>` parameter. Injects `PreviewAnnotations.js`, the embedded CSS for annotation styling, and a frozen `window.__spectacleAnnotations__` payload. |
| `Render/Assets/preview-annotations.css` *(new)* | All annotation styling: hover indicator, composer, comment cards, badge, orphan panel. Dark and high-contrast variants. |
| `Render/Assets/preview-annotations.js` *(new)* | Wires click → composer; renders existing comments next to their blocks from the payload; routes save/delete/resolve through `window.chrome.webview.postMessage`. |
| `Annotations/Comment.cs` | Immutable record: `Id, BlockAnchor, OriginalText, Body, CreatedAt, ResolvedAt?`. |
| `Annotations/BlockAnchor.cs` | Immutable record: `Kind, Line, TextHash, OccurrenceIndex, LeadingText`. |
| `Annotations/AnnotationStore.cs` | Loads/saves per-file sidecar JSON at `%LOCALAPPDATA%\Spectacle\annotations\<sha256-of-canonical-path>.json`. Atomic write (temp file + `File.Move(overwrite: true)`). Tolerates missing/corrupt files (logs + returns empty). |
| `Annotations/AnnotationMatcher.cs` | Strict `(Kind, TextHash)` match. Returns `MatchResult { Matched: IReadOnlyList<MatchedComment>, Orphaned: IReadOnlyList<Comment> }`. |
| `Annotations/RevisionPlanExporter.cs` | Produces the revision-plan markdown shown in §2 from a current set of matched comments + source file metadata. |
| `Web/WebViewHost` *(extended)* | New `WebMessageReceived` handler dispatching JSON messages by `type`: `commentSave`, `commentDelete`, `commentResolve`, `orphanReanchor`, `requestCopy`, `requestExport`. |
| `MainWindow` *(extended)* | Hosts the new top bar (visible iff `comments.Count + orphans.Count > 0`) with **Copy revision plan**, **Export revision plan…**, and count status. |

### 6.2 Sidecar JSON Schema

```json
{
  "fileVersion": 1,
  "sourcePath": "C:\\path\\to\\README.md",
  "sourceHashAtWrite": "sha256-hex",
  "comments": [
    {
      "id": "uuid-v4",
      "blockAnchor": {
        "kind": "paragraph",
        "line": 42,
        "textHash": "sha256-hex",
        "occurrenceIndex": 0,
        "leadingText": "first 80 chars of block text"
      },
      "originalText": "exact original block markdown captured at comment creation",
      "body": "user's revision instruction",
      "createdAt": "2026-05-15T10:00:00Z",
      "resolvedAt": null
    }
  ]
}
```

`sourceHashAtWrite` is informational; matching uses per-block `textHash`, not the whole-file hash.

### 6.3 Sidecar Path

`%LOCALAPPDATA%\Spectacle\annotations\<sha256-of-canonical-path>.json`

Canonical path: `Path.GetFullPath(file)` lowercased on Windows. Using a hash of the path keeps the filename stable and avoids any character-class issues. The sidecar contains the human-readable `sourcePath` for diagnostics.

## 7. Scope Discipline (Non-Goals)

- No editing of the source `.md` from inside Spectacle.
- No applying the revision plan automatically. Export is one-way; the user pastes the plan into the LLM tool of their choice or runs it themselves.
- No collaboration / multi-user sync — sidecar is local-only.
- No inline span-level comments (e.g., highlighting three words). Block-level only.
- No threading on comments. One revision instruction per comment; use multiple comments on the same block for related concerns.
- No keyboard shortcut for a "review mode" — there is no mode.
- No migration path for v0 sidecars (this is v1 of the schema; `fileVersion` exists to allow one later).

## 8. Accessibility

- Each `.md-block` gets `tabindex="0"`. Tabbing through the document focuses blocks in source order; `Enter` opens the composer. Mirrors the hover→click path for keyboard users.
- Focus on a `.md-block` uses the same `:focus-visible` 2 px outline rule the v1 spec already mandates.
- Composer is a real `<textarea>`, focused on open.
- Comment cards: `<article role="comment" aria-label="Revision request 1 of 2 on paragraph at line 42">…</article>` so Narrator announces them as discrete regions.
- Color is never the sole signal: the `💬 N` badge gives a text/glyph indication for blocks with comments. Orphan count appears as text in the top bar.
- Both the dark and high-contrast CSS variants include matching annotation styles. The `@media (forced-colors: active)` rule already in `preview.css` is extended to keep annotation borders and composer outlines on the forced palette.

## 9. Keyboard Shortcuts

| Keys | Action |
|---|---|
| `Tab` / `Shift+Tab` | Move focus through blocks and through the composer / comment-card buttons. |
| `Enter` (on focused block) | Open composer for that block. |
| `Esc` (in composer) | Cancel composer. |
| `Ctrl+Enter` (in composer) | Save. |

Reserved v1-spec keys (`Ctrl+S`, `Ctrl+Z`, `Ctrl+Y`, `Ctrl+F`, `Ctrl+H`, `Ctrl+W`, `Ctrl+N`, `Ctrl+O`, arrow chords) remain reserved for the future editor — this feature does **not** claim them.

## 10. Persistence and Lifecycle

- The sidecar is loaded once on file open and on every `Document.Changed` (auto-reload).
- Every comment mutation (save / delete / resolve) writes the sidecar atomically before the UI re-renders.
- If the sidecar file is missing or corrupt, it is treated as "no comments." A corruption is logged to stderr; the file is renamed to `<hash>.json.corrupt-<timestamp>` so the user can recover manually.
- The sidecar directory is created lazily on first write.
- No background save loop. No dirty state. Save is synchronous from the user's perspective.

## 11. Testing Strategy

- **`BlockTaggerTests`** — snapshot tests against fixtures (one fixture per block kind, plus a mixed-document fixture). Assert each block carries the expected `data-block-id`, `data-kind`, `data-line`, `data-text-hash`.
- **`AnnotationMatcherTests`** — matrix over (source unchanged | block edited | block deleted | block inserted above | block moved | block kind changed) × verify expected `matched` / `orphaned` outcomes. Includes the "two blocks with identical text" case to verify `(kind, textHash)` alone is sufficient given block ordering as a tiebreaker.
- **`AnnotationStoreTests`** — roundtrip JSON; atomic-write semantics (no partial file remains after a simulated mid-write crash); corrupt-file rename behavior.
- **`RevisionPlanExporterTests`** — golden-file snapshot of the generated revision-plan markdown for a fixture with 3 comments across kinds, including one with a multi-line `body`.
- **Manual smoke:**
  1. Hover/click each block kind; confirm hover indicator and composer.
  2. Save a comment; confirm it persists after reopening the file.
  3. Edit the source on disk to alter a commented block; reopen; confirm comment appears in the orphan panel and re-anchor-manually works.
  4. Add three comments across different blocks; click **Copy revision plan**; paste into an LLM; verify the LLM applies them correctly.
  5. Toggle Windows Contrast Themes; confirm annotation UI engages high-contrast variant.
  6. Tab through blocks with the keyboard; press `Enter`; confirm composer opens and `Esc` / `Ctrl+Enter` work.
  7. Narrator announces comment cards as discrete regions with the expected label.

## 12. Open Risks

| Risk | Mitigation |
|---|---|
| Two blocks in the same document have identical text (e.g., two empty paragraphs). | Handled by the occurrence-index tiebreak in §4.1. |
| `Block.Span` in Markdig may not cover trailing whitespace consistently across kinds. | `textHash` is computed over a normalized form: trim trailing whitespace per line, normalize line endings to `\n`. The exact normalization is captured in `BlockAnchor.cs` and tested. |
| Sidecar directory path contains atypical characters on some user systems. | Hash-of-canonical-path avoids the issue entirely; the sidecar filename is always 64 hex chars + `.json`. |
| User accumulates orphans they never clean up. | Orphan panel shows count; `Delete` is one click; orphans never auto-merge into the revision plan output. |
| LLM applies the revision plan but the source file has changed underneath. | The plan instructs the LLM to verify each Original matches; the file-level SHA in the header is an additional check. Spectacle does not validate post-application. |

## 13. Defaults

| Question | Default |
|---|---|
| Composer placement | Inline, directly below clicked block |
| Storage | Per-file sidecar in `%LOCALAPPDATA%\Spectacle\annotations\` |
| Orphan policy | Strict — never silently re-attach |
| Mode toggle | None — annotation affordances always available in preview |
| Multiple comments per block | Allowed; stacked chronologically |
| Resolve behavior | Greys out and collapses; remains in sidecar |
| Export to clipboard | Primary export path |
| Export to file | Secondary (`<file>.revisions.md`) |
| Apply revisions automatically | No |
