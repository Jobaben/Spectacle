---
title: Artifact context and cross-session continuity
status: implemented
stage: design
---

# Cross-session artifact continuity — design

## Overview

Spectacle already runs `claude -p` against the open document: the triage panel's `a` builds a
brief, `ClaudeRevisionPrompt` wraps it in the in-place contract, and `ClaudeRevisionRunner` spawns
the process and decodes its stream. What that path has never carried is *memory*. Each run is a
brand-new process with a brand-new session and no conversational history, so the second revision
of a document knows nothing about why the first one made the choices it did.

For a document an agent authored over several sessions, that is the whole problem. A request as
small as "change the retry interval to 30 seconds" arrives at a session that cannot see the three
architectures the previous session investigated, the two it rejected, or the reasons it rejected
them. The agent's only options are to guess or to flatten — and flattening is what actually
happens: the accumulated reasoning is replaced by a one-line note about the retry interval.

This design makes the Markdown file itself the durable context boundary. A reserved front-matter
namespace, `artifact_context`, carries the materially relevant state of every prior session in
compressed form. A fresh session reads it, treats it as inherited context, applies the new
request, merges what this session learned, and recompresses. Conversation continuity stops being
a prerequisite for document continuity.

The headless invariant, stated as the acceptance requirement it is: a fresh Claude Code process
given the target artifact, the repository, and a revision request must continue the work correctly
without `--continue`, `--resume`, a session id, or any viewer-side transcript.

## What Spectacle owns and what it does not

The single most important boundary in this design: **Spectacle never writes `artifact_context`.**

Two reasons, both load-bearing.

`FrontMatter` is a read-only parser and a deliberate YAML *subset* — scalars, quoted scalars,
block and flow sequences, nested mappings by indentation. It has no writer and no round-tripper.
Teaching it to modify one namespace while preserving every unrelated key byte for byte is a large
sub-project, and the requirement asks for none of it: the merge is step 10 of a sequence
explicitly framed as instructions to the agent.

More fundamentally, `ClaudeRevisionRunner` is built on the invariant that it "knows nothing about
*what* changed" — the agent saves, the watcher fires, the pipeline re-renders and re-grades. A
second writer editing the same file mid-run would race both the agent and the watcher, and would
land as a phantom loop iteration the reader could not attribute to anyone.

So Spectacle's three jobs are to **launch correctly, prompt correctly, and read what came back**.
Detection lives in C#; repair lives in the prompt.

## Components

### `ClaudeProjectRoot` (Ai)

Resolves the directory a viewer-started Claude process must run in.

Markers are `CLAUDE.md` and a `.claude/` directory. The walk starts at the document's own
directory and climbs, and the chosen root is the **outermost** marker-bearing ancestor, ceilinged
at the enclosing git repository root.

Outermost rather than nearest is the decision worth recording. `ConfigLocator` uses nearest-wins
for `.spectacle.json`, which is right for a config file meant to be overridden per directory.
Claude Code configuration is not that shape: a `docs/.claude/` holding one narrow setting would
shadow the repository root, and launching there would silently drop the root's `CLAUDE.md`,
`settings.json`, rules, and hooks — precisely the ungoverned session this design exists to
prevent. Climbing to the outermost marker inside the repository guarantees the whole configuration
loads; subdirectory instructions still apply, because Claude Code reads nested `CLAUDE.md` files
when it reads files beneath them.

The git root is the ceiling so the walk cannot escape into a parent checkout whose `.claude/` has
nothing to do with this artifact. The git root itself needs no marker of its own, since the rule
selects the outermost *marker-bearing* ancestor at or below it, which may be a subdirectory. The
ceiling probe accepts `.git` as either a directory or a file: a linked worktree and a submodule
both mark their root with a *file* holding a `gitdir:` pointer, and a directory-only probe would
climb straight past a worktree root into whatever encloses it.

A document outside any repository has no git ceiling, and there the naive rule fails outright:
almost every developer has a `~/.claude/` holding user-level settings, so an outermost-marker walk
running to the filesystem root would pick the **home directory** as the project root for any
document under it — including anything in `%TEMP%`. That is not a project scope; it is the user
scope Claude Code loads anyway, and launching there would run the artifact's revision against no
project configuration at all while appearing to have resolved one. So the walk stops strictly
below the user profile directory: markers at or above `%USERPROFILE%` are never a project root.
With no git root and no marker below the home directory, resolution fails, and the fallback rule
below decides what happens.

Filesystem probes are injected as `Func<string, bool>`, following the pattern
`ClaudeCliLocator.Detect` already established, so resolution is exercised without touching disk.

### `ArtifactContext` (Ai)

A read-only view of the `artifact_context` namespace, reporting one of three states: `Absent`,
`Present`, or `Malformed`.

It does not read the namespace through `FrontMatterEntry` values. The front-matter parser has no
block-scalar support, so `history: >` parses to the literal value `">"` with its continuation
lines dropped, and a sequence of mappings — the exact shape `decisions` uses —

```yaml
decisions:
  - decision: Use a 30-second retry delay.
    reason: Production telemetry showed 10 seconds was too aggressive.
```

parses the first line as a sequence item and the second as a nested key `decisions.reason`.
Extending the parser to model this correctly would change what every existing document's metadata
card and required-key template see, which is out of scope and not without risk.

Instead `ArtifactContext` uses `FrontMatter.Parse` only for the header's line bounds, then slices
the raw line region: the `artifact_context:` line at indent zero and its indented children up to
the next indent-zero key. From that region it reports which recognized sections are present —
`purpose`, `history`, `decisions`, `constraints`, `evidence`, `assumptions`, `rejected`,
`unresolved` — and any structural issues.

A document is `Malformed` when the header is present but unclosed, when `artifact_context` carries
a scalar where a mapping belongs, when the namespace is declared with no children, or when the key
appears twice. Malformed is a signal to the prompt, never a reason to discard: the run is told to
repair conservatively and preserve every readable line.

### `ClaudeRevisionPrompt` (Ai, extended)

The existing in-place contract is unchanged — target path, edit that file and no other, apply the
brief and nothing else, do not print the document, save in coherent passes, change the quoted
block a comment anchors to. Those rules keep the live loop working and are not renegotiated here.

A cross-session handoff section is added ahead of the brief, establishing:

- this is an independent session with no memory of any previous one;
- read the complete artifact before materially revising it;
- `artifact_context` is authoritative inherited context, not documentation about the file;
- the Markdown body is the current artifact state;
- after revising, merge this session's materially relevant context into the existing capsule and
  recompress it;
- represent a changed decision as one current decision carrying its reason, not as two
  contradictory entries, and record the transition in `history` only when the change itself is
  materially causal;
- an `unresolved` item this session answered moves into the state that answers it rather than
  staying open;
- extract the request's intent, not its literal wording;
- preserve unrelated front matter;
- do not finish with a structurally invalid artifact.

The section varies by state. `Absent` seeds the namespace from this session. `Present` names the
sections already there. `Malformed` carries the detected issues verbatim with the conservative
repair instruction.

Ordering inside the capsule optimizes for reconstruction rather than chronology: current purpose,
current decisions, current constraints, current unresolved state, important evidence, then
material causal history. A future agent should reach the current state without replaying every
event.

### `ClaudeArtifactRevisionService` (Ai)

The central integration boundary. The viewer calls this and nothing else; no caller anywhere
assembles a `claude -p` invocation of its own.

The sequence is: resolve the project root, read the artifact, inspect its `artifact_context`,
decide whether the run may proceed, build the prompt, and start the runner in the resolved root.
It returns a typed outcome carrying whether the run started, the resolved root, the working
directory actually used, and a one-line detail for the run chip.

**Fallback rule.** When no project root resolves, the run proceeds from the document's own
directory — for a managed artifact as much as an unmanaged one — and the chip says which scope it
got: `user scope only — no project root for this artifact`.

This is a deliberate reversal of the original requirement's "fail clearly if a required policy root
cannot be determined", made after the premise behind it turned out to be wrong. The refusal was
justified on the grounds that a fallback run is *ungoverned*. It is not. Claude Code loads the
user-scope `~/.claude` configuration — `CLAUDE.md` and settings alike — whatever the working
directory is, so a fallback run is governed by **less**, not by nothing. What it misses is the
repository's own `artifact-context-policy.md`, not all instruction.

Weighed against that, refusing costs more than it buys. It would block revision of every managed
artifact that happens to live outside a configured project, and a document acquires a capsule
precisely by being revised — so the first capsule a document grows would lock it out of every
subsequent revision until someone added a `CLAUDE.md`. Spectacle's own repository is the worked
example: it has no `CLAUDE.md` and no `.claude/`, and the git-root ceiling stops the walk there, so
under the refusing rule every capsule-bearing document in this repository would have been
unrevisable.

Naming the scope on the chip keeps what the requirement was actually protecting — nothing runs in a
weaker scope *silently* — without the failure mode. A reader who sees the note and wants full
project scope adds a `CLAUDE.md`; the choice stays theirs, and it is visible rather than inferred.

`--bare` is never used. It is already absent from `BuildStartInfo`, so this design adds a
regression test rather than an implementation — a future startup-speed optimization must not
silently disable the configuration loading that continuity depends on.

### `MainWindow` (modified)

The `ClaudeReviseRequested` handler calls the service instead of the runner, passing a callback the
service invokes with the scope note immediately *before* the process spawns. That ordering is the
whole reason the callback exists rather than a return value: `ClaudeRevisionRunner` raises
`Started` on a worker thread, `OnClaudeRunStarted` sets the running chip, and a note applied after
`Revise` returned would race that event and usually lose. `OnClaudeRunStarted` takes the note and
shows it until the run's own stream reports work, at which point what the run is *doing* is the
more useful thing for the chip to carry.

## Testing

Three tiers, none of which require a Claude subscription, credentials, or network access.

**Unit.** Project-root resolution, including the `docs/.claude/` shadowing case, the git-root
ceiling, and the no-marker result. The context reader across every malformed shape. Prompt content
for all three states. A `BuildStartInfo` regression asserting `--bare` appears in no argument.

**Fixture continuity.** Canned artifacts standing in for two independent sessions: a session-A
artifact whose capsule records three investigated architectures, two rejections with reasons, and
an open `unresolved` item; and a session-B artifact revised against a new requirement. The
assertions are properties of the artifact, not of the model — B preserves A's still-valid
decisions, carries the supersession into `history` rather than leaving two contradictory current
decisions, leaves nothing under `unresolved` that it answered, and stays parseable. This tests the
capsule as a data structure, which is the part that must hold regardless of which model wrote it.

**Stub-CLI process.** A real spawned process through the real wrapper, using the
`SPECTACLE_CLAUDE_CLI` override that already exists for exactly this. The stub reads the prompt
from stdin, edits the target file, and emits genuine stream-json. It proves the launch path
end to end: the working directory is the resolved project root, the prompt reaches stdin intact
with its handoff section, the edit lands in the target file, and the stream decodes into a result.
It does not prove a real model merges context well — the fixture tier covers that half.

The division is explicit: the real-model A → B → C run is not automated. Automating it would take
a billable, nondeterministic, credential-bearing CI job, and the same guarantees are reachable by
splitting the claim in two — the launch path proven by execution, the merge semantics by fixtures.

## Out of scope

- **No YAML writer.** Covered above; the agent writes, Spectacle reads.
- **No new gate rule.** A blocking `artifact_context` rule would change the verdict of every
  existing document in every project using Spectacle. If one is wanted later it should be advisory
  and conditional on the namespace already being present.
- **No `--gate` self-invocation inside the prompt.** The reader re-grades every save and shows the
  badge; telling the run to shell out to Spectacle adds a process and a failure mode for a signal
  the human already has on screen.

## Acceptance criteria

- [x] A brand-new `claude -p` session can reconstruct the materially relevant prior work from
      `artifact_context` without `--continue` or `--resume`. (`ArtifactContinuityTests`)
- [x] The Markdown viewer launches managed revisions from the correct Claude project scope and
      does not use `--bare`. (`ClaudeProjectRootTests`,
      `ClaudeCliTests.The_launch_never_uses_bare_mode`, `ArtifactRevisionLaunchTests`)
- [x] Two consecutive independent `claude -p` sessions preserve and semantically merge context
      rather than replacing it. (`ArtifactContinuityTests`)
- [x] The actual viewer/wrapper invocation path has an integration test proving cross-session
      continuity. (`ArtifactRevisionLaunchTests`)
