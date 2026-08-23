---
title: Revision loop and triage
status: implemented
stage: design
---

# The revision loop in the reader — design

## Overview

Spectacle already closed the write → gate → revise loop *around* the reader: `--gate` failed the
pipeline, `--fix-brief` wrote the next prompt, `--review --baseline` diffed two versions, and the
reader re-rendered and re-graded on every save. What was missing was the loop *inside* the reader.
Each render replaced the last one wholesale, so a person supervising an agent got a badge that
flickered from red to red and no answer to the three questions they actually had:

1. **What did that save just do?** Fixed two findings and introduced one is a different event from
   fixed nothing — but both looked like "the page blinked".
2. **Where did the agent touch the document?** Re-reading the whole document per save does not
   scale past the second iteration.
3. **Is this converging?** Four saves in, the only way to know whether the loop was making
   progress was to remember the counts yourself.

And when the verdict disagreed with the reviewer's judgement, the reader offered nothing between
"fix everything" and editing the document to add suppression comments. This change adds the
session memory and the triage bench, without changing what a verdict *is*.

## Design

### `LoopSession` (Gate)

A per-window session log. `Advance(text, report, verdict, blocks, at)` hashes the text and returns
`null` when it has not moved — so a theme flip or a comment save, which re-render the same text,
can never masquerade as a revision. A real change appends a `LoopIteration` carrying the verdict's
tallies, a `ReviewDelta` against the previous report, and the ids of the changed blocks.

Two decisions carry weight:

- **The delta is `ReviewDelta.Compute`, not a new diff.** The toast's "2 fixed · 1 new" and
  `--review --baseline`'s answer must be the same statement, for the same reason the badge is the
  same `GateVerdict` the gate exits on. Line-insensitive identity means a finding that merely
  moved never reads as fixed-plus-new.
- **Changed blocks come from the render's own `TaggedBlock` hashes.** The annotation matcher
  already anchors comments by (kind, normalized-text hash, occurrence index); the loop reuses the
  same multiset to mark what a save touched, so "changed" in the HUD and "still anchored" in the
  review comments can never drift apart. The multiset budget (`OccurrenceIndex >= previous count`)
  flags exactly the surplus copy of a duplicated block. The opening render marks nothing —
  flashing every block on open would teach the reader to ignore the markers.

History is capped at 200 iterations with numbering preserved; a reader left open under a chatty
workflow must not grow without bound.

### `GateTriage` (Gate)

Waives are a session-scoped set of finding keys, where `KeyOf` is `(check, rule, message)` — the
same identity the delta uses, so a waive follows its finding as revisions move it and evaporates
when the finding does (`Prune` runs on every render). `Without` filters a verdict and recomputes
the blocking count under the same threshold.

The line that must not blur: **a waive changes the brief, never the verdict.** Inline
`spectacle-disable-*` directives change the verdict for everyone, durably, in the document — that
is suppression, and it stays in the document where it is visible. A waive changes only which
findings the copied fix brief carries, so a reviewer can hand an agent "fix these four" while the
badge keeps honestly counting six. The pipeline's exit code is not negotiable from the reader.

### Pipeline and host wiring

`LiveGate.Grade` returns the verdict *and* the report it was graded from (the loop diffs reports;
recomputing the review a second time per render would double the grading cost). The pipeline owns
the `LoopSession` and the waive set, injects `window.__spectacleLoop__` and a `triage` block into
the gate payload, and handles two new host messages: `gateWaive` (no re-render — the page updated
optimistically and the next render echoes the set) and `copyFixBrief`, which builds
`FixBriefExporter.Build(GateTriage.Without(verdict, waived))` and raises `CopyTextRequested` for
the window to place on the clipboard — the same division of labour as `Ctrl+Shift+C`.

### `preview-loop.js` (the HUD)

Renders nothing until the loop has actually looped (iteration ≥ 2): a document nobody is revising
keeps its corners clean. Then: a toast per iteration (deduplicated through `sessionStorage`, since
the stable preview origin survives re-renders — the same mechanism keynav uses for scroll), edge
markers on the changed blocks, a pill with the trend, and the `l` panel with a sparkline and the
timeline. It registers its capture listener after the gate's, which settles every priority
question by construction: an open gate panel swallows `l` itself, and the gate's `blockedTarget`
learned to respect an open loop panel — the one direction script order cannot handle.

The gate panel gained the triage keys (`Space`, `c`), a progress line, and open/selection
persistence through `sessionStorage` — waiving five findings while the agent saves underneath is
not five panel re-openings.

## Testing

The split follows the existing rule: logic in xUnit, layout and keys in real Chromium.

- `LoopSessionTests` — advance-on-change-only, delta correctness, surplus-copy detection, the
  history cap.
- `GateTriageTests` — key stability across lines, tally recomputation, coverage carried unchanged,
  pruning.
- `PreviewHtmlLoopTests` / `PreviewLoopPipelineTests` — payload injection, the `</` guard, waive
  round-tripping, the brief covering exactly the unwaived findings, and waives never changing the
  snapshot verdict.
- `preview-loop.browser.test.js` — the HUD's whole surface, per theme, plus containment in both
  directions and toast deduplication across a simulated save re-render. The triage additions to
  `preview-gate.browser.test.js` drive `Space`/`c` against a captured host bridge and assert the
  exact messages, then re-serve the page as a save would and check everything comes back.

The first run of the loop suite caught a real defect the DOM could not have: the toast faded via
opacity, so it was still hit-testable and visible underneath the panel it was yielding to. It now
fades only on its timer and is removed outright when superseded.
