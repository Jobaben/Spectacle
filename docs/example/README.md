# A revision loop, saved as three iterations

[`spec/`](spec/) is a replayable session of the write → gate → revise loop: one agent-written
document at three points in its life, plus the [`.spectacle.json`](spec/.spectacle.json) that
grades it. Everything the README's
[revision loop](../../README.md#the-revision-loop-in-the-reader) section shows was rendered from
these files.

| File | What it is | Gate |
| --- | --- | --- |
| `spec/payment-flow-v1.md` | The raw draft, residue and all: an unfilled `{{capture_ttl}}` token, "Certainly!" framing, an unfinished-task marker, a truncation marker, an empty required `reviewer` key, a missing required section | **FAIL** — 6 errors, 1 warning, 1 advisory |
| `spec/payment-flow-v2.md` | The agent applied the brief: five findings fixed, one new bare URL introduced, the unfinished-task marker still open | **FAIL** — 1 error, 2 warnings, 1 advisory |
| `spec/payment-flow-v3.md` | The pass that closes it out | **PASS** — 1 warning under the error threshold |

The config requires the `Overview` / `Acceptance criteria` / `Rollout` sections and the
`workflow` / `run` / `stage` / `reviewer` metadata keys, grades `bare-urls` down to a warning and
`prose` to advisory, and fails on errors. It lives next to the documents it governs, so this
README — one folder up — is graded by Spectacle's defaults instead.

## Watch it live

Open a working copy in the reader and play the agent yourself:

```powershell
cd spec
copy payment-flow-v1.md payment-flow.md
Spectacle.exe payment-flow.md            # GATE FAIL — press v for findings, Space to waive, c for the brief
copy payment-flow-v2.md payment-flow.md  # toast: Iteration 2 · ✓ 5 fixed · +1 new · 1 blocking remain
copy payment-flow-v3.md payment-flow.md  # toast: Iteration 3 · ✓ 2 fixed · gate passes — press l for the timeline
```

## Or run it headless

```powershell
cd spec
Spectacle.exe payment-flow-v1.md --gate                            # exit 1, six errors
Spectacle.exe payment-flow-v1.md --fix-brief                       # the brief an agent revises from
Spectacle.exe payment-flow-v2.md --review --baseline payment-flow-v1.md   # the same delta the toast shows
Spectacle.exe payment-flow-v3.md --gate                            # exit 0 — pass with one warning
```
