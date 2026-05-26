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
    if (inReanchor()) {
      return Array.prototype.slice.call(document.querySelectorAll(".md-block"));
    }
    var selector = ".sp-orphan-row, .md-block, .sp-card";
    return Array.prototype.slice.call(document.querySelectorAll(selector));
  }

  function kindOf(el) {
    if (!el) return null;
    if (el.classList.contains("md-block")) return "block";
    if (el.classList.contains("sp-card")) return "card";
    if (el.classList.contains("sp-orphan-row")) return "orphan";
    return null;
  }

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
    savePointer(target);
  }

  function currentFocus() {
    var active = document.activeElement;
    if (active && kindOf(active)) return active;
    // No recognized focus yet (e.g., initial load with activeElement === body).
    // Returning null lets move() interpret "no current" as index -1, so
    // ArrowDown lands on the first focusable, not the second.
    return null;
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
      applyRoving(el);
      el.focus({ preventScroll: true });
      savePointer(el);
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
    if (e.key === "g" && !e.ctrlKey && !e.metaKey && !e.altKey) {
      if (gPending) { e.preventDefault(); disarmG(); jumpFirst(); return; }
      armG();
      return;
    }

    disarmG();

    var focused = currentFocus();
    var kind = kindOf(focused);

    if (kind === "block") {
      var bareEnter = e.key === "Enter" && !e.ctrlKey && !e.metaKey && !e.altKey && !e.shiftKey;
      var bareC = e.key === "c" && !e.ctrlKey && !e.metaKey && !e.altKey;
      if (bareEnter || bareC) {
        e.preventDefault();
        var blockId = focused.getAttribute("data-block-id");
        if (blockId && window.__sp_startCompose) {
          window.__sp_startCompose(blockId, null);
        }
      }
      return;
    }

    if (kind === "card") {
      var noMods = !e.ctrlKey && !e.metaKey && !e.altKey;
      if (e.key === "e" && noMods) { e.preventDefault(); clickAction(focused, "edit"); }
      else if (e.key === "r" && noMods) { e.preventDefault(); clickAction(focused, "resolve"); }
      else if (e.key === "d" && noMods) { e.preventDefault(); clickAction(focused, "delete"); }
      return;
    }

    if (kind === "orphan") {
      var noModsOrphan = !e.ctrlKey && !e.metaKey && !e.altKey;
      if (e.key === "d" && noModsOrphan) { e.preventDefault(); clickAction(focused, "delete"); }
      else if (e.key === "a" && noModsOrphan) { e.preventDefault(); clickAction(focused, "reanchor"); }
      return;
    }
  }

  function clickAction(container, action) {
    var btn = container.querySelector('button[data-sp-action="' + action + '"]');
    if (btn) btn.click();
  }

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

  function toggleOverlay() { overlayOpen() ? closeOverlay() : openOverlay(); }

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

  // -------- Init --------

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

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", init);
  } else {
    init();
  }
})();
