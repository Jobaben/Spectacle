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
  //
  // The panel is also the triage bench. Space waives the selected finding — it stays in the
  // verdict (the badge and the pipeline never lie), but drops out of the brief — and "c" copies
  // the fix brief for everything not waived: the next prompt for the authoring agent, assembled
  // host-side by the same FixBriefExporter the --fix-brief command uses. Waives are keyed
  // line-insensitively and held by the host, so they survive both re-renders and revisions.
  //
  // When the host found a Claude CLI on this machine (GATE.claude.available), the copy round-trip
  // is unnecessary: "a" hands the same brief to `claude -p` in a background process addressed at
  // the open file, and the agent's saves land here live — each one an iteration in the loop HUD.
  // A chip above the badge shows the run while it is in flight (and a failure afterwards), payload-
  // driven so it survives the re-renders the run's own saves cause.
  //
  // The same two keys work with the panel *collapsed*, where they act on the reviewer's half of
  // the loop instead: "c" copies the revision brief built from the unresolved review comments and
  // "a" hands that brief to the same Claude runner. Opening the panel is thus a modifier on what
  // gets revised — collapsed, the reviewer's comments; open, the triaged gate findings.

  var GATE = window.__spectacleGate__ || null;
  var CLAUDE = (GATE && GATE.claude) || null;

  var STORAGE_OPEN = "spectacle.gatePanelOpen";
  var STORAGE_SELECTED = "spectacle.gateSelected";

  var badgeEl = null;
  var panelEl = null;
  var listEl = null;
  var progressEl = null;
  var copiedEl = null;
  var copiedTimer = null;
  var optionEls = [];
  var selected = -1;
  var prevFocus = null;
  var waived = {};

  (GATE && GATE.triage && GATE.triage.waived || []).forEach(function (k) { waived[k] = true; });

  function sendToHost(type, payload) {
    if (!window.chrome || !window.chrome.webview) return;
    window.chrome.webview.postMessage(JSON.stringify(Object.assign({ type: type }, payload)));
  }

  function store(key, value) {
    try {
      if (value === null) sessionStorage.removeItem(key);
      else sessionStorage.setItem(key, value);
    } catch (err) { /* ignore */ }
  }

  function stored(key) {
    try { return sessionStorage.getItem(key); } catch (err) { return null; }
  }

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

    // The triage line: how much of what the panel shows the brief will carry.
    if ((GATE.findings || []).length) {
      progressEl = document.createElement("p");
      progressEl.className = "sp-gate-progress";
      panel.appendChild(progressEl);
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
        if (f.key && waived[f.key]) li.classList.add("sp-gate-waived");
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
    // Don't offer a jump when there is nothing to jump to, and don't offer the Claude hand-off
    // when the host found no CLI.
    footer.textContent = findings.length
      ? (CLAUDE && CLAUDE.available
        ? "Enter jump · Space waive · c copy fix brief · a Claude revises in place · Esc close"
        : "Enter jump · Space waive · c copy fix brief · Esc close")
      : "Esc to close";
    panel.appendChild(footer);

    document.body.appendChild(panel);
    updateProgress();
    return panel;
  }

  // -------- Triage --------

  function waivedCount() {
    return (GATE.findings || []).reduce(function (n, f) {
      return n + (f.key && waived[f.key] ? 1 : 0);
    }, 0);
  }

  function updateProgress() {
    if (!progressEl) return;
    var total = (GATE.findings || []).length;
    var w = waivedCount();
    progressEl.textContent = w === 0
      ? total + " finding(s) · brief covers all " + total
      : total + " finding(s) · " + w + " waived · brief covers " + (total - w);
  }

  function toggleWaive() {
    var finding = (GATE.findings || [])[selected];
    if (!finding || !finding.key) return;

    var now = !waived[finding.key];
    waived[finding.key] = now;
    if (!now) delete waived[finding.key];

    var el = optionEls[selected];
    if (el) el.classList.toggle("sp-gate-waived", now);
    updateProgress();
    // The host owns the durable set — it echoes it back through the next render's payload, so
    // this optimistic flip and the persisted state can never drift for long.
    sendToHost("gateWaive", { key: finding.key, waived: now });
  }

  function copyBrief() {
    var covered = (GATE.findings || []).length - waivedCount();
    sendToHost("copyFixBrief", {});
    announce(covered === 1
      ? "Fix brief copied — 1 finding"
      : "Fix brief copied — " + covered + " findings");
  }

  // The hands-free variant of copyBrief: same triaged brief, handed to the host's Claude runner.
  // The optimistic announcement covers the gap until the host's "running" state comes back
  // through the next render's payload.
  function reviseWithClaude() {
    if (!CLAUDE || !CLAUDE.available) return;
    if (CLAUDE.state === "running") {
      announce("Claude is already revising — saves land here as they happen");
      return;
    }
    var covered = (GATE.findings || []).length - waivedCount();
    if (covered <= 0) {
      announce("Every finding is waived — nothing to hand to Claude");
      return;
    }
    sendToHost("claudeRevise", {});
    announce(covered === 1
      ? "Handed to Claude — 1 finding. Saves land here live."
      : "Handed to Claude — " + covered + " findings. Saves land here live.");
  }

  function announce(text) {
    if (!panelEl) return;
    if (!copiedEl) {
      copiedEl = document.createElement("div");
      copiedEl.className = "sp-gate-copied";
      copiedEl.setAttribute("role", "status");
      panelEl.appendChild(copiedEl);
    }
    copiedEl.textContent = text;
    copiedEl.classList.add("sp-gate-copied-visible");
    if (copiedTimer) clearTimeout(copiedTimer);
    copiedTimer = setTimeout(function () {
      if (copiedEl) copiedEl.classList.remove("sp-gate-copied-visible");
    }, 2200);
  }

  // -------- Collapsed-panel revision keys (the reviewer's half of the loop) --------

  // The reviewer's comments come through the annotations payload; a comment counts here while it
  // is unresolved and still anchored (orphans travel separately and never reach the brief).
  function unresolvedCommentCount() {
    var annot = window.__spectacleAnnotations__ || {};
    var list = annot.comments || [];
    var n = 0;
    for (var i = 0; i < list.length; i++) {
      if (!list[i].resolvedAt) n++;
    }
    return n;
  }

  function orphanFocused(target) {
    var el = target || document.activeElement;
    return !!(el && el.classList && el.classList.contains("sp-orphan-row"));
  }

  // The collapsed keys have no panel to write status into, so they speak through the ambient
  // hint toast keynav owns — the same element the re-anchor flow already announces through.
  function hint(text) {
    if (window.__sp_flash_hint) window.__sp_flash_hint(text);
  }

  function copyCommentBrief() {
    var n = unresolvedCommentCount();
    if (!n) { hint("No unresolved comments — nothing to copy"); return; }
    sendToHost("copyCommentBrief", {});
    hint(n === 1
      ? "Revision brief copied — 1 comment"
      : "Revision brief copied — " + n + " comments");
  }

  // The hands-free variant of copyCommentBrief, mirroring reviseWithClaude: same brief, handed to
  // the host's Claude runner, with every refusal explained rather than swallowed.
  function reviseCommentsWithClaude() {
    if (!CLAUDE || !CLAUDE.available) {
      hint("Claude CLI not found — c copies the brief instead");
      return;
    }
    if (CLAUDE.state === "running") {
      hint("Claude is already revising — saves land here as they happen");
      return;
    }
    var n = unresolvedCommentCount();
    if (!n) { hint("No unresolved comments — nothing to hand to Claude"); return; }
    sendToHost("claudeReviseComments", {});
    hint(n === 1
      ? "Handed to Claude — 1 comment. Saves land here live."
      : "Handed to Claude — " + n + " comments. Saves land here live.");
  }

  // -------- Claude run chip --------

  // Ambient state for the background run, visible without the panel: the reader should never
  // wonder whether anything is happening while Claude works. Rebuilt from the payload on every
  // render — a run's own saves re-render this page, and the chip must ride through that. A clean
  // finish shows nothing here: the loop HUD's toast already announces the save that ended it.
  function buildClaudeChip() {
    if (!CLAUDE || !CLAUDE.available) return;
    if (CLAUDE.state !== "running" && CLAUDE.state !== "failed") return;

    var chip = document.createElement("div");
    chip.id = "sp-claude-chip";
    chip.setAttribute("role", "status");

    if (CLAUDE.state === "running") {
      var pulse = document.createElement("span");
      pulse.className = "sp-claude-pulse";
      pulse.setAttribute("aria-hidden", "true");
      pulse.textContent = "✳";
      chip.appendChild(pulse);
      // The detail is the run's live progress from its stream-json feed ("turn 3 · 2 edits") —
      // the reader sees the run working, not just a pulse that could mean anything.
      chip.appendChild(document.createTextNode("Claude is revising this document…" +
        (CLAUDE.detail ? " " + CLAUDE.detail : "")));
    } else {
      chip.className = "sp-claude-chip-failed";
      chip.textContent = "Claude revision failed" + (CLAUDE.detail ? " — " + CLAUDE.detail : "");
    }

    document.body.appendChild(chip);
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
    store(STORAGE_SELECTED, String(selected));
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
    store(STORAGE_OPEN, "1");
  }

  function close() {
    if (!panelEl || !isOpen()) return;
    panelEl.hidden = true;
    if (badgeEl) badgeEl.setAttribute("aria-expanded", "false");
    if (prevFocus && typeof prevFocus.focus === "function") prevFocus.focus();
    prevFocus = null;
    store(STORAGE_OPEN, null);
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
    // The revision-loop timeline is registered after this script, so its open panel cannot swallow
    // "v" itself the way this panel swallows "l" — it has to be respected from this side.
    var loop = document.getElementById("sp-loop-panel");
    if (loop && !loop.hidden) return true;
    var el = target || document.activeElement;
    if (el && el.isContentEditable === true) return true;
    return !!(el && (el.tagName === "TEXTAREA" || el.tagName === "INPUT"));
  }

  function onKeyDown(e) {
    if (!panelEl) return;

    if (!isOpen()) {
      var bare = !e.ctrlKey && !e.metaKey && !e.altKey;
      // "v" for verdict, bare only.
      if (e.key === "v" && bare) {
        if (blockedTarget(e.target)) return;
        e.preventDefault();
        // stopImmediatePropagation, not stopPropagation: the other overlays are capture listeners
        // on this same element, so only the "immediate" form keeps the key away from them.
        e.stopImmediatePropagation();
        open();
        return;
      }
      // With the panel collapsed, the same two revision keys act on the *reviewer's* half of the
      // loop: "c" copies the brief built from the unresolved comments and "a" hands it to Claude.
      // Opening the panel is the modifier — the keys then cover the triaged findings instead.
      if (e.key === "c" && bare) {
        if (blockedTarget(e.target)) return;
        e.preventDefault();
        e.stopImmediatePropagation();
        copyCommentBrief();
        return;
      }
      if (e.key === "a" && bare) {
        // A focused orphan row owns "a" (begin re-anchor): the narrower gesture wins.
        if (blockedTarget(e.target) || orphanFocused(e.target)) return;
        e.preventDefault();
        e.stopImmediatePropagation();
        reviseCommentsWithClaude();
        return;
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
      case " ": e.preventDefault(); toggleWaive(); return;
      case "c": e.preventDefault(); copyBrief(); return;
      case "a": e.preventDefault(); reviseWithClaude(); return;
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
    buildClaudeChip();
    document.addEventListener("keydown", onKeyDown, true);

    // Triage survives the re-render that follows every save: the panel reopens where it was, so
    // waiving five findings while an agent revises underneath is not five panel re-openings.
    if (stored(STORAGE_OPEN) === "1") {
      var restored = parseInt(stored(STORAGE_SELECTED) || "0", 10);
      if (isFinite(restored) && restored >= 0 && restored < optionEls.length) selected = restored;
      open();
    }
  }
})();
