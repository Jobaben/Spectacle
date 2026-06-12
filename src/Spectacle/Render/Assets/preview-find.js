(function () {
  "use strict";
  // preview-find.js — in-document text search ("Find", Ctrl+F) for Spectacle.
  //
  // Highlighting uses the CSS Custom Highlight API (CSS.highlights + Range), so
  // matches are painted without mutating the DOM — the .md-block structure,
  // data-* attributes and keynav focus that the rest of the preview depends on
  // are left completely untouched. Where the runtime lacks the API we fall back
  // to a plain text selection on the current match.

  var HAS_HL = typeof Highlight !== "undefined" &&
    typeof CSS !== "undefined" && CSS && CSS.highlights;

  var barEl = null;
  var inputEl = null;
  var countEl = null;
  var ranges = [];
  var current = -1;
  var prevFocus = null;

  // -------- Bar construction --------

  function build() {
    var bar = document.createElement("div");
    bar.id = "sp-find";
    bar.setAttribute("role", "search");
    bar.hidden = true;

    inputEl = document.createElement("input");
    inputEl.id = "sp-find-input";
    inputEl.type = "text";
    inputEl.setAttribute("aria-label", "Find in document");
    inputEl.setAttribute("placeholder", "Find");
    inputEl.setAttribute("autocomplete", "off");
    inputEl.setAttribute("spellcheck", "false");

    countEl = document.createElement("span");
    countEl.id = "sp-find-count";
    countEl.setAttribute("aria-live", "polite");

    var prev = mkButton("prev", "Previous match", "↑", function () { step(-1); });
    var next = mkButton("next", "Next match", "↓", function () { step(1); });
    var close = mkButton("close", "Close find", "✕", hide);

    bar.appendChild(inputEl);
    bar.appendChild(countEl);
    bar.appendChild(prev);
    bar.appendChild(next);
    bar.appendChild(close);
    document.body.appendChild(bar);

    inputEl.addEventListener("input", function () { search(inputEl.value); });
    return bar;
  }

  function mkButton(act, label, glyph, onClick) {
    var b = document.createElement("button");
    b.type = "button";
    b.setAttribute("data-act", act);
    b.setAttribute("aria-label", label);
    b.title = label;
    b.textContent = glyph;
    // Keep focus in the input so typing continues uninterrupted after a click.
    b.addEventListener("mousedown", function (e) { e.preventDefault(); });
    b.addEventListener("click", onClick);
    return b;
  }

  function isOpen() { return barEl && !barEl.hidden; }

  // -------- Search --------

  function clearHighlights() {
    ranges = [];
    current = -1;
    if (HAS_HL) {
      CSS.highlights.delete("sp-find");
      CSS.highlights.delete("sp-find-current");
    }
  }

  function search(query) {
    clearHighlights();
    var q = (query || "").toLowerCase();
    if (!q) { updateCount(); return; }

    var main = document.querySelector("main");
    if (!main) { updateCount(); return; }

    var walker = document.createTreeWalker(main, NodeFilter.SHOW_TEXT, {
      acceptNode: function (n) {
        if (!n.nodeValue) return NodeFilter.FILTER_REJECT;
        var p = n.parentNode;
        if (p && (p.nodeName === "SCRIPT" || p.nodeName === "STYLE")) {
          return NodeFilter.FILTER_REJECT;
        }
        return NodeFilter.FILTER_ACCEPT;
      }
    });

    var node;
    while ((node = walker.nextNode())) {
      var hay = node.nodeValue.toLowerCase();
      var from = 0;
      var idx;
      while ((idx = hay.indexOf(q, from)) !== -1) {
        var r = document.createRange();
        r.setStart(node, idx);
        r.setEnd(node, idx + q.length);
        ranges.push(r);
        from = idx + q.length;
      }
    }

    if (HAS_HL && ranges.length) {
      CSS.highlights.set("sp-find", newHighlight(ranges));
    }

    if (ranges.length) {
      current = 0;
      reveal();
    }
    updateCount();
  }

  // Highlight's constructor is variadic over ranges; spread to support any count.
  function newHighlight(rs) {
    return new (Function.prototype.bind.apply(Highlight, [null].concat(rs)))();
  }

  function step(delta) {
    if (!ranges.length) return;
    current = (current + delta + ranges.length) % ranges.length;
    reveal();
    updateCount();
  }

  function reveal() {
    var r = ranges[current];
    if (!r) return;

    if (HAS_HL) {
      CSS.highlights.set("sp-find-current", newHighlight([r]));
    } else {
      // No Highlight API: use the live selection so the current match is visible.
      var sel = window.getSelection();
      sel.removeAllRanges();
      sel.addRange(r.cloneRange());
    }

    var anchor = r.startContainer;
    var el = anchor.nodeType === 1 ? anchor : anchor.parentElement;
    if (el && el.scrollIntoView) el.scrollIntoView({ block: "center" });
  }

  function updateCount() {
    if (!countEl) return;
    if (!inputEl.value) { countEl.textContent = ""; return; }
    countEl.textContent = ranges.length
      ? (current + 1) + " / " + ranges.length
      : "No results";
    countEl.classList.toggle("sp-find-empty", ranges.length === 0);
  }

  // -------- Open / close --------

  function show() {
    if (!barEl) barEl = build();
    if (window.__sp_outline_close) window.__sp_outline_close();
    if (!isOpen()) prevFocus = document.activeElement;
    barEl.hidden = false;
    inputEl.focus();
    inputEl.select();
    if (inputEl.value) search(inputEl.value);
  }

  function hide() {
    if (!isOpen()) return;
    barEl.hidden = true;
    clearHighlights();
    if (!HAS_HL) {
      var sel = window.getSelection();
      if (sel) sel.removeAllRanges();
    }
    // Return focus to the block that had it, so keyboard navigation resumes
    // where the reader left off.
    if (prevFocus && prevFocus !== inputEl && document.contains(prevFocus)) {
      prevFocus.focus({ preventScroll: true });
    }
    prevFocus = null;
  }
  window.__sp_find_open = show;
  window.__sp_find_close = hide;

  // -------- Key handling (capture phase: runs before keynav) --------

  function suppressed() {
    // Don't steal Ctrl+F from the comment composer, the help overlay, or while
    // re-anchoring.
    if (document.body.classList.contains("sp-reanchor-mode")) return true;
    var help = document.getElementById("sp-help");
    if (help && !help.hidden) return true;
    var a = document.activeElement;
    return !!(a && a.tagName === "TEXTAREA");
  }

  function onKeyCapture(e) {
    var ctrl = e.ctrlKey || e.metaKey;

    if (ctrl && !e.altKey && (e.key === "f" || e.key === "F")) {
      if (suppressed()) return;
      e.preventDefault();
      e.stopImmediatePropagation();
      show();
      return;
    }

    if (!isOpen()) return;

    if (e.key === "Escape") {
      e.preventDefault(); e.stopImmediatePropagation(); hide(); return;
    }
    if (e.key === "Enter") {
      e.preventDefault(); e.stopImmediatePropagation();
      step(e.shiftKey ? -1 : 1); return;
    }
    if (e.key === "F3") {
      e.preventDefault(); e.stopImmediatePropagation();
      step(e.shiftKey ? -1 : 1); return;
    }

    // While the bar is open, keep every other key away from the keynav
    // dispatcher (so "g", "?", arrows, etc. type into the field rather than
    // navigating), but let the input receive the character normally.
    e.stopImmediatePropagation();
  }

  document.addEventListener("keydown", onKeyCapture, true);
})();
