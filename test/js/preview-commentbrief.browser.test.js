// preview-commentbrief.browser.test.js — the collapsed-panel revision keys, driven in real
// Chromium.
//
// With the gate panel collapsed, "c" copies the revision brief built from the reviewer's
// unresolved comments and "a" hands that brief to the host's Claude runner — the panel-open
// versions of the same keys keep covering the triaged findings, so opening the panel is a
// modifier on what gets revised. These tests drive the assembled preview document with the
// captured host bridge standing in for WebView2 and assert the routing in both panel states, the
// hint announcements, and that the narrower gestures keep their keys: Enter still composes on a
// focused block (bare "c" no longer does), a focused orphan row still takes "a" for re-anchor,
// and the composer's textarea swallows everything.
//
// Run:  cd test/js && npm install && npx playwright install chromium && node preview-commentbrief.browser.test.js
// Set SPECTACLE_CHROMIUM to a Chromium binary to use one that is already on the machine.
'use strict';

const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright-core');

const ASSETS = path.join(__dirname, '..', '..', 'src', 'Spectacle', 'Render', 'Assets');
const asset = (name) => fs.readFileSync(path.join(ASSETS, name), 'utf8');

// ---------- the document under test ----------

const block = (i, kind, line, html) => {
  const tag = kind === 'heading' ? 'h2' : 'p';
  return `<${tag} class="md-block" data-block-id="b${i}" data-kind="${kind}" data-line="${line}" ` +
    `data-text-hash="h${i}" data-occurrence-index="0" tabindex="0">${html}</${tag}>`;
};

const BODY = [
  `<h1 class="md-block" data-block-id="b0" data-kind="heading" data-line="1" data-text-hash="h0" data-occurrence-index="0" tabindex="0">Payment capture design</h1>`,
  block(1, 'paragraph', 3, 'The capture flow is simple.'),
  block(2, 'paragraph', 5, 'Captures expire after a while.'),
].join('\n');

const comment = (id, blockId, line, body, resolved) => ({
  id: id,
  body: body,
  originalText: 'Original text anchored at ' + blockId + '.',
  createdAt: '2026-08-23T10:00:00Z',
  resolvedAt: resolved ? '2026-08-23T11:00:00Z' : null,
  blockAnchor: {
    kind: 'paragraph', line: line, textHash: 'h' + blockId, occurrenceIndex: 0,
    leadingText: 'Original text anchored at ' + blockId + '.', blockIdAtRender: blockId,
  },
});

const ORPHAN = {
  id: 'c-orphan',
  body: 'This block was removed.',
  blockAnchor: { kind: 'paragraph', line: 9, leadingText: 'A block that no longer exists.' },
};

const keyed = (f) => Object.assign({ key: f.check + '|' + f.rule + '|' + f.message }, f);
const FINDINGS = [
  { severity: 'error', rule: 'ai-artifacts/assistant-voice', check: 'ai-artifacts', line: 3, message: "assistant framing 'Certainly!'", remedy: 'Delete the framing sentence.' },
].map(keyed);

function gate(claude) {
  return {
    status: 'fail', passed: false, failOn: 'error',
    counts: { blocking: 1, error: 1, warning: 0, info: 0, suppressed: 0 },
    coverage: { checksDisabled: [], suppressed: 0 },
    triage: { waived: [] },
    metadata: [],
    findings: FINDINGS,
    claude: claude,
  };
}

// Mirrors PreviewHtml.Build: same assets, same order, same payload guard, captured host bridge.
function buildHtml(gatePayload, annotations) {
  const json = (v) => JSON.stringify(v).replace(/<\//g, '<\\/');
  return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width,initial-scale=1" />
  <style>${asset('dark.css')}</style>
  <style>${asset('preview.css')}</style>
  <style>${asset('prism.css')}</style>
  <style>${asset('preview-annotations.css')}</style>
  <style>${asset('preview-keynav.css')}</style>
  <style>${asset('preview-find.css')}</style>
  <style>${asset('preview-outline.css')}</style>
  <style>${asset('preview-gate.css')}</style>
  <style>${asset('preview-loop.css')}</style>
</head>
<body>
  <main role="main">
${BODY}
  </main>
  <script>${asset('prism.min.js')}</script>
  <script>window.__posted = []; window.chrome = { webview: { postMessage: function (m) { window.__posted.push(m); } } };</script>
  <script>window.__spectacleAnnotations__ = ${json(annotations)};</script>
  <script>window.__spectacleOutline__ = ${json([])};</script>
  <script>window.__spectacleGate__ = ${json(gatePayload)};</script>
  <script>window.__spectacleLoop__ = null;</script>
  <script>${asset('preview-annotations.js')}</script>
  <script>${asset('preview-keynav.js')}</script>
  <script>${asset('preview-find.js')}</script>
  <script>${asset('preview-outline.js')}</script>
  <script>${asset('preview-gate.js')}</script>
  <script>${asset('preview-loop.js')}</script>
</body>
</html>`;
}

const TWO_OPEN = {
  comments: [
    comment('c-1', 'b1', 3, 'Name the failure modes.', false),
    comment('c-2', 'b2', 5, 'State the expiry window.', false),
    comment('c-3', 'b2', 5, 'Old ask, already handled.', true),
  ],
  orphaned: [ORPHAN],
};

const ALL_RESOLVED = {
  comments: [comment('c-1', 'b1', 3, 'Old ask, already handled.', true)],
  orphaned: [],
};

const IDLE = { available: true, state: 'idle', detail: null };

// ---------- harness ----------

let failures = 0;
function check(name, ok, detail) {
  if (ok) console.log('  ok   ' + name);
  else { console.log('  FAIL ' + name + (detail ? ' — ' + detail : '')); failures++; }
}

(async () => {
  const browser = await chromium.launch({
    executablePath: process.env.SPECTACLE_CHROMIUM || undefined,
    args: ['--no-sandbox'],
  });

  async function openPage(gatePayload, annotations) {
    const page = await browser.newPage({ viewport: { width: 1100, height: 800 } });
    const errors = [];
    page.on('pageerror', (e) => errors.push(String(e)));
    page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()); });
    await page.setContent(buildHtml(gatePayload, annotations), { waitUntil: 'load' });
    return { page, errors };
  }

  const posted = (page) => page.evaluate(() => window.__posted.map((m) => JSON.parse(m)));
  const countOf = async (page, type) =>
    (await posted(page)).filter((m) => m.type === type).length;
  const hintText = async (page) => {
    const hint = page.locator('#sp-hint');
    return (await hint.count()) ? hint.innerText() : '';
  };

  // ---- collapsed panel, no Claude CLI ----
  {
    console.log('\n[collapsed, no Claude CLI]');
    const { page, errors } = await openPage(gate(null), TWO_OPEN);

    // Reading position: a block focused, as after any keyboard navigation. (A click would start
    // composing, and with an orphan row focused "a" belongs to re-anchor — covered further down.)
    await page.keyboard.press('ArrowDown');
    await page.keyboard.press('c');
    check('"c" posts exactly one copyCommentBrief', (await countOf(page, 'copyCommentBrief')) === 1);
    check('"c" does not open the comment composer', (await page.locator('.sp-composer').count()) === 0);
    check('the copy is announced with the unresolved count',
      (await hintText(page)).includes('Revision brief copied — 2 comments'), await hintText(page));

    await page.keyboard.press('a');
    check('"a" posts nothing without a CLI', (await countOf(page, 'claudeReviseComments')) === 0);
    check('the refusal points at the clipboard path',
      (await hintText(page)).includes('Claude CLI not found'), await hintText(page));
    check('no runtime errors', errors.length === 0, errors.join(' | '));
    await page.close();
  }

  // ---- collapsed panel, CLI idle: the hand-off ----
  {
    console.log('\n[collapsed, Claude CLI idle]');
    const { page, errors } = await openPage(gate(IDLE), TWO_OPEN);

    await page.keyboard.press('ArrowDown');
    await page.keyboard.press('a');
    check('"a" posts exactly one claudeReviseComments', (await countOf(page, 'claudeReviseComments')) === 1);
    check('"a" never posts the findings hand-off', (await countOf(page, 'claudeRevise')) === 0);
    check('the hand-off is announced with the unresolved count',
      (await hintText(page)).includes('Handed to Claude — 2 comments'), await hintText(page));
    check('no runtime errors', errors.length === 0, errors.join(' | '));
    await page.close();
  }

  // ---- collapsed panel, CLI mid-run ----
  {
    console.log('\n[collapsed, Claude mid-run]');
    const { page, errors } = await openPage(
      gate({ available: true, state: 'running', detail: null }), TWO_OPEN);

    await page.keyboard.press('ArrowDown');
    await page.keyboard.press('a');
    check('"a" posts nothing mid-run', (await countOf(page, 'claudeReviseComments')) === 0);
    check('the refusal names the run',
      (await hintText(page)).includes('already revising'), await hintText(page));
    check('no runtime errors', errors.length === 0, errors.join(' | '));
    await page.close();
  }

  // ---- collapsed panel, nothing unresolved ----
  {
    console.log('\n[collapsed, nothing unresolved]');
    const { page, errors } = await openPage(gate(IDLE), ALL_RESOLVED);

    await page.keyboard.press('c');
    check('"c" posts nothing', (await countOf(page, 'copyCommentBrief')) === 0);
    check('the empty copy is explained',
      (await hintText(page)).includes('No unresolved comments'), await hintText(page));

    await page.keyboard.press('a');
    check('"a" posts nothing', (await countOf(page, 'claudeReviseComments')) === 0);
    check('no runtime errors', errors.length === 0, errors.join(' | '));
    await page.close();
  }

  // ---- the narrower gestures keep their keys ----
  {
    console.log('\n[composing and re-anchoring still win]');
    const { page, errors } = await openPage(gate(IDLE), TWO_OPEN);

    // Enter on the focused block opens the composer; keys typed inside post nothing.
    await page.keyboard.press('ArrowDown');
    check('keyboard focus reaches a block', await page.evaluate(
      () => document.activeElement && document.activeElement.classList.contains('md-block')));
    await page.keyboard.press('Enter');
    check('Enter still opens the composer', (await page.locator('.sp-composer').count()) === 1);
    await page.keyboard.type('call out the retry cap and abort');
    check('typing in the composer posts nothing',
      (await countOf(page, 'copyCommentBrief')) === 0 &&
      (await countOf(page, 'claudeReviseComments')) === 0);
    await page.keyboard.press('Escape');
    check('Esc closes the composer', (await page.locator('.sp-composer').count()) === 0);

    // A focused orphan row keeps "a" for the re-anchor flow.
    await page.locator('.sp-orphan-row').click();
    await page.keyboard.press('a');
    check('"a" on an orphan row begins re-anchor',
      await page.evaluate(() => document.body.classList.contains('sp-reanchor-mode')));
    check('…and does not hand anything to Claude', (await countOf(page, 'claudeReviseComments')) === 0);
    await page.keyboard.press('Escape');

    check('no runtime errors', errors.length === 0, errors.join(' | '));
    await page.close();
  }

  // ---- panel open: the keys cover the findings, exactly as before ----
  {
    console.log('\n[panel open, the findings keys are untouched]');
    const { page, errors } = await openPage(gate(IDLE), TWO_OPEN);

    await page.keyboard.press('v');
    await page.keyboard.press('c');
    check('"c" in the open panel copies the fix brief', (await countOf(page, 'copyFixBrief')) === 1);
    check('…not the comment brief', (await countOf(page, 'copyCommentBrief')) === 0);

    await page.keyboard.press('a');
    check('"a" in the open panel hands off the findings', (await countOf(page, 'claudeRevise')) === 1);
    check('…not the comments', (await countOf(page, 'claudeReviseComments')) === 0);
    check('no runtime errors', errors.length === 0, errors.join(' | '));
    await page.close();
  }

  // ---- the help sheet owns the screen ----
  {
    console.log('\n[help sheet open]');
    const { page, errors } = await openPage(gate(IDLE), TWO_OPEN);

    await page.keyboard.press('?');
    await page.keyboard.press('c');
    await page.keyboard.press('a');
    check('neither key posts under the help sheet',
      (await countOf(page, 'copyCommentBrief')) === 0 &&
      (await countOf(page, 'claudeReviseComments')) === 0);
    check('no runtime errors', errors.length === 0, errors.join(' | '));
    await page.close();
  }

  await browser.close();
  if (failures) { console.log('\n' + failures + ' failure(s)'); process.exit(1); }
  console.log('\nAll collapsed-panel revision-key tests passed.');
})().catch((err) => { console.error(err); process.exit(1); });
