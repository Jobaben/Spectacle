---
title: AI workflow gate
status: implemented
stage: design
---

# AI workflow gate — design

## Overview

Spectacle already had ~25 checks and a one-shot `--review` verdict. What it did not have was a way
to be *the* gate in an unattended pipeline. This change adds that, without changing what Spectacle
is: a Markdown reader you open a file in.

Four gaps separated "a linter with a lot of rules" from "a gate a workflow can depend on".

1. **Front matter was read as prose.** No pipeline enabled Markdig's YAML front-matter extension,
   so `title: Draft` followed by the closing `---` parsed as a *setext heading*. The metadata header
   silently became the document's first `h2` on essentially every generated document, polluting the
   outline, the heading hierarchy and the TOC check. The header was also invisible as data: nothing
   validated it, rendered it, or handed its values to a caller.
2. **Generation residue was not a defect.** Every check would pass a document opening with
   "Certainly! Here's the updated specification:" and closing with "…rest of the file unchanged".
   These are the failures unique to a model writing the file, and no Markdown linter catches them.
3. **The verdict was all-or-nothing.** One `IssueCount`, one exit code. A team finding a check too
   strict could only turn it off — after which nobody saw it again.
4. **Findings described problems, not fixes.** An authoring agent handed "toc/stale-toc-entry at
   line 40" has to infer the edit, and will sometimes infer wrong or rewrite half the document.

## Design

### Front matter as data (`FrontMatter`, `FrontMatterChecker`)

A dependency-free YAML *subset* parser — scalars, quoted scalars, block and flow sequences, nested
mappings by indentation, flattened to dotted keys. That covers every metadata header a generator
emits without taking on a YAML dependency, and an unrecognized construct is skipped rather than
throwing: a malformed header must not crash a headless gate.

Two decisions carry weight:

- **A header is recognized only at line 1.** The same rule every static-site generator applies, so
  what Spectacle calls front matter is what the rest of the toolchain calls front matter. A block
  further down is ordinary Markdown — and is reported by the `misplaced-front-matter` rule, since it
  is the signature of concatenated generator output.
- **`Strip` blanks the header's lines rather than removing them.** Line counts are preserved, so a
  finding still points at the right line of the real file. Every content check reads the stripped
  body; only the front-matter check reads the raw document. An *unclosed* header is left untouched:
  there is no fence to bound, and blanking to EOF would hide every other finding.

`UseYamlFrontMatter()` is also enabled on all 14 Markdig pipelines, so a raw document handed
straight to a checker or the renderer parses the header as metadata rather than as a heading.

### Generation residue (`AiArtifactChecker`)

Four rules — `unfilled-template`, `assistant-voice`, `truncated-output`, `placeholder-target` — over
`MarkdownTextScanner.ProseLines`, so fenced code and inline code spans are immune (a docs page about
templating shows `` `{{name}}` `` freely).

Patterns are tuned against false positives, which matter more here than recall: a gate that cries
wolf gets switched off. Openers are anchored to line start ("of course" mid-sentence is English);
`if you would like` requires "me to"; a rule of underscores is skipped when the line is a thematic
break; and the IANA-reserved `example.com` is deliberately *not* a placeholder target — it exists so
documentation can show a URL that points nowhere on purpose. At most one finding per rule per line.

### One finding stream, then a policy (`FindingStream`, `GatePolicy`, `GateVerdict`)

`FindingStream` flattens a `ReviewReport`'s twenty-odd typed collections into one ordered list of
`GateFinding(CheckId, RuleId, Severity, Line, Message)`. SARIF, GitHub annotations, JUnit, the fix
brief, the terminal verdict and the reader's overlay all read the stream, so a new check is wired in
one place instead of six exporters. `RuleCatalog` carries each rule's description, default severity
and **remedy**; SARIF's private catalogue was folded into it.

Findings carry catalogued default severities; `GatePolicy` re-grades them afterwards. Keeping those
steps separate means the stream stays a faithful description of what was found, independent of what
any project chooses to block on.

Two deliberate constraints on grading:

- **`Info` never blocks**, whatever the threshold. Advice is advice; the lowest setting cannot turn
  hedging prose into a build failure.
- **`--review` keeps its old contract.** It gates on `IssueCount`, unchanged. Grading applies to
  `--gate` (and to SARIF levels), so no existing pipeline changes behaviour.

`GateVerdict` also carries what makes the boolean trustworthy: disabled checks, suppressed findings,
and the document's own metadata. A pass with six checks off is a different fact from a clean pass,
and every output states which one it is.

### Closing the loop (`FixBriefExporter`)

The verdict rewritten as instructions for the authoring tool. Three details do the work: each
finding carries the catalogued remedy (so no human translates rule ids); findings are ordered
**bottom-up** (an edit at line 22 shifts every line after it, so a top-down list hands the tool
stale numbers halfway through); and the scope is stated explicitly, with inline suppression offered
as the escape hatch, so a fix pass stays a fix pass.

### The reader shows the same verdict (`LiveGate`, `preview-gate.js`)

`PreviewPipeline` computes a `GateVerdict` on every render via `LiveGate`, which resolves the same
project config and grades as the CLI. The preview gets a badge, a findings panel with jump-to-line
(`v`), and a metadata card. A reader showing its own approximation of the gate would be a second
opinion nobody asked for — the moment the two disagreed, the reader's would stop being trusted.

## Non-goals

- **Not an editor.** The gate reports and instructs; it never rewrites the document.
- **Not a full YAML parser.** The subset covers metadata headers. Anchors, multi-line scalars and
  tags are out of scope; an unrecognized construct is skipped, not an error.
- **Not a replacement for `--review`.** The old verdict and its exit code are unchanged.

## How this is verified

Three layers, because no single one reaches the whole product:

| Layer | Where | What it proves |
|---|---|---|
| Checks, grading, exporters, the injected payload | xUnit, Windows CI | the verdict is right, and every output carries it |
| Host → gate → HTML, live re-grade on file change | xUnit against a real file through `FileDocument` and `PreviewPipeline`'s `IPreviewSink` | a verdict actually reaches the WebView, tracks the file, and equals the CLI's for the same document and config |
| Overlay layout and interaction | Playwright in real Chromium, Linux CI | what the reader will actually see and do — WebView2 *is* Chromium |

The middle and outer layers were added after the fact, and both earned their place immediately. The
browser layer replaced a hand-rolled DOM stub that had passed all of its assertions while four real
defects were live (broken key containment, the panel opening under the modal help sheet, a dead
toggle, and a jump offered with nothing to jump to). A stub only checks the logic you thought to
model.

What remains outside automated coverage is the WPF window itself — chrome, the WebView2 control's own
plumbing, and file-association registration. Those are thin, unchanged by this work, and the seam
below them (`IPreviewSink`) is covered.

## Acceptance criteria

- [x] Front matter renders as metadata, not as an `h2`, and is excluded from the outline, the
      hierarchy check, the block diff and the statistics.
- [x] A content finding's line number still points at the right line of the real file.
- [x] `--check-front-matter` enforces `requiredFrontMatter`, and is silent with no template and a
      well-formed (or absent) header.
- [x] `--check-ai-artifacts` reports the four residue rules and is immune to code, code spans,
      thematic breaks and the reserved example domain.
- [x] `--gate` grades by `severity` / `failOn`, exits non-zero only at or above the threshold, and
      handles a file, a folder, and standard input.
- [x] `--gate` emits text, JSON, Markdown, SARIF, GitHub annotations and JUnit XML.
- [x] `--fix-brief` orders findings bottom-up, splits required from optional, and carries a remedy
      per finding.
- [x] The reader's verdict equals the command's verdict for the same document and config.
- [x] Every rule the finding stream can emit is catalogued with a description and a remedy.
- [x] Reduced coverage (disabled checks, inline suppressions) is stated in every output.
- [x] The overlay follows the same key-containment and blocked-target contract as the reader's other
      overlays, verified in a real browser.
- [x] Rewriting the file under the watcher re-grades the badge without reopening the document.
