(function () {
  "use strict";
  // preview-gate.js — the live quality gate in the reader ("v") for Spectacle.
  //
  // The verdict comes from window.__spectacleGate__, emitted by PreviewHtml from the same
  // GateVerdict the --gate command exits on. That is the whole point: the reader is not showing an
  // approximation of the gate, it is showing the gate. Open a document an AI workflow just wrote
  // and its pass/fail state, its provenance metadata, and every finding with the fix for it are
  // there without running anything.
  //
  // Three pieces: a metadata card at the top of the document (front matter, rendered as data), a
  // badge in the corner (pass/fail at a glance), and a panel listing findings, where selecting one
  // scrolls to the line and flashes it.

  var GATE = window.__spectacleGate__ || null;

  var badgeEl = null;
  var panelEl = null;
  var listEl = null;
  var optionEls = [];
  var selected = -1;
  var prevFocus = null;

  // -------- Metadata card --------

  function buildMetadata() {
    if (!GATE || !GATE.metadata || !GATE.metadata.length) return;

    var main = document.querySelector("main") || document.body;
    var card = document.createElement("section");
    card.id = "sp-gate-meta";
    card.setAttribute("aria-label", "Document metadata");

    var dl = document.createElement("dl");
    GATE.metadata.forEach(function (pair) {
      var dt = document.createElement("dt");
      dt.textContent = pair.key;
      var dd = document.createElement("dd");
      // An empty required value is a finding in its own right; show that it is empty rather than
      // rendering a blank row the reader has to interpret.
      dd.textContent = pair.value && pair.value.length ? pair.value : "—";
      dl.appendChild(dt);
      dl.appendChild(dd);
    });

    card.appendChild(dl);
    main.insertBefore(card, main.firstChild);
  }

  // -------- Badge --------

  function buildBadge() {
    var badge = document.createElement("button");
    badge.id = "sp-gate-badge";
    badge.type = "button";
    badge.setAttribute("aria-expanded", "false");
    badge.setAttribute("aria-controls", "sp-gate-panel");
    if (GATE.passed) badge.className = "sp-gate-ok";

    var dot = document.createElement("span");
    dot.className = "sp-gate-dot";
    dot.setAttribute("aria-hidden", "true");
    badge.appendChild(dot);

    var label = document.createElement("span");
    label.className = "sp-gate-label";
    label.textContent = GATE.passed ? "GATE PASS" : "GATE FAIL";
    badge.appendChild(label);

    var counts = document.createElement("span");
    counts.className = "sp-gate-counts";
    counts.textContent = countsText();
    badge.appendChild(counts);

    // Screen readers get the whole verdict, not just the two words, because the counts and the
    // coverage caveat are what make the verdict meaningful.
    badge.setAttribute("aria-label",
      "Quality gate " + (GATE.passed ? "passed" : "failed") + ". " + summaryText() +
      " Press v to review findings.");

    badge.addEventListener("click", function () { toggle(); });
    document.body.appendChild(badge);
    return badge;
  }

  function countsText() {
    var c = GATE.counts || {};
    var parts = [];
    if (c.error) parts.push(c.error + "E");
    if (c.warning) parts.push(c.warning + "W");
    if (c.info) parts.push(c.info + "A");
    return parts.length ? parts.join(" · ") : "clean";
  }

  function summaryText() {
    var c = GATE.counts || {};
    return (c.blocking || 0) + " blocking, " + (c.error || 0) + " error, " +
      (c.warning || 0) + " warning, " + (c.info || 0) + " advisory; threshold " +
      (GATE.failOn || "error") + ".";
  }

  function coverageText() {
    var cov = GATE.coverage || {};
    var notes = [];
    if (cov.suppressed) notes.push(cov.suppressed + " finding(s) suppressed inline");
    if (cov.checksDisabled && cov.checksDisabled.length) {
      notes.push("checks off: " + cov.checksDisabled.join(", "));
    }
    return notes.length ? "Reduced coverage — " + notes.join("; ") + "." : "";
  }

  // -------- Panel --------

  function buildPanel() {
    var panel = document.createElement("div");
    panel.id = "sp-gate-panel";
    panel.setAttribute("role", "dialog");
    panel.setAttribute("aria-modal", "true");
    panel.setAttribute("aria-labelledby", "sp-gate-title");
    panel.setAttribute("tabindex", "-1");
    panel.hidden = true;

    var title = document.createElement("h2");
    title.id = "sp-gate-title";
    title.textContent = GATE.passed ? "Gate passed" : "Gate failed";
    panel.appendChild(title);

    var summary = document.createElement("p");
    summary.className = "sp-gate-summary";
    summary.textContent = summaryText();
    panel.appendChild(summary);

    var coverage = coverageText();
    if (coverage) {
      var warn = document.createElement("p");
      warn.className = "sp-gate-coverage";
      warn.textContent = coverage;
      panel.appendChild(warn);
    }

    var findings = GATE.findings || [];
    if (!findings.length) {
      var empty = document.createElement("p");
      empty.className = "sp-gate-empty";
      empty.textContent = "No findings. Every enabled check is clean.";
      panel.appendChild(empty);
      optionEls = [];
    } else {
      listEl = document.createElement("ul");
      listEl.id = "sp-gate-list";
      listEl.setAttribute("role", "listbox");
      listEl.setAttribute("aria-label", "Gate findings");

      optionEls = findings.map(function (f, i) {
        var li = document.createElement("li");
        li.id = "sp-gate-opt-" + i;
        li.className = "sp-gate-item";
        li.setAttribute("role", "option");
        li.setAttribute("aria-selected", "false");
        li.appendChild(itemHead(f));

        var msg = document.createElement("div");
        msg.className = "sp-gate-msg";
        msg.textContent = f.message || "";
        li.appendChild(msg);

        if (f.remedy) {
          var fix = document.createElement("div");
          fix.className = "sp-gate-fix";
          fix.textContent = "Fix: " + f.remedy;
          li.appendChild(fix);
        }

        li.addEventListener("mousedown", function (e) { e.preventDefault(); });
        li.addEventListener("click", function () { activate(i); });
        listEl.appendChild(li);
        return li;
      });

      panel.appendChild(listEl);
    }

    var footer = document.createElement("div");
    footer.className = "sp-gate-footer";
    // Don't offer a jump when there is nothing to jump to.
    footer.textContent = findings.length
      ? "Enter to jump to the line · Esc to close"
      : "Esc to close";
    panel.appendChild(footer);

    document.body.appendChild(panel);
    return panel;
  }

  function itemHead(f) {
    var head = document.createElement("div");
    head.className = "sp-gate-head";

    var sev = document.createElement("span");
    var name = f.severity === "error" || f.severity === "warning" ? f.severity : "info";
    sev.className = "sp-gate-sev sp-gate-sev-" + name;
    sev.textContent = f.severity;
    head.appendChild(sev);

    var line = document.createElement("span");
    line.className = "sp-gate-line";
    line.textContent = "line " + f.line;
    head.appendChild(line);

    var rule = document.createElement("span");
    rule.className = "sp-gate-rule";
    rule.textContent = f.rule;
    head.appendChild(rule);

    return head;
  }

  function isOpen() { return panelEl && !panelEl.hidden; }

  // -------- Selection and jumping --------

  function setSelected(i) {
    if (!optionEls.length) return;
    var next = Math.max(0, Math.min(optionEls.length - 1, i));
    if (selected >= 0 && optionEls[selected]) {
      optionEls[selected].setAttribute("aria-selected", "false");
    }
    selected = next;
    var el = optionEls[selected];
    el.setAttribute("aria-selected", "true");
    if (listEl) listEl.setAttribute("aria-activedescendant", el.id || "");
    el.scrollIntoView({ block: "nearest" });
  }

  function activate(i) {
    if (i >= 0) setSelected(i);
    var finding = (GATE.findings || [])[selected];
    if (!finding) return;
    close();
    jumpToLine(finding.line);
  }

  // Findings carry a source line; the rendered document carries data-line on each block. A
  // finding's line is often *inside* a block rather than at its first line (a bare URL mid
  // paragraph), so the target is the last block starting at or before it.
  function jumpToLine(line) {
    var blocks = Array.prototype.slice.call(document.querySelectorAll("[data-line]"));
    if (!blocks.length) return;

    var target = null;
    for (var i = 0; i < blocks.length; i++) {
      var at = parseInt(blocks[i].getAttribute("data-line"), 10);
      if (!isFinite(at) || at > line) break;
      target = blocks[i];
    }
    if (!target) target = blocks[0];

    target.scrollIntoView({ block: "center" });
    flash(target);
    // Focus so the reader's keyboard navigation continues from where the jump landed; every
    // tagged block carries tabindex="0" for exactly this.
    if (typeof target.focus === "function") target.focus({ preventScroll: true });
  }

  function flash(el) {
    el.classList.remove("sp-gate-flash");
    // Reading offsetWidth forces a reflow, so re-adding the class restarts the animation instead
    // of being coalesced into a no-op.
    void el.offsetWidth;
    el.classList.add("sp-gate-flash");
  }

  // -------- Open / close --------

  function open() {
    if (!panelEl || isOpen()) return;
    prevFocus = document.activeElement;
    panelEl.hidden = false;
    if (badgeEl) badgeEl.setAttribute("aria-expanded", "true");
    panelEl.focus();
    if (optionEls.length) setSelected(selected < 0 ? 0 : selected);
  }

  function close() {
    if (!panelEl || !isOpen()) return;
    panelEl.hidden = true;
    if (badgeEl) badgeEl.setAttribute("aria-expanded", "false");
    if (prevFocus && typeof prevFocus.focus === "function") prevFocus.focus();
    prevFocus = null;
  }

  function toggle() { if (isOpen()) close(); else open(); }

  // -------- Key handling (capture phase: runs before keynav) --------

  // The same guard preview-outline.js applies to "t". Another overlay that owns the screen — the
  // keyboard-help sheet, re-anchor mode — or a field taking input must not have its key stolen, or
  // "v" opens this panel underneath something modal.
  function blockedTarget(target) {
    if (document.body.classList && document.body.classList.contains("sp-reanchor-mode")) return true;
    var help = document.getElementById("sp-help");
    if (help && !help.hidden) return true;
    var el = target || document.activeElement;
    if (el && el.isContentEditable === true) return true;
    return !!(el && (el.tagName === "TEXTAREA" || el.tagName === "INPUT"));
  }

  function onKeyDown(e) {
    if (!panelEl) return;

    if (!isOpen()) {
      // "v" for verdict, bare only.
      if (e.key === "v" && !e.ctrlKey && !e.metaKey && !e.altKey) {
        if (blockedTarget(e.target)) return;
        e.preventDefault();
        // stopImmediatePropagation, not stopPropagation: the other overlays are capture listeners
        // on this same element, so only the "immediate" form keeps the key away from them.
        e.stopImmediatePropagation();
        open();
      }
      return;
    }

    // While open, the panel keeps every key away from the document behind it — otherwise "g", "?"
    // and the arrows would drive the page the reader can still see.
    //
    // The containment reaches as far as script order allows: capture listeners fire in registration
    // order, and preview-find.js and preview-outline.js are loaded first, so those two still take
    // their own shortcuts (Ctrl+F, "t") from under this panel. That asymmetry already exists between
    // find and outline for the same reason, and it is harmless — both open beside this panel rather
    // than over it, and Esc closes whichever has focus.
    e.stopImmediatePropagation();

    switch (e.key) {
      case "Escape": e.preventDefault(); close(); return;
      case "ArrowDown": e.preventDefault(); setSelected(selected + 1); return;
      case "ArrowUp": e.preventDefault(); setSelected(selected - 1); return;
      case "Home": e.preventDefault(); setSelected(0); return;
      case "End": e.preventDefault(); setSelected(optionEls.length - 1); return;
      case "Enter": e.preventDefault(); activate(-1); return;
      // The shortcut is a toggle, so the same key closes it — matching "t" on the outline.
      case "v": e.preventDefault(); close(); return;
      default: e.preventDefault(); return;
    }
  }

  // -------- Init --------

  // No verdict means no gate was computed for this document (an exported HTML file, for instance):
  // render nothing at all rather than an empty panel.
  if (GATE) {
    buildMetadata();
    badgeEl = buildBadge();
    panelEl = buildPanel();
    document.addEventListener("keydown", onKeyDown, true);
  }
})();
