---
title: Hands-free revision through the Claude CLI
status: implemented
stage: design
---

# Hands-free revision through the Claude CLI — design

## Overview

The triage bench already assembles the next prompt: `Space` waives, `c` copies the fix brief, and
the human carries it to whichever agent wrote the document. When that agent is the Claude Code CLI
on the same machine, the carrying is pure overhead — copy, switch windows, paste, wait, switch
back. Worse, the manual hand-off was observed to break the loop outright: given the brief alone,
`claude` wrote the revised text to a *new* Markdown file next to the original. The open document
never changed, the watcher never fired, the timeline recorded nothing, and a `*.revised.md`
accumulated on disk.

This change makes the reader do the hand-off itself, and makes the in-place contract explicit:

1. **Detect.** On window open, a PATH scan looks for a Claude Code install (`claude.exe`, the npm
   `claude.cmd` shim, or a bare `claude`). `SPECTACLE_CLAUDE_CLI` pins a specific binary and is
   authoritative — a wrong pin means "not installed", never "fall back to PATH". No CLI found
   means nothing changes anywhere: the clipboard path is untouched.
2. **Hand off.** With a CLI found, the findings panel's footer offers `a` beside `c`. The key
   sends the *same* triaged brief the clipboard would get, wrapped in a prompt whose first rule is
   the in-place contract, to `claude -p` in a background process.
3. **Watch.** The runner never touches the document. Claude saves the file; the existing watcher
   fires; the pipeline re-renders, re-grades, and advances the loop session — every save a toast,
   a changed-block marker set, and an iteration row, exactly as if a human had run the agent by
   hand. A chip in the bottom-left corner shows the run while it is in flight and holds a one-line
   reason if it fails.

## Design

### `ClaudeCliLocator`, `ClaudeRevisionPrompt`, `ClaudeRevisionRunner` (Ai)

Three small classes, none of which knows about WPF or the preview:

- **Locator** — a pure PATH probe (`Detect(overridePath, pathValue, fileExists)`) with the real
  environment supplied by the parameterless overload. Quoted, padded, and broken PATH entries are
  skipped rather than fatal.
- **Prompt** — wraps the brief in the in-place contract: the absolute target path, "create no
  other file" (the observed `*.revised.md` failure mode is named explicitly), no chat output, no
  changelog appended, and a nudge to save in a few coherent passes so the timeline stays legible.
- **Runner** — one `claude -p --permission-mode acceptEdits` process at a time, prompt written to
  stdin (no command-line quoting or length limits), working directory = the document's directory,
  both output streams drained. `acceptEdits` in print mode is the sandbox: file edits are
  auto-approved, anything that would need an interactive permission prompt is refused because
  nobody is there to answer it. A second `TryStart` mid-run is rejected, not queued — the brief it
  would carry was computed against a document the current run is still rewriting. The npm `.cmd`
  shim is run through `cmd.exe` because CreateProcess with redirected streams wants a real
  executable. Every accepted run ends in exactly one `Completed`, including launch failures, so
  the "running" chip can never stick.

### Pipeline and host wiring

The pipeline treats the runner exactly like the clipboard — a host concern it signals but never
touches:

- `SetClaudeStatus(ClaudeRevisionStatus)` stores availability + run state and re-renders, so the
  page's chip and footer are payload-driven and survive the re-renders the run's own saves cause.
- A `claudeRevise` host message builds the same triaged brief `copyFixBrief` builds and raises
  `ClaudeReviseRequested` — but only when a CLI exists and no run is in flight, whatever the page
  believed when it sent the message.
- `MainWindow` composes the pieces: locator at construction, prompt built per request, runner
  events mapped back to `SetClaudeStatus` (running / done / failed-with-reason).

The gate payload grows a `claude` field (`available`, `state`, `detail`); `null` for the export
path, which is how the static HTML keeps rendering nothing new.

### `preview-gate.js` (the overlay)

`a` in the open panel calls the hand-off, with the refusals explained in the same status element
the copy confirmation uses: already running, or every finding waived. The footer offers the key
only when the payload says a CLI exists. The chip renders from the payload on every load —
bottom-left, because the badge and the loop pill hold the bottom-right and the toast the
bottom-center. A clean finish shows no chip at all: the loop HUD's own toast is already the
announcement that matters.

## Testing

- **xUnit** — the locator probe (PATH forms, candidate order, the authoritative pin), the prompt's
  contract word by word, the runner's start-info, and the runner against real stub `.cmd`
  processes: stdin delivery, exit-code mapping, stderr's first line as the failure detail, launch
  failure still completing, and the one-run-at-a-time gate. Pipeline tests drive the
  `claudeRevise` message end to end and assert the brief equals the clipboard's, respects waives,
  and is refused without a CLI or mid-run.
- **Playwright (real Chromium)** — `preview-claude.browser.test.js`: the footer offers `a` only
  when a CLI exists, one keypress posts exactly one `claudeRevise`, mid-run and fully-waived
  refusals stay on screen, the chip renders for running/failed (and only those), sits clear of the
  badge, and the failure detail is shown verbatim.
