# Keyboard Navigation — Design

**Status:** Draft
**Date:** 2026-05-26
**Scope:** Make every interactive feature of Spectacle operable without a mouse, while preserving existing mouse behavior for other users.

---

## 1. Goal

A user who prefers the keyboard can open a `.md` file, read it, add a comment, edit it, resolve it, delete it, manage orphans, and re-anchor — all without touching the mouse. Mouse users see no change in behavior.

The hard part lives **inside the WebView2 preview**. WPF window-level shortcuts already work (Ctrl+R, F5, zoom, F11, Esc, Ctrl+Shift+C/E). The block / comment / orphan surface inside the preview is mouse-only today.

## 2. Non-goals (v1)

- Vim-style modal editing or a `:` command palette.
- A command-search box ("fuzzy find a shortcut").
- Persistent on-screen hint bar.
- Persisting composer drafts across re-renders.
- Printable / exportable cheatsheet.
- Configurable key bindings.

## 3. Focus model

A single new JS module — `preview-keynav.js` — owns keyboard focus inside the WebView2. It coexists with `preview-annotations.js`; that file keeps doing DOM rendering and mouse handlers, untouched except for tiny hooks (one place where new DOM is built needs the orphan rows to get class `sp-orphan-row` and a `data-comment-id`).

**Focusables.** Three kinds, distinguished by class:

- `.md-block` — a content block (heading, paragraph, list, table, code, blockquote). Tagged by `BlockTagger`.
- `.sp-card` — a comment card (live), with `data-comment-id`.
- `.sp-orphan-row` — one row in the orphan panel, with `data-comment-id` (new class; today these are plain `<li>`).

**Roving tabindex.** Exactly one focusable has `tabindex="0"` at any moment; all others have `tabindex="-1"`. DOM order is navigation order, which is already: orphan panel rows → first block → its comment cards → next block → its cards → … → end of document.

**Single pointer.** `preview-keynav.js` keeps one `currentId` (the focused element's stable id and kind). On `ArrowDown` it picks the next focusable in DOM order; on `ArrowUp` the previous. `Home` / `End` jump to first / last. **No wrap-around** at edges.

**Key delivery.** One `keydown` listener on `document` reads `event.key` and dispatches based on which class the focused element has. Unknown keys are not intercepted — browser default (e.g., PageUp/PageDown scroll) still applies.

**Mouse coexistence.** All existing click handlers in `preview-annotations.js` stay. A click on a `.md-block`, `.sp-card`, or `.sp-orphan-row` also moves the keyboard pointer to that element, so users can mix freely.

**Boundary with WPF.** WPF keeps its 8 current `Window.InputBindings`. They continue to work because they fire before the WebView2 sees the key (when the WPF window has focus and the key matches a binding). The `?` help overlay lives in JS, inside the WebView2.

## 4. Key map

All non-global keys are non-modifier single keys (lowercase), to keep them ergonomic on the home row.

### Global (WPF — unchanged)

| Key | Action |
|---|---|
| Ctrl+R / F5 | Reload from disk |
| Ctrl+= / Ctrl+- / Ctrl+0 | Zoom in / out / reset |
| F11 | Fullscreen toggle |
| Esc | Close window (only when no overlay/composer/re-anchor active — see precedence below) |
| Ctrl+Shift+C | Copy revision plan |
| Ctrl+Shift+E | Export revision plan… |

### Preview-wide (JS, regardless of which item is focused)

| Key | Action |
|---|---|
| `?` (Shift+/) | Open help overlay |
| `g` then `g` | Jump to first focusable |
| `G` (Shift+g) | Jump to last focusable |

### On `.md-block`

| Key | Action |
|---|---|
| `↑` / `↓` | Previous / next focusable |
| `Home` / `End` | First / last focusable |
| `Enter` or `c` | Open composer for new comment on this block |

### On `.sp-card` (comment)

| Key | Action |
|---|---|
| `↑` / `↓` / `Home` / `End` | Navigation |
| `e` | Edit (opens composer pre-filled) |
| `r` | Resolve / Reopen toggle |
| `d` | Delete |

### On `.sp-orphan-row`

| Key | Action |
|---|---|
| `↑` / `↓` / `Home` / `End` | Navigation |
| `d` | Delete |
| `a` | Begin re-anchor |

### In composer (existing — unchanged)

| Key | Action |
|---|---|
| `Esc` | Cancel and close composer |
| `Ctrl+Enter` | Save |

### In re-anchor mode

| Key | Action |
|---|---|
| `↑` / `↓` | Move pointer over target `.md-block`s (others inert) |
| `Enter` | Confirm — re-anchor to focused block |
| `Esc` | Cancel re-anchor |

### In help overlay

| Key | Action |
|---|---|
| `Esc` or `?` | Close overlay |

### Esc precedence

A single unwind chain — top wins:

1. Help overlay → close overlay
2. Composer → cancel and close composer
3. Re-anchor mode → cancel re-anchor
4. (none of the above) → close window (WPF binding)

So a stray Esc never closes the window when work is in progress.

## 5. Help overlay

A single in-WebView `<div role="dialog" aria-modal="true">` injected once by `preview-keynav.js`. Hidden by default; shown on `?`.

**Look.** Centered card, ~520 px wide, dark backdrop (`rgba(0,0,0,0.5)`) over the preview. Title "Keyboard shortcuts". Two-column key/description rows grouped by section header (Global, Preview-wide, On block, On comment, On orphan, Composer, Re-anchor). Bottom-right footer: "`Esc` to close". Inherits the preview's dark theme — no new color tokens.

**Sections shown.** All seven sections above from the key map, in the same order. The eighth section ("In help overlay") is omitted because its only key is shown in the footer. Static — does not filter by current focus, because part of its job is to teach what's possible elsewhere.

**Behavior.**

- Opens on `?` from anywhere except inside the composer textarea (composer Esc/Ctrl+Enter take priority).
- While open, all other keys are swallowed.
- Closes on `Esc` or `?` again. Closing restores focus to whatever had it before opening.
- Re-opening is idempotent.

**Single source of truth.** The key/description rows are generated from one `KEYMAP` table in `preview-keynav.js`. The same table drives the actual `keydown` dispatcher, so the overlay can never drift from real behavior. Adding a binding means adding one row.

**Accessibility.**

- `role="dialog"`, `aria-modal="true"`, `aria-labelledby` pointing to the title.
- Focus moves to the overlay on open; restored on close.
- High-contrast mode: when `HighContrastWatcher` flags HC, overlay uses system colors (`Window`, `WindowText`, `Highlight`) instead of the dark palette, matching how the rest of the preview shifts.

## 6. Focus persistence

Preview re-renders on every file change (`DebouncedFileWatcher`), theme switch, or Ctrl+R. Each is a full `NavigateToString` — DOM identity gone, JS state wiped.

**Strategy.** Persist a tiny pointer in `sessionStorage`. `sessionStorage` survives navigation within the same WebView2 lifetime; it is wiped on app close — which is exactly our scope.

```text
spectacle.focus = { kind: "block"|"card"|"orphan", id: <data-block-id|data-comment-id> }
spectacle.helpOpen = "1" | (absent)
```

**On init (`preview-keynav.js`):**

1. Read `spectacle.focus`.
2. Try to find a matching element. Fallbacks, in order: exact id → block at previous DOM position (for block kind) → anchoring block (for card kind) → orphan row (for newly orphaned card) → first focusable → none.
3. Set roving tabindex, call `.focus()`, scroll into view (`scrollIntoView({ block: "nearest" })`).
4. If `spectacle.helpOpen` is set, reopen the overlay.

On every focus change, write the pointer. On overlay open/close, write/clear `helpOpen`.

### Edge cases

| Situation | Behavior |
|---|---|
| Stored block id no longer exists | Fall back to block at the previous DOM position; else first focusable. |
| Stored comment was deleted | Fall back to its anchoring block. |
| Stored comment became orphaned | Focus lands on its row in the orphan panel. |
| Stored orphan became matched | Focus lands on the now-matched comment card. |
| First-ever load (no stored pointer) | First focusable. |
| Re-anchor mode active when re-render fires | Cancel re-anchor; flash status hint "Re-anchor cancelled by document change"; restore focus from pointer. |
| Composer open when re-render fires | Composer destroyed (DOM gone). Unsaved text is lost (v1 — same as today). Pointer restores to the block the composer was attached to. |
| Help overlay open when re-render fires | Overlay reopened on init via `spectacle.helpOpen` flag. |
| Mouse click before re-render | Pointer was already updated by click handler; no special handling. |

**Scroll behavior.** `scrollIntoView({ block: "nearest" })` rather than `"center"` — less jumpy when the focus is already in view.

**Status hints.** A small ephemeral toast element (`#sp-hint`) at the bottom of the preview, reused for "your action did something" feedback: re-anchor cancelled, etc. Fades after 2 seconds, replaceable. Used sparingly.

## 7. Visual focus indicator

Roving tabindex moves an invisible pointer; the user must *see* where they are.

**Default.** 2 px solid outline in `#4ea1ff` (the accent color already used by the "Copy revision plan" button in `MainWindow.xaml`), `outline-offset: 2px`, `border-radius: 4px`. Applied via `:focus-visible` only — so mouse clicks don't paint a ring.

```css
.md-block:focus-visible,
.sp-card:focus-visible,
.sp-orphan-row:focus-visible {
  outline: 2px solid #4ea1ff;
  outline-offset: 2px;
  border-radius: 4px;
}
```

**Per-kind nuance.**

- `.md-block` — outline only; no background change (would interfere with code blocks and tables).
- `.sp-card` — outline plus a 4 px left border in `#4ea1ff` replacing the existing border, signalling "armed".
- `.sp-card.sp-resolved` — same ring but `#9aa0a6` (muted), signalling that `r` will reopen rather than resolve.
- `.sp-orphan-row` — outline plus subtle row background tint.

**Re-anchor mode override.** When `body.sp-reanchor-mode` is set, only `.md-block` is focusable; cards/orphans are inert. The focus ring becomes dashed (`outline-style: dashed`) and the cursor changes to `crosshair`.

**High-contrast mode.** When HC is active, the ring becomes `Highlight` (system color) at 3 px width. Matches how the rest of the preview already shifts to system colors.

**Contrast.** `#4ea1ff` outline at 2 px against the `#1e1e1e` background ≈ 5.2:1, meeting WCAG 1.4.11 non-text contrast (3:1).

## 8. Architecture

```
src/Spectacle/Render/Assets/
  preview-annotations.js        (existing — unchanged except orphan-row class hook)
  preview-annotations.css       (existing — unchanged)
  preview-keynav.js             (NEW — owns focus, dispatch, overlay)
  preview-keynav.css            (NEW — focus ring, overlay styles)

src/Spectacle/Render/
  PreviewHtml.cs                (modified — inject new JS + CSS)
```

`preview-keynav.js` exposes nothing globally except an init function called by the same DOMContentLoaded path as `preview-annotations.js`. It depends on `preview-annotations.js` being present (for the rendered DOM and the post-message helper for button actions); load order matters — keynav goes **after** annotations.

`preview-annotations.js` keeps owning the click handlers and the `post()` helper. `preview-keynav.js` calls into the DOM exactly as the buttons do — it triggers the same logical actions (open composer, save, delete) by invoking the same code paths or by `.click()`ing the existing buttons. **No duplication of business logic.**

`PreviewHtml.cs` is the single place where injection order is asserted: annotations CSS → annotations JS → keynav CSS → keynav JS → annotation payload. Tests assert this order.

## 9. Testing

### xUnit (C# — `test/Spectacle.Tests`)

| Test | Verifies |
|---|---|
| `PreviewHtmlInjectsKeyNavScript` | Rendered HTML includes `preview-keynav.js` after `preview-annotations.js`. |
| `PreviewHtmlInjectsKeyNavCss` | Rendered HTML includes `preview-keynav.css`. |
| `KeymapSingleSourceOfTruth` | Regression guard: the JS file references one `KEYMAP` const that is used both by the dispatcher block and by the overlay-build block (string match). |
| Existing rendering / annotation tests | Still pass — new injection does not break block-tagger anchors or annotation rendering. |

We do not unit-test JS focus logic in C# (no JS engine in the test project; adding one is overkill for this scope). Integration-level checks are manual.

### Manual WebView smoke checklist

Run against a fixture with 5+ blocks, 2 comments on different blocks, 1 orphan.

1. `↓` from initial focus walks: orphan row → first block → next block → … → into comment cards in place → next block. Focus ring follows.
2. `Home` / `End` jump to first / last. `G` jumps to last. `gg` jumps to first.
3. On a block: `Enter` and `c` both open the composer. `Esc` closes. `Ctrl+Enter` saves.
4. On a comment: `e` opens edit composer pre-filled. `r` toggles resolved (visual: muted ring after). `d` deletes; focus falls back to anchoring block.
5. On an orphan row: `d` deletes; `a` enters re-anchor; `↓`/`↑` move focus over blocks (dashed ring, crosshair cursor); `Enter` confirms; `Esc` cancels.
6. `?` opens overlay; `Esc` and `?` both close; focus is restored to the prior element.
7. Esc precedence: open overlay then press Esc → overlay closes, window stays. Open composer, press Esc → composer closes, window stays. With nothing active, Esc closes the window.
8. Edit the file externally → re-render → focus restored to same comment/block (or correct fallback if removed). Help overlay remains open if it was.
9. Ctrl+R reload → same restoration behavior.
10. Mouse click on a block moves the focus ring; keys then operate on the clicked element. Mouse users see no regression in click → composer flow.
11. F11 fullscreen → arrow keys still navigate.
12. Enable Windows high-contrast mode → focus ring uses system `Highlight` color; overlay uses system colors.
13. All eight existing WPF shortcuts still work.

### Build / verify gate

Per the user's global rule: `dotnet build` and `dotnet test` must pass before declaring "ready for IDE verification". The manual WebView pass happens in the IDE.

## 10. Out of scope (explicit reminders)

- README's keyboard table is the canonical user doc; this design extends it. The README will be updated in implementation, not in v1 spec.
- No configurable bindings.
- No persistent footer hint bar.
- No vim modes / command palette.
- No JS-side unit testing framework introduced.
- Composer draft persistence across re-renders is not solved here.
