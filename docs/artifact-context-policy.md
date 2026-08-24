# Artifact context policy

A Markdown artifact revised by an agent carries its own memory in a reserved front-matter
namespace, `artifact_context`. Conversation continuity is not required for document continuity: a
brand-new `claude -p` process handed the file, the repository and a new request must be able to
continue the work correctly, with no `--continue`, no `--resume`, no session id, and no
viewer-side transcript.

## The namespace

```yaml
artifact_context:
  purpose: >
    What this artifact is for, in one or two sentences.
  decisions:
    - decision: The current decision, stated as current.
      reason: Why it holds.
  constraints:
    - Bounds the work must respect.
  unresolved:
    - Questions still open right now.
  evidence:
    - What was measured or observed, when it changes what a future session would do.
  assumptions:
    - What is being taken as true without proof.
  rejected:
    - alternative: What was considered.
      reason: Why it was not chosen.
  history: >
    The material causal history — how the artifact got to its current state, compressed.
```

Every section is optional; a capsule carries the ones that matter. The order above is the order to
write them in: it is optimized for reconstruction, not chronology, so a future session reaches the
current state without replaying every event.

## Rules for a revising session

1. Read the complete file before materially revising it, front matter included.
2. Treat `artifact_context` as authoritative inherited context and the Markdown body as the
   current state of the artifact.
3. Apply the requested revision.
4. Merge what this session materially introduced into the existing capsule and recompress. Merge —
   do not replace, and do not simply append.
5. The capsule is current semantic state plus material causal history, **not an append-only event
   log**. A superseded decision becomes one current decision carrying its reason; the transition
   goes into `history` only when the change itself is materially causal.
6. A question this session answered leaves `unresolved`, and its outcome moves into the decision or
   constraint that answers it.
7. Record the request's material intent and reason, not the conversational wording it arrived in,
   unless the exact wording is itself significant.
8. Only `artifact_context` is yours to rewrite. Preserve unrelated front matter.
9. A malformed capsule is repaired conservatively. Never discard one and start over.
10. Do not finish while the artifact is structurally invalid.

## Worked example

A session changes a retry interval a previous session set. Wrong:

```yaml
decisions:
  - decision: Use a 10-second retry delay.
    reason: Initial conservative value.
  - decision: Use a 30-second retry delay.
    reason: Telemetry.
```

Two contradictory current decisions, and a future session cannot tell which one holds. Right:

```yaml
decisions:
  - decision: Use a 30-second retry delay.
    reason: Production telemetry showed the original 10-second interval was too aggressive.
history: >
  The retry interval began at a conservative 10 seconds and was raised to 30 after a week of
  production telemetry.
```

## How Spectacle enforces this

Spectacle never writes `artifact_context`. It resolves the artifact's Claude project root and
launches the revision there, so this policy and the project's other configuration load rather than
the user-scope `~/.claude` configuration alone; and it reads the namespace and tells the run what it
found, so a fresh session is told what it inherited before it starts editing.

When no project root can be established, the revision still runs — from the document's own folder,
with `user scope only — no project root` on the chip. Claude Code loads `~/.claude` whatever the
working directory is, so such a run is governed by less rather than by nothing; what it misses is
this file. Refusing instead would lock a document out of revision the moment it grew its first
capsule. The scope is named rather than enforced: if a project wants this policy to reach every
revision of its documents, it needs a `CLAUDE.md` or a `.claude/` at its root.

See [the design](superpowers/specs/2026-08-24-artifact-context-continuity-design.md) for why each
of those is the way it is.
