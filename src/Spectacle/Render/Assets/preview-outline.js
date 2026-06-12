(function () {
  "use strict";
  // preview-outline.js — document outline / table of contents ("t") for Spectacle.
  //
  // Entries come from window.__spectacleOutline__, emitted by PreviewHtml from the
  // C# OutlineExtractor (heading level/text/slug/line in document order). Selecting
  // an entry scrolls to the heading by its id, falling back to its data-line block
  // when the heading carried no auto-id.

  var ENTRIES = Array.isArray(window.__spectacleOutline__)
    ? window.__spectacleOutline__ : [];

  var panelEl = null;
  var listEl = null;
  var optionEls = [];
  var selected = -1;
  var prevFocus = null;

  // -------- Panel construction --------

  function build() {
    var panel = document.createElement("div");
    panel.id = "sp-outline";
    panel.setAttribute("role", "dialog");
    panel.setAttribute("aria-modal", "true");
    panel.setAttribute("aria-labelledby", "sp-outline-title");
    panel.setAttribute("tabindex", "-1");
    panel.hidden = true;

    var title = document.createElement("h2");
    title.id = "sp-outline-title";
    title.textContent = "Document outline";
    panel.appendChild(title);

    listEl = document.createElement("ul");
    listEl.id = "sp-outline-list";
    listEl.setAttribute("role", "listbox");
    listEl.setAttribute("aria-label", "Document outline");

    optionEls = ENTRIES.map(function (entry, i) {
      var li = document.createElement("li");
      li.id = "sp-outline-opt-" + i;
      li.setAttribute("role", "option");
      li.setAttribute("aria-selected", "false");
      li.className = "sp-outline-item sp-outline-l" + clampLevel(entry.level);
      li.textContent = entry.text;
      li.title = entry.text;
      li.addEventListener("mousedown", function (e) { e.preventDefault(); });
      li.addEventListener("click", function () { activate(i); });
      listEl.appendChild(li);
      return li;
    });

    panel.appendChild(listEl);

    var footer = document.createElement("div");
    footer.className = "sp-outline-footer";
    footer.textContent = "Enter to jump · Esc to close";
    panel.appendChild(footer);

    document.body.appendChild(panel);
    return panel;
  }

  function clampLevel(level) {
    var n = parseInt(level, 10);
    if (!isFinite(n) || n < 1) return 1;
    return n > 6 ? 6 : n;
  }

  function isOpen() { return panelEl && !panelEl.hidden; }

  // -------- Selection --------

  function setSelected(i) {
    if (!optionEls.length) return;
    var next = Math.max(0, Math.min(optionEls.length - 1, i));
    if (selected >= 0 && optionEls[selected]) {
      optionEls[selected].setAttribute("aria-selected", "false");
    }
    selected = next;
    var el = optionEls[selected];
    el.setAttribute("aria-selected", "true");
    listEl.setAttribute("aria-activedescendant", el.id || "");
    el.scrollIntoView({ block: "nearest" });
  }

  function activate(i) {
    if (i >= 0) setSelected(i);
    var entry = ENTRIES[selected];
    hide();
    if (entry) jumpTo(entry);
  }

  function jumpTo(entry) {
    var target = null;
    if (entry.id) target = document.getElementById(entry.id);
    if (!target && entry.line != null) {
      target = document.querySelector('.md-block[data-line="' + entry.line + '"]');
    }
    if (!target) return;
    target.scrollIntoView({ block: "start" });
    // Hand keyboard focus to the heading so block navigation continues from here.
    if (target.classList.contains("md-block")) {
      target.focus({ preventScroll: true });
    }
  }

  // -------- Open / close --------

  function show() {
    if (window.__sp_find_close) window.__sp_find_close();
    if (!ENTRIES.length) {
      if (window.__sp_flash_hint) window.__sp_flash_hint("No headings in document");
      return;
    }
    if (!panelEl) panelEl = build();
    if (isOpen()) return;
    prevFocus = document.activeElement;
    panelEl.hidden = false;
    panelEl.focus({ preventScroll: true });
    setSelected(nearestEntry());
  }

  function hide() {
    if (!isOpen()) return;
    panelEl.hidden = true;
    if (prevFocus && document.contains(prevFocus)) {
      prevFocus.focus({ preventScroll: true });
    }
    prevFocus = null;
  }
  window.__sp_outline_close = hide;

  // Pick the heading nearest the top of the viewport so the panel opens with the
  // reader's current section preselected.
  function nearestEntry() {
    var best = 0;
    var bestTop = -Infinity;
    for (var i = 0; i < ENTRIES.length; i++) {
      var el = ENTRIES[i].id ? document.getElementById(ENTRIES[i].id) : null;
      if (!el) continue;
      var top = el.getBoundingClientRect().top;
      if (top <= 1 && top > bestTop) { bestTop = top; best = i; }
    }
    return best;
  }

  // -------- Key handling (capture phase: runs before keynav) --------

  function blockedTarget() {
    if (document.body.classList.contains("sp-reanchor-mode")) return true;
    var help = document.getElementById("sp-help");
    if (help && !help.hidden) return true;
    var a = document.activeElement;
    return !!(a && (a.tagName === "TEXTAREA" || a.tagName === "INPUT"));
  }

  function onKeyCapture(e) {
    if (!isOpen()) {
      if (e.key === "t" && !e.ctrlKey && !e.metaKey && !e.altKey) {
        if (blockedTarget()) return;
        e.preventDefault();
        e.stopImmediatePropagation();
        show();
      }
      return;
    }

    // Panel owns all keys while open.
    e.stopImmediatePropagation();

    switch (e.key) {
      case "Escape": e.preventDefault(); hide(); break;
      case "ArrowDown": e.preventDefault(); setSelected(selected + 1); break;
      case "ArrowUp": e.preventDefault(); setSelected(selected - 1); break;
      case "Home": e.preventDefault(); setSelected(0); break;
      case "End": e.preventDefault(); setSelected(optionEls.length - 1); break;
      case "Enter": e.preventDefault(); activate(-1); break;
      case "t": e.preventDefault(); hide(); break;
      default: e.preventDefault(); break;
    }
  }

  document.addEventListener("keydown", onKeyCapture, true);
})();
