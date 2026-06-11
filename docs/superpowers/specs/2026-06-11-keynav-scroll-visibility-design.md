# Keyboard-nav focus always scrolls target into comfortable view

**Date:** 2026-06-11
**Status:** Approved

## Problem

`focusTarget` in `src/Spectacle/Render/Assets/preview-keynav.js` only scrolls when the
target has *zero* visible pixels (`isFullyOffscreen`). A block with a 1px sliver in
view receives focus with no scroll, leaving it effectively below the fold. These
no-scroll steps compound as the user arrows toward the end of a document, so focus
outruns the viewport and the last block ends up entirely offscreen, forcing manual
scrolling. When the gate does fire, `scrollIntoView({ block: "nearest" })` lands the
block flush against the viewport edge with no breathing room.

`init()`'s first-ever-load reveal has the same gated pattern and the same sliver bug.

## Decision

Approach A: let the browser own the geometry. Scroll unconditionally on keyboard
focus and use CSS `scroll-margin` for breathing room. (Rejected: manual visibility
math + `window.scrollBy` in JS — reimplements browser behavior, more edge cases;
centering the focused block — jumpy when arrowing quickly.)

## Changes

### JS — `src/Spectacle/Render/Assets/preview-keynav.js`

1. `focusTarget`: replace the `isFullyOffscreen(target)` gate with an unconditional
   `target.scrollIntoView({ block: "nearest" })`. The `opts.preventScroll` early-out
   is preserved. `syncPointerOnMouse` does not call `focusTarget` and is untouched —
   mouse clicks still never scroll.
2. `init()`: in the first-ever-load branch (no stored scroll), call
   `target.scrollIntoView({ block: "nearest" })` unconditionally. The stored-scroll
   restore branch is unchanged — re-renders still restore exact scroll position.
3. Delete `isFullyOffscreen` (no remaining callers).

### CSS — `src/Spectacle/Render/Assets/preview-keynav.css`

New rule alongside the focus-ring styles:

```css
.md-block, .sp-card, .sp-orphan-row {
  scroll-margin-top: 48px;
  scroll-margin-bottom: 48px;
}
```

48px ≈ two lines of body text (16px × 1.6 line-height). `scrollIntoView` treats
scroll-margin as part of the element's box, so a focused block lands with ~2 lines
of context rather than flush at the edge. Supported in all Chromium versions
WebView2 ships with.

## Resulting behavior

- Arrowing to a block already comfortably in view: no scroll (no jumpiness).
- Arrowing to a partially visible or offscreen block: minimal scroll until
  block + 48px margin is in view.
- Block taller than the viewport: near edge aligns (with margin); once it spans the
  viewport, further presses are no-ops.
- Document ends: `scrollIntoView` clamps at max scroll; `main`'s 64px bottom padding
  keeps the last block fully visible.
- Scrolling stays instant — no `scroll-behavior: smooth` exists in any stylesheet —
  so holding the arrow key cannot accumulate lag.
- Home/End/`G`/`gg` and re-anchor mode all route through `focusTarget` and inherit
  the fix automatically.

## Error handling

Nothing new. `scrollIntoView({ block: "nearest" })` cannot throw on attached
elements, and `focusTarget` already early-returns on null targets.

## Testing

No JS harness exists; the repo's pattern is asset-content assertions in
`test/Spectacle.Tests` (e.g. `PreviewHtmlTests`).

- **Automated:** one asset-level test asserting `preview-keynav.css` contains the
  `scroll-margin` rule covering `.md-block`, `.sp-card`, and `.sp-orphan-row`, and
  that `preview-keynav.js` no longer contains `isFullyOffscreen`.
- **Validation:** `dotnet build` + `dotnet test`.
- **Manual (IDE):** arrow through a long document to the end — every focused block
  fully visible with margin; `G`/`End` lands the last block fully in view; mouse
  click does not scroll; re-render still restores scroll position; re-anchor mode
  arrow moves reveal targets.

## Out of scope

- Mouse-click focus behavior (`syncPointerOnMouse`).
- Scroll-position persistence/restore across re-renders.
- The unused `opts` parameter threading in `focusTarget`.
