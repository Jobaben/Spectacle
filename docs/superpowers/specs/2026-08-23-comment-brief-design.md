---
title: The comment brief — one pair of revision keys, the panel as the modifier
status: implemented
stage: design
---

# The comment brief — design

## Overview

The revision loop had two hand-off channels that did not know about each other. The gate's
findings travelled as a brief: `c` in the findings panel copied the triaged fix brief, `a` handed
it to the Claude CLI, both in an agent-addressed format with an explicit contract. The reviewer's
own comments travelled as a revision plan behind `Ctrl+Shift+C` / `Ctrl+Shift+E` — a different
format, a different pair of keys, and no Claude hand-off at all. From the authoring agent's point
of view there is only one question — *what should I change?* — and the reviewer's asks are usually
the more important half of the answer.

This change gives the comments the same treatment the findings already had, and collapses the key
surface to one pair:

- **Panel collapsed:** `c` copies a revision brief built from every **unresolved** comment; `a`
  hands that brief to the Claude runner.
- **Panel open:** `c` and `a` keep covering the triaged findings, exactly as before.

`v` is therefore both the menu expand *and* the modifier deciding what gets revised. The
`Ctrl+Shift+C` / `Ctrl+Shift+E` chords and the top-bar buttons are removed; `--revision-plan`
remains the headless route for comments.

## Design

### `CommentBriefExporter` (Annotations)

The brief mirrors `FixBriefExporter`'s voice: an explicit how-to-apply contract, bottom-up
ordering (an edit at line 12 shifts every line after it), and each instruction paired with its
block quoted verbatim — the detail the revision plan already carried and the fix brief cannot
(findings have no anchored original). Only unresolved comments participate: a resolved comment is
work already done, and re-issuing it would send the agent revising blocks the reviewer signed off
on. Orphans are excluded for the same reason the revision plan drops them — nothing to quote,
nothing to find.

### Pipeline and host wiring

Two new host messages, `copyCommentBrief` and `claudeReviseComments`, are the comment-side twins
of `copyFixBrief` / `claudeRevise`. They build from the pipeline's own `MatchResult` (the same
matched set the cards render from) and travel the exact same host paths: `CopyTextRequested` for
the clipboard, `ClaudeReviseRequested` for the runner — which already wraps whatever brief it is
given in the in-place contract, so the runner, the chip, and the one-run-at-a-time rule needed no
changes at all. Neither message ever emits an empty brief: no unresolved comments means no event,
because an empty brief would send an agent off to revise nothing.

### `preview-gate.js` (the keys)

The collapsed-panel branch of the gate overlay's capture handler takes bare `c` and `a` under the
same guards as `v` (no other overlay owning the screen, no input focused), plus one narrower-wins
rule: a focused orphan row keeps `a` for the re-anchor flow. Because the collapsed keys have no
panel to write status into, they announce through keynav's ambient hint toast — what was copied
("Revision brief copied — 2 comments"), what was handed over, or why nothing was ("No unresolved
comments", "Claude is already revising", "Claude CLI not found — c copies the brief instead").
The unresolved count comes from the annotations payload the cards already render from, so the two
can never disagree.

One key moved to make room: composing a comment on a focused block is now `Enter` alone. Bare `c`
used to be a synonym, and a key that sometimes composes and sometimes copies a brief — depending
on whether a block happens to hold focus, which after any navigation one always does — would have
made the copy path unreachable.

## Testing

- **xUnit** — `CommentBriefExporterTests` (the contract's wording, verbatim quoting, bottom-up
  ordering, the empty case) and `CommentBriefPipelineTests` (unresolved-only content, resolved
  comments excluded, the clipboard and the runner carrying identical text, the CLI/mid-run/empty
  gates, and the two briefs staying distinct).
- **Playwright (real Chromium)** — `preview-commentbrief.browser.test.js`: `c`/`a` routing in both
  panel states, exactly one message per keypress, every hint announcement and refusal, Enter still
  composing on a block, `a` on an orphan row still re-anchoring, the composer swallowing both
  keys, and the help sheet owning the screen.
