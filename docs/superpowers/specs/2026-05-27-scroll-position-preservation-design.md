# Scroll Position Preservation — Design

**Status:** Draft
**Date:** 2026-05-27
**Scope:** Preserve the preview's vertical scroll position during arrow-key navigation and across host-driven re-renders (creating, editing, resolving, deleting, or re-anchoring revision requests).

---

## 1. Goal

The user's vertical reading position in the preview is preserved as much as possible. Two scenarios today reset it:

1. **Arrow-key navigation** (↑/↓/Home/End/gg/G, plus arrow keys in re-anchor mode) calls `target.scrollIntoView({ block: "nearest" })` on every focus move. `block: "nearest"` scrolls whenever any portion of the new focus is occluded, which produces a visible jump even when the focused element is mostly visible.
2. **Re-rendering after an annotation action** (`commentSave`, `commentResolve`, `commentDelete`, `orphanReanchor`) calls `WebViewHost.SetHtml(html)` → `Web.NavigateToString(html)`. This is a full WebView navigation: scroll resets to 0. Then keynav's `init()` calls `scrollIntoView({ block: "nearest" })` on the restored focus, which moves the viewport to the focus position — not the user's prior reading position.

Goal: keep the scroll position. Only auto-scroll when a navigated focusable would otherwise be fully offscreen (zero pixels in the viewport).

## 2. Non-goals (v1)

- Anchor-based offset preservation (capturing the focused element's exact viewport offset and re-aligning to it after re-render). Plain `scrollY` persistence is sufficient.
- Host-side (C#) scroll handling via `ExecuteScriptAsync` around `NavigateToString`. The JS-only approach covers both scenarios.
- Persistence across application restarts. `sessionStorage` scope (per WebView session) is correct.
- Horizontal scroll preservation. The preview is single-column; no horizontal scroll exists.
- Composer textarea focus behavior. The textarea's default focus scroll is correct UX and is untouched.

## 3. Current behavior

`src/Spectacle/Render/Assets/preview-keynav.js`:

- `focusTarget(target, opts)` (line 129) always calls `target.scrollIntoView({ block: "nearest" })` unless `opts.preventScroll` is set. Of the callers — `move()`, `jumpFirst()`, `jumpLast()`, re-anchor cancel — none pass `preventScroll`, so every arrow press, Home/End, gg, G, and re-anchor-cancel triggers a scroll-into-view.
- `init()` (line 458) restores focus from `sessionStorage[STORAGE_FOCUS]` and then calls `scrollIntoView({ block: "nearest" })` on the restored target (line 469).

`src/Spectacle/Web/WebViewHost.xaml.cs` `SetHtml` (line 47) calls `Web.NavigateToString(html)` — a full navigation that resets the WebView's scroll to 0 on every action.

`sessionStorage` already holds `spectacle.focus`, `spectacle.helpOpen`, and `spectacle.reanchorLostOnRender`. There is no scroll-position entry.

## 4. Design

All changes are inside `src/Spectacle/Render/Assets/preview-keynav.js`. No host (WPF) changes. No new files. No new dependencies.

### 4.1 New constant

Add alongside the existing storage keys:

```js
var STORAGE_SCROLL = "spectacle.scrollY";
```

### 4.2 New helper: `isFullyOffscreen`

```js
function isFullyOffscreen(el) {
  var r = el.getBoundingClientRect();
  var vh = window.innerHeight || document.documentElement.clientHeight;
  return r.bottom <= 0 || r.top >= vh;
}
```

Definition of "fully offscreen": zero pixels of the element's bounding rect intersect the viewport on the vertical axis. The boundary cases — `r.bottom == 0` (entire element above the viewport, bottom edge touching the top) and `r.top == vh` (entire element below, top edge touching the bottom) — both yield zero visible pixels and are correctly treated as offscreen by the `<=` and `>=` comparisons. Any element with even one visible pixel returns `false`.

### 4.3 New scroll listener (debounced via `requestAnimationFrame`)

```js
var scrollSaveScheduled = false;
function onScroll() {
  if (scrollSaveScheduled) return;
  scrollSaveScheduled = true;
  requestAnimationFrame(function () {
    scrollSaveScheduled = false;
    try { sessionStorage.setItem(STORAGE_SCROLL, String(window.scrollY)); }
    catch (err) { /* ignore */ }
  });
}
```

Attached in `init()` with `window.addEventListener("scroll", onScroll, { passive: true })`. One sessionStorage write per animation frame, regardless of scroll velocity.

### 4.4 Modified `focusTarget(target, opts)`

Replace the unconditional `scrollIntoView` with the offscreen check; always pass `preventScroll: true` to `.focus()`:

```js
function focusTarget(target, opts) {
  if (!target) return;
  applyRoving(target);
  target.focus({ preventScroll: true });
  if (!(opts && opts.preventScroll) && isFullyOffscreen(target)) {
    target.scrollIntoView({ block: "nearest" });
  }
  savePointer(target);
}
```

The `opts.preventScroll` parameter is retained for callers that explicitly want no scroll under any condition (currently no such caller exists, but the contract stays open).

### 4.5 Modified `init()`

Replace the existing focus-restoration block:

```js
function init() {
  var all = focusables();

  var stored = loadPointer();
  var target = stored ? findTarget(stored) : null;
  if (!target && all.length > 0) target = all[0];

  if (target) {
    applyRoving(target);
    target.focus({ preventScroll: true });
  }

  // Restore scroll position from prior render, if any.
  var storedScroll = null;
  try { storedScroll = sessionStorage.getItem(STORAGE_SCROLL); }
  catch (err) { /* ignore */ }
  if (storedScroll !== null) {
    var y = parseFloat(storedScroll);
    if (isFinite(y)) window.scrollTo(0, y);
  } else if (target) {
    // First-ever load: keep prior behavior of revealing the initial focus.
    if (isFullyOffscreen(target)) target.scrollIntoView({ block: "nearest" });
  }

  window.addEventListener("scroll", onScroll, { passive: true });

  document.addEventListener("keydown", onKeyDown);
  syncPointerOnMouse();

  // Restore help overlay if it was open across the re-render.
  try {
    if (sessionStorage.getItem(STORAGE_HELP) === "1") openOverlay();
  } catch (err) { /* ignore */ }

  // If re-anchor was lost on render, flash a hint and clear the flag.
  try {
    if (sessionStorage.getItem(STORAGE_REANCHOR_LOST) === "1") {
      sessionStorage.removeItem(STORAGE_REANCHOR_LOST);
      flashHint("Re-anchor cancelled by document change");
    }
  } catch (err) { /* ignore */ }
}
```

Key points:

- Focus is restored with `preventScroll: true` — no implicit scroll during focus restoration.
- If `STORAGE_SCROLL` has a value, we `window.scrollTo(0, y)` to that position. The browser clamps `y` to the document's current `scrollHeight` automatically, so a shorter document after deletion is handled without special-case code.
- If `STORAGE_SCROLL` is absent (first load this session), preserve the existing behavior of revealing the initial focus, but only when needed (using `isFullyOffscreen`).

### 4.6 Data flow

```
user scrolls            → scroll listener (rAF) → sessionStorage[STORAGE_SCROLL]
user presses ↓ / Home   → move()/jump…() → focusTarget()
                          → focus(preventScroll)
                          → scrollIntoView only if isFullyOffscreen
user creates a comment  → host posts message → AnnotationStore updates
                          → host calls WebViewHost.SetHtml(newHtml)
                          → NavigateToString resets WebView scroll to 0
                          → keynav init() runs
                          → restores focus (preventScroll)
                          → restores scrollY from sessionStorage
```

Programmatic `window.scrollTo` in `init()` fires the scroll listener once. The rAF wrapper coalesces it into a no-op re-save of the same value. Harmless.

## 5. Trade-offs and known limits

- **Layout shift after re-render.** Adding or removing a card changes the document height between blocks. A stored `scrollY` of 800 may correspond to a slightly different visual location after a new card pushes content down by ~120 DIPs. The visual shift is bounded by one card's height and is acceptable for v1. Approach B (anchor + viewport-offset preservation) is the principled fix and is reserved as a v2 option.
- **No active focus indicator when arrowing offscreen.** Per §1's behavior rule and the user's "Recommended" choice during brainstorming, partial visibility counts as visible — so two consecutive `ArrowDown` presses across a tall card can leave the focus ring almost out of view before the third press finally triggers an auto-scroll. This is the deliberate point of "keep the position".
- **Re-anchor cancel.** After `Esc` cancels re-anchor mode, the existing code calls `focusTarget(focusables()[0])`. With the new conditional rule, that first block scrolls into view only if it is fully offscreen. If the user had scrolled far down before cancelling, the first block stays where it is in the document; their viewport position is preserved. Acceptable and consistent with the global rule.
- **sessionStorage quota.** A single string of `window.scrollY` (typically ≤ 10 characters) is trivially within quota even alongside the other stored entries.

## 6. Testing

No automated tests. The repo currently has no JS-level test harness for `preview-keynav.js`, and scroll behavior is inherently visual.

Verification path (per `CLAUDE.md`: "ready for IDE verification"):

1. `dotnet build` — must succeed clean.
2. Existing `Spectacle.Tests` suite — must still pass.
3. Manual via IDE launch of a sample `.md` with enough content to scroll and several existing revision requests:
   - Scroll halfway. Press `↓` repeatedly across visible blocks → viewport does **not** scroll until the new focus is fully below the visible area.
   - Press `End` from the top → viewport scrolls only enough to bring the last focusable into view (existing behavior; unchanged by the conditional check).
   - Scroll halfway, focus a mid-document block, press `Enter` to compose, save → after re-render, viewport scrollY matches pre-save value (±1 card-height tolerance).
   - On a focused comment card mid-document, press `r` (resolve) → after re-render, viewport preserved.
   - On a focused comment card mid-document, press `d` (delete) → after re-render, viewport preserved (or clamped if it would exceed the new shorter document).
   - Begin re-anchor on an orphan from far down, press `Esc` → viewport stays at its current scrollY; no jump to top.
   - Close and reopen Spectacle → first render lands at top with first focusable revealed (current first-run behavior).

## 7. Affected files

- `src/Spectacle/Render/Assets/preview-keynav.js` — add constant, helper, listener; modify `focusTarget` and `init` (~25 net lines added).

No other files, no new dependencies, no new public API, no host (WPF) changes.
