# Quickstart

Spectacle is a Markdown reader with a built-in quality gate for documents that AI workflows
write. You read in it, it grades what you read, and it turns the grade into the next
instruction for whichever agent wrote the file. This page is the first ten minutes plus the
vocabulary — everything else lives in the [README](README.md).

## Ten minutes

1. **Open a file.** `Spectacle.exe design.md`. No setup needed — the gate is already running.
2. **Read the corner.** The badge says `GATE PASS` or `GATE FAIL` with counts like `2E 1W`
   (2 errors, 1 warning). Green badge = your pipeline's `--gate` will also pass. Same
   computation, not an approximation.
3. **Press `v`.** The findings panel lists everything the gate found: severity, line, rule,
   and the concrete fix. Arrows move, `Enter` jumps to the line in the document.
4. **Let your agent revise the file.** Keep Spectacle open — it watches the file. Each save
   shows a toast like `Iteration 2 · ✓ 5 fixed · +1 new · 1 blocking remain`, and marks the
   blocks that save changed so you only re-read what moved.
5. **Press `l` when you wonder if it's converging.** The timeline shows one row per save and
   a bar chart of blocking counts — shrinking bars mean the loop is working.
6. **Close the loop.** In the findings panel: `Space` waives anything you disagree with,
   `c` copies a fix brief of the rest. Paste that brief as the agent's next prompt. Repeat
   until the badge is green.
7. **Or skip the copying.** If the [Claude Code CLI](https://claude.com/claude-code) is
   installed, the panel also offers `a`: Spectacle hands the brief to `claude -p` in the
   background, Claude revises the open document in place, and steps 4–5 happen by themselves.
   A corner chip shows the run (and the reason, if it fails).

To try this without an agent, [`docs/example/spec/`](docs/example/spec/) ships three saved
iterations of one document — copy v1 over a working file, open it, then copy v2 and v3 over
it and watch steps 4–6 happen.

## The words on the screen

| Term | Means |
| --- | --- |
| **Gate** | The graded review: every check runs, every finding gets a severity, and the document passes or fails as a whole. Same thing in the reader (badge) and the CLI (`--gate`, exit code 0/1). |
| **Verdict** | One gate result: pass/fail, the counts, and the findings. |
| **Finding** | One problem at one place: a severity, a line, a rule, a message, and a fix. |
| **Check → rule** | A check is a family (`ai-artifacts`, `bare-urls`); a rule is one thing it catches (`ai-artifacts/unfilled-template`). You disable checks; findings cite rules. |
| **Severity** | How much a finding matters: `error`, `warning`, or `info` (advisory). Set per project in `.spectacle.json`. |
| **Threshold / blocking** | The `failOn` line (default `error`). Findings at or above it are *blocking* — they fail the gate. A warning under an `error` threshold is reported but doesn't block. |
| **Front matter** | The `---`-fenced YAML header a workflow stamps on its output. Rendered as the metadata card at the top; validated against `requiredFrontMatter`; echoed into the verdict. |
| **Iteration** | One real save of the file while the reader is open. Theme flips and comment saves don't count — only text changes do. |
| **Delta** | What an iteration changed in the review: findings *fixed*, findings *new* (introduced), and what remains. Same math as `--review --baseline`. |
| **Waive** | A session-only "leave this out of the brief" mark (`Space` in the panel). The badge and the pipeline still count the finding — only the copied brief shrinks. Gone when the finding is fixed or the window closes. |
| **Suppression** | The permanent version: an HTML comment in the document itself, `<!-- spectacle-disable-next-line bare-urls -->`. Changes the verdict for everyone and is visible in the file. |
| **Fix brief** | The findings rewritten as instructions addressed to the tool that wrote the document, ordered bottom-up so line numbers stay valid while it edits. `c` in the panel, `--fix-brief` in the CLI. |
| **Hand-off (`a`)** | The fix brief given straight to the Claude Code CLI instead of the clipboard, with an in-place contract: revise this exact file, create no new one. Offered only when a `claude` install is detected (pin one with `SPECTACLE_CLAUDE_CLI`). |
| **Coverage** | The honesty note on a verdict: which checks were disabled and how many findings were suppressed, so a clean pass can't hide a narrowed gate. |
| **Comments / revision plan** | Your own margin notes (`Enter` on a block), separate from the gate. They export as a revision plan (`Ctrl+Shift+C`) — the human-authored counterpart to the fix brief. |

Rule of thumb for the two easily confused pairs: **waive** is "not in this brief", **suppress**
is "not ever, and visibly so"; the **fix brief** carries the gate's findings, the **revision
plan** carries your comments.

## Where things live

- **Project contract:** `.spectacle.json` next to your docs (`--init-config` scaffolds one) —
  required sections, required front-matter keys, severities, threshold. Reader and CLI both
  discover the nearest one automatically.
- **Session state:** the iteration timeline and waives live in the open window and reset when
  it closes. Spectacle never edits your document — it is read-only by design.
- **Everything else:** press `?` in the reader for the full keyboard sheet, and see the
  [README](README.md) for the CLI, CI recipes, and how each check works.
