(function () {
  "use strict";
  var data = window.__spectacleAnnotations__ || { comments: [], orphaned: [] };

  function post(type, payload) {
    if (!window.chrome || !window.chrome.webview) return;
    window.chrome.webview.postMessage(JSON.stringify(Object.assign({ type: type }, payload)));
  }

  function uuid() {
    return ([1e7]+-1e3+-4e3+-8e3+-1e11).replace(/[018]/g, function (c) {
      return (c ^ crypto.getRandomValues(new Uint8Array(1))[0] & 15 >> c / 4).toString(16);
    });
  }

  function escapeHtml(s) {
    return s.replace(/[&<>"']/g, function (ch) {
      return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[ch];
    });
  }

  function formatTimestamp(iso) {
    try {
      var d = new Date(iso);
      return d.toLocaleString();
    } catch (e) { return iso; }
  }

  function buildCard(comment, index) {
    var card = document.createElement("article");
    card.className = "sp-card";
    if (comment.resolvedAt) card.className += " sp-resolved";
    card.setAttribute("role", "comment");
    card.setAttribute("data-comment-id", comment.id);
    card.setAttribute("aria-label",
      "Revision request " + index + " on " +
      comment.blockAnchor.kind + " at line " + comment.blockAnchor.line);

    var header = document.createElement("div");
    header.className = "sp-header";
    header.textContent = "Revision request #" + index;
    card.appendChild(header);

    var meta = document.createElement("div");
    meta.className = "sp-meta";
    meta.textContent = formatTimestamp(comment.createdAt) +
      (comment.resolvedAt ? " · Resolved" : "");
    card.appendChild(meta);

    var body = document.createElement("div");
    body.className = "sp-body";
    body.textContent = comment.body;
    card.appendChild(body);

    var actions = document.createElement("div");
    actions.className = "sp-actions";

    var editBtn = document.createElement("button");
    editBtn.textContent = "Edit";
    editBtn.addEventListener("click", function () { startCompose(comment.blockAnchor.blockIdAtRender, comment); });

    var resolveBtn = document.createElement("button");
    resolveBtn.textContent = comment.resolvedAt ? "Reopen" : "Resolve";
    resolveBtn.addEventListener("click", function () {
      post("commentResolve", { commentId: comment.id, resolved: !comment.resolvedAt });
    });

    var deleteBtn = document.createElement("button");
    deleteBtn.textContent = "Delete";
    deleteBtn.addEventListener("click", function () {
      post("commentDelete", { commentId: comment.id });
    });

    actions.appendChild(editBtn);
    actions.appendChild(resolveBtn);
    actions.appendChild(deleteBtn);
    card.appendChild(actions);

    return card;
  }

  function startCompose(blockId, existing) {
    var block = document.querySelector('[data-block-id="' + blockId + '"]');
    if (!block) return;

    var existingComposer = document.querySelector(".sp-composer");
    if (existingComposer) existingComposer.remove();

    var composer = document.createElement("div");
    composer.className = "sp-card sp-composer";

    var header = document.createElement("div");
    header.className = "sp-header";
    header.textContent = existing ? "Edit revision request" : "New revision request";
    composer.appendChild(header);

    var textarea = document.createElement("textarea");
    textarea.value = existing ? existing.body : "";
    composer.appendChild(textarea);

    var actions = document.createElement("div");
    actions.className = "sp-actions";

    var saveBtn = document.createElement("button");
    saveBtn.className = "sp-primary";
    saveBtn.textContent = "Save";
    saveBtn.disabled = textarea.value.trim().length === 0;
    saveBtn.addEventListener("click", function () { commit(); });

    var cancelBtn = document.createElement("button");
    cancelBtn.textContent = "Cancel";
    cancelBtn.addEventListener("click", function () { composer.remove(); });

    textarea.addEventListener("input", function () {
      saveBtn.disabled = textarea.value.trim().length === 0;
    });
    textarea.addEventListener("keydown", function (e) {
      if (e.key === "Escape") { composer.remove(); }
      else if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) {
        if (!saveBtn.disabled) commit();
      }
    });

    actions.appendChild(saveBtn);
    actions.appendChild(cancelBtn);
    composer.appendChild(actions);

    function commit() {
      var body = textarea.value.trim();
      if (body.length === 0) return;
      if (existing) {
        post("commentSave", { commentId: existing.id, blockId: blockId, body: body });
      } else {
        post("commentSave", { commentId: uuid(), blockId: blockId, body: body });
      }
    }

    block.insertAdjacentElement("afterend", composer);
    textarea.focus();
  }

  function renderExistingComments() {
    var byBlock = {};
    (data.comments || []).forEach(function (c) {
      var key = c.blockAnchor.blockIdAtRender;
      if (!key) return;
      (byBlock[key] = byBlock[key] || []).push(c);
    });

    Object.keys(byBlock).forEach(function (blockId) {
      var block = document.querySelector('[data-block-id="' + blockId + '"]');
      if (!block) return;
      var comments = byBlock[blockId];

      block.setAttribute("data-has-comments", "true");
      block.setAttribute("data-comment-count", String(comments.length));

      var anchor = block;
      comments.forEach(function (c, i) {
        var card = buildCard(c, i + 1);
        anchor.insertAdjacentElement("afterend", card);
        anchor = card;
      });
    });
  }

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
      li.innerHTML = "<strong>" + escapeHtml(c.blockAnchor.kind) + "</strong>: " +
        escapeHtml(c.blockAnchor.leadingText) + " — " +
        '<button type="button" data-action="delete" data-id="' + escapeHtml(c.id) + '">Delete</button> ' +
        '<button type="button" data-action="reanchor" data-id="' + escapeHtml(c.id) + '">Re-anchor manually</button>';
      list.appendChild(li);
    });
    panel.appendChild(list);

    list.addEventListener("click", function (e) {
      var btn = e.target.closest("button");
      if (!btn) return;
      var id = btn.getAttribute("data-id");
      var action = btn.getAttribute("data-action");
      if (action === "delete") post("commentDelete", { commentId: id });
      else if (action === "reanchor") beginReanchor(id);
    });

    main.insertBefore(panel, main.firstChild);
  }

  function beginReanchor(commentId) {
    document.body.classList.add("sp-reanchor-mode");
    function onClick(e) {
      var block = e.target.closest(".md-block");
      if (!block) return;
      e.preventDefault();
      e.stopPropagation();
      document.body.classList.remove("sp-reanchor-mode");
      document.removeEventListener("click", onClick, true);
      post("orphanReanchor", {
        commentId: commentId,
        blockId: block.getAttribute("data-block-id")
      });
    }
    document.addEventListener("click", onClick, true);
  }

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
      b.addEventListener("keydown", function (e) {
        if (e.key === "Enter") {
          e.preventDefault();
          startCompose(b.getAttribute("data-block-id"), null);
        }
      });
    });
  }

  function init() {
    renderOrphans();
    renderExistingComments();
    wireBlockClicks();
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", init);
  } else {
    init();
  }
})();
