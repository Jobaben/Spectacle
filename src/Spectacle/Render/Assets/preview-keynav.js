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
