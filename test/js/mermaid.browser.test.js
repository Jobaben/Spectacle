// mermaid.browser.test.js — the reader's diagram rendering, driven in real Chromium.
//
// WebView2 *is* Chromium, so this is the same engine the reader renders in. Nothing here can be
// reached from the xUnit suite: the C# side emits a container and hands mermaid a configuration, and
// whether a diagram then actually *draws* is a question only a browser answers.
//
// The division of labour with the C# tests: MermaidRenderingTests asserts the emitted markup and the
// exact configuration values, MermaidCheckerTests asserts the gate's findings, PaletteContrastTests
// asserts the palette's ratios. This file asserts the things that need the bundle — every diagram
// type draws, a bad diagram degrades to its source instead of taking the page down, an undescribed
// diagram still reaches a screen reader with a name, and the gate's list of diagram keywords still
// matches what the vendored bundle actually recognizes.
//
// Run:  cd test/js && npm install && npx playwright install chromium && node mermaid.browser.test.js
// Set SPECTACLE_CHROMIUM to a Chromium binary to use one that is already on the machine.
'use strict';

const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright-core');

const ASSETS = path.join(__dirname, '..', '..', 'src', 'Spectacle', 'Render', 'Assets');
const SRC = path.join(__dirname, '..', '..', 'src', 'Spectacle', 'Render');
const asset = (name) => fs.readFileSync(path.join(ASSETS, name), 'utf8');

// ---------- the diagrams under test ----------

// One per diagram type the gate claims to support, plus the two failure shapes. `head` is what the
// fence opens with; `label` names the case in the output.
const DIAGRAMS = [
  { label: 'flowchart (described)', src: 'flowchart TD\n  accTitle: Login flow\n  accDescr: A client posts credentials and gets a token.\n  A[Client] -->|POST| B{Valid?}\n  B -->|yes| C[Token]\n  B -->|no| D[401]' },
  { label: 'graph', src: 'graph LR\n  A --> B --> C' },
  { label: 'sequence', src: 'sequenceDiagram\n  participant C as Client\n  participant A as Auth\n  C->>A: POST /login\n  A-->>C: token\n  Note over A: signs with the rotating key' },
  { label: 'class', src: 'classDiagram\n  class Token {\n    +String subject\n    +isExpired() bool\n  }\n  Token <|-- RefreshToken' },
  { label: 'state', src: 'stateDiagram-v2\n  [*] --> Anonymous\n  Anonymous --> Authed: login\n  Authed --> [*]' },
  { label: 'er', src: 'erDiagram\n  USER ||--o{ SESSION : has' },
  { label: 'gantt', src: 'gantt\n  title Rollout\n  dateFormat YYYY-MM-DD\n  section Phase 1\n  Design :a1, 2026-01-01, 14d\n  Build :after a1, 21d' },
  { label: 'pie', src: 'pie title Token lifetimes\n  "under 5 min" : 42\n  "5-60 min" : 35\n  "over 1 hour" : 23' },
  { label: 'journey', src: 'journey\n  title Signing in\n  section Login\n    Enter credentials: 3: Client\n    Receive token: 5: Client' },
  { label: 'mindmap', src: 'mindmap\n  root((Auth))\n    Tokens\n      Access\n    Keys' },
  { label: 'timeline', src: 'timeline\n  title Key rotation\n  2026-01 : First key\n  2026-04 : Rotated' },
  { label: 'gitGraph', src: 'gitGraph\n  commit\n  branch feature\n  commit\n  checkout main\n  merge feature' },
  { label: 'xychart', src: 'xychart-beta\n  title "Latency"\n  x-axis [jan, feb, mar]\n  y-axis "ms" 0 --> 100\n  line [45, 62, 38]' },
  { label: 'quadrant', src: 'quadrantChart\n  title Effort vs value\n  x-axis Low --> High\n  y-axis Low --> High\n  Rotate keys: [0.3, 0.8]' },
  { label: 'requirement', src: 'requirementDiagram\n  requirement rotation {\n    id: 1\n    text: keys rotate\n    risk: high\n    verifymethod: test\n  }' },
  { label: 'C4', src: 'C4Context\n  title System\n  Person(user, "Reader")' },
  { label: 'sankey', src: 'sankey-beta\n\nlogin,token,42\nlogin,denied,8' },
  { label: 'block', src: 'block-beta\n  columns 2\n  A B' },
];

const BAD = { label: 'unparseable', src: 'flowchart TD\n  A[Unclosed --> B{{{' };
const EMPTY = { label: 'empty', src: '' };
const ALL = DIAGRAMS.concat([BAD, EMPTY]);

// ---------- the document under test ----------

// Mirrors MermaidCodeBlockRenderer: a figure carrying BlockTagger's identity attributes and the
// pending marker, wrapping the diagram's escaped source in a pre/code.
function figure(i, diagram) {
  const escaped = diagram.src
    .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
  return `<figure class="language-mermaid md-block" data-block-id="b${i}" data-kind="code" ` +
    `data-line="${i * 3 + 1}" data-text-hash="h${i}" data-occurrence-index="0" tabindex="0" ` +
    `data-mermaid="pending">\n<pre class="mermaid-source"><code>${escaped}</code></pre>\n</figure>`;
}

// Mirrors the flags in MermaidAssets.ConfigJson that change behaviour rather than colour. The palette
// itself is asserted in the C# tests; what matters here is that diagrams are rendered by the script
// (startOnLoad off), from untrusted text (strict), with stable ids.
const CONFIG = {
  startOnLoad: false,
  securityLevel: 'strict',
  theme: 'base',
  darkMode: true,
  deterministicIds: true,
  flowchart: { useMaxWidth: true },
  themeVariables: { background: '#252526', primaryColor: '#2d2d30', primaryTextColor: '#d4d4d4' },
};

function buildHtml(theme) {
  const body = ALL.map((d, i) => figure(i, d)).join('\n');
  return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <style>${asset(theme === 'hc' ? 'hc.css' : 'dark.css')}</style>
  <style>${asset('preview.css')}</style>
  <style>${asset('prism.css')}</style>
  <style>${asset('mermaid.css')}</style>
</head>
<body>
  <main role="main">
${body}
  </main>
  <script>window.__spectacleMermaid__ = ${JSON.stringify(CONFIG)};</script>
  <script>${asset('mermaid.min.js')}</script>
  <script>${asset('preview-mermaid.js')}</script>
</body>
</html>`;
}

// ---------- harness ----------

let failures = 0;
function check(name, ok, detail) {
  if (ok) console.log('  ok   ' + name);
  else { console.log('  FAIL ' + name + (detail ? ' — ' + detail : '')); failures++; }
}

const settled = () =>
  document.querySelectorAll('figure[data-mermaid="pending"]').length === 0;

(async () => {
  const browser = await chromium.launch({
    executablePath: process.env.SPECTACLE_CHROMIUM || undefined,
    args: ['--no-sandbox'],
  });

  for (const theme of ['dark', 'hc']) {
    const page = await browser.newPage({ viewport: { width: 1100, height: 900 } });
    const errors = [];
    const offsite = [];
    page.on('pageerror', (e) => errors.push(String(e)));
    page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()); });
    // setContent serves about:blank, so any request at all is one reaching off the page.
    page.on('request', (r) => { if (!/^(about|data|blob):/.test(r.url())) offsite.push(r.url()); });

    await page.setContent(buildHtml(theme), { waitUntil: 'load' });
    console.log(`\n[${theme}]`);

    await page.waitForFunction(settled, null, { timeout: 60000 }).catch(() => {});

    const state = await page.evaluate(() => Array.from(
      document.querySelectorAll('figure[data-mermaid]')).map((f) => ({
        state: f.getAttribute('data-mermaid'),
        hasSvg: !!f.querySelector('.mermaid-diagram svg'),
        hasToggle: !!f.querySelector('details.mermaid-source-toggle'),
        hasSource: !!f.querySelector('pre.mermaid-source'),
        isBlock: f.classList.contains('md-block') && f.getAttribute('tabindex') === '0',
        blockId: f.getAttribute('data-block-id'),
        svgLabel: (() => { const s = f.querySelector('svg'); return s && s.getAttribute('aria-label'); })(),
        svgTitle: (() => {
          const s = f.querySelector('svg');
          if (!s) return null;
          for (const c of s.children) if (c.tagName === 'title') return c.textContent;
          return null;
        })(),
        detail: (f.querySelector('.mermaid-error-detail') || {}).textContent || null,
      })));

    check('no page errors while drawing', errors.length === 0, errors.slice(0, 2).join(' | '));
    check('nothing is fetched from off the page', offsite.length === 0, offsite.slice(0, 3).join(' | '));
    check('every figure reached a terminal state',
      state.every((s) => s.state === 'done' || s.state === 'error'),
      state.filter((s) => s.state === 'pending').length + ' still pending');

    // Each supported diagram type draws.
    DIAGRAMS.forEach((d, i) => {
      check(`${d.label} draws`, state[i] && state[i].state === 'done' && state[i].hasSvg,
        state[i] && (state[i].detail || state[i].state));
    });

    // A drawn diagram keeps its identity and its text alternative.
    check('a drawn diagram is still a focusable block', state[0].isBlock && state[0].blockId === 'b0');
    check('a drawn diagram keeps its source in a disclosure',
      DIAGRAMS.every((_, i) => state[i].hasToggle && state[i].hasSource));

    // Accessible naming.
    check('accTitle becomes the drawing\'s name', state[0].svgTitle === 'Login flow');
    check('a described diagram gets no invented label', !state[0].svgLabel);
    const undescribed = state[DIAGRAMS.findIndex((d) => d.label === 'sequence')];
    check('an undescribed diagram is named and says so',
      /diagram|chart|graph|map|line/.test(undescribed.svgLabel || '') &&
      /no description provided/.test(undescribed.svgLabel || ''),
      undescribed.svgLabel);
    // A node's own tooltip <title> must not be mistaken for the diagram's name. The journey diagram
    // has exactly that shape: shapes carrying <title> children, and no accTitle of its own.
    const journey = state[DIAGRAMS.findIndex((d) => d.label === 'journey')];
    check('a node tooltip is not read as the diagram\'s name',
      /no description provided/.test(journey.svgLabel || ''), journey.svgLabel);

    // The two failure shapes degrade rather than break.
    const bad = state[DIAGRAMS.length];
    check('an unparseable diagram is marked as failed', bad.state === 'error');
    check('an unparseable diagram keeps its source on screen', bad.hasSource && !bad.hasToggle);
    check('an unparseable diagram reports mermaid\'s own reason',
      /parse error/i.test(bad.detail || ''), bad.detail);

    const empty = state[DIAGRAMS.length + 1];
    check('an empty diagram is marked as failed', empty.state === 'error');
    check('an empty diagram says it is empty', /empty/i.test(empty.detail || ''), empty.detail);

    // mermaid appends a scratch element to the body to measure text in, and does not always remove
    // it when a render throws.
    const strays = await page.evaluate(() =>
      document.querySelectorAll('body > [id^="dspectacle-mermaid"]').length);
    check('no measuring scratch elements are left behind', strays === 0, String(strays));

    await page.close();
  }

  // ---------- the keyword list the gate checks against ----------
  //
  // MermaidChecker reports a diagram whose opening keyword mermaid does not register. That list lives
  // in C# and the authority lives in the bundle, so they can drift apart the moment the bundle is
  // bumped: a keyword wrongly listed lets a diagram that cannot draw pass the gate, and one wrongly
  // missing turns a diagram that draws fine into a false finding. Both directions are checked here.
  console.log('\n[diagram keywords]');
  const csharp = fs.readFileSync(path.join(SRC, 'MermaidExtension.cs'), 'utf8');
  const block = csharp.match(/DiagramKeywords\s*=\s*\{([\s\S]*?)\};/);
  const listed = block ? block[1].match(/"([^"]+)"/g).map((s) => s.slice(1, -1)) : [];
  check('the C# keyword list was found', listed.length > 0);

  const page = await browser.newPage();
  await page.setContent('<html><body></body></html>');
  await page.addScriptTag({ path: path.join(ASSETS, 'mermaid.min.js') });

  const verdicts = await page.evaluate(async (keywords) => {
    // detectType answers for nothing at all until mermaid has registered its built-in diagrams,
    // which it does on initialize. The reader never hits this because preview-mermaid.js initializes
    // before it renders; a test that asks detectType cold gets a uniform "no" and would look like
    // total drift.
    window.mermaid.initialize({ startOnLoad: false });
    await window.mermaid.registerExternalDiagrams([], { lazyLoad: false });

    const detect = (kw) => {
      try { return { kw, type: window.mermaid.detectType(kw + '\n') }; }
      catch (e) { return { kw, type: null }; }
    };
    return {
      listed: keywords.map(detect),
      // Keywords mermaid's documentation describes but this bundle does not register (zenuml ships
      // as a separate plugin; radar and usecase answer only to their -beta spellings), plus a plain
      // invention and a miscapitalized real one. None may be in the C# list.
      absent: ['zenuml', 'usecase', 'usecase-beta', 'radar', 'bogusDiagram', 'classdiagram',
        'sequencediagram'].map(detect),
    };
  }, listed);

  const unrecognized = verdicts.listed.filter((v) => v.type === null).map((v) => v.kw);
  check('every keyword the gate accepts is one the bundle recognizes',
    unrecognized.length === 0, unrecognized.join(', '));

  // Compared exactly, because the gate compares exactly: mermaid draws `classDiagram` and refuses
  // `classdiagram`, so a case-insensitive check here would hide the very drift it is looking for.
  const wronglyListed = verdicts.absent
    .filter((v) => v.type === null)
    .map((v) => v.kw)
    .filter((kw) => listed.includes(kw));
  check('no keyword the bundle rejects is on the gate\'s list',
    wronglyListed.length === 0, wronglyListed.join(', '));

  const rejected = verdicts.absent.filter((v) => v.type === null).map((v) => v.kw);
  check('the bundle really does reject the keywords the gate omits',
    ['zenuml', 'radar', 'classdiagram'].every((kw) => rejected.includes(kw)),
    'recognized after all: ' + verdicts.absent.filter((v) => v.type !== null).map((v) => v.kw).join(', '));

  await page.close();
  await browser.close();
  console.log(failures === 0 ? '\nall browser assertions passed' : `\n${failures} assertion(s) failed`);
  process.exit(failures === 0 ? 0 : 1);
})();
