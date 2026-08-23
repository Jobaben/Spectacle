// preview-loop.browser.test.js — the reader's revision-loop HUD, driven in real Chromium.
//
// Same approach as the gate suite: the page is assembled exactly as PreviewHtml.Build emits it,
// with the same assets in the same order, and driven with real keystrokes. The loop payload is the
// shape LoopSession produces — history rows with per-iteration tallies, the latest delta in full,
// and the changed block ids.
//
// Run:  cd test/js && npm install && npx playwright install chromium && node preview-loop.browser.test.js
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
  `<h1 class="md-block" data-block-id="b0" data-kind="heading" data-line="6" data-text-hash="h0" data-occurrence-index="0" tabindex="0">Authentication design</h1>`,
  block(1, 'paragraph', 8, 'The service issues a signed token on login.'),
  block(2, 'heading', 10, 'Overview'),
  block(3, 'paragraph', 12, 'The token lifetime is 30 minutes, refreshed on activity.'),
  block(4, 'heading', 14, 'Rollout'),
  block(5, 'paragraph', 16, 'Rollout is keyed by tenant, largest first.'),
  ...Array.from({ length: 16 }, (_, k) =>
    block(6 + k, 'paragraph', 18 + k * 2, `Filler paragraph ${k + 1}, so a jump is observable.`)),
].join('\n');

// A session in its third iteration: converging (5 -> 3 -> 1 blocking), with the latest save
// fixing two findings, introducing one, and addressing one of the reviewer's two comments.
const SESSION = {
  iteration: 3,
  history: [
    { n: 1, at: '2026-08-23T09:41:12Z', blocking: 5, errors: 5, warnings: 2, advisories: 1, fixed: 0, introduced: 0, commentsAddressed: 0, commentsOpen: 2 },
    { n: 2, at: '2026-08-23T09:43:40Z', blocking: 3, errors: 3, warnings: 1, advisories: 1, fixed: 3, introduced: 1, commentsAddressed: 0, commentsOpen: 2 },
    { n: 3, at: '2026-08-23T09:45:05Z', blocking: 1, errors: 1, warnings: 1, advisories: 0, fixed: 2, introduced: 1, commentsAddressed: 1, commentsOpen: 1 },
  ],
  delta: {
    fixed: [
      { category: 'ai-artifacts', rule: 'unfilled-template', line: 12, message: "unsubstituted template token '{{token_ttl}}'" },
      { category: 'lint', rule: 'placeholder', line: 16, message: "placeholder marker 'TODO'" },
    ],
    introduced: [
      { category: 'bare-urls', rule: 'bare-url', line: 16, message: 'bare URL: https://internal.example/keys' },
    ],
    persisting: 1,
  },
  comments: {
    addressed: [
      { body: 'Spell out how the refresh window is enforced.', context: 'The token lifetime is 30 minutes', line: 12 },
    ],
  },
  changedBlockIds: ['b3', 'b5'],
};

const FIRST_RENDER = {
  iteration: 1,
  history: [{ n: 1, at: '2026-08-23T09:41:12Z', blocking: 5, errors: 5, warnings: 2, advisories: 1, fixed: 0, introduced: 0, commentsAddressed: 0, commentsOpen: 0 }],
  delta: null,
  comments: { addressed: [] },
  changedBlockIds: [],
};

// The reviewer's one still-open comment, exactly as PreviewHtml's annotations payload carries it —
// the loop headline counts what is open live from this payload, not from the loop history.
const ANNOTATIONS = {
  comments: [
    {
      id: 'c-2', body: 'Say which tenant goes first.',
      originalText: 'Rollout is keyed by tenant, largest first.',
      createdAt: '2026-08-23T09:40:00Z', resolvedAt: null,
      blockAnchor: {
        kind: 'paragraph', line: 16, textHash: 'h5', occurrenceIndex: 0,
        leadingText: 'Rollout is keyed by tenant, largest first.', blockIdAtRender: 'b5',
      },
    },
  ],
  orphaned: [],
};

const GATE = {
  status: 'fail', passed: false, failOn: 'error',
  counts: { blocking: 1, error: 1, warning: 1, info: 0, suppressed: 0 },
  coverage: { checksDisabled: [], suppressed: 0 },
  triage: { waived: [] },
  metadata: [{ key: 'workflow', value: 'spec-writer' }],
  findings: [
    { key: 'bare-urls|bare-urls/bare-url|bare URL: https://internal.example/keys', severity: 'error', rule: 'bare-urls/bare-url', check: 'bare-urls', line: 16, message: 'bare URL: https://internal.example/keys', remedy: 'Wrap the URL in a descriptive link.' },
  ],
};

function buildHtml(theme, loop, annotations) {
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
  <script>window.__spectacleAnnotations__ = ${json(annotations || { comments: [], orphaned: [] })};</script>
  <script>window.__spectacleOutline__ = ${json([{ level: 1, text: 'Authentication design', id: 'authentication-design', line: 6 }])};</script>
  <script>window.__spectacleGate__ = ${json(GATE)};</script>
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

  // ---- the ambient HUD only appears once the loop has looped, but "l" always answers ----
  {
    console.log('\n[first render]');
    const page = await browser.newPage({ viewport: { width: 1100, height: 800 } });
    const errors = [];
    page.on('pageerror', (e) => errors.push(String(e)));
    await page.setContent(buildHtml('dark', FIRST_RENDER), { waitUntil: 'load' });
    check('no pill on iteration 1', (await page.locator('#sp-loop-pill').count()) === 0);
    check('no toast on iteration 1', (await page.locator('#sp-loop-toast').count()) === 0);
    check('no changed-block markers on iteration 1', (await page.locator('.sp-loop-changed').count()) === 0);

    // The timeline itself exists from the first render — it just isn't advertised yet.
    await page.keyboard.press('l');
    const panel = page.locator('#sp-loop-panel');
    check('"l" opens the timeline on iteration 1', await panel.isVisible());
    check('the opening iteration is the only row', (await page.locator('.sp-loop-row').count()) === 1);
    check('panel counts one iteration', (await panel.innerText()).includes('1 iteration(s)'));
    await page.keyboard.press('Escape');
    check('Escape closes the iteration-1 timeline', !(await panel.isVisible()));

    check('no runtime errors', errors.length === 0, errors.join(' | '));
    await page.close();
  }

  {
    console.log('\n[no session]');
    const page = await browser.newPage({ viewport: { width: 1100, height: 800 } });
    await page.setContent(buildHtml('dark', null), { waitUntil: 'load' });
    check('no HUD at all without a session payload', (await page.locator('#sp-loop-pill').count()) === 0);
    await page.close();
  }

  // ---- an active session, per theme ----
  for (const theme of ['dark', 'light', 'hc']) {
    console.log(`\n[${theme} / iteration 3]`);
    const page = await browser.newPage({ viewport: { width: 1100, height: 800 } });
    const errors = [];
    page.on('pageerror', (e) => errors.push(String(e)));
    let served = buildHtml(theme, SESSION, ANNOTATIONS);
    await page.route('http://spectacle.test/**', (route) =>
      route.fulfill({ contentType: 'text/html', body: served }));
    await page.goto('http://spectacle.test/doc.html', { waitUntil: 'load' });
    const vp = page.viewportSize();

    // Toast.
    const toast = page.locator('#sp-loop-toast');
    check('the save is announced', await toast.isVisible());
    const toastText = await toast.innerText();
    check('toast names the iteration', toastText.includes('Iteration 3'));
    check('toast counts the fixes', toastText.includes('2 fixed'));
    check('toast counts the addressed comments', toastText.includes('1 comment addressed'));
    check('toast counts the regressions', toastText.includes('1 new'));
    check('toast counts what remains', toastText.includes('1 blocking remain'));

    // Changed-block markers.
    check('the touched blocks are marked', (await page.locator('.sp-loop-changed').count()) === 2);
    check('the marked blocks are the payload ids', await page.evaluate(() => {
      const ids = Array.from(document.querySelectorAll('.sp-loop-changed')).map((el) => el.getAttribute('data-block-id'));
      return ids.join(',') === 'b3,b5';
    }));
    const markerColor = await page.evaluate(() => {
      const el = document.querySelector('.sp-loop-changed');
      return getComputedStyle(el, '::before').backgroundColor;
    });
    check('the marker actually paints', !!markerColor && markerColor !== 'rgba(0, 0, 0, 0)', markerColor);

    // Pill.
    const pill = page.locator('#sp-loop-pill');
    check('the iteration pill is shown', await pill.isVisible());
    check('pill names the iteration', (await pill.innerText()).includes('iter 3'));
    const pillBox = await pill.boundingBox();
    const badgeBox = await page.locator('#sp-gate-badge').boundingBox();
    check('pill sits above the gate badge, on screen',
      !!pillBox && !!badgeBox && pillBox.y + pillBox.height <= badgeBox.y + 2 &&
      pillBox.x + pillBox.width <= vp.width, JSON.stringify({ pillBox, badgeBox }));

    // Panel.
    await page.keyboard.press('l');
    const panel = page.locator('#sp-loop-panel');
    check('"l" opens the timeline', await panel.isVisible());
    check('the toast yields to the panel', !(await toast.isVisible()));
    const panelText = await panel.innerText();
    check('headline reads converging', panelText.includes('Converging'));
    check('one sparkline bar per iteration', (await page.locator('#sp-loop-spark .sp-loop-bar').count()) === 3);
    check('every bar stacks its open-comment share',
      (await page.locator('#sp-loop-spark .sp-loop-bar-seg-comments').count()) === 3);
    check('every bar keeps its blocking share',
      (await page.locator('#sp-loop-spark .sp-loop-bar-seg-blocking').count()) === 3);
    if (theme !== 'hc') {
      const segColours = await page.evaluate(() => ['.sp-loop-bar-seg-blocking', '.sp-loop-bar-seg-comments'].map((s) => {
        const el = document.querySelector('#sp-loop-spark ' + s);
        return el ? getComputedStyle(el).backgroundColor : null;
      }));
      check('blocking and comment shares paint distinct colours',
        segColours.every((c) => c && c !== 'rgba(0, 0, 0, 0)') && segColours[0] !== segColours[1],
        JSON.stringify(segColours));
    }
    check('one timeline row per iteration', (await page.locator('.sp-loop-row').count()) === 3);
    check('rows are newest first', (await page.locator('.sp-loop-row').first().innerText()).includes('#3'));
    check('the latest row details the fixes', panelText.includes('{{token_ttl}}'));
    check('the latest row details the regression', panelText.includes('internal.example/keys'));
    check('the latest row details the addressed comment, in the reviewer\'s words',
      panelText.includes('Spell out how the refresh window is enforced.'));
    check('the latest row tallies the addressed comments', panelText.includes('💬 1 addressed'));
    check('history rows carry the open-comment count', panelText.includes('💬1'));
    check('headline counts the still-open comments', panelText.includes('1 comment still open.'));

    if (theme !== 'hc') {
      const colours = await page.evaluate(() => ['.sp-loop-fixed', '.sp-loop-new'].map((s) => {
        const el = document.querySelector('#sp-loop-panel ' + s);
        return el ? getComputedStyle(el).color : null;
      }));
      check('fixed and new resolve to distinct colours',
        colours.every((c) => c && c !== 'rgba(0, 0, 0, 0)') && colours[0] !== colours[1],
        JSON.stringify(colours));
    }

    // Clicking an introduced finding jumps to its line.
    const before = await page.evaluate(() => window.scrollY);
    await page.locator('.sp-loop-detail-new').first().click();
    check('clicking a new finding closes the panel', !(await panel.isVisible()));
    await page.waitForTimeout(150);
    check('the destination block is focused',
      (await page.evaluate(() => document.activeElement && document.activeElement.getAttribute('data-line'))) === '16',
      String(before));

    // Clicking an addressed comment jumps to where its block sat, to verify the ask was answered.
    await page.keyboard.press('l');
    await page.locator('.sp-loop-detail-comment').first().click();
    check('clicking an addressed comment closes the panel', !(await panel.isVisible()));
    await page.waitForTimeout(150);
    check('the revised block behind the comment is focused',
      (await page.evaluate(() => document.activeElement && document.activeElement.getAttribute('data-line'))) === '12');

    // Containment in both directions.
    await page.keyboard.press('l');
    check('"v" is refused while the timeline owns the screen', await (async () => {
      await page.keyboard.press('v');
      return !(await page.locator('#sp-gate-panel').isVisible());
    })());
    await page.keyboard.press('Escape');
    check('Escape closes the timeline', !(await panel.isVisible()));
    await page.keyboard.press('v');
    check('"l" is refused while the gate panel owns the screen', await (async () => {
      await page.keyboard.press('l');
      return !(await panel.isVisible());
    })());
    await page.keyboard.press('Escape');

    // The re-render that follows a save: same iteration, so no second toast — but an open
    // timeline comes back open.
    await page.keyboard.press('l');
    await page.goto('http://spectacle.test/doc.html', { waitUntil: 'load' });
    check('no repeat toast for an already-announced iteration',
      (await page.locator('#sp-loop-toast').count()) === 0);
    check('the open timeline survives the re-render', await page.locator('#sp-loop-panel').isVisible());
    await page.keyboard.press('Escape');

    check('no runtime errors', errors.length === 0, errors.join(' | '));
    await page.close();
  }

  // ---- a comments-only session: the gate is clean throughout, the reviewer's asks converge ----
  {
    console.log('\n[comments only]');
    const COMMENTS_ONLY = {
      iteration: 3,
      history: [
        { n: 1, at: '2026-08-23T10:01:00Z', blocking: 0, errors: 0, warnings: 0, advisories: 0, fixed: 0, introduced: 0, commentsAddressed: 0, commentsOpen: 2 },
        { n: 2, at: '2026-08-23T10:02:00Z', blocking: 0, errors: 0, warnings: 0, advisories: 0, fixed: 0, introduced: 0, commentsAddressed: 1, commentsOpen: 1 },
        { n: 3, at: '2026-08-23T10:03:00Z', blocking: 0, errors: 0, warnings: 0, advisories: 0, fixed: 0, introduced: 0, commentsAddressed: 1, commentsOpen: 0 },
      ],
      delta: { fixed: [], introduced: [], persisting: 0 },
      comments: { addressed: [{ body: 'Say which tenant goes first.', context: 'Rollout is keyed by tenant', line: 16 }] },
      changedBlockIds: ['b5'],
    };
    const page = await browser.newPage({ viewport: { width: 1100, height: 800 } });
    const errors = [];
    page.on('pageerror', (e) => errors.push(String(e)));
    await page.setContent(buildHtml('dark', COMMENTS_ONLY), { waitUntil: 'load' });
    await page.keyboard.press('l');

    const bars = await page.evaluate(() => Array.from(
      document.querySelectorAll('#sp-loop-spark .sp-loop-bar')).map((b) => ({
        clean: b.classList.contains('sp-loop-bar-clean'),
        height: b.style.height,
        comments: b.querySelectorAll('.sp-loop-bar-seg-comments').length,
        blocking: b.querySelectorAll('.sp-loop-bar-seg-blocking').length,
      })));
    check('open comments give the bars height even with the gate clean',
      bars.length === 3 && bars[0].height === '100%' && bars[1].height === '50%',
      JSON.stringify(bars));
    check('a bar with open comments is not painted clean',
      !bars[0].clean && !bars[1].clean, JSON.stringify(bars));
    check('the comment share is the whole bar when nothing blocks',
      bars[0].comments === 1 && bars[0].blocking === 0, JSON.stringify(bars));
    check('the settled iteration keeps the clean stub',
      bars[2].clean && bars[2].comments === 0, JSON.stringify(bars));

    check('no runtime errors', errors.length === 0, errors.join(' | '));
    await page.close();
  }

  // ---- help sheet documents the shortcut ----
  {
    console.log('\n[help sheet]');
    const page = await browser.newPage({ viewport: { width: 1100, height: 800 } });
    await page.setContent(buildHtml('dark', SESSION), { waitUntil: 'load' });
    await page.keyboard.press('?');
    const help = await page.locator('#sp-help').innerText();
    check('help documents the loop shortcut', help.includes('revision-loop'));
    check('help documents the loop panel keys', help.includes('Close loop timeline'));
    check('help documents waiving', help.includes('Waive'));
    await page.close();
  }

  await browser.close();
  console.log(failures === 0 ? '\nall browser assertions passed' : `\n${failures} assertion(s) failed`);
  process.exit(failures === 0 ? 0 : 1);
})();
