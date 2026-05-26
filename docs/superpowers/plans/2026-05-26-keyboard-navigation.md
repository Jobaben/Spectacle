# Keyboard Navigation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every interactive feature of Spectacle operable without a mouse — block/comment/orphan focus with arrow keys, contextual single-letter hotkeys, a `?` help overlay, and focus that survives re-renders. Mouse behavior is unchanged.

**Architecture:** One new JS module (`preview-keynav.js`) owns a roving tabindex across `.md-block`, `.sp-card`, and `.sp-orphan-row` elements inside the WebView2. It dispatches keys to actions based on which class the focused element has. A single `KEYMAP` constant feeds both the dispatcher and the help overlay (single source of truth). Existing mouse handlers in `preview-annotations.js` stay; keynav calls into them by `.click()`-ing the same buttons. WPF `Window.InputBindings` keep their existing 8 shortcuts untouched.

**Tech Stack:** Vanilla JS (ES5 — matches the existing `preview-annotations.js` style), CSS, .NET 8 / WPF for embedding. xUnit + FluentAssertions for the small set of C# tests (asset injection). Manual WebView2 smoke pass per the spec checklist for behavior.

**Spec:** `docs/superpowers/specs/2026-05-26-keyboard-navigation-design.md`

---

## File Structure

- **NEW:** `src/Spectacle/Render/Assets/preview-keynav.js` — focus controller. Holds `KEYMAP`, roving tabindex, key dispatcher, overlay, sessionStorage persistence, status hint toast.
- **NEW:** `src/Spectacle/Render/Assets/preview-keynav.css` — focus rings, overlay styles, hint toast, high-contrast overrides.
- **MODIFY:** `src/Spectacle/Render/Assets/preview-annotations.js` — add `sp-orphan-row` class + `data-comment-id` to each orphan `<li>`; remove the inline `Enter`-on-block handler (now owned by keynav); expose `window.__sp_startCompose` so keynav can call the existing composer.
- **MODIFY:** `src/Spectacle/Render/Assets/preview-annotations.css` — delete the existing `.md-block:focus-visible` rule (now owned by keynav; spec uses different geometry).
- **MODIFY:** `src/Spectacle/Render/PreviewHtml.cs` — load the two new assets and inject them in the correct order (annotations CSS before keynav CSS in `<head>`; annotations JS before keynav JS at end of `<body>`).
- **MODIFY:** `src/Spectacle/Spectacle.csproj` — register the two new assets as `EmbeddedResource`.
- **MODIFY:** `test/Spectacle.Tests/PreviewHtmlTests.cs` — assert keynav CSS and JS are injected and in the right order.
- **MODIFY:** `README.md` — extend the **Keyboard** section with the new bindings.

---

## Task 1: Scaffold assets and injection plumbing

**Files:**
- Create: `src/Spectacle/Render/Assets/preview-keynav.js`
- Create: `src/Spectacle/Render/Assets/preview-keynav.css`
- Modify: `src/Spectacle/Spectacle.csproj`
- Modify: `src/Spectacle/Render/PreviewHtml.cs`
- Modify: `test/Spectacle.Tests/PreviewHtmlTests.cs`

- [ ] **Step 1: Create the empty JS module**

Create `src/Spectacle/Render/Assets/preview-keynav.js` with:

```javascript
(function () {
  "use strict";
  // preview-keynav.js — keyboard focus controller for Spectacle.
  // Tasks 2-11 of docs/superpowers/plans/2026-05-26-keyboard-navigation.md
  // populate this module incrementally. Empty in Task 1 so the injection
  // plumbing can be verified independently.
})();
```

- [ ] **Step 2: Create the empty CSS module**

Create `src/Spectacle/Render/Assets/preview-keynav.css` with:

```css
/* preview-keynav.css — focus indicators, overlay, hint toast.
   Tasks 10-11 populate this. Empty in Task 1. */
```

- [ ] **Step 3: Register both assets as embedded resources**

In `src/Spectacle/Spectacle.csproj`, the existing `ItemGroup` for embedded resources (lines 19-27) currently lists 7 assets. Add the two new ones at the end of that group, so the block becomes:

```xml
  <ItemGroup>
    <EmbeddedResource Include="Render\Assets\preview.css" />
    <EmbeddedResource Include="Render\Assets\dark.css" />
    <EmbeddedResource Include="Render\Assets\hc.css" />
    <EmbeddedResource Include="Render\Assets\prism.min.js" />
    <EmbeddedResource Include="Render\Assets\prism.css" />
    <EmbeddedResource Include="Render\Assets\preview-annotations.css" />
    <EmbeddedResource Include="Render\Assets\preview-annotations.js" />
    <EmbeddedResource Include="Render\Assets\preview-keynav.css" />
    <EmbeddedResource Include="Render\Assets\preview-keynav.js" />
  </ItemGroup>
```

- [ ] **Step 4: Write the failing injection tests**

In `test/Spectacle.Tests/PreviewHtmlTests.cs`, append these three new tests at the end of the class (before the final `}`):

```csharp
[Fact]
public void Build_embeds_keynav_css_after_annotations_css()
{
    var matched = new Spectacle.Annotations.MatchResult(
        System.Array.Empty<Spectacle.Annotations.MatchedComment>(),
        System.Array.Empty<Spectacle.Annotations.Comment>());
    var html = PreviewHtml.Build("", "x", PreviewTheme.Dark, matched);

    var annotationsCssMarker = html.IndexOf(".sp-composer");
    var keynavCssMarker = html.IndexOf("preview-keynav.css — focus indicators");

    annotationsCssMarker.Should().BeGreaterThan(0, "annotations CSS must still be embedded");
    keynavCssMarker.Should().BeGreaterThan(0, "keynav CSS must be embedded");
    keynavCssMarker.Should().BeGreaterThan(annotationsCssMarker,
        "keynav CSS must appear after annotations CSS so its rules win on conflict");
}

[Fact]
public void Build_embeds_keynav_js_after_annotations_js()
{
    var matched = new Spectacle.Annotations.MatchResult(
        System.Array.Empty<Spectacle.Annotations.MatchedComment>(),
        System.Array.Empty<Spectacle.Annotations.Comment>());
    var html = PreviewHtml.Build("", "x", PreviewTheme.Dark, matched);

    var annotationsJsMarker = html.IndexOf("__spectacleAnnotations__");
    var keynavJsMarker = html.IndexOf("preview-keynav.js — keyboard focus controller");

    annotationsJsMarker.Should().BeGreaterThan(0, "annotations JS payload must be present");
    keynavJsMarker.Should().BeGreaterThan(0, "keynav JS must be embedded");
    keynavJsMarker.Should().BeGreaterThan(annotationsJsMarker,
        "keynav JS must load after annotations JS so it can call into __sp_* helpers");
}

[Fact]
public void Build_without_match_result_still_includes_keynav()
{
    // Even without comments, keynav must be present so block navigation
    // works on plain documents.
    var html = PreviewHtml.Build("<p>hi</p>", "x", PreviewTheme.Dark);

    html.Should().Contain("preview-keynav.js — keyboard focus controller");
    html.Should().Contain("preview-keynav.css — focus indicators");
}
```

- [ ] **Step 5: Run the new tests to verify they fail**

Run:

```bash
dotnet test test/Spectacle.Tests/Spectacle.Tests.csproj --filter "FullyQualifiedName~PreviewHtmlTests"
```

Expected: the three new tests fail with assertion errors (markers not found / indices ≤ 0). All previously-passing tests still pass.

- [ ] **Step 6: Wire the new assets into `PreviewHtml`**

In `src/Spectacle/Render/PreviewHtml.cs`, after the existing `AnnotationsJs` field (line 20), add two more `Lazy<string>` fields:

```csharp
    private static readonly Lazy<string> KeynavCss = new(() => LoadAsset("preview-keynav.css"));
    private static readonly Lazy<string> KeynavJs = new(() => LoadAsset("preview-keynav.js"));
```

Then in the `Build` method's HTML template (currently lines 36-57), update the `<head>` `<style>` block and the end-of-`<body>` `<script>` block so the new asset is appended in each. The full replacement template is:

```csharp
        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width,initial-scale=1" />
              <base href="{{baseHref}}" />
              <style>{{themeCss}}</style>
              <style>{{PreviewCss.Value}}</style>
              <style>{{PrismCss.Value}}</style>
              <style>{{AnnotationsCss.Value}}</style>
              <style>{{KeynavCss.Value}}</style>
            </head>
            <body>
              <main role="main">
            {{bodyHtml}}
              </main>
              <script>{{PrismJs.Value}}</script>
              <script>window.__spectacleAnnotations__ = {{payloadJson}};</script>
              <script>{{AnnotationsJs.Value}}</script>
              <script>{{KeynavJs.Value}}</script>
            </body>
            </html>
            """;
```

- [ ] **Step 7: Run the tests to verify they pass**

Run:

```bash
dotnet build src/Spectacle/Spectacle.csproj
dotnet test test/Spectacle.Tests/Spectacle.Tests.csproj
```

Expected: build succeeds with no warnings; all tests pass, including the three new ones.

- [ ] **Step 8: Commit**

```bash
git add src/Spectacle/Render/Assets/preview-keynav.js src/Spectacle/Render/Assets/preview-keynav.css src/Spectacle/Spectacle.csproj src/Spectacle/Render/PreviewHtml.cs test/Spectacle.Tests/PreviewHtmlTests.cs
git commit -m "feat(render): scaffold preview-keynav assets and injection"
```

---

## Task 2: Define the `KEYMAP` single source of truth

**Files:**
- Modify: `src/Spectacle/Render/Assets/preview-keynav.js`
- Modify: `test/Spectacle.Tests/PreviewHtmlTests.cs`

KEYMAP is a per-context table of `{ key, label, action }` rows. The dispatcher (Task 3+) reads `action` to call functions; the overlay (Task 8) reads `key` and `label` to render rows. Both pull from the same array so they cannot drift.

- [ ] **Step 1: Write the failing structure test**

In `test/Spectacle.Tests/PreviewHtmlTests.cs`, append this test:

```csharp
[Fact]
public void Keynav_js_declares_single_keymap_constant()
{
    var html = PreviewHtml.Build("<p>hi</p>", "x", PreviewTheme.Dark);

    // KEYMAP is the single source of truth; it must be declared exactly once
    // (the dispatcher and the overlay both read from it).
    var occurrences = System.Text.RegularExpressions.Regex
        .Matches(html, @"\bvar KEYMAP\s*=\s*\{").Count;

    occurrences.Should().Be(1, "KEYMAP must be declared exactly once in preview-keynav.js");
    html.Should().Contain("preview-wide");
    html.Should().Contain("on-block");
    html.Should().Contain("on-card");
    html.Should().Contain("on-orphan");
    html.Should().Contain("in-composer");
    html.Should().Contain("in-reanchor");
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test test/Spectacle.Tests/Spectacle.Tests.csproj --filter "Keynav_js_declares_single_keymap_constant"
```

Expected: FAIL — KEYMAP not yet present.

- [ ] **Step 3: Add KEYMAP to preview-keynav.js**

Replace the contents of `src/Spectacle/Render/Assets/preview-keynav.js` with:

```javascript
(function () {
  "use strict";
  // preview-keynav.js — keyboard focus controller for Spectacle.

  // KEYMAP is the single source of truth for keyboard bindings.
  // - dispatcher (Task 3+) reads `action` to route keypresses
  // - overlay (Task 8) reads `key` and `label` to render rows
  // Sections correspond to the focus contexts in the design spec.
  var KEYMAP = {
    "global": {
      title: "Global",
      rows: [
        { key: "Ctrl+R / F5",         label: "Reload from disk",           action: null /* WPF */ },
        { key: "Ctrl+= / - / 0",      label: "Zoom in / out / reset",      action: null /* WPF */ },
        { key: "F11",                 label: "Toggle fullscreen",          action: null /* WPF */ },
        { key: "Esc",                 label: "Close window (when idle)",   action: null /* WPF */ },
        { key: "Ctrl+Shift+C",        label: "Copy revision plan",         action: null /* WPF */ },
        { key: "Ctrl+Shift+E",        label: "Export revision plan…",      action: null /* WPF */ }
      ]
    },
    "preview-wide": {
      title: "Preview-wide",
      rows: [
        { key: "?",                   label: "Open this help",             action: "help.toggle" },
        { key: "gg",                  label: "Jump to first",              action: "nav.first" },
        { key: "G",                   label: "Jump to last",               action: "nav.last" }
      ]
    },
    "on-block": {
      title: "On block",
      rows: [
        { key: "↑ / ↓",               label: "Previous / next focusable",  action: "nav.prevnext" },
        { key: "Home / End",          label: "First / last focusable",     action: "nav.firstlast" },
        { key: "Enter or c",          label: "Add comment on this block",  action: "block.compose" }
      ]
    },
    "on-card": {
      title: "On comment",
      rows: [
        { key: "↑ / ↓ / Home / End",  label: "Navigation",                 action: "nav.move" },
        { key: "e",                   label: "Edit comment",               action: "card.edit" },
        { key: "r",                   label: "Resolve / reopen",           action: "card.resolve" },
        { key: "d",                   label: "Delete comment",             action: "card.delete" }
      ]
    },
    "on-orphan": {
      title: "On orphan",
      rows: [
        { key: "↑ / ↓ / Home / End",  label: "Navigation",                 action: "nav.move" },
        { key: "d",                   label: "Delete orphan",              action: "orphan.delete" },
        { key: "a",                   label: "Begin re-anchor",            action: "orphan.reanchor" }
      ]
    },
    "in-composer": {
      title: "In composer",
      rows: [
        { key: "Esc",                 label: "Cancel",                     action: null /* in preview-annotations.js */ },
        { key: "Ctrl+Enter",          label: "Save",                       action: null /* in preview-annotations.js */ }
      ]
    },
    "in-reanchor": {
      title: "Re-anchor mode",
      rows: [
        { key: "↑ / ↓",               label: "Move target",                action: "reanchor.move" },
        { key: "Enter",               label: "Confirm target",             action: "reanchor.confirm" },
        { key: "Esc",                 label: "Cancel",                     action: "reanchor.cancel" }
      ]
    }
  };

  // Subsequent tasks attach KEYMAP-driven dispatcher and overlay to window/document.
  // Stored here so subsequent tasks can `require`-style pull it via closure.
  window.__sp_keymap = KEYMAP;
})();
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
dotnet build src/Spectacle/Spectacle.csproj
dotnet test test/Spectacle.Tests/Spectacle.Tests.csproj --filter "Keynav_js_declares_single_keymap_constant"
```

Expected: PASS. All other tests still pass.

- [ ] **Step 5: Commit**

```bash
git add src/Spectacle/Render/Assets/preview-keynav.js test/Spectacle.Tests/PreviewHtmlTests.cs
git commit -m "feat(render): KEYMAP single source of truth for keyboard bindings"
```

---

## Task 3: Roving tabindex and arrow navigation

This task introduces the focus pointer over `.md-block` and `.sp-card`. Orphan rows are added in Task 6. The dispatcher only handles navigation keys here (ArrowUp/Down, Home, End, gg, G, click-to-focus); per-context action keys come in Tasks 4-7.

This task also **removes the duplicate `.md-block:focus-visible` rule** from `preview-annotations.css` and the **inline Enter handler** from `preview-annotations.js`, both of which are owned by keynav from now on.

**Files:**
- Modify: `src/Spectacle/Render/Assets/preview-keynav.js`
- Modify: `src/Spectacle/Render/Assets/preview-keynav.css`
- Modify: `src/Spectacle/Render/Assets/preview-annotations.css`
- Modify: `src/Spectacle/Render/Assets/preview-annotations.js`

- [ ] **Step 1: Add a default focus ring to keynav.css**

Replace the contents of `src/Spectacle/Render/Assets/preview-keynav.css` with:

```css
/* preview-keynav.css — focus indicators, overlay, hint toast.
   Geometry per docs/superpowers/specs/2026-05-26-keyboard-navigation-design.md §7. */

.md-block:focus-visible,
.sp-card:focus-visible,
.sp-orphan-row:focus-visible {
  outline: 2px solid #4ea1ff;
  outline-offset: 2px;
  border-radius: 4px;
}
```

(Per-kind nuance and high-contrast variants come in Task 10.)

- [ ] **Step 2: Remove the conflicting block-focus rule from preview-annotations.css**

In `src/Spectacle/Render/Assets/preview-annotations.css`, delete the existing rule at lines 9-14:

```css
.md-block:focus-visible {
  border-left-color: var(--focus, #7cb7ff);
  outline: 2px solid var(--focus, #7cb7ff);
  outline-offset: 2px;
  border-radius: 2px;
}
```

(The keynav.css rule supersedes it. The `.md-block` declaration above it — `border-left`, `padding-left`, etc. — stays.)

- [ ] **Step 3: Remove the inline Enter-on-block handler from preview-annotations.js**

In `src/Spectacle/Render/Assets/preview-annotations.js`, inside `wireBlockClicks` (lines 220-242), delete the `keydown` listener block. The full function becomes:

```javascript
  function wireBlockClicks() {
    var blocks = document.querySelectorAll(".md-block");
    var downAt = null;
    blocks.forEach(function (b) {
      b.addEventListener("mousedown", function (e) { downAt = { x: e.clientX, y: e.clientY }; });
      b.addEventListener("mouseup", function (e) {
        if (!downAt) return;
        var dx = e.clientX - downAt.x, dy = e.clientY - downAt.y;
        var moved = Math.sqrt(dx * dx + dy * dy);
        downAt = null;
        if (moved > 4) return;
        if (window.getSelection && String(window.getSelection())) return;
        if (document.body.classList.contains("sp-reanchor-mode")) return;
        startCompose(b.getAttribute("data-block-id"), null);
      });
    });
  }
```

(The Enter behavior moves to keynav in Task 4.)

- [ ] **Step 4: Replace preview-keynav.js with the navigation implementation**

Replace the contents of `src/Spectacle/Render/Assets/preview-keynav.js` with the following. The KEYMAP block from Task 2 is preserved verbatim at the top; new code follows it.

```javascript
(function () {
  "use strict";
  // preview-keynav.js — keyboard focus controller for Spectacle.

  var KEYMAP = {
    "global": {
      title: "Global",
      rows: [
        { key: "Ctrl+R / F5",         label: "Reload from disk",           action: null /* WPF */ },
        { key: "Ctrl+= / - / 0",      label: "Zoom in / out / reset",      action: null /* WPF */ },
        { key: "F11",                 label: "Toggle fullscreen",          action: null /* WPF */ },
        { key: "Esc",                 label: "Close window (when idle)",   action: null /* WPF */ },
        { key: "Ctrl+Shift+C",        label: "Copy revision plan",         action: null /* WPF */ },
        { key: "Ctrl+Shift+E",        label: "Export revision plan…",      action: null /* WPF */ }
      ]
    },
    "preview-wide": {
      title: "Preview-wide",
      rows: [
        { key: "?",                   label: "Open this help",             action: "help.toggle" },
        { key: "gg",                  label: "Jump to first",              action: "nav.first" },
        { key: "G",                   label: "Jump to last",               action: "nav.last" }
      ]
    },
    "on-block": {
      title: "On block",
      rows: [
        { key: "↑ / ↓",               label: "Previous / next focusable",  action: "nav.prevnext" },
        { key: "Home / End",          label: "First / last focusable",     action: "nav.firstlast" },
        { key: "Enter or c",          label: "Add comment on this block",  action: "block.compose" }
      ]
    },
    "on-card": {
      title: "On comment",
      rows: [
        { key: "↑ / ↓ / Home / End",  label: "Navigation",                 action: "nav.move" },
        { key: "e",                   label: "Edit comment",               action: "card.edit" },
        { key: "r",                   label: "Resolve / reopen",           action: "card.resolve" },
        { key: "d",                   label: "Delete comment",             action: "card.delete" }
      ]
    },
    "on-orphan": {
      title: "On orphan",
      rows: [
        { key: "↑ / ↓ / Home / End",  label: "Navigation",                 action: "nav.move" },
        { key: "d",                   label: "Delete orphan",              action: "orphan.delete" },
        { key: "a",                   label: "Begin re-anchor",            action: "orphan.reanchor" }
      ]
    },
    "in-composer": {
      title: "In composer",
      rows: [
        { key: "Esc",                 label: "Cancel",                     action: null /* in preview-annotations.js */ },
        { key: "Ctrl+Enter",          label: "Save",                       action: null /* in preview-annotations.js */ }
      ]
    },
    "in-reanchor": {
      title: "Re-anchor mode",
      rows: [
        { key: "↑ / ↓",               label: "Move target",                action: "reanchor.move" },
        { key: "Enter",               label: "Confirm target",             action: "reanchor.confirm" },
        { key: "Esc",                 label: "Cancel",                     action: "reanchor.cancel" }
      ]
    }
  };
  window.__sp_keymap = KEYMAP;

  // -------- Focusables --------

  // DOM-order list of focusable elements. Recomputed when the layout might have
  // changed (we never DOM-mutate independently — preview-annotations.js owns
  // insertions and only does so before init or as a result of a re-render
  // dispatched from the host). Composer + re-anchor mode adjust the list at
  // request time.
  function focusables() {
    var selector = ".md-block, .sp-card";
    return Array.prototype.slice.call(document.querySelectorAll(selector));
  }

  function kindOf(el) {
    if (!el) return null;
    if (el.classList.contains("md-block")) return "block";
    if (el.classList.contains("sp-card")) return "card";
    if (el.classList.contains("sp-orphan-row")) return "orphan";
    return null;
  }

  // -------- Roving tabindex --------

  function applyRoving(target) {
    var all = focusables();
    all.forEach(function (el) {
      el.setAttribute("tabindex", el === target ? "0" : "-1");
    });
  }

  function focusTarget(target, opts) {
    if (!target) return;
    applyRoving(target);
    target.focus({ preventScroll: !!(opts && opts.preventScroll) });
    if (!opts || !opts.preventScroll) {
      target.scrollIntoView({ block: "nearest" });
    }
  }

  function currentFocus() {
    var active = document.activeElement;
    if (active && kindOf(active)) return active;
    var first = focusables()[0];
    return first || null;
  }

  function move(delta) {
    var all = focusables();
    if (all.length === 0) return;
    var idx = all.indexOf(currentFocus());
    if (idx === -1) idx = 0;
    else idx = Math.max(0, Math.min(all.length - 1, idx + delta));
    focusTarget(all[idx]);
  }

  function jumpFirst() {
    var all = focusables();
    if (all[0]) focusTarget(all[0]);
  }

  function jumpLast() {
    var all = focusables();
    if (all.length) focusTarget(all[all.length - 1]);
  }

  // -------- Click → pointer sync --------

  function syncPointerOnMouse() {
    document.addEventListener("mousedown", function (e) {
      var el = e.target.closest && e.target.closest(".md-block, .sp-card, .sp-orphan-row");
      if (!el) return;
      // Don't steal scroll position from native mouse interaction.
      applyRoving(el);
      // Programmatic focus from a real mouse interaction does NOT trigger
      // `:focus-visible` (browser heuristic), so no ring is painted.
      el.focus({ preventScroll: true });
    }, true);
  }

  // -------- Key dispatcher --------

  // gg-sequence state: 'g' press sets gPending=true for 1.5s.
  var gPending = false;
  var gTimer = null;

  function armG() {
    gPending = true;
    if (gTimer) clearTimeout(gTimer);
    gTimer = setTimeout(function () { gPending = false; }, 1500);
  }
  function disarmG() {
    gPending = false;
    if (gTimer) { clearTimeout(gTimer); gTimer = null; }
  }

  function onKeyDown(e) {
    // Composer textarea owns its own keys (Esc, Ctrl+Enter).
    if (e.target && e.target.tagName === "TEXTAREA") return;

    // Re-anchor mode and the help overlay are handled by their owners
    // (Tasks 7 and 8). For now, only navigation keys are dispatched here.

    if (e.key === "ArrowDown") { e.preventDefault(); disarmG(); move(+1); return; }
    if (e.key === "ArrowUp")   { e.preventDefault(); disarmG(); move(-1); return; }
    if (e.key === "Home")      { e.preventDefault(); disarmG(); jumpFirst(); return; }
    if (e.key === "End")       { e.preventDefault(); disarmG(); jumpLast(); return; }
    if (e.key === "G")         { e.preventDefault(); disarmG(); jumpLast(); return; }
    if (e.key === "g") {
      if (gPending) { e.preventDefault(); disarmG(); jumpFirst(); return; }
      armG();
      return;
    }
    // Other keys: bail (no preventDefault), so the browser still does its thing
    // and Task 4+ can layer on per-kind actions without conflict.
  }

  // -------- Init --------

  function init() {
    var all = focusables();
    if (all.length === 0) return;
    applyRoving(all[0]);
    document.addEventListener("keydown", onKeyDown);
    syncPointerOnMouse();
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", init);
  } else {
    init();
  }
})();
```

- [ ] **Step 5: Build and smoke-test**

Run:

```bash
dotnet build src/Spectacle/Spectacle.csproj
dotnet test test/Spectacle.Tests/Spectacle.Tests.csproj
```

Expected: build succeeds, all tests pass (no test regressions; the focus-ring CSS change does not break any existing assertion).

**Manual smoke** (open `test/Spectacle.Tests/Fixtures/revision-plan-3-comments.md` in Spectacle):

- On open, the first `.md-block` shows a blue focus ring.
- `↓` advances to the next focusable (block, then comment card, then block, etc.). Ring follows.
- `↑` reverses.
- `Home` goes to the first focusable; `End` to the last.
- `gg` jumps to first; `G` jumps to last.
- Click a block with the mouse — the ring does NOT appear (focus, but `:focus-visible` doesn't match for mouse). The next `↓` then advances from the clicked block.
- Mouse-clicking a block still opens the composer as before.
- Esc still closes the window (the existing WPF binding fires; keynav doesn't preventDefault).

- [ ] **Step 6: Commit**

```bash
git add src/Spectacle/Render/Assets/preview-keynav.js src/Spectacle/Render/Assets/preview-keynav.css src/Spectacle/Render/Assets/preview-annotations.css src/Spectacle/Render/Assets/preview-annotations.js
git commit -m "feat(render): roving tabindex and arrow navigation in preview-keynav"
```

---

## Task 4: On-block action — Enter and c open the composer

**Files:**
- Modify: `src/Spectacle/Render/Assets/preview-annotations.js`
- Modify: `src/Spectacle/Render/Assets/preview-keynav.js`

The existing `startCompose(blockId, existing)` function in `preview-annotations.js` already does what we need — open a composer attached to a block. We expose it on `window.__sp_startCompose` so keynav can call it without duplication.

- [ ] **Step 1: Expose `startCompose` on `window.__sp_startCompose`**

In `src/Spectacle/Render/Assets/preview-annotations.js`, immediately AFTER the `function startCompose(...)` definition (it currently ends at line 140 with the closing brace + insertion line), add:

```javascript
  // Exposed for preview-keynav.js. Stable API: (blockId, existingCommentOrNull).
  window.__sp_startCompose = startCompose;
```

Place the line so it sits just before the `function renderExistingComments()` definition.

- [ ] **Step 2: Dispatch Enter and `c` on a focused block**

In `src/Spectacle/Render/Assets/preview-keynav.js`, replace the `onKeyDown` function with:

```javascript
  function onKeyDown(e) {
    // Composer textarea owns its own keys (Esc, Ctrl+Enter).
    if (e.target && e.target.tagName === "TEXTAREA") return;

    if (e.key === "ArrowDown") { e.preventDefault(); disarmG(); move(+1); return; }
    if (e.key === "ArrowUp")   { e.preventDefault(); disarmG(); move(-1); return; }
    if (e.key === "Home")      { e.preventDefault(); disarmG(); jumpFirst(); return; }
    if (e.key === "End")       { e.preventDefault(); disarmG(); jumpLast(); return; }
    if (e.key === "G")         { e.preventDefault(); disarmG(); jumpLast(); return; }
    if (e.key === "g") {
      if (gPending) { e.preventDefault(); disarmG(); jumpFirst(); return; }
      armG();
      return;
    }

    disarmG();

    var focused = currentFocus();
    var kind = kindOf(focused);

    if (kind === "block") {
      if (e.key === "Enter" || e.key === "c") {
        e.preventDefault();
        var blockId = focused.getAttribute("data-block-id");
        if (blockId && window.__sp_startCompose) {
          window.__sp_startCompose(blockId, null);
        }
        return;
      }
    }
  }
```

- [ ] **Step 3: Build and smoke-test**

```bash
dotnet build src/Spectacle/Spectacle.csproj
dotnet test test/Spectacle.Tests/Spectacle.Tests.csproj
```

Expected: build succeeds, all tests pass.

**Manual smoke** (same fixture as before):

- Focus a block via arrows. Press `Enter` — composer appears under that block.
- Esc inside the composer cancels (existing behavior).
- Ctrl+Enter saves (existing behavior).
- Focus another block via arrows. Press `c` — composer appears under it.
- Type `g` when on a block — no composer; sets `gg` pending. Press `g` again — jumps to first.
- Mouse-clicking a block still opens a composer (existing path untouched).

- [ ] **Step 4: Commit**

```bash
git add src/Spectacle/Render/Assets/preview-annotations.js src/Spectacle/Render/Assets/preview-keynav.js
git commit -m "feat(render): Enter / c open composer on focused block"
```

---

## Task 5: On-card actions — e edit, r resolve/reopen, d delete

**Files:**
- Modify: `src/Spectacle/Render/Assets/preview-annotations.js`
- Modify: `src/Spectacle/Render/Assets/preview-keynav.js`

The existing card buttons already have click handlers (`preview-annotations.js:55-77`). Rather than duplicate the logic, keynav locates the relevant button by text and calls `.click()` on it. This keeps a single behavior path.

For brittleness reasons we **tag the buttons** with data attributes instead of matching by text, so future label changes (e.g., localization) don't break keynav.

- [ ] **Step 1: Tag the card buttons**

In `src/Spectacle/Render/Assets/preview-annotations.js`, inside `buildCard` (currently lines 58-77), update each of the three button creations to add a `data-sp-action` attribute. The full updated section is:

```javascript
    var editBtn = document.createElement("button");
    editBtn.textContent = "Edit";
    editBtn.setAttribute("data-sp-action", "edit");
    editBtn.addEventListener("click", function () { startCompose(comment.blockAnchor.blockIdAtRender, comment); });

    var resolveBtn = document.createElement("button");
    resolveBtn.textContent = comment.resolvedAt ? "Reopen" : "Resolve";
    resolveBtn.setAttribute("data-sp-action", "resolve");
    resolveBtn.addEventListener("click", function () {
      post("commentResolve", { commentId: comment.id, resolved: !comment.resolvedAt });
    });

    var deleteBtn = document.createElement("button");
    deleteBtn.textContent = "Delete";
    deleteBtn.setAttribute("data-sp-action", "delete");
    deleteBtn.addEventListener("click", function () {
      post("commentDelete", { commentId: comment.id });
    });
```

Also annotate the card itself so keynav can find the comment id. The card is built at the top of `buildCard`; update that block so the card carries `data-resolved` reflecting state (used by Task 10's per-kind CSS). The card creation (currently lines 30-34) becomes:

```javascript
    var card = document.createElement("article");
    card.className = "sp-card";
    if (comment.resolvedAt) card.className += " sp-resolved";
    card.setAttribute("role", "comment");
    card.setAttribute("data-comment-id", comment.id);
    card.setAttribute("data-resolved", comment.resolvedAt ? "true" : "false");
    card.setAttribute("aria-label",
      "Revision request " + index + " on " +
      comment.blockAnchor.kind + " at line " + comment.blockAnchor.line);
```

- [ ] **Step 2: Dispatch e / r / d when focused on a card**

In `src/Spectacle/Render/Assets/preview-keynav.js`, extend `onKeyDown` so that — after the navigation block and after the `block` handler — it also handles `card`. Replace `onKeyDown` with:

```javascript
  function onKeyDown(e) {
    if (e.target && e.target.tagName === "TEXTAREA") return;

    if (e.key === "ArrowDown") { e.preventDefault(); disarmG(); move(+1); return; }
    if (e.key === "ArrowUp")   { e.preventDefault(); disarmG(); move(-1); return; }
    if (e.key === "Home")      { e.preventDefault(); disarmG(); jumpFirst(); return; }
    if (e.key === "End")       { e.preventDefault(); disarmG(); jumpLast(); return; }
    if (e.key === "G")         { e.preventDefault(); disarmG(); jumpLast(); return; }
    if (e.key === "g") {
      if (gPending) { e.preventDefault(); disarmG(); jumpFirst(); return; }
      armG();
      return;
    }

    disarmG();

    var focused = currentFocus();
    var kind = kindOf(focused);

    if (kind === "block") {
      if (e.key === "Enter" || e.key === "c") {
        e.preventDefault();
        var blockId = focused.getAttribute("data-block-id");
        if (blockId && window.__sp_startCompose) {
          window.__sp_startCompose(blockId, null);
        }
      }
      return;
    }

    if (kind === "card") {
      if (e.key === "e") {
        e.preventDefault();
        clickAction(focused, "edit");
      } else if (e.key === "r") {
        e.preventDefault();
        clickAction(focused, "resolve");
      } else if (e.key === "d") {
        e.preventDefault();
        clickAction(focused, "delete");
      }
      return;
    }
  }

  function clickAction(container, action) {
    var btn = container.querySelector('button[data-sp-action="' + action + '"]');
    if (btn) btn.click();
  }
```

- [ ] **Step 3: Build and smoke-test**

```bash
dotnet build src/Spectacle/Spectacle.csproj
dotnet test test/Spectacle.Tests/Spectacle.Tests.csproj
```

Expected: build and tests pass.

**Manual smoke** (open the 3-comments fixture):

- Arrow-navigate to a comment card. Ring appears around it.
- Press `e` — composer opens pre-filled with that comment's body.
- Cancel; arrow back to the card. Press `r` — card becomes muted (resolved). Press `r` again — back to normal.
- Press `d` — card disappears; focus falls back to nearest focusable.
- All three button clicks still work via mouse.

- [ ] **Step 4: Commit**

```bash
git add src/Spectacle/Render/Assets/preview-annotations.js src/Spectacle/Render/Assets/preview-keynav.js
git commit -m "feat(render): e/r/d hotkeys on focused comment card"
```

---

## Task 6: Orphan rows — class, focus, and on-orphan actions

The orphan list is currently rendered as bare `<li>` elements (`preview-annotations.js:181-188`). We tag each row with class `sp-orphan-row` + `data-comment-id`, include it in the focusables list (already declared in keynav's selector), and add dispatch for `d` and `a`.

**Files:**
- Modify: `src/Spectacle/Render/Assets/preview-annotations.js`
- Modify: `src/Spectacle/Render/Assets/preview-keynav.js`

- [ ] **Step 1: Add class and data-id to each orphan row**

In `src/Spectacle/Render/Assets/preview-annotations.js`, replace the `renderOrphans` function body (currently lines 167-201) with:

```javascript
  function renderOrphans() {
    if (!data.orphaned || data.orphaned.length === 0) return;
    var main = document.querySelector("main") || document.body;
    var panel = document.createElement("div");
    panel.className = "sp-orphans";
    panel.setAttribute("role", "region");
    panel.setAttribute("aria-label", "Orphaned revision requests");

    var header = document.createElement("div");
    header.className = "sp-orphans-header";
    header.textContent = "Orphaned (" + data.orphaned.length + ") ▾";
    panel.appendChild(header);

    var list = document.createElement("ul");
    data.orphaned.forEach(function (c) {
      var li = document.createElement("li");
      li.className = "sp-orphan-row";
      li.setAttribute("data-comment-id", c.id);
      li.setAttribute("role", "listitem");
      li.innerHTML = "<strong>" + escapeHtml(c.blockAnchor.kind) + "</strong>: " +
        escapeHtml(c.blockAnchor.leadingText) + " — " +
        '<button type="button" data-sp-action="delete" data-id="' + escapeHtml(c.id) + '">Delete</button> ' +
        '<button type="button" data-sp-action="reanchor" data-id="' + escapeHtml(c.id) + '">Re-anchor manually</button>';
      list.appendChild(li);
    });
    panel.appendChild(list);

    list.addEventListener("click", function (e) {
      var btn = e.target.closest("button");
      if (!btn) return;
      var id = btn.getAttribute("data-id");
      var action = btn.getAttribute("data-sp-action");
      if (action === "delete") post("commentDelete", { commentId: id });
      else if (action === "reanchor") beginReanchor(id);
    });

    main.insertBefore(panel, main.firstChild);
  }
```

(Two changes from the original: each `<li>` gets `class="sp-orphan-row"` + `data-comment-id` + `role="listitem"`; and the buttons inside use `data-sp-action` instead of `data-action` to match the convention introduced in Task 5.)

- [ ] **Step 2: Include orphan rows in the focusable selector**

In `src/Spectacle/Render/Assets/preview-keynav.js`, update the `focusables` function:

```javascript
  function focusables() {
    var selector = ".sp-orphan-row, .md-block, .sp-card";
    return Array.prototype.slice.call(document.querySelectorAll(selector));
  }
```

(Order matches DOM order: orphans render before `<main>` content per `renderOrphans`.)

- [ ] **Step 3: Dispatch d / a when focused on an orphan row**

In `src/Spectacle/Render/Assets/preview-keynav.js`, replace `onKeyDown` with:

```javascript
  function onKeyDown(e) {
    if (e.target && e.target.tagName === "TEXTAREA") return;

    if (e.key === "ArrowDown") { e.preventDefault(); disarmG(); move(+1); return; }
    if (e.key === "ArrowUp")   { e.preventDefault(); disarmG(); move(-1); return; }
    if (e.key === "Home")      { e.preventDefault(); disarmG(); jumpFirst(); return; }
    if (e.key === "End")       { e.preventDefault(); disarmG(); jumpLast(); return; }
    if (e.key === "G")         { e.preventDefault(); disarmG(); jumpLast(); return; }
    if (e.key === "g") {
      if (gPending) { e.preventDefault(); disarmG(); jumpFirst(); return; }
      armG();
      return;
    }

    disarmG();

    var focused = currentFocus();
    var kind = kindOf(focused);

    if (kind === "block") {
      if (e.key === "Enter" || e.key === "c") {
        e.preventDefault();
        var blockId = focused.getAttribute("data-block-id");
        if (blockId && window.__sp_startCompose) {
          window.__sp_startCompose(blockId, null);
        }
      }
      return;
    }

    if (kind === "card") {
      if (e.key === "e") { e.preventDefault(); clickAction(focused, "edit"); }
      else if (e.key === "r") { e.preventDefault(); clickAction(focused, "resolve"); }
      else if (e.key === "d") { e.preventDefault(); clickAction(focused, "delete"); }
      return;
    }

    if (kind === "orphan") {
      if (e.key === "d") { e.preventDefault(); clickAction(focused, "delete"); }
      else if (e.key === "a") { e.preventDefault(); clickAction(focused, "reanchor"); }
      return;
    }
  }
```

- [ ] **Step 4: Build and smoke-test**

```bash
dotnet build src/Spectacle/Spectacle.csproj
dotnet test test/Spectacle.Tests/Spectacle.Tests.csproj
```

Expected: pass.

**Manual smoke** (need a doc with at least one orphan — open a `.md` file, add a comment via the app, then edit the underlying file to remove the commented block, save, let it reload):

- The orphan panel renders. Arrow-navigate from the first item — the first focusable is now the orphan row.
- Ring around the orphan row.
- Press `d` — orphan disappears.
- Re-create an orphan. Press `a` — the doc enters re-anchor mode (existing visual: `body.sp-reanchor-mode` is set; in Task 7 keyboard target-picking will work; for now mouse click on a block still confirms).

- [ ] **Step 5: Commit**

```bash
git add src/Spectacle/Render/Assets/preview-annotations.js src/Spectacle/Render/Assets/preview-keynav.js
git commit -m "feat(render): orphan rows focusable, d/a hotkeys to delete or re-anchor"
```

---

## Task 7: Keyboard re-anchor mode + status hint toast

Re-anchor mode today (`preview-annotations.js:203-218`) installs a one-shot click listener that captures any click on a `.md-block`. To make it keyboard-driven, we:

1. Track the "active re-anchor comment id" in keynav state.
2. While re-anchor mode is on, restrict focusables to `.md-block` only; arrows move target focus; `Enter` posts the message; `Esc` cancels.
3. Keep mouse re-anchor working (existing capture-phase click listener stays).
4. Add a small `#sp-hint` toast element to keynav for ephemeral feedback (used here when re-anchor is cancelled by a re-render in Task 11; also good user feedback when keyboard re-anchor confirms).

**Files:**
- Modify: `src/Spectacle/Render/Assets/preview-annotations.js`
- Modify: `src/Spectacle/Render/Assets/preview-keynav.js`
- Modify: `src/Spectacle/Render/Assets/preview-keynav.css`

- [ ] **Step 1: Refactor `beginReanchor` so keyboard and mouse share an entry point**

In `src/Spectacle/Render/Assets/preview-annotations.js`, replace `beginReanchor` (currently lines 203-218) with:

```javascript
  function beginReanchor(commentId) {
    document.body.classList.add("sp-reanchor-mode");
    window.__sp_reanchor_active = commentId;

    function complete(blockId) {
      document.body.classList.remove("sp-reanchor-mode");
      window.__sp_reanchor_active = null;
      document.removeEventListener("click", onClick, true);
      post("orphanReanchor", { commentId: commentId, blockId: blockId });
    }

    function cancel() {
      document.body.classList.remove("sp-reanchor-mode");
      window.__sp_reanchor_active = null;
      document.removeEventListener("click", onClick, true);
    }

    function onClick(e) {
      var block = e.target.closest(".md-block");
      if (!block) return;
      e.preventDefault();
      e.stopPropagation();
      complete(block.getAttribute("data-block-id"));
    }

    document.addEventListener("click", onClick, true);

    // Exposed for preview-keynav.js. Stable API:
    //   confirm(blockId) — commits and exits the mode
    //   cancel()         — exits without committing
    window.__sp_reanchor_confirm = function (blockId) { complete(blockId); };
    window.__sp_reanchor_cancel  = function () { cancel(); };
  }
```

- [ ] **Step 2: Add re-anchor mode handling and hint toast to keynav.js**

In `src/Spectacle/Render/Assets/preview-keynav.js`, add the following two helpers near the top of the IIFE, immediately after the `kindOf` function:

```javascript
  function inReanchor() {
    return document.body.classList.contains("sp-reanchor-mode");
  }

  // -------- Hint toast --------

  function ensureHint() {
    var el = document.getElementById("sp-hint");
    if (el) return el;
    el = document.createElement("div");
    el.id = "sp-hint";
    el.setAttribute("role", "status");
    el.setAttribute("aria-live", "polite");
    document.body.appendChild(el);
    return el;
  }

  var hintTimer = null;
  function flashHint(message) {
    var el = ensureHint();
    el.textContent = message;
    el.classList.add("sp-hint-visible");
    if (hintTimer) clearTimeout(hintTimer);
    hintTimer = setTimeout(function () {
      el.classList.remove("sp-hint-visible");
    }, 2000);
  }
  window.__sp_flash_hint = flashHint;
```

Update `focusables()` to restrict to `.md-block` while in re-anchor mode:

```javascript
  function focusables() {
    if (inReanchor()) {
      return Array.prototype.slice.call(document.querySelectorAll(".md-block"));
    }
    var selector = ".sp-orphan-row, .md-block, .sp-card";
    return Array.prototype.slice.call(document.querySelectorAll(selector));
  }
```

Replace `onKeyDown` so it handles re-anchor mode keys BEFORE the generic dispatch:

```javascript
  function onKeyDown(e) {
    if (e.target && e.target.tagName === "TEXTAREA") return;

    // ---- Re-anchor mode owns its keys ----
    if (inReanchor()) {
      if (e.key === "Escape") {
        e.preventDefault();
        if (window.__sp_reanchor_cancel) window.__sp_reanchor_cancel();
        // After cancel, ensure focus lands somewhere sensible.
        var first = focusables()[0];
        if (first) focusTarget(first);
        return;
      }
      if (e.key === "ArrowDown") { e.preventDefault(); move(+1); return; }
      if (e.key === "ArrowUp")   { e.preventDefault(); move(-1); return; }
      if (e.key === "Home")      { e.preventDefault(); jumpFirst(); return; }
      if (e.key === "End")       { e.preventDefault(); jumpLast(); return; }
      if (e.key === "Enter") {
        e.preventDefault();
        var target = currentFocus();
        if (target && kindOf(target) === "block" && window.__sp_reanchor_confirm) {
          window.__sp_reanchor_confirm(target.getAttribute("data-block-id"));
          flashHint("Re-anchored");
        }
        return;
      }
      // Swallow other keys in re-anchor mode; don't fall through.
      return;
    }

    // ---- Normal mode ----

    if (e.key === "ArrowDown") { e.preventDefault(); disarmG(); move(+1); return; }
    if (e.key === "ArrowUp")   { e.preventDefault(); disarmG(); move(-1); return; }
    if (e.key === "Home")      { e.preventDefault(); disarmG(); jumpFirst(); return; }
    if (e.key === "End")       { e.preventDefault(); disarmG(); jumpLast(); return; }
    if (e.key === "G")         { e.preventDefault(); disarmG(); jumpLast(); return; }
    if (e.key === "g") {
      if (gPending) { e.preventDefault(); disarmG(); jumpFirst(); return; }
      armG();
      return;
    }

    disarmG();

    var focused = currentFocus();
    var kind = kindOf(focused);

    if (kind === "block") {
      if (e.key === "Enter" || e.key === "c") {
        e.preventDefault();
        var blockId = focused.getAttribute("data-block-id");
        if (blockId && window.__sp_startCompose) {
          window.__sp_startCompose(blockId, null);
        }
      }
      return;
    }

    if (kind === "card") {
      if (e.key === "e") { e.preventDefault(); clickAction(focused, "edit"); }
      else if (e.key === "r") { e.preventDefault(); clickAction(focused, "resolve"); }
      else if (e.key === "d") { e.preventDefault(); clickAction(focused, "delete"); }
      return;
    }

    if (kind === "orphan") {
      if (e.key === "d") { e.preventDefault(); clickAction(focused, "delete"); }
      else if (e.key === "a") { e.preventDefault(); clickAction(focused, "reanchor"); }
      return;
    }
  }
```

- [ ] **Step 3: Add minimal hint toast CSS**

In `src/Spectacle/Render/Assets/preview-keynav.css`, append:

```css
#sp-hint {
  position: fixed;
  left: 50%;
  bottom: 24px;
  transform: translateX(-50%);
  background: #252526;
  color: #d4d4d4;
  border: 1px solid #3c3c3c;
  border-radius: 4px;
  padding: 8px 14px;
  font-size: 0.9em;
  opacity: 0;
  pointer-events: none;
  transition: opacity 150ms ease;
  z-index: 1000;
}
#sp-hint.sp-hint-visible { opacity: 1; }
```

- [ ] **Step 4: Build and smoke-test**

```bash
dotnet build src/Spectacle/Spectacle.csproj
dotnet test test/Spectacle.Tests/Spectacle.Tests.csproj
```

Expected: pass.

**Manual smoke** (open a doc with an orphan):

- Focus the orphan row. Press `a` — `body.sp-reanchor-mode` is set; only blocks are now focusable.
- Arrow-navigate over blocks. Press `Enter` on a chosen block — orphan re-anchors; the doc reloads with the comment now matched; "Re-anchored" hint flashes briefly.
- Start re-anchor again; press `Esc` — mode cancels, no commit.
- Mouse-driven re-anchor still works (click on a block confirms).

- [ ] **Step 5: Commit**

```bash
git add src/Spectacle/Render/Assets/preview-annotations.js src/Spectacle/Render/Assets/preview-keynav.js src/Spectacle/Render/Assets/preview-keynav.css
git commit -m "feat(render): keyboard re-anchor mode and status hint toast"
```

---

## Task 8: Help overlay (`?` to open/close)

The overlay renders from `KEYMAP` so it cannot drift from the dispatcher. While open it swallows all other keys, traps focus, and `Esc` or `?` closes it.

**Files:**
- Modify: `src/Spectacle/Render/Assets/preview-keynav.js`
- Modify: `src/Spectacle/Render/Assets/preview-keynav.css`

- [ ] **Step 1: Add overlay builder and toggle to keynav.js**

In `src/Spectacle/Render/Assets/preview-keynav.js`, near the bottom of the IIFE (before `init`), add:

```javascript
  // -------- Help overlay --------

  function buildOverlay() {
    var overlay = document.createElement("div");
    overlay.id = "sp-help";
    overlay.setAttribute("role", "dialog");
    overlay.setAttribute("aria-modal", "true");
    overlay.setAttribute("aria-labelledby", "sp-help-title");
    overlay.setAttribute("tabindex", "-1");
    overlay.hidden = true;

    var card = document.createElement("div");
    card.className = "sp-help-card";

    var title = document.createElement("h2");
    title.id = "sp-help-title";
    title.textContent = "Keyboard shortcuts";
    card.appendChild(title);

    // KEYMAP sections in spec order; "in-help" is omitted intentionally.
    var sectionOrder = [
      "global", "preview-wide", "on-block", "on-card",
      "on-orphan", "in-composer", "in-reanchor"
    ];
    sectionOrder.forEach(function (key) {
      var section = KEYMAP[key];
      if (!section) return;
      var h = document.createElement("h3");
      h.textContent = section.title;
      card.appendChild(h);
      var dl = document.createElement("dl");
      section.rows.forEach(function (row) {
        var dt = document.createElement("dt");
        dt.textContent = row.key;
        var dd = document.createElement("dd");
        dd.textContent = row.label;
        dl.appendChild(dt);
        dl.appendChild(dd);
      });
      card.appendChild(dl);
    });

    var footer = document.createElement("div");
    footer.className = "sp-help-footer";
    footer.textContent = "Esc to close";
    card.appendChild(footer);

    overlay.appendChild(card);
    document.body.appendChild(overlay);
    return overlay;
  }

  var overlayEl = null;
  var prevFocus = null;

  function overlayOpen() { return overlayEl && !overlayEl.hidden; }

  function openOverlay() {
    if (!overlayEl) overlayEl = buildOverlay();
    if (overlayOpen()) return;
    prevFocus = document.activeElement;
    overlayEl.hidden = false;
    overlayEl.focus({ preventScroll: true });
  }

  function closeOverlay() {
    if (!overlayOpen()) return;
    overlayEl.hidden = true;
    if (prevFocus && document.contains(prevFocus)) {
      prevFocus.focus({ preventScroll: true });
    }
    prevFocus = null;
  }

  function toggleOverlay() { overlayOpen() ? closeOverlay() : openOverlay(); }
```

- [ ] **Step 2: Wire overlay into the dispatcher**

In the same file, replace `onKeyDown` so the overlay owns its keys FIRST (before re-anchor and before normal mode):

```javascript
  function onKeyDown(e) {
    if (e.target && e.target.tagName === "TEXTAREA") return;

    // ---- Help overlay owns its keys ----
    if (overlayOpen()) {
      if (e.key === "Escape" || e.key === "?") {
        e.preventDefault();
        closeOverlay();
      } else {
        // Swallow everything else while overlay is open.
        e.preventDefault();
      }
      return;
    }

    // ---- Preview-wide: `?` opens overlay ----
    if (e.key === "?") {
      e.preventDefault();
      openOverlay();
      return;
    }

    // ---- Re-anchor mode owns its keys ----
    if (inReanchor()) {
      if (e.key === "Escape") {
        e.preventDefault();
        if (window.__sp_reanchor_cancel) window.__sp_reanchor_cancel();
        var first = focusables()[0];
        if (first) focusTarget(first);
        return;
      }
      if (e.key === "ArrowDown") { e.preventDefault(); move(+1); return; }
      if (e.key === "ArrowUp")   { e.preventDefault(); move(-1); return; }
      if (e.key === "Home")      { e.preventDefault(); jumpFirst(); return; }
      if (e.key === "End")       { e.preventDefault(); jumpLast(); return; }
      if (e.key === "Enter") {
        e.preventDefault();
        var target = currentFocus();
        if (target && kindOf(target) === "block" && window.__sp_reanchor_confirm) {
          window.__sp_reanchor_confirm(target.getAttribute("data-block-id"));
          flashHint("Re-anchored");
        }
        return;
      }
      return;
    }

    // ---- Normal mode ----

    if (e.key === "ArrowDown") { e.preventDefault(); disarmG(); move(+1); return; }
    if (e.key === "ArrowUp")   { e.preventDefault(); disarmG(); move(-1); return; }
    if (e.key === "Home")      { e.preventDefault(); disarmG(); jumpFirst(); return; }
    if (e.key === "End")       { e.preventDefault(); disarmG(); jumpLast(); return; }
    if (e.key === "G")         { e.preventDefault(); disarmG(); jumpLast(); return; }
    if (e.key === "g") {
      if (gPending) { e.preventDefault(); disarmG(); jumpFirst(); return; }
      armG();
      return;
    }

    disarmG();

    var focused = currentFocus();
    var kind = kindOf(focused);

    if (kind === "block") {
      if (e.key === "Enter" || e.key === "c") {
        e.preventDefault();
        var blockId = focused.getAttribute("data-block-id");
        if (blockId && window.__sp_startCompose) {
          window.__sp_startCompose(blockId, null);
        }
      }
      return;
    }

    if (kind === "card") {
      if (e.key === "e") { e.preventDefault(); clickAction(focused, "edit"); }
      else if (e.key === "r") { e.preventDefault(); clickAction(focused, "resolve"); }
      else if (e.key === "d") { e.preventDefault(); clickAction(focused, "delete"); }
      return;
    }

    if (kind === "orphan") {
      if (e.key === "d") { e.preventDefault(); clickAction(focused, "delete"); }
      else if (e.key === "a") { e.preventDefault(); clickAction(focused, "reanchor"); }
      return;
    }
  }
```

- [ ] **Step 3: Add overlay CSS**

Append to `src/Spectacle/Render/Assets/preview-keynav.css`:

```css
#sp-help {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  z-index: 999;
  display: flex;
  align-items: center;
  justify-content: center;
}
#sp-help[hidden] { display: none; }
#sp-help .sp-help-card {
  background: #252526;
  color: #d4d4d4;
  border: 1px solid #3c3c3c;
  border-radius: 6px;
  padding: 20px 24px;
  width: 520px;
  max-height: 80vh;
  overflow-y: auto;
  box-shadow: 0 8px 28px rgba(0, 0, 0, 0.6);
}
#sp-help h2 {
  margin: 0 0 12px 0;
  font-size: 1.1em;
}
#sp-help h3 {
  margin: 16px 0 6px 0;
  font-size: 0.95em;
  color: #4ea1ff;
}
#sp-help dl {
  margin: 0;
  display: grid;
  grid-template-columns: 160px 1fr;
  gap: 4px 12px;
}
#sp-help dt {
  font-family: ui-monospace, "Cascadia Mono", Consolas, monospace;
  font-size: 0.9em;
  color: #d4d4d4;
}
#sp-help dd {
  margin: 0;
  font-size: 0.9em;
  color: #9aa0a6;
}
#sp-help .sp-help-footer {
  margin-top: 16px;
  text-align: right;
  font-size: 0.85em;
  color: #9aa0a6;
}
```

- [ ] **Step 4: Build and smoke-test**

```bash
dotnet build src/Spectacle/Spectacle.csproj
dotnet test test/Spectacle.Tests/Spectacle.Tests.csproj
```

Expected: pass.

**Manual smoke:**

- Press `?` — overlay appears, focus moves to the overlay; backdrop dims the doc.
- Press any non-Esc / non-`?` key — overlay stays; underlying doc does nothing.
- Press `Esc` — overlay closes; focus returns to the previously focused element.
- Press `?` again — opens; press `?` again — closes.
- Open the composer; press `?` while the textarea has focus — `?` types into the textarea (not intercepted).

- [ ] **Step 5: Commit**

```bash
git add src/Spectacle/Render/Assets/preview-keynav.js src/Spectacle/Render/Assets/preview-keynav.css
git commit -m "feat(render): help overlay rendered from KEYMAP"
```

---

## Task 9: Esc precedence chain verification

The chain (per spec §4): overlay → composer → re-anchor → window. Most pieces already work after Tasks 7 and 8 because each layer handles its own Esc. This task is a focused review + a test for the case keynav can verify: the dispatcher must not intercept Esc in normal mode (so WPF gets it).

**Files:**
- Modify: `src/Spectacle/Render/Assets/preview-keynav.js`

- [ ] **Step 1: Audit `onKeyDown` for stray Esc handling**

Read `src/Spectacle/Render/Assets/preview-keynav.js`. Confirm that in the "normal mode" branch (after `inReanchor()` and `overlayOpen()` checks), there is NO `if (e.key === "Escape")` branch — Esc must fall through to WPF. If you find one, delete it.

This audit is a no-op if Tasks 7 and 8 were implemented correctly. The check is the deliverable.

- [ ] **Step 2: Smoke-test the chain**

No code change in this step. Run the app:

```bash
dotnet build src/Spectacle/Spectacle.csproj
```

Then in the IDE, open a fixture and verify these in order:

1. Open the help overlay (`?`). Press Esc → overlay closes, window stays open.
2. Open the composer on a block (`Enter` or click). Press Esc → composer closes, window stays open.
3. Press `a` on an orphan to enter re-anchor mode. Press Esc → re-anchor cancels, window stays open.
4. With nothing active, press Esc → window closes (WPF binding fires).

If any of those four fail, fix the responsible owner (overlay in keynav, composer in `preview-annotations.js` textarea keydown, re-anchor in keynav re-anchor branch) and retest.

- [ ] **Step 3: Commit (only if audit found a stray Esc to remove)**

If Step 1 found nothing, skip this step. Otherwise:

```bash
git add src/Spectacle/Render/Assets/preview-keynav.js
git commit -m "fix(render): drop stray Esc handler so WPF close binding still fires"
```

---

## Task 10: Visual focus indicators — per-kind nuance and high-contrast

**Files:**
- Modify: `src/Spectacle/Render/Assets/preview-keynav.css`

- [ ] **Step 1: Add per-kind focus styles**

In `src/Spectacle/Render/Assets/preview-keynav.css`, replace the existing focus-ring block at the top with the following expanded block. The hint and overlay rules from Tasks 7-8 stay below it. Full updated file content:

```css
/* preview-keynav.css — focus indicators, overlay, hint toast.
   Geometry per docs/superpowers/specs/2026-05-26-keyboard-navigation-design.md §7. */

/* ---- Default focus ring ---- */

.md-block:focus-visible,
.sp-card:focus-visible,
.sp-orphan-row:focus-visible {
  outline: 2px solid #4ea1ff;
  outline-offset: 2px;
  border-radius: 4px;
}

/* ---- Per-kind nuance ---- */

/* Card: outline plus a 4px accent left border replacing the existing 3px. */
.sp-card:focus-visible {
  border-left: 4px solid #4ea1ff;
}

/* Resolved card: muted ring so `r` is clearly "reopen". */
.sp-card.sp-resolved:focus-visible,
.sp-card[data-resolved="true"]:focus-visible {
  outline-color: #9aa0a6;
  border-left-color: #9aa0a6;
}

/* Orphan row: outline plus a subtle row tint. */
.sp-orphan-row:focus-visible {
  background-color: rgba(78, 161, 255, 0.08);
}

/* ---- Re-anchor mode ---- */

body.sp-reanchor-mode .md-block:focus-visible {
  outline-style: dashed;
  cursor: crosshair;
}

/* ---- Hint toast ---- */

#sp-hint {
  position: fixed;
  left: 50%;
  bottom: 24px;
  transform: translateX(-50%);
  background: #252526;
  color: #d4d4d4;
  border: 1px solid #3c3c3c;
  border-radius: 4px;
  padding: 8px 14px;
  font-size: 0.9em;
  opacity: 0;
  pointer-events: none;
  transition: opacity 150ms ease;
  z-index: 1000;
}
#sp-hint.sp-hint-visible { opacity: 1; }

/* ---- Help overlay ---- */

#sp-help {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  z-index: 999;
  display: flex;
  align-items: center;
  justify-content: center;
}
#sp-help[hidden] { display: none; }
#sp-help .sp-help-card {
  background: #252526;
  color: #d4d4d4;
  border: 1px solid #3c3c3c;
  border-radius: 6px;
  padding: 20px 24px;
  width: 520px;
  max-height: 80vh;
  overflow-y: auto;
  box-shadow: 0 8px 28px rgba(0, 0, 0, 0.6);
}
#sp-help h2 { margin: 0 0 12px 0; font-size: 1.1em; }
#sp-help h3 { margin: 16px 0 6px 0; font-size: 0.95em; color: #4ea1ff; }
#sp-help dl {
  margin: 0;
  display: grid;
  grid-template-columns: 160px 1fr;
  gap: 4px 12px;
}
#sp-help dt {
  font-family: ui-monospace, "Cascadia Mono", Consolas, monospace;
  font-size: 0.9em;
  color: #d4d4d4;
}
#sp-help dd { margin: 0; font-size: 0.9em; color: #9aa0a6; }
#sp-help .sp-help-footer {
  margin-top: 16px;
  text-align: right;
  font-size: 0.85em;
  color: #9aa0a6;
}

/* ---- High-contrast (Windows forced-colors) ---- */

@media (forced-colors: active) {
  .md-block:focus-visible,
  .sp-card:focus-visible,
  .sp-orphan-row:focus-visible {
    outline: 3px solid Highlight;
    outline-offset: 2px;
  }
  body.sp-reanchor-mode .md-block:focus-visible {
    outline-style: dashed;
  }
  #sp-hint {
    background: Canvas;
    color: CanvasText;
    border-color: CanvasText;
  }
  #sp-help { background: rgba(0, 0, 0, 0.6); }
  #sp-help .sp-help-card {
    background: Window;
    color: WindowText;
    border-color: WindowText;
  }
  #sp-help h3 { color: Highlight; }
  #sp-help dt { color: WindowText; }
  #sp-help dd { color: WindowText; }
  #sp-help .sp-help-footer { color: WindowText; }
}
```

- [ ] **Step 2: Build and smoke-test**

```bash
dotnet build src/Spectacle/Spectacle.csproj
dotnet test test/Spectacle.Tests/Spectacle.Tests.csproj
```

Expected: pass.

**Manual smoke** (a doc with at least one resolved comment and one orphan):

- Focus a block — blue ring.
- Focus a comment card — blue ring + thicker blue left border.
- Focus a resolved card — muted (grey) ring + grey left border.
- Focus an orphan row — blue ring + subtle blue tint behind the row.
- Enter re-anchor mode — focus ring becomes dashed; cursor shows crosshair over blocks.
- Enable Windows high-contrast (Settings → Accessibility → Contrast themes) — focus rings use system `Highlight` color; overlay uses system colors.

- [ ] **Step 3: Commit**

```bash
git add src/Spectacle/Render/Assets/preview-keynav.css
git commit -m "feat(render): per-kind focus styles, dashed re-anchor ring, HC support"
```

---

## Task 11: Focus persistence across re-renders

The preview wipes itself on every doc change, theme switch, or Ctrl+R. `sessionStorage` survives `NavigateToString` within a WebView2 lifetime. Persist `{ kind, id }` of the focused element; restore on init.

Also persist `helpOpen` so the overlay survives a re-render.

If re-anchor mode was active when a re-render fires, the new init cannot resume it (re-anchor state lives in JS memory only). The current code naturally drops it (DOM is gone), but we add a hint message so the user understands.

**Files:**
- Modify: `src/Spectacle/Render/Assets/preview-keynav.js`

- [ ] **Step 1: Add the persistence helpers and integrate them into focus + overlay**

In `src/Spectacle/Render/Assets/preview-keynav.js`, add this block immediately above the `init` function:

```javascript
  // -------- Persistence (sessionStorage) --------

  var STORAGE_FOCUS = "spectacle.focus";
  var STORAGE_HELP  = "spectacle.helpOpen";
  var STORAGE_REANCHOR_LOST = "spectacle.reanchorLostOnRender";

  function pointerOf(el) {
    if (!el) return null;
    var kind = kindOf(el);
    if (!kind) return null;
    if (kind === "block") {
      var bid = el.getAttribute("data-block-id");
      return bid ? { kind: kind, id: bid } : null;
    }
    if (kind === "card") {
      var cid = el.getAttribute("data-comment-id");
      if (!cid) return null;
      // Cards are inserted as siblings after their anchoring block. Walk back
      // to capture the block id so we can fall back if the card is deleted.
      var sib = el.previousElementSibling;
      while (sib && !sib.classList.contains("md-block")) {
        sib = sib.previousElementSibling;
      }
      var anchorBlockId = sib ? sib.getAttribute("data-block-id") : null;
      return { kind: kind, id: cid, blockId: anchorBlockId };
    }
    if (kind === "orphan") {
      var oid = el.getAttribute("data-comment-id");
      return oid ? { kind: kind, id: oid } : null;
    }
    return null;
  }

  function savePointer(el) {
    try {
      var p = pointerOf(el);
      if (p) sessionStorage.setItem(STORAGE_FOCUS, JSON.stringify(p));
    } catch (err) { /* ignore */ }
  }

  function loadPointer() {
    try {
      var raw = sessionStorage.getItem(STORAGE_FOCUS);
      if (!raw) return null;
      return JSON.parse(raw);
    } catch (err) { return null; }
  }

  // Fallback chain per spec §6:
  //   block kind:   exact id → null (init falls through to first focusable)
  //   card kind:    exact card → orphan row with same id → anchoring block
  //   orphan kind:  exact orphan row → card with same id (became matched)
  function findTarget(p) {
    if (!p || !p.id) return null;
    if (p.kind === "block") {
      return document.querySelector('.md-block[data-block-id="' + cssEscape(p.id) + '"]');
    }
    if (p.kind === "card") {
      var card = document.querySelector('.sp-card[data-comment-id="' + cssEscape(p.id) + '"]');
      if (card) return card;
      var orphan = document.querySelector('.sp-orphan-row[data-comment-id="' + cssEscape(p.id) + '"]');
      if (orphan) return orphan;
      if (p.blockId) {
        return document.querySelector('.md-block[data-block-id="' + cssEscape(p.blockId) + '"]');
      }
      return null;
    }
    if (p.kind === "orphan") {
      var orphan2 = document.querySelector('.sp-orphan-row[data-comment-id="' + cssEscape(p.id) + '"]');
      if (orphan2) return orphan2;
      return document.querySelector('.sp-card[data-comment-id="' + cssEscape(p.id) + '"]');
    }
    return null;
  }

  // Minimal CSS.escape polyfill for attribute selectors — sessionStorage holds
  // ids that might contain quotes/backslashes in pathological cases.
  function cssEscape(s) {
    return String(s).replace(/(["\\])/g, "\\$1");
  }
```

- [ ] **Step 2: Save the pointer on every focus change**

In `src/Spectacle/Render/Assets/preview-keynav.js`, update `focusTarget` to persist:

```javascript
  function focusTarget(target, opts) {
    if (!target) return;
    applyRoving(target);
    target.focus({ preventScroll: !!(opts && opts.preventScroll) });
    if (!opts || !opts.preventScroll) {
      target.scrollIntoView({ block: "nearest" });
    }
    savePointer(target);
  }
```

Also save on the mouse-click path. Replace `syncPointerOnMouse` with:

```javascript
  function syncPointerOnMouse() {
    document.addEventListener("mousedown", function (e) {
      var el = e.target.closest && e.target.closest(".md-block, .sp-card, .sp-orphan-row");
      if (!el) return;
      applyRoving(el);
      el.focus({ preventScroll: true });
      savePointer(el);
    }, true);
  }
```

- [ ] **Step 3: Persist the overlay-open flag**

In `src/Spectacle/Render/Assets/preview-keynav.js`, update `openOverlay` and `closeOverlay`:

```javascript
  function openOverlay() {
    if (!overlayEl) overlayEl = buildOverlay();
    if (overlayOpen()) return;
    prevFocus = document.activeElement;
    overlayEl.hidden = false;
    overlayEl.focus({ preventScroll: true });
    try { sessionStorage.setItem(STORAGE_HELP, "1"); } catch (err) { /* ignore */ }
  }

  function closeOverlay() {
    if (!overlayOpen()) return;
    overlayEl.hidden = true;
    if (prevFocus && document.contains(prevFocus)) {
      prevFocus.focus({ preventScroll: true });
    }
    prevFocus = null;
    try { sessionStorage.removeItem(STORAGE_HELP); } catch (err) { /* ignore */ }
  }
```

- [ ] **Step 4: Mark re-anchor as "lost on render" when it was active**

Re-anchor mode cannot survive a re-render. The cleanest signal is to set a flag right before the (impending) re-render destroys everything. We don't have a host-side "about to re-render" hook, but we DO know that **if the page reloaded while `body.sp-reanchor-mode` was set, the previous page lost it.** We can detect that across reloads by setting the flag at `beginReanchor` and clearing it at `complete`/`cancel`.

Since `beginReanchor` lives in `preview-annotations.js`, the simplest cross-file way is to **set the flag from keynav** when it sees `inReanchor()` on init. But on init the body class is already gone (new DOM). Instead: have `preview-annotations.js`'s `beginReanchor` set the storage flag at entry and clear it at complete/cancel — so a leftover "1" at next init means "we got re-rendered mid re-anchor".

In `src/Spectacle/Render/Assets/preview-annotations.js`, update `beginReanchor` to set/clear the flag. The relevant edits inside the function body:

```javascript
  function beginReanchor(commentId) {
    document.body.classList.add("sp-reanchor-mode");
    window.__sp_reanchor_active = commentId;
    try { sessionStorage.setItem("spectacle.reanchorLostOnRender", "1"); } catch (err) { /* ignore */ }

    function complete(blockId) {
      document.body.classList.remove("sp-reanchor-mode");
      window.__sp_reanchor_active = null;
      try { sessionStorage.removeItem("spectacle.reanchorLostOnRender"); } catch (err) { /* ignore */ }
      document.removeEventListener("click", onClick, true);
      post("orphanReanchor", { commentId: commentId, blockId: blockId });
    }

    function cancel() {
      document.body.classList.remove("sp-reanchor-mode");
      window.__sp_reanchor_active = null;
      try { sessionStorage.removeItem("spectacle.reanchorLostOnRender"); } catch (err) { /* ignore */ }
      document.removeEventListener("click", onClick, true);
    }

    function onClick(e) {
      var block = e.target.closest(".md-block");
      if (!block) return;
      e.preventDefault();
      e.stopPropagation();
      complete(block.getAttribute("data-block-id"));
    }

    document.addEventListener("click", onClick, true);
    window.__sp_reanchor_confirm = function (blockId) { complete(blockId); };
    window.__sp_reanchor_cancel  = function () { cancel(); };
  }
```

- [ ] **Step 5: Restore focus and overlay on init**

In `src/Spectacle/Render/Assets/preview-keynav.js`, replace the `init` function with:

```javascript
  function init() {
    var all = focusables();

    // Restore focus from sessionStorage with fallback chain.
    var stored = loadPointer();
    var target = stored ? findTarget(stored) : null;
    if (!target && all.length > 0) target = all[0];

    if (target) {
      applyRoving(target);
      target.focus({ preventScroll: true });
      target.scrollIntoView({ block: "nearest" });
    }

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

- [ ] **Step 6: Build and smoke-test**

```bash
dotnet build src/Spectacle/Spectacle.csproj
dotnet test test/Spectacle.Tests/Spectacle.Tests.csproj
```

Expected: pass.

**Manual smoke** (open the 3-comments fixture):

- Arrow-navigate to the 3rd block. Save the file (e.g., `:w` from your editor / touch the file). Preview re-renders → ring is back on the 3rd block.
- Focus a comment card. Externally edit the file. Re-render → if the comment still resolves to the same block, focus restored to the same card. If not, falls back per the chain.
- Open the help overlay. Externally edit the file. Re-render → overlay is reopened.
- Delete a focused comment (via `d`). Focus falls back to the anchoring block.
- Press `a` on an orphan to enter re-anchor mode. Externally edit the file. Re-render → "Re-anchor cancelled by document change" hint flashes; focus is restored.
- Ctrl+R reload — same restoration behavior as external edit.

- [ ] **Step 7: Commit**

```bash
git add src/Spectacle/Render/Assets/preview-keynav.js src/Spectacle/Render/Assets/preview-annotations.js
git commit -m "feat(render): focus + help-overlay persistence across re-renders"
```

---

## Task 12: README — extend the Keyboard section

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Replace the Keyboard section**

In `README.md`, replace the entire `## Keyboard` section (currently lines 26-33) with:

```markdown
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
```

- [ ] **Step 2: Commit**

```bash
git add README.md
git commit -m "docs: extend keyboard section with full navigation cheatsheet"
```

---

## Final verification

After Task 12 is committed, run the full spec §9 smoke checklist end-to-end in a single session:

```bash
dotnet build src/Spectacle/Spectacle.csproj
dotnet test test/Spectacle.Tests/Spectacle.Tests.csproj
```

Expected: all tests pass.

Then run the app in the IDE against `test/Spectacle.Tests/Fixtures/revision-plan-3-comments.md` (and a fixture you've cultivated to have an orphan) and walk the 13-step manual checklist from the spec. Each step should match the described behavior.

If anything fails, the failing task's smoke section is the first place to look.
