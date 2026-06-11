# Keynav Scroll Visibility Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keyboard navigation always scrolls the focused block into comfortable view (fully visible with ~2 lines of margin) instead of only when it is 100% offscreen.

**Architecture:** Let the browser own the geometry. `focusTarget` in `preview-keynav.js` calls `scrollIntoView({ block: "nearest" })` unconditionally (the old gate only fired at zero visible pixels, leaving sliver-visible blocks below the fold), and a CSS `scroll-margin` rule on the three focusable kinds gives every scroll 48px of breathing room. `init()`'s first-load reveal gets the same treatment; the then-unused `isFullyOffscreen` helper is deleted.

**Tech Stack:** Vanilla JS + CSS assets embedded by `PreviewHtml.Build` (C#/.NET 8, WebView2 host). Tests: xUnit + FluentAssertions, asserting on the built HTML string (existing pattern in `PreviewHtmlTests.cs`).

**Spec:** `docs/superpowers/specs/2026-06-11-keynav-scroll-visibility-design.md`

**Validation limits:** `dotnet build` and `dotnet test` work from the CLI. Do NOT `dotnet run` or `docker-compose up`. Behavioral verification happens in the IDE — final status is "ready for IDE verification", never "working".

---

## File Structure

- Modify: `src/Spectacle/Render/Assets/preview-keynav.css` — add scroll-margin rule (presentation concern lives with the other keynav styles)
- Modify: `src/Spectacle/Render/Assets/preview-keynav.js` — remove the offscreen gate in `focusTarget` and `init()`; delete `isFullyOffscreen`
- Modify: `test/Spectacle.Tests/PreviewHtmlTests.cs` — two new asset-content tests guarding the rule and the gate removal

---

### Task 1: CSS scroll-margin on focusable kinds

**Files:**
- Modify: `src/Spectacle/Render/Assets/preview-keynav.css` (after the "Per-kind nuance" / before the "Re-anchor mode" section)
- Test: `test/Spectacle.Tests/PreviewHtmlTests.cs`

- [ ] **Step 1: Write the failing test**

Add to the bottom of the `PreviewHtmlTests` class in `test/Spectacle.Tests/PreviewHtmlTests.cs`:

```csharp
[Fact]
public void Keynav_css_gives_focusables_scroll_margin()
{
    var html = PreviewHtml.Build("<p>hi</p>", "x", PreviewTheme.Dark);

    // One rule must cover all three focusable kinds so every keyboard-focus
    // scrollIntoView lands the target with breathing room, not flush at the edge.
    System.Text.RegularExpressions.Regex.IsMatch(html,
            @"\.md-block,\s*\.sp-card,\s*\.sp-orphan-row\s*\{[^}]*scroll-margin-top:\s*48px;[^}]*scroll-margin-bottom:\s*48px;")
        .Should().BeTrue("keynav CSS must give .md-block, .sp-card and .sp-orphan-row 48px scroll margins");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Spectacle.Tests/Spectacle.Tests.csproj --filter "FullyQualifiedName~Keynav_css_gives_focusables_scroll_margin"`
Expected: FAIL — "Expected ... to be true ... but found False" (rule does not exist yet).

- [ ] **Step 3: Add the CSS rule**

In `src/Spectacle/Render/Assets/preview-keynav.css`, insert between the "Per-kind nuance" section (ends with the `.sp-orphan-row:focus-visible` rule, line 31) and the `/* ---- Re-anchor mode ---- */` comment:

```css
/* ---- Scroll breathing room ---- */

/* scrollIntoView treats scroll-margin as part of the element's box, so a
   focused element lands ~2 body lines (16px x 1.6 x 2 = 51px ~ 48px) from the
   viewport edge instead of flush against it. */
.md-block,
.sp-card,
.sp-orphan-row {
  scroll-margin-top: 48px;
  scroll-margin-bottom: 48px;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Spectacle.Tests/Spectacle.Tests.csproj --filter "FullyQualifiedName~Keynav_css_gives_focusables_scroll_margin"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Spectacle/Render/Assets/preview-keynav.css test/Spectacle.Tests/PreviewHtmlTests.cs
git commit -m "feat(keynav): add 48px scroll-margin to focusable kinds"
```

---

### Task 2: Unconditional scrollIntoView on keyboard focus

**Files:**
- Modify: `src/Spectacle/Render/Assets/preview-keynav.js:129-136` (delete `isFullyOffscreen`), `:150-158` (`focusTarget`), `:497-503` (`init` first-load branch)
- Test: `test/Spectacle.Tests/PreviewHtmlTests.cs`

- [ ] **Step 1: Write the failing test**

Add to the bottom of the `PreviewHtmlTests` class:

```csharp
[Fact]
public void Keynav_js_scrolls_focus_target_unconditionally()
{
    var html = PreviewHtml.Build("<p>hi</p>", "x", PreviewTheme.Dark);

    // The zero-pixels-visible gate left sliver-visible blocks below the fold
    // (spec 2026-06-11). Focus must always scrollIntoView; the helper is gone.
    html.Should().NotContain("isFullyOffscreen");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Spectacle.Tests/Spectacle.Tests.csproj --filter "FullyQualifiedName~Keynav_js_scrolls_focus_target_unconditionally"`
Expected: FAIL — "Did not expect ... to contain \"isFullyOffscreen\"".

- [ ] **Step 3: Edit `preview-keynav.js`**

Three edits.

**3a.** Delete the `isFullyOffscreen` function and its comment (currently lines 129–136):

```js
  // Zero pixels of the element's bounding rect intersect the viewport on the
  // vertical axis. Boundary cases (bottom touching top, top touching bottom)
  // yield zero visible pixels and are treated as offscreen.
  function isFullyOffscreen(el) {
    var r = el.getBoundingClientRect();
    var vh = window.innerHeight || document.documentElement.clientHeight;
    return r.bottom <= 0 || r.top >= vh;
  }
```

**3b.** In `focusTarget`, drop the gate. Replace:

```js
    if (!(opts && opts.preventScroll) && isFullyOffscreen(target)) {
      target.scrollIntoView({ block: "nearest" });
    }
```

with:

```js
    if (!(opts && opts.preventScroll)) {
      target.scrollIntoView({ block: "nearest" });
    }
```

(`block: "nearest"` is a no-op when the target — including its scroll-margin — is already fully visible, so arrowing within view does not move the page.)

**3c.** In `init()`, make the first-ever-load reveal unconditional. Replace:

```js
    } else if (target && isFullyOffscreen(target)) {
      // First-ever load: keep prior behavior of revealing the initial focus.
      target.scrollIntoView({ block: "nearest" });
    }
```

with:

```js
    } else if (target) {
      // First-ever load: reveal the initial focus.
      target.scrollIntoView({ block: "nearest" });
    }
```

Do NOT touch `syncPointerOnMouse` — mouse clicks must keep `focus({ preventScroll: true })` with no `scrollIntoView`. Do NOT touch the stored-scroll restore branch in `init()`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Spectacle.Tests/Spectacle.Tests.csproj --filter "FullyQualifiedName~Keynav_js_scrolls_focus_target_unconditionally"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Spectacle/Render/Assets/preview-keynav.js test/Spectacle.Tests/PreviewHtmlTests.cs
git commit -m "fix(keynav): always scroll keyboard focus target into view"
```

---

### Task 3: Full validation

**Files:** none new.

- [ ] **Step 1: Build the solution**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Run the full test suite**

Run: `dotnet test`
Expected: all tests PASS (including all pre-existing `PreviewHtmlTests`).

- [ ] **Step 3: Report status**

Report **"ready for IDE verification"** with this manual checklist (run the app from the IDE on a long markdown document):

1. Arrow (↓) from top to bottom — every focused block ends fully visible with ~48px margin from the viewport edge; no step leaves the focused block below the fold.
2. `G` / `End` — last block lands fully in view.
3. `gg` / `Home` — first block lands fully in view.
4. Arrowing among blocks already in mid-viewport — page does not move.
5. Mouse-click a block near the viewport edge — no scroll occurs.
6. Edit the file externally to trigger a re-render — scroll position is restored exactly (stored-scroll branch untouched).
7. Re-anchor mode (`a` on an orphan, then ↑/↓) — move targets scroll into view the same way.
