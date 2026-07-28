(function () {
  "use strict";
  // preview-mermaid.js — draws the ```mermaid fences MermaidCodeBlockRenderer emitted.
  //
  // Configuration comes from window.__spectacleMermaid__, built by MermaidAssets.ConfigJson from
  // the C# side so a diagram is painted in the same palette (and passes the same contrast tests)
  // as the document around it.
  //
  // Diagrams are drawn one at a time, each in its own try/catch, because the documents Spectacle
  // reads are frequently ones a model wrote: a diagram whose syntax mermaid rejects has to leave
  // that one figure showing its source and let every other diagram on the page draw. mermaid's own
  // startOnLoad pass cannot do that, which is why it is off and this file calls render itself.

  var CONFIG = window.__spectacleMermaid__ || {};
  var PENDING = 'figure[data-mermaid="pending"]';

  function list() {
    return Array.prototype.slice.call(document.querySelectorAll(PENDING));
  }

  function sourceEl(fig) {
    return fig.querySelector(".mermaid-source");
  }

  // The diagram's definition, as written in the fence. textContent decodes the entities the C#
  // renderer escaped, so mermaid sees the original characters.
  function sourceOf(fig) {
    var pre = sourceEl(fig);
    return pre ? pre.textContent : "";
  }

  // The diagram-type keyword, for a fallback label on a diagram that declares no accTitle/accDescr.
  function typeOf(src) {
    var line = String(src).replace(/^\s+/, "").split(/\r?\n/)[0] || "";
    var word = line.split(/[\s;{(]/)[0] || "";
    return word.replace(/[^A-Za-z0-9-]/g, "").toLowerCase();
  }

  // Wraps the source in a collapsed disclosure: the text alternative to the drawing, kept for every
  // diagram that draws successfully. The gate reports a diagram with no accDescr, but a document
  // that has not been through the gate yet should still not be a dead end for a screen reader.
  function stow(fig) {
    var pre = sourceEl(fig);
    if (!pre) return;
    var details = document.createElement("details");
    details.className = "mermaid-source-toggle";
    var summary = document.createElement("summary");
    summary.textContent = "Diagram source";
    details.appendChild(summary);
    fig.insertBefore(details, pre);
    details.appendChild(pre);
  }

  // accTitle/accDescr land as <title>/<desc> immediately under the <svg>. Mermaid also puts a
  // <title> inside individual shapes to carry their tooltip, so the search has to be limited to
  // direct children — otherwise one node's tooltip becomes the whole diagram's name.
  function described(svg) {
    for (var i = 0; i < svg.children.length; i++) {
      var tag = svg.children[i].tagName;
      if (tag === "title" || tag === "desc") return true;
    }
    return false;
  }

  // "flowchart-v2" -> "flowchart", "stateDiagram" -> "state diagram", "gitGraph" -> "git graph".
  function readable(kind) {
    return String(kind)
      .replace(/-v2$/, "")
      .replace(/-beta$/, "")
      .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
      .toLowerCase()
      .trim();
  }

  // Mermaid describes the drawing itself when the diagram declares accTitle/accDescr. When it
  // declares neither, the graphic would reach a screen reader with no name at all, so it gets one
  // that says what kind of diagram it is and admits the description is missing — rather than
  // inventing a description of a picture nobody has read.
  function name(svg, src) {
    if (svg.getAttribute("aria-label") || described(svg)) return;
    var kind = readable(svg.getAttribute("aria-roledescription") || typeOf(src) || "mermaid");
    if (!/(diagram|chart|graph|map|line)$/.test(kind)) kind += " diagram";
    svg.setAttribute("aria-label", kind + ", no description provided");
  }

  function drew(fig, svg, src) {
    var holder = document.createElement("div");
    holder.className = "mermaid-diagram";
    holder.innerHTML = svg;
    var el = holder.querySelector("svg");
    if (el) {
      name(el, src);
      // The figure is the focusable block; the drawing inside it must not be a second tab stop.
      el.setAttribute("focusable", "false");
      el.removeAttribute("tabindex");
    }
    stow(fig);
    fig.insertBefore(holder, fig.firstChild);
    fig.setAttribute("data-mermaid", "done");
  }

  function failed(fig, detail) {
    var note = document.createElement("p");
    note.className = "mermaid-error";
    note.appendChild(document.createTextNode(
      "This diagram could not be drawn. Its source is shown below."));
    if (detail) {
      var pre = document.createElement("span");
      pre.className = "mermaid-error-detail";
      pre.textContent = String(detail);
      note.appendChild(pre);
    }
    fig.insertBefore(note, fig.firstChild);
    fig.setAttribute("data-mermaid", "error");
  }

  // mermaid.render appends a scratch element to the body to measure text in, and does not always
  // remove it when the render throws.
  function sweep(id) {
    var stray = document.getElementById("d" + id);
    if (stray && stray.parentNode) stray.parentNode.removeChild(stray);
  }

  function one(fig, index) {
    var id = "spectacle-mermaid-" + index;
    var src = sourceOf(fig);

    if (!src || !src.replace(/\s/g, "")) {
      failed(fig, "The diagram is empty.");
      return Promise.resolve();
    }

    var started;
    try {
      started = window.mermaid.render(id, src);
    } catch (e) {
      // A synchronous throw: mermaid rejects some malformed input before it returns a promise.
      sweep(id);
      failed(fig, e && e.message ? e.message : e);
      return Promise.resolve();
    }

    return Promise.resolve(started).then(function (result) {
      sweep(id);
      var svg = result && result.svg;
      if (!svg) { failed(fig, "mermaid produced no drawing."); return; }
      drew(fig, svg, src);
    }, function (e) {
      sweep(id);
      failed(fig, e && e.message ? e.message : e);
    });
  }

  function run() {
    var figs = list();
    if (!figs.length) return;

    if (!window.mermaid || typeof window.mermaid.render !== "function") {
      // No bundle: leave every figure pending, which renders as the source. Say why once.
      figs.forEach(function (fig) { failed(fig, "The mermaid renderer did not load."); });
      return;
    }

    try {
      window.mermaid.initialize(CONFIG);
    } catch (e) {
      figs.forEach(function (fig) { failed(fig, e && e.message ? e.message : e); });
      return;
    }

    // Sequentially: mermaid keeps per-render state on a single global, so overlapping renders
    // interleave and produce diagrams drawn with another diagram's configuration.
    figs.reduce(function (chain, fig, i) {
      return chain.then(function () { return one(fig, i); });
    }, Promise.resolve());
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", run);
  } else {
    run();
  }
})();
