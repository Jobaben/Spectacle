(function () {
  "use strict";
  // preview-loop.js — the revision-loop HUD ("l") for Spectacle.
  //
  // The timeline comes from window.__spectacleLoop__, emitted by PreviewHtml from the LoopSession
  // the pipeline advances on every real save. The reader already re-renders and re-grades when an
  // agent rewrites the file; this layer is the memory of that loop: a toast saying what the save
  // just fixed and broke, an edge marker on every block the revision touched, and a panel with the
  // whole session's convergence — iteration by iteration, one bar per save.
  //
  // Everything here is presentation. The counts are the gate's own counts and the deltas are
  // ReviewDelta's; if this HUD ever disagreed with `--review --baseline`, the HUD would be wrong.

  var LOOP = window.__spectacleLoop__ || null;

  var STORAGE_SEEN = "spectacle.loopLastSeen";
  var STORAGE_OPEN = "spectacle.loopPanelOpen";
  var TOAST_MS = 6000;

  var pillEl = null;
  var panelEl = null;
  var toastEl = null;
  var toastTimer = null;
  var prevFocus = null;

  function read(key) {
    try { return sessionStorage.getItem(key); } catch (err) { return null; }
  }
  function write(key, value) {
    try {
      if (value === null) sessionStorage.removeItem(key);
      else sessionStorage.setItem(key, value);
    } catch (err) { /* ignore */ }
  }

  function history() { return (LOOP && LOOP.history) || []; }
  function latest() { var h = history(); return h.length ? h[h.length - 1] : null; }
  function previous() { var h = history(); return h.length > 1 ? h[h.length - 2] : null; }

  // The panel and its "l" shortcut answer from the first render — a reader who asks for the
  // timeline should always get it, even when it only has the opening iteration to show. The
  // *ambient* HUD (pill, toast, changed-block markers) still waits for the loop to actually
  // loop: iteration 1 is just "the document was opened", and advertising it would teach the
  // reader to ignore the pill.
  var active = !!(LOOP && latest());
  var looped = !!(active && LOOP.iteration >= 2);

  // -------- Changed-block markers --------

  function markChangedBlocks() {
    var ids = LOOP.changedBlockIds || [];
    for (var i = 0; i < ids.length; i++) {
      var el = document.querySelector('[data-block-id="' + cssEscape(ids[i]) + '"]');
      if (el) el.classList.add("sp-loop-changed");
    }
  }

  function cssEscape(s) {
    if (window.CSS && CSS.escape) return CSS.escape(s);
    return String(s).replace(/[^a-zA-Z0-9_-]/g, "\\$&");
  }

  // -------- Trend --------

  function trend() {
    var now = latest();
    var prev = previous();
    if (now.blocking === 0) return "clean";
    if (!prev) return "holding";
    if (now.blocking < prev.blocking) return "converging";
    if (now.blocking > prev.blocking) return "diverging";
    return "holding";
  }

  function trendText() {
    var now = latest();
    var prev = previous();
    switch (trend()) {
      case "clean": return "Clean — the gate passes.";
      case "converging": return "Converging — " + now.blocking + " blocking, down from " + prev.blocking + ".";
      case "diverging": return "Diverging — " + now.blocking + " blocking, up from " + prev.blocking + ".";
      default: return "Holding — " + now.blocking + " blocking, unchanged.";
    }
  }

  // -------- Toast --------

  function deltaParts(iter) {
    return { fixed: iter.fixed || 0, introduced: iter.introduced || 0, remaining: iter.blocking };
  }

  function buildToast() {
    var it = latest();
    var d = deltaParts(it);
    var toast = document.createElement("div");
    toast.id = "sp-loop-toast";
    toast.setAttribute("role", "status");

    var iterSpan = document.createElement("span");
    iterSpan.className = "sp-loop-iter";
    iterSpan.textContent = "Iteration " + it.n;
    toast.appendChild(iterSpan);

    var fixedSpan = document.createElement("span");
    fixedSpan.className = "sp-loop-fixed";
    fixedSpan.textContent = "✓ " + d.fixed + " fixed";
    toast.appendChild(fixedSpan);

    if (d.introduced) {
      var newSpan = document.createElement("span");
      newSpan.className = "sp-loop-new";
      newSpan.textContent = "+" + d.introduced + " new";
      toast.appendChild(newSpan);
    }

    var remainSpan = document.createElement("span");
    remainSpan.className = "sp-loop-remain";
    remainSpan.textContent = d.remaining === 0 ? "gate passes" : d.remaining + " blocking remain";
    toast.appendChild(remainSpan);

    toast.setAttribute("aria-label",
      "Revision " + it.n + ": " + d.fixed + " finding(s) fixed, " + d.introduced +
      " introduced, " + d.remaining + " blocking remain. Press l for the loop timeline.");

    toast.addEventListener("click", function () { hideToast(); open(); });
    document.body.appendChild(toast);
    return toast;
  }

  function maybeToast() {
    // One toast per iteration: a theme flip re-renders the same iteration and must not repeat it.
    var seen = parseInt(read(STORAGE_SEEN) || "0", 10);
    var it = latest();
    if (!isFinite(seen)) seen = 0;
    if (it.n <= seen) return;
    write(STORAGE_SEEN, String(it.n));

    toastEl = buildToast();
    toastTimer = setTimeout(function () { hideToast(true); }, TOAST_MS);
  }

  // The timeout fades the toast out; anything that supersedes it (opening the panel, clicking it)
  // removes it outright — a toast lingering translucent under a panel reads as a stuck control.
  function hideToast(fade) {
    if (!toastEl) return;
    if (toastTimer) { clearTimeout(toastTimer); toastTimer = null; }
    var el = toastEl;
    toastEl = null;
    if (!fade) {
      if (el.parentNode) el.parentNode.removeChild(el);
      return;
    }
    el.classList.add("sp-loop-leaving");
    setTimeout(function () { if (el.parentNode) el.parentNode.removeChild(el); }, 650);
  }

  // -------- Pill --------

  function buildPill() {
    var it = latest();
    var pill = document.createElement("button");
    pill.id = "sp-loop-pill";
    pill.type = "button";
    pill.setAttribute("aria-expanded", "false");
    pill.setAttribute("aria-controls", "sp-loop-panel");

    var arrowFor = { clean: "✓", converging: "↓", diverging: "↑", holding: "→" };
    var classFor = {
      clean: "sp-loop-trend-down", converging: "sp-loop-trend-down",
      diverging: "sp-loop-trend-up", holding: "sp-loop-trend-flat"
    };
    var t = trend();

    var label = document.createElement("span");
    label.textContent = "↻ iter " + it.n;
    pill.appendChild(label);

    var arrow = document.createElement("span");
    arrow.className = classFor[t];
    arrow.setAttribute("aria-hidden", "true");
    arrow.textContent = arrowFor[t];
    pill.appendChild(arrow);

    pill.setAttribute("aria-label",
      "Revision loop: iteration " + it.n + ". " + trendText() + " Press l for the timeline.");
    pill.addEventListener("click", function () { toggle(); });
    document.body.appendChild(pill);
    return pill;
  }

  // -------- Panel --------

  function countsText(iter) {
    var parts = [];
    if (iter.errors) parts.push(iter.errors + "E");
    if (iter.warnings) parts.push(iter.warnings + "W");
    if (iter.advisories) parts.push(iter.advisories + "A");
    return parts.length ? parts.join(" ") : "clean";
  }

  function timeText(iso) {
    var d = new Date(iso);
    if (isNaN(d.getTime())) return "";
    var pad = function (n) { return (n < 10 ? "0" : "") + n; };
    return pad(d.getHours()) + ":" + pad(d.getMinutes()) + ":" + pad(d.getSeconds());
  }

  function buildPanel() {
    var panel = document.createElement("div");
    panel.id = "sp-loop-panel";
    panel.setAttribute("role", "dialog");
    panel.setAttribute("aria-modal", "true");
    panel.setAttribute("aria-labelledby", "sp-loop-title");
    panel.setAttribute("tabindex", "-1");
    panel.hidden = true;

    var title = document.createElement("h2");
    title.id = "sp-loop-title";
    title.textContent = "Revision loop";
    panel.appendChild(title);

    var headline = document.createElement("p");
    headline.className = "sp-loop-headline";
    var verdictSpan = document.createElement("span");
    verdictSpan.className = "sp-loop-verdict-" + trend();
    verdictSpan.textContent = trendText();
    headline.appendChild(verdictSpan);
    headline.appendChild(document.createTextNode(
      " " + history().length + " iteration(s) this session."));
    panel.appendChild(headline);

    panel.appendChild(buildSparkline());
    panel.appendChild(buildTimeline());

    var footer = document.createElement("div");
    footer.className = "sp-loop-footer";
    footer.textContent = "Click a new finding to jump to it · Esc to close";
    panel.appendChild(footer);

    document.body.appendChild(panel);
    return panel;
  }

  function buildSparkline() {
    var spark = document.createElement("div");
    spark.id = "sp-loop-spark";
    spark.setAttribute("role", "img");

    var h = history();
    var max = 1;
    for (var i = 0; i < h.length; i++) if (h[i].blocking > max) max = h[i].blocking;

    var described = [];
    for (var j = 0; j < h.length; j++) {
      var bar = document.createElement("span");
      bar.className = "sp-loop-bar" +
        (h[j].blocking === 0 ? " sp-loop-bar-clean" : "") +
        (j === h.length - 1 ? " sp-loop-bar-latest" : "");
      // A clean iteration keeps a visible stub via min-height; everything else scales linearly.
      bar.style.height = Math.round((h[j].blocking / max) * 100) + "%";
      bar.title = "Iteration " + h[j].n + ": " + h[j].blocking + " blocking";
      spark.appendChild(bar);
      described.push(h[j].blocking);
    }

    spark.setAttribute("aria-label",
      "Blocking findings per iteration: " + described.join(", ") + ".");
    return spark;
  }

  function buildTimeline() {
    var list = document.createElement("ul");
    list.id = "sp-loop-list";

    var h = history();
    // Newest first: the row the reader came for is the save that just happened.
    for (var i = h.length - 1; i >= 0; i--) {
      list.appendChild(buildRow(h[i], i === h.length - 1));
    }
    return list;
  }

  function buildRow(iter, isLatest) {
    var li = document.createElement("li");
    li.className = "sp-loop-row";

    var head = document.createElement("div");
    head.className = "sp-loop-row-head";

    var n = document.createElement("span");
    n.className = "sp-loop-row-n";
    n.textContent = "#" + iter.n;
    head.appendChild(n);

    var time = document.createElement("span");
    time.className = "sp-loop-row-time";
    time.textContent = timeText(iter.at);
    head.appendChild(time);

    var counts = document.createElement("span");
    counts.className = "sp-loop-row-counts";
    counts.textContent = countsText(iter);
    head.appendChild(counts);

    li.appendChild(head);

    if (iter.n > 1) {
      var delta = document.createElement("div");
      delta.className = "sp-loop-row-delta";
      var fx = document.createElement("span");
      fx.className = "sp-loop-fixed";
      fx.textContent = "✓ " + (iter.fixed || 0) + " fixed";
      delta.appendChild(fx);
      delta.appendChild(document.createTextNode(" · "));
      var nw = document.createElement("span");
      nw.className = "sp-loop-new";
      nw.textContent = "+" + (iter.introduced || 0) + " new";
      delta.appendChild(nw);
      li.appendChild(delta);
    }

    // Finding-level detail travels only with the latest iteration — it is the payload's `delta`,
    // and it is what makes the row actionable rather than a scoreboard.
    if (isLatest && LOOP.delta) {
      var detail = document.createElement("ul");
      detail.className = "sp-loop-detail";
      var fixed = LOOP.delta.fixed || [];
      var introduced = LOOP.delta.introduced || [];
      for (var i = 0; i < introduced.length; i++) detail.appendChild(detailItem(introduced[i], false));
      for (var j = 0; j < fixed.length; j++) detail.appendChild(detailItem(fixed[j], true));
      if (detail.childNodes.length) li.appendChild(detail);
    }

    return li;
  }

  function detailItem(f, isFixed) {
    var li = document.createElement("li");
    li.className = isFixed ? "sp-loop-detail-fixed" : "sp-loop-detail-new";

    var sign = document.createElement("span");
    sign.className = "sp-loop-sign";
    sign.textContent = isFixed ? "✓" : "+";
    li.appendChild(sign);

    var rule = document.createElement("span");
    rule.className = "sp-loop-detail-rule";
    rule.textContent = f.rule;
    li.appendChild(rule);

    var msg = document.createElement("span");
    msg.className = "sp-loop-detail-msg";
    msg.textContent = f.message;
    li.appendChild(msg);

    if (!isFixed) {
      li.setAttribute("title", "Jump to line " + f.line);
      li.addEventListener("click", function () { close(); jumpToLine(f.line); });
    }
    return li;
  }

  // Same landing rule as the gate panel: the target is the last block starting at or before the
  // finding's line, and it gets the gate's own flash so a jump looks the same wherever it started.
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
    target.classList.remove("sp-gate-flash");
    void target.offsetWidth;
    target.classList.add("sp-gate-flash");
    if (typeof target.focus === "function") target.focus({ preventScroll: true });
  }

  // -------- Open / close --------

  function isOpen() { return panelEl && !panelEl.hidden; }

  function open() {
    if (!panelEl || isOpen()) return;
    prevFocus = document.activeElement;
    panelEl.hidden = false;
    if (pillEl) pillEl.setAttribute("aria-expanded", "true");
    panelEl.focus();
    write(STORAGE_OPEN, "1");
  }

  function close() {
    if (!panelEl || !isOpen()) return;
    panelEl.hidden = true;
    if (pillEl) pillEl.setAttribute("aria-expanded", "false");
    if (prevFocus && typeof prevFocus.focus === "function") prevFocus.focus();
    prevFocus = null;
    write(STORAGE_OPEN, null);
  }

  function toggle() { if (isOpen()) close(); else open(); }

  // -------- Key handling (capture phase; registered after the gate, so an open gate panel
  // keeps every key including "l") --------

  function blockedTarget(target) {
    if (document.body.classList && document.body.classList.contains("sp-reanchor-mode")) return true;
    var help = document.getElementById("sp-help");
    if (help && !help.hidden) return true;
    var gate = document.getElementById("sp-gate-panel");
    if (gate && !gate.hidden) return true;
    var el = target || document.activeElement;
    if (el && el.isContentEditable === true) return true;
    return !!(el && (el.tagName === "TEXTAREA" || el.tagName === "INPUT"));
  }

  function onKeyDown(e) {
    if (!panelEl) return;

    if (!isOpen()) {
      // "l" for loop, bare only.
      if (e.key === "l" && !e.ctrlKey && !e.metaKey && !e.altKey) {
        if (blockedTarget(e.target)) return;
        e.preventDefault();
        e.stopImmediatePropagation();
        hideToast();
        open();
      }
      return;
    }

    // While open, keep keys away from the document — the same containment the gate panel applies.
    // Earlier-registered overlays (find, outline, gate) still take their own shortcuts first; the
    // gate and outline guards treat this open panel as owning the screen for exactly that reason.
    e.stopImmediatePropagation();

    switch (e.key) {
      case "Escape": e.preventDefault(); close(); return;
      case "l": e.preventDefault(); close(); return;
      case "ArrowDown": e.preventDefault(); scrollList(60); return;
      case "ArrowUp": e.preventDefault(); scrollList(-60); return;
      default: e.preventDefault(); return;
    }
  }

  function scrollList(dy) {
    var list = document.getElementById("sp-loop-list");
    if (list) list.scrollTop += dy;
  }

  // -------- Init --------

  if (active) {
    if (looped) {
      markChangedBlocks();
      pillEl = buildPill();
    }
    panelEl = buildPanel();
    document.addEventListener("keydown", onKeyDown, true);

    // The panel survives a re-render (the agent saving again while the timeline is open). An
    // open timeline already announces the change, so it also absorbs the toast for this
    // iteration; otherwise the toast fires once per iteration this browser session.
    if (read(STORAGE_OPEN) === "1") {
      open();
      write(STORAGE_SEEN, String(latest().n));
    } else if (looped) {
      maybeToast();
    }
  }
})();
