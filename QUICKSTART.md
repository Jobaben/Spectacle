# Quickstart

Spectacle is a Markdown reader with a built-in quality gate for documents that AI workflows
write. You read in it, it grades what you read, and it turns the grade into the next
instruction for whichever agent wrote the file. This page is the first ten minutes plus the
vocabulary — everything else lives in the [README](README.md).

## The first ten minutes

1. **Open a file.** `Spectacle.exe design.md`. No setup needed — the gate is already running.
   The reader is keyboard-first: arrows step block to block, `t` opens the outline, `Ctrl+F`
   finds in the document, and `?` shows the full keyboard sheet.
2. **Read the corner.** The badge says `GATE PASS` or `GATE FAIL` with counts like `2E · 1W`
   (2 errors, 1 warning — `clean` when there are none). `--gate` is the same grade without a
   window: `Spectacle.exe design.md --gate` exits 0 when the document passes and 1 when it has
   blocking findings, so CI, a commit hook, or the agent that wrote the file can branch on it.
   Both audiences read one verdict — you get the badge, they get the exit code. Green badge =
   your pipeline's `--gate` will also pass. Same computation, not an approximation.
3. **Press `v`.** The findings panel lists everything the gate found: severity, line, rule,
   and the concrete fix. Arrows move, `Enter` jumps to the line in the document.
4. **Add your own asks.** `Enter` on a block adds a comment — the reviewer's half of the
   loop, tracked exactly like the gate's findings. A focused comment is yours to manage:
   `e` edits it, `r` resolves it, `d` deletes it, and a comment whose block a revision
   removed waits in the orphan tray until you re-anchor it (`a` on the orphan) or drop it.
   In the findings panel, `Space` waives any finding you disagree with, so the brief you
   send next carries only what you actually want fixed.
5. **Press `a` — the loop closes itself.** If the [Claude Code CLI](https://claude.com/claude-code)
   is installed, `a` hands a revision brief to `claude -p` in the background and Claude
   revises the open document in place; a corner chip shows the run working live from the
   CLI's own event stream (`turn 3 · 2 edits`) — one run at a time, and the reason if it
   fails. The panel decides what the brief carries: open, the triaged
   findings; closed, your unresolved comments. No CLI, or an agent that lives elsewhere?
   `c` copies the same brief to the clipboard — paste it as the agent's next prompt.
6. **Watch the saves land.** Keep Spectacle open — it watches the file. Each save shows a
   toast like `Iteration 2 · ✓ 5 fixed · +1 new · 1 blocking remain` (`gate passes` once
   nothing blocking is left), adds `💬 1 comment addressed` when it rewrites a commented
   block, and marks the blocks that save changed so you only re-read what moved. An addressed
   comment is resolved automatically — it drops out of the next brief instead of stranding
   in the orphan tray.
7. **Press `l` when you wonder if it's converging.** From the second save a corner pill
   (`↻ iter 2` plus a trend arrow) already answers at a glance; `l` — or clicking the pill —
   opens the timeline: one row per save and a bar per iteration, the gate's blocking count at
   the base, your open comments stacked on top (each row tallies its own, `💬2` beside the
   gate counts). The newest bar is live: add or resolve a comment between saves and its
   comment layer moves immediately — that's the document's current state, not a new iteration.
   Shrinking bars mean the loop is working; a bar is clean only when both are zero. Every
   background run is a row of its own too: Claude's closing message as its receipt, its turn
   and edit counts, `🤖` on the saves it produced — and a run that failed or finished without
   saving anything says so right there, instead of leaving silent bars that tell you nothing.
   Revise again — `a` or `c` — until the badge is green and no comments remain open.

To try this without an agent, [`docs/example/spec/`](docs/example/spec/) ships three saved
iterations of one document — copy v1 over a working file, open it, then copy v2 and v3 over
it and watch steps 6–7 happen.

## The words on the screen

- **Gate** — The graded review: every check runs, every finding gets a severity, and the
  document passes or fails as a whole. Same thing in the reader (badge) and the CLI, where
  `Spectacle.exe <file> --gate` runs it headlessly and exits 0 (pass) or 1 (blocking findings).
- **Verdict** — One gate result: pass/fail, the counts, and the findings.
- **Finding** — One problem at one place: a severity, a line, a rule, a message, and a fix.
- **Check → rule** — A check is a family (`ai-artifacts`, `bare-urls`); a rule is one thing it
  catches (`ai-artifacts/unfilled-template`). You disable checks; findings cite rules.
- **Severity** — How much a finding matters: `error`, `warning`, or `info` (advisory). Set per
  project in `.spectacle.json`.
- **Threshold / blocking** — The `failOn` line in `.spectacle.json` — the lowest severity that
  fails the gate (default `error`). Findings at or above it are *blocking* — they fail the
  gate. A warning under an `error` threshold is reported but doesn't block.
- **Front matter** — The `---`-fenced YAML header a workflow stamps on its output. Rendered as
  the metadata card at the top; validated against `requiredFrontMatter`, the `.spectacle.json`
  list of metadata keys every document must declare; echoed into the verdict.
- **Iteration** — One real save of the file while the reader is open. Theme flips and comment
  saves don't count — only text changes do.
- **Delta** — What an iteration changed in the review: findings *fixed*, findings *new*
  (introduced), and what remains. Same math as `Spectacle.exe <file> --review --baseline <old>`,
  which prints that diff against an older copy of the document.
- **Waive** — A session-only "leave this out of the brief" mark (`Space` in the panel). The
  badge and the pipeline still count the finding — only the copied brief shrinks. Gone when the
  finding is fixed or the window closes.
- **Suppression** — The permanent version: an HTML comment in the document itself,
  `<!-- spectacle-disable-next-line bare-urls -->`. Changes the verdict for everyone and is
  visible in the file.
- **Fix brief** — The findings rewritten as instructions addressed to the tool that wrote the
  document, ordered bottom-up so line numbers stay valid while it edits. `c` in the panel;
  `Spectacle.exe <file> --fix-brief [out]` in the CLI, which writes that brief to a file and
  exits with the gate's own code.
- **Hand-off (`a`)** — A brief given straight to the Claude Code CLI instead of the clipboard,
  with an in-place contract: revise this exact file, create no new one. Panel open, it carries
  the triaged findings; panel closed, your unresolved comments. Offered only when a `claude`
  install is detected (pin a specific one by pointing the `SPECTACLE_CLAUDE_CLI` environment
  variable at its executable).
- **Coverage** — The honesty note on a verdict: which checks were disabled and how many
  findings were suppressed, so a clean pass can't hide a narrowed gate.
- **Comments / comment brief** — Your own margin notes (`Enter` on a block), separate from the
  gate. With the findings panel closed, `c` copies them as a revision brief — the
  human-authored counterpart to the fix brief. `Spectacle.exe <file> --revision-plan [out]` is
  the headless route, exporting the same brief to a file (add `--unresolved` for open comments
  only).

Rule of thumb for the two easily confused pairs: **waive** is "not in this brief", **suppress**
is "not ever, and visibly so"; the **fix brief** carries the gate's findings, the **comment
brief** carries your comments — one pair of keys (`c` / `a`), with the panel deciding which.

## Where things live

- **Project contract:** `.spectacle.json` next to your docs (`--init-config` scaffolds one) —
  required sections, required front-matter keys, severities, threshold. Reader and CLI both
  discover the nearest one automatically.
- **Session state:** the iteration timeline and waives live in the open window and reset when
  it closes. Spectacle never edits your document — it is read-only by design.
- **Everything else:** press `?` in the reader for the full keyboard sheet, and see the
  [README](README.md) for the CLI, CI recipes, and how each check works.
