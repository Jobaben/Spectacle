// preview-keynav.browser.test.js — hierarchical Esc, driven in real Chromium.
//
// Esc must close the topmost open layer and nothing else; only an idle preview may escalate to
// the host. In the app the escalation is a `closeWindow` web message the WPF window closes on
// (the Escape KeyBinding it replaced closed the window unconditionally — panels, find bar and
// composer be damned). Here chrome.webview is shimmed, so the assertion surface is exactly the
// contract: which layer closed, and whether the message fired.
//
// Run:  cd test/js && npm install && npx playwright install chromium && node preview-keynav.browser.test.js
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
  `<h1 class="md-block" data-block-id="b0" data-kind="heading" data-line="1" data-text-hash="h0" data-occurrence-index="0" tabindex="0" id="title">Escape routing</h1>`,
  block(1, 'paragraph', 3, 'A paragraph to focus and to find text in.'),
  block(2, 'paragraph', 5, 'Another paragraph.'),
].join('\n');

const GATE = {
  status: 'fail', passed: false, failOn: 'error',
  counts: { blocking: 1, error: 1, warning: 0, info: 0, suppressed: 0 },
  coverage: { checksDisabled: [], suppressed: 0 },
  triage: { waived: [] },
  metadata: [],
  findings: [
    { key: 'lint|lint/placeholder|placeholder marker', severity: 'error', rule: 'lint/placeholder', check: 'lint', line: 3, message: 'placeholder marker', remedy: 'Replace it.' },
  ],
};

const LOOP = {
  iteration: 2,
  history: [
    { n: 1, at: '2026-08-23T09:41:12Z', blocking: 2, errors: 2, warnings: 0, advisories: 0, fixed: 0, introduced: 0 },
    { n: 2, at: '2026-08-23T09:43:40Z', blocking: 1, errors: 1, warnings: 0, advisories: 0, fixed: 1, introduced: 0 },
  ],
  delta: { fixed: [], introduced: [], persisting: 1 },
  changedBlockIds: ['b1'],
};

function buildHtml() {
  const json = (v) => JSON.stringify(v).replace(/<\//g, '<\\/');
  return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width,initial-scale=1" />
  <style>${asset('dark.css')}</style>
  <style>${asset('preview.css')}</style>
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
  <script>window.__spectacleAnnotations__ = ${json({ comments: [], orphaned: [] })};</script>
  <script>window.__spectacleOutline__ = ${json([{ level: 1, text: 'Escape routing', id: 'title', line: 1 }])};</script>
  <script>window.__spectacleGate__ = ${json(GATE)};</script>
  <script>window.__spectacleLoop__ = ${json(LOOP)};</script>
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

  const page = await browser.newPage({ viewport: { width: 1100, height: 800 } });
  const errors = [];
  page.on('pageerror', (e) => errors.push(String(e)));

  // The WPF host's message channel, shimmed: the page must not know the difference. Served over
  // a routed navigation (not setContent) so the init script is installed before the page runs.
  await page.addInitScript(() => {
    window.__posted = [];
    window.chrome = { webview: { postMessage: (m) => window.__posted.push(JSON.parse(m)) } };
  });
  const served = buildHtml();
  await page.route('http://spectacle.test/**', (route) =>
    route.fulfill({ contentType: 'text/html', body: served }));
  await page.goto('http://spectacle.test/doc.html', { waitUntil: 'load' });

  const closeRequests = () =>
    page.evaluate(() => window.__posted.filter((m) => m.type === 'closeWindow').length);

  // ---- each layer takes its own Esc; no escalation while anything is open ----

  console.log('\n[find bar]');
  await page.keyboard.press('Control+f');
  check('Ctrl+F opens find', await page.locator('#sp-find').isVisible());
  await page.keyboard.press('Escape');
  check('Esc closes find', !(await page.locator('#sp-find').isVisible()));
  check('no close request while find took the Esc', (await closeRequests()) === 0);

  console.log('\n[outline]');
  await page.keyboard.press('t');
  check('"t" opens the outline', await page.locator('#sp-outline').count() > 0 &&
    await page.evaluate(() => { const el = document.getElementById('sp-outline'); return !!el && !el.hidden; }));
  await page.keyboard.press('Escape');
  check('Esc closes the outline', await page.evaluate(() => { const el = document.getElementById('sp-outline'); return !el || el.hidden; }));
  check('no close request while the outline took the Esc', (await closeRequests()) === 0);

  console.log('\n[gate panel]');
  await page.keyboard.press('v');
  check('"v" opens the gate panel', await page.locator('#sp-gate-panel').isVisible());
  await page.keyboard.press('Escape');
  check('Esc closes the gate panel', !(await page.locator('#sp-gate-panel').isVisible()));
  check('no close request while the gate took the Esc', (await closeRequests()) === 0);

  console.log('\n[loop timeline]');
  await page.keyboard.press('l');
  check('"l" opens the timeline', await page.locator('#sp-loop-panel').isVisible());
  await page.keyboard.press('Escape');
  check('Esc closes the timeline', !(await page.locator('#sp-loop-panel').isVisible()));
  check('no close request while the timeline took the Esc', (await closeRequests()) === 0);

  console.log('\n[help sheet]');
  await page.keyboard.press('?');
  check('"?" opens help', await page.locator('#sp-help').isVisible());
  await page.keyboard.press('Escape');
  check('Esc closes help', !(await page.locator('#sp-help').isVisible()));
  check('no close request while help took the Esc', (await closeRequests()) === 0);

  console.log('\n[composer]');
  await page.locator('.md-block[data-block-id="b1"]').click();
  await page.keyboard.press('Enter');
  check('Enter on a block opens the composer', await page.locator('.sp-composer').isVisible());
  await page.keyboard.press('Escape');
  check('Esc cancels the composer', (await page.locator('.sp-composer').count()) === 0);
  check('no close request while the composer took the Esc', (await closeRequests()) === 0);

  // ---- only the idle preview escalates ----

  console.log('\n[idle]');
  await page.keyboard.press('Escape');
  check('idle Esc asks the host to close the window', (await closeRequests()) === 1);
  await page.keyboard.press('Escape');
  check('each idle Esc asks exactly once', (await closeRequests()) === 2);

  check('no runtime errors', errors.length === 0, errors.join(' | '));
  await page.close();

  await browser.close();
  console.log(failures === 0 ? '\nall browser assertions passed' : `\n${failures} assertion(s) failed`);
  process.exit(failures === 0 ? 0 : 1);
})();
