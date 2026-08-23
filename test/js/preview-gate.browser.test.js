// preview-gate.browser.test.js — the reader's gate overlay, driven in real Chromium.
//
// WebView2 *is* Chromium, so this is the same engine the reader renders in. The test assembles the
// preview document exactly as PreviewHtml.Build does — the same asset files, in the same order, with
// the same payload injection — then drives it with real keystrokes.
//
// It replaced an earlier version that ran against a hand-rolled DOM stub. The stub passed every one
// of its assertions while three real defects were live: the overlay didn't follow the containment
// contract the other overlays share, it opened underneath the modal help sheet, and the empty panel
// offered a jump with nothing to jump to. A stub can only check the logic you thought to model; the
// browser checks what the reader will actually get.
//
// Run:  cd test/js && npm install && npx playwright install chromium && node preview-gate.browser.test.js
// Set SPECTACLE_CHROMIUM to a Chromium binary to use one that is already on the machine.
'use strict';

const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright-core');

const ASSETS = path.join(__dirname, '..', '..', 'src', 'Spectacle', 'Render', 'Assets');
const asset = (name) => fs.readFileSync(path.join(ASSETS, name), 'utf8');

// ---------- the document under test ----------

// A block as BlockTagger emits it: the class, the data-* attributes, and the tabindex the reader's
// keyboard navigation relies on.
const block = (i, kind, line, html) => {
  const tag = kind === 'heading' ? 'h2' : 'p';
  return `<${tag} class="md-block" data-block-id="b${i}" data-kind="${kind}" data-line="${line}" ` +
    `data-text-hash="h${i}" data-occurrence-index="0" tabindex="0">${html}</${tag}>`;
};

const BODY = [
  `<h1 class="md-block" data-block-id="b0" data-kind="heading" data-line="6" data-text-hash="h0" data-occurrence-index="0" tabindex="0">Authentication design</h1>`,
  block(1, 'paragraph', 8, 'Certainly! Here is the updated specification you asked for.'),
  block(2, 'heading', 10, 'Overview'),
  block(3, 'paragraph', 12, 'The service issues a signed token on login and rejects an expired one with <code>401</code>. The token lifetime is {{token_ttl}} minutes.'),
  block(4, 'heading', 14, 'Acceptance criteria'),
  `<ul class="md-block" data-block-id="b5" data-kind="list-item" data-line="16" data-text-hash="h5" data-occurrence-index="0" tabindex="0"><li>A valid credential pair returns a token.</li><li>An expired token is rejected.</li></ul>`,
  block(6, 'paragraph', 20, 'We should probably cache the public key, see https://internal.example/keys for the rotation schedule.'),
  block(7, 'heading', 22, 'Rollout'),
  block(8, 'paragraph', 24, 'The rest of the document is unchanged.'),
  // Enough length that a jump actually scrolls.
  ...Array.from({ length: 14 }, (_, k) =>
    block(9 + k, 'paragraph', 26 + k * 2, `Filler paragraph ${k + 1}, so the document is long enough to scroll and a jump is observable.`)),
].join('\n');

// A failing verdict with one finding at each severity, reduced coverage, and a blank metadata
// value. Findings carry the line-insensitive `key` the triage layer waives by, exactly as
// PreviewHtml emits it (check|rule|message).
const keyed = (f) => Object.assign({ key: f.check + '|' + f.rule + '|' + f.message }, f);
const FAILING = {
  status: 'fail', passed: false, failOn: 'error',
  counts: { blocking: 4, error: 4, warning: 1, info: 1, suppressed: 1 },
  coverage: { checksDisabled: ['duplication'], suppressed: 1 },
  triage: { waived: [] },
  metadata: [
    { key: 'workflow', value: 'spec-writer' },
    { key: 'stage', value: 'draft' },
    { key: 'run.model', value: 'claude-opus-5' },
    { key: 'reviewer', value: '' },
  ],
  findings: [
    { severity: 'error', rule: 'front-matter/empty-value', check: 'front-matter', line: 5, message: "required front-matter key 'reviewer' is present but empty", remedy: 'Fill in the value, or remove the key if it truly does not apply.' },
    { severity: 'error', rule: 'ai-artifacts/assistant-voice', check: 'ai-artifacts', line: 8, message: "assistant framing 'Certainly!'", remedy: 'Delete the framing sentence.' },
    { severity: 'error', rule: 'ai-artifacts/unfilled-template', check: 'ai-artifacts', line: 12, message: "unsubstituted template token '{{token_ttl}}'", remedy: 'Replace the token with its value.' },
    { severity: 'error', rule: 'ai-artifacts/truncated-output', check: 'ai-artifacts', line: 24, message: "truncation marker 'The rest of the document is unchanged'", remedy: 'Write the omitted content, or cut the section and its marker.' },
    { severity: 'warning', rule: 'bare-urls/bare-url', check: 'bare-urls', line: 20, message: 'bare URL: https://internal.example/keys', remedy: 'Wrap the URL in a descriptive link.' },
    { severity: 'info', rule: 'prose/hedge', check: 'prose', line: 20, message: "hedging: 'should probably'", remedy: 'Commit to the decision.' },
  ].map(keyed),
};

const PASSING = {
  status: 'pass', passed: true, failOn: 'error',
  counts: { blocking: 0, error: 0, warning: 0, info: 0, suppressed: 0 },
  coverage: { checksDisabled: [], suppressed: 0 },
  triage: { waived: [] },
  metadata: [{ key: 'workflow', value: 'spec-writer' }, { key: 'stage', value: 'final' }],
  findings: [],
};

// Mirrors PreviewHtml.Build, including the `</` -> `<\/` payload guard and the script order that
// decides which overlay wins a shortcut. `loop` is the revision-loop payload (null for none), and
// `bridge` injects a captured host bridge the way WebView2 provides one.
function buildHtml(theme, gate, loop, bridge) {
  const json = (v) => JSON.stringify(v).replace(/<\//g, '<\\/');
  return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width,initial-scale=1" />
  <style>${asset(theme === 'hc' ? 'hc.css' : theme === 'light' ? 'light.css' : 'dark.css')}</style>
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
  ${bridge ? '<script>window.__posted = []; window.chrome = { webview: { postMessage: function (m) { window.__posted.push(m); } } };</script>' : ''}
  <script>window.__spectacleAnnotations__ = ${json({ comments: [], orphaned: [] })};</script>
  <script>window.__spectacleOutline__ = ${json([
    { level: 1, text: 'Authentication design', id: 'authentication-design', line: 6 },
    { level: 2, text: 'Overview', id: 'overview', line: 10 },
    { level: 2, text: 'Acceptance criteria', id: 'acceptance-criteria', line: 14 },
    { level: 2, text: 'Rollout', id: 'rollout', line: 22 },
  ])};</script>
  <script>window.__spectacleGate__ = ${gate ? json(gate) : 'null'};</script>
  <script>window.__spectacleLoop__ = ${loop ? json(loop) : 'null'};</script>
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

  const cases = [
    ['dark', FAILING, 'dark / failing'],
    ['dark', PASSING, 'dark / passing'],
    ['light', FAILING, 'light / failing'],
    ['hc', FAILING, 'high contrast / failing'],
  ];

  for (const [theme, gate, label] of cases) {
    const page = await browser.newPage({ viewport: { width: 1100, height: 800 } });
    const errors = [];
    page.on('pageerror', (e) => errors.push(String(e)));
    page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()); });

    await page.setContent(buildHtml(theme, gate), { waitUntil: 'load' });
    console.log(`\n[${label}]`);
    check('no runtime errors on load', errors.length === 0, errors.join(' | '));

    // ---- badge ----
    const badge = page.locator('#sp-gate-badge');
    const vp = page.viewportSize();
    check('badge is visible', await badge.isVisible());
    const badgeBox = await badge.boundingBox();
    check('badge sits in the bottom-right corner, fully on screen',
      !!badgeBox && badgeBox.x + badgeBox.width <= vp.width && badgeBox.y + badgeBox.height <= vp.height &&
      badgeBox.x > vp.width * 0.6 && badgeBox.y > vp.height * 0.6, JSON.stringify(badgeBox));
    // Catches the stylesheet failing to apply at all, which a DOM stub cannot see.
    check('badge has real laid-out size', !!badgeBox && badgeBox.width > 90 && badgeBox.height > 22, JSON.stringify(badgeBox));
    check('badge states the verdict', (await badge.innerText()).includes(gate.passed ? 'GATE PASS' : 'GATE FAIL'));

    // ---- metadata card ----
    const card = page.locator('#sp-gate-meta');
    check('metadata card is visible', await card.isVisible());
    const cardBox = await card.boundingBox();
    const h1Box = await page.locator('h1').boundingBox();
    check('card renders above the document', cardBox.y < h1Box.y);
    check('card shows the metadata', (await card.innerText()).includes('spec-writer'));

    // ---- panel ----
    await page.keyboard.press('v');
    const panel = page.locator('#sp-gate-panel');
    check('"v" opens the panel', await panel.isVisible());
    const panelBox = await panel.boundingBox();
    check('panel is docked to the right edge at full height',
      !!panelBox && Math.abs(panelBox.x + panelBox.width - vp.width) < 2 && panelBox.height >= vp.height - 2,
      JSON.stringify(panelBox));
    check('panel lists every finding', (await page.locator('.sp-gate-item').count()) === gate.findings.length);

    if (gate.findings.length) {
      check('panel shows the fix for each finding', (await panel.innerText()).includes('Fix:'));
      check('panel declares reduced coverage', (await panel.innerText()).includes('Reduced coverage'));
      const colours = await page.evaluate(() => ['error', 'warning', 'info'].map((s) => {
        const el = document.querySelector('.sp-gate-sev-' + s);
        return el ? getComputedStyle(el).color : null;
      }));
      check('every severity colour resolves', colours.every((c) => c && c !== 'rgba(0, 0, 0, 0)'), JSON.stringify(colours));
      // High contrast drops the hues on purpose; dark and light each keep them distinct.
      if (theme !== 'hc') check('severity colours are distinct', new Set(colours).size === 3, JSON.stringify(colours));
      // preview-gate.css falls back to the dark hues when a theme leaves --gate-* unset, and the
      // fallback is silent — on a light page it would be a label at ~2.5:1. So the light run
      // asserts the resolved colours are not the dark ones.
      if (theme === 'light') {
        const darkSeverities = ['rgb(244, 135, 113)', 'rgb(220, 220, 170)', 'rgb(156, 220, 254)'];
        check('light theme names its own severity hues',
          colours.every((c) => !darkSeverities.includes(c)), JSON.stringify(colours));
      }
    } else {
      check('empty panel says so', (await panel.innerText()).includes('No findings'));
      check('empty panel offers no jump', !(await panel.innerText()).includes('Enter to jump'));
      const footerBox = await page.locator('.sp-gate-footer').boundingBox();
      check('footer stays pinned to the bottom of the panel',
        Math.abs(footerBox.y + footerBox.height - panelBox.y - panelBox.height) < 2, JSON.stringify(footerBox));
    }

    // ---- jumping to a finding ----
    if (gate.findings.length) {
      const before = await page.evaluate(() => window.scrollY);
      for (let i = 0; i < 3; i++) await page.keyboard.press('ArrowDown');  // the line-24 finding
      await page.keyboard.press('Enter');
      check('Enter closes the panel', !(await panel.isVisible()));
      await page.waitForTimeout(150);
      check('Enter scrolled the document', (await page.evaluate(() => window.scrollY)) !== before);
      check('the destination is flashed', (await page.locator('.sp-gate-flash').count()) === 1);
      check('focus moved to the block containing the line',
        (await page.evaluate(() => document.activeElement && document.activeElement.getAttribute('data-line'))) === '24');
    }

    // ---- containment, from both directions ----
    // The panel keeps keys away from the document behind it. Its reach stops at the overlays whose
    // capture listeners are registered earlier (find, outline) — see preview-gate.js.
    if (!(await panel.isVisible())) await page.keyboard.press('v');
    check('"v" toggles the open panel closed', await (async () => {
      await page.keyboard.press('v');
      return !(await panel.isVisible());
    })());

    check('"t" opens the outline when nothing owns the screen', await (async () => {
      await page.keyboard.press('t');
      return await page.locator('#sp-outline').isVisible();
    })());
    check('the open outline refuses "v"', await (async () => {
      await page.keyboard.press('v');
      return !(await panel.isVisible());
    })());
    await page.keyboard.press('Escape');
    check('outline closed', !(await page.locator('#sp-outline').isVisible()));

    await page.keyboard.press('?');
    check('"?" opens the keyboard help', await page.locator('#sp-help').isVisible());
    check('help documents the gate shortcut', (await page.locator('#sp-help').innerText()).includes('quality gate'));
    check('the open help sheet refuses "v"', await (async () => {
      await page.keyboard.press('v');
      return !(await panel.isVisible());
    })());
    await page.keyboard.press('Escape');
    check('help closed', !(await page.locator('#sp-help').isVisible()));

    check('"v" works again once nothing else owns the screen', await (async () => {
      await page.keyboard.press('v');
      return await panel.isVisible();
    })());
    check('Escape closes the panel', await (async () => {
      await page.keyboard.press('Escape');
      return !(await panel.isVisible());
    })());

    check('no runtime errors after interaction', errors.length === 0, errors.join(' | '));
    await page.close();
  }

  // The exported, static HTML takes the no-verdict path: no gate was computed, so no overlay at all.
  const page = await browser.newPage();
  await page.setContent(buildHtml('dark', null), { waitUntil: 'load' });
  console.log('\n[no verdict]');
  check('no badge when no gate was computed', (await page.locator('#sp-gate-badge').count()) === 0);
  check('no metadata card when no gate was computed', (await page.locator('#sp-gate-meta').count()) === 0);
  check('the document itself still renders', (await page.locator('h1').innerText()).includes('Authentication'));
  await page.close();

  // ---- triage: waiving, the fix brief, and state across the save re-render ----
  // Served over a stable origin, because that is what the reader gives the preview: panel state
  // rides sessionStorage across the re-render that follows every save.
  {
    console.log('\n[triage]');
    const tPage = await browser.newPage({ viewport: { width: 1100, height: 800 } });
    const tErrors = [];
    tPage.on('pageerror', (e) => tErrors.push(String(e)));

    let served = buildHtml('dark', FAILING, null, true);
    await tPage.route('http://spectacle.test/**', (route) =>
      route.fulfill({ contentType: 'text/html', body: served }));
    await tPage.goto('http://spectacle.test/doc.html', { waitUntil: 'load' });

    await tPage.keyboard.press('v');
    const panel = tPage.locator('#sp-gate-panel');
    check('panel opens', await panel.isVisible());
    check('footer offers triage', (await tPage.locator('.sp-gate-footer').innerText()).includes('Space waive'));
    check('progress says the brief covers everything',
      (await tPage.locator('.sp-gate-progress').innerText()).includes('brief covers all 6'));

    // Waive the selected finding.
    await tPage.keyboard.press(' ');
    const first = tPage.locator('.sp-gate-item').first();
    check('Space marks the finding waived', (await first.getAttribute('class')).includes('sp-gate-waived'));
    check('progress counts the waive',
      (await tPage.locator('.sp-gate-progress').innerText()).includes('1 waived · brief covers 5'));
    let posted = await tPage.evaluate(() => window.__posted.map((m) => JSON.parse(m)));
    check('the waive reached the host with the finding key',
      posted.some((m) => m.type === 'gateWaive' && m.waived === true && m.key === FAILING.findings[0].key),
      JSON.stringify(posted));

    // Copy the brief for what is left.
    await tPage.keyboard.press('c');
    posted = await tPage.evaluate(() => window.__posted.map((m) => JSON.parse(m)));
    check('"c" asks the host for the fix brief', posted.some((m) => m.type === 'copyFixBrief'));
    check('the copy is confirmed on screen with the covered count',
      (await tPage.locator('.sp-gate-copied').innerText()).includes('5 findings'));

    // Waiving is a toggle.
    await tPage.keyboard.press(' ');
    posted = await tPage.evaluate(() => window.__posted.map((m) => JSON.parse(m)));
    check('Space again restores the finding',
      posted.some((m) => m.type === 'gateWaive' && m.waived === false) &&
      !(await first.getAttribute('class')).includes('sp-gate-waived'));
    await tPage.keyboard.press(' '); // waive it back for the persistence leg
    await tPage.keyboard.press('ArrowDown');

    // The save re-render: the host pushes fresh HTML whose payload echoes the waive set. The
    // panel must come back open, on the same finding, with the waive intact.
    served = buildHtml(
      'dark', Object.assign({}, FAILING, { triage: { waived: [FAILING.findings[0].key] } }), null, true);
    await tPage.goto('http://spectacle.test/doc.html', { waitUntil: 'load' });
    check('panel reopens after the re-render', await tPage.locator('#sp-gate-panel').isVisible());
    check('selection survives the re-render',
      (await tPage.locator('.sp-gate-item').nth(1).getAttribute('aria-selected')) === 'true');
    check('the waive comes back from the payload',
      (await tPage.locator('.sp-gate-item').first().getAttribute('class')).includes('sp-gate-waived'));
    check('progress still counts it',
      (await tPage.locator('.sp-gate-progress').innerText()).includes('1 waived · brief covers 5'));

    check('no runtime errors in triage', tErrors.length === 0, tErrors.join(' | '));
    await tPage.close();
  }

  await browser.close();
  console.log(failures === 0 ? '\nall browser assertions passed' : `\n${failures} assertion(s) failed`);
  process.exit(failures === 0 ? 0 : 1);
})();
