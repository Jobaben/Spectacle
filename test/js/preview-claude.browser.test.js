// preview-claude.browser.test.js — the hands-free revision hand-off in the gate overlay, driven in
// real Chromium.
//
// When the host found a Claude CLI (GATE.claude.available) the triage panel's "a" hands the fix
// brief to `claude -p` instead of the clipboard, and a chip above the badge shows the background
// run. These tests drive the same assembled preview document the reader gets, with the captured
// host bridge standing in for WebView2, and assert the three legs: the offer appears only when a
// CLI exists, "a" posts exactly one claudeRevise (and refuses sensibly mid-run or fully waived),
// and the chip tracks the payload's run state across renders — including a failure's detail.
//
// Run:  cd test/js && npm install && npx playwright install chromium && node preview-claude.browser.test.js
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
  `<h1 class="md-block" data-block-id="b0" data-kind="heading" data-line="6" data-text-hash="h0" data-occurrence-index="0" tabindex="0">Payment capture design</h1>`,
  block(1, 'paragraph', 8, 'Certainly! Here is the updated capture design.'),
  block(2, 'heading', 10, 'Overview'),
  block(3, 'paragraph', 12, 'A capture request is idempotent. Captures expire {{expiry_hours}} hours after authorization.'),
].join('\n');

const keyed = (f) => Object.assign({ key: f.check + '|' + f.rule + '|' + f.message }, f);
const FINDINGS = [
  { severity: 'error', rule: 'ai-artifacts/assistant-voice', check: 'ai-artifacts', line: 8, message: "assistant framing 'Certainly!'", remedy: 'Delete the framing sentence.' },
  { severity: 'error', rule: 'ai-artifacts/unfilled-template', check: 'ai-artifacts', line: 12, message: "unsubstituted template token '{{expiry_hours}}'", remedy: 'Replace the token with its value.' },
].map(keyed);

// A failing verdict whose `claude` field is the case under test.
function gate(claude, waived) {
  return {
    status: 'fail', passed: false, failOn: 'error',
    counts: { blocking: 2, error: 2, warning: 0, info: 0, suppressed: 0 },
    coverage: { checksDisabled: [], suppressed: 0 },
    triage: { waived: waived || [] },
    metadata: [{ key: 'workflow', value: 'spec-writer' }],
    findings: FINDINGS,
    claude: claude,
  };
}

// Mirrors PreviewHtml.Build: same assets, same order, same payload guard, captured host bridge.
function buildHtml(gatePayload) {
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
  <script>window.__spectacleAnnotations__ = ${json({ comments: [], orphaned: [] })};</script>
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

  async function openPage(gatePayload) {
    const page = await browser.newPage({ viewport: { width: 1100, height: 800 } });
    const errors = [];
    page.on('pageerror', (e) => errors.push(String(e)));
    page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()); });
    await page.setContent(buildHtml(gatePayload), { waitUntil: 'load' });
    return { page, errors };
  }

  const posted = (page) => page.evaluate(() => window.__posted.map((m) => JSON.parse(m)));
  const reviseCount = async (page) =>
    (await posted(page)).filter((m) => m.type === 'claudeRevise').length;

  // ---- no CLI on the host ----
  {
    console.log('\n[no Claude CLI]');
    const { page, errors } = await openPage(gate(null));

    await page.keyboard.press('v');
    const footer = await page.locator('.sp-gate-footer').innerText();
    check('footer still offers the clipboard path', footer.includes('c copy fix brief'));
    check('footer does not offer the Claude hand-off', !footer.includes('Claude'));

    await page.keyboard.press('a');
    check('"a" posts nothing', (await reviseCount(page)) === 0);
    check('no run chip exists', (await page.locator('#sp-claude-chip').count()) === 0);
    check('no runtime errors', errors.length === 0, errors.join(' | '));
    await page.close();
  }

  // ---- CLI found, idle: the hand-off ----
  {
    console.log('\n[Claude CLI idle]');
    const { page, errors } = await openPage(gate({ available: true, state: 'idle', detail: null }));

    await page.keyboard.press('v');
    const footer = await page.locator('.sp-gate-footer').innerText();
    check('footer offers the Claude hand-off', footer.includes('a Claude revises in place'));
    check('no chip while nothing runs', (await page.locator('#sp-claude-chip').count()) === 0);

    await page.keyboard.press('a');
    check('"a" posts exactly one claudeRevise', (await reviseCount(page)) === 1);
    check('the hand-off is confirmed on screen with the covered count',
      (await page.locator('.sp-gate-copied').innerText()).includes('Handed to Claude — 2 findings'));

    // The panel keeps "a" away from the document, so nothing else fired.
    const all = await posted(page);
    check('no other message piggybacked', all.length === 1, JSON.stringify(all));
    check('no runtime errors', errors.length === 0, errors.join(' | '));
    await page.close();
  }

  // ---- CLI running: the chip, and no double-fire ----
  {
    console.log('\n[Claude running]');
    const { page, errors } = await openPage(gate({ available: true, state: 'running', detail: null }));

    const chip = page.locator('#sp-claude-chip');
    check('the run chip is visible without opening anything', await chip.isVisible());
    check('the chip says who is writing', (await chip.innerText()).includes('Claude is revising'));

    // Bottom-left, because the other ambient corners are taken: the badge and the loop pill hold
    // the bottom-right, the loop toast the bottom-center.
    const vp = page.viewportSize();
    const chipBox = await chip.boundingBox();
    const badgeBox = await page.locator('#sp-gate-badge').boundingBox();
    check('the chip sits in the bottom-left corner, clear of the badge',
      !!chipBox && !!badgeBox && chipBox.y > vp.height * 0.6 &&
      chipBox.x < vp.width * 0.3 && chipBox.x + chipBox.width < badgeBox.x - 4 &&
      chipBox.y + chipBox.height <= vp.height,
      JSON.stringify({ chipBox, badgeBox }));

    await page.keyboard.press('v');
    await page.keyboard.press('a');
    check('"a" mid-run posts nothing', (await reviseCount(page)) === 0);
    check('the refusal is explained on screen',
      (await page.locator('.sp-gate-copied').innerText()).includes('already revising'));
    check('no runtime errors', errors.length === 0, errors.join(' | '));
    await page.close();
  }

  // ---- a failed run holds its reason ----
  {
    console.log('\n[Claude failed]');
    const { page, errors } = await openPage(
      gate({ available: true, state: 'failed', detail: 'claude exited with code 1' }));

    const chip = page.locator('#sp-claude-chip');
    check('the failure chip is visible', await chip.isVisible());
    check('the chip carries the reason',
      (await chip.innerText()).includes('failed — claude exited with code 1'));

    // A failed run is over: the next hand-off must go through.
    await page.keyboard.press('v');
    await page.keyboard.press('a');
    check('"a" after a failure posts again', (await reviseCount(page)) === 1);
    check('no runtime errors', errors.length === 0, errors.join(' | '));
    await page.close();
  }

  // ---- everything waived: nothing to send ----
  {
    console.log('\n[all findings waived]');
    const { page, errors } = await openPage(
      gate({ available: true, state: 'idle', detail: null }, FINDINGS.map((f) => f.key)));

    await page.keyboard.press('v');
    await page.keyboard.press('a');
    check('"a" with everything waived posts nothing', (await reviseCount(page)) === 0);
    check('the refusal is explained on screen',
      (await page.locator('.sp-gate-copied').innerText()).includes('Every finding is waived'));
    check('no runtime errors', errors.length === 0, errors.join(' | '));
    await page.close();
  }

  // ---- the help sheet documents the shortcut ----
  {
    console.log('\n[keyboard help]');
    const { page, errors } = await openPage(gate({ available: true, state: 'idle', detail: null }));

    await page.keyboard.press('?');
    check('help documents the Claude hand-off',
      (await page.locator('#sp-help').innerText()).includes('Claude revises the document in place'));
    check('no runtime errors', errors.length === 0, errors.join(' | '));
    await page.close();
  }

  await browser.close();
  console.log(failures === 0 ? '\nall browser assertions passed' : `\n${failures} assertion(s) failed`);
  process.exit(failures === 0 ? 0 : 1);
})();
