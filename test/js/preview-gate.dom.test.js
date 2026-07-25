// preview-gate.dom.test.js — behaviour test for the reader's gate overlay.
//
// The C# side of the overlay is covered by PreviewGateTests (the payload the preview injects). This
// covers the other half: what preview-gate.js actually builds from that payload, and how it responds
// to keys. Until now the preview's JavaScript had no tests at all, which is a real gap now that the
// gate badge is a load-bearing part of the product rather than decoration.
//
// It runs on a hand-rolled DOM stub instead of a headless browser on purpose: the script touches a
// small, boring slice of the DOM API, and a stub keeps the check dependency-free — `node` is already
// on the CI runner, so this costs the build nothing.
//
// Run:  node test/js/preview-gate.dom.test.js
'use strict';

const fs = require('fs');
const path = require('path');

const SCRIPT = path.join(__dirname, '..', '..', 'src', 'Spectacle', 'Render', 'Assets', 'preview-gate.js');

// ---------- DOM stub ----------

function makeEl(tag) {
  return {
    tagName: (tag || 'div').toUpperCase(),
    children: [],
    attributes: {},
    classList: {
      _set: new Set(),
      add(c) { this._set.add(c); },
      remove(c) { this._set.delete(c); },
      contains(c) { return this._set.has(c); },
    },
    _listeners: {},
    style: {},
    hidden: false,
    textContent: '',
    id: '',
    className: '',
    type: '',
    title: '',
    // Read by the flash helper to force a reflow; any number will do.
    offsetWidth: 10,
    appendChild(c) { this.children.push(c); c.parentNode = this; return c; },
    insertBefore(c, ref) {
      const i = ref ? this.children.indexOf(ref) : 0;
      this.children.splice(i < 0 ? 0 : i, 0, c);
      c.parentNode = this;
      return c;
    },
    setAttribute(k, v) { this.attributes[k] = String(v); },
    getAttribute(k) { return k in this.attributes ? this.attributes[k] : null; },
    addEventListener(t, fn) { (this._listeners[t] = this._listeners[t] || []).push(fn); },
    scrollIntoView() { this._scrolled = true; },
    focus() { this._focused = true; },
    get firstChild() { return this.children[0] || null; },
  };
}

const body = makeEl('body');
const main = makeEl('main');
body.appendChild(main);

// Two rendered blocks carrying data-line, exactly as BlockTagger emits them.
const heading = makeEl('h1'); heading.setAttribute('data-line', '5');
const paragraph = makeEl('p'); paragraph.setAttribute('data-line', '9');
main.appendChild(heading);
main.appendChild(paragraph);

const keyHandlers = [];
globalThis.document = {
  body,
  activeElement: null,
  createElement: makeEl,
  querySelector: (sel) => (sel === 'main' ? main : null),
  querySelectorAll: (sel) => (sel === '[data-line]' ? [heading, paragraph] : []),
  addEventListener(type, fn) { if (type === 'keydown') keyHandlers.push(fn); },
};

// A failing verdict with one finding at each severity, reduced coverage, and a blank metadata
// value — enough for every branch the script has.
globalThis.window = {
  __spectacleGate__: {
    status: 'fail',
    passed: false,
    failOn: 'error',
    counts: { blocking: 2, error: 2, warning: 1, info: 1, suppressed: 1 },
    coverage: { checksDisabled: ['duplication'], suppressed: 1 },
    metadata: [{ key: 'workflow', value: 'spec-writer' }, { key: 'stage', value: '' }],
    findings: [
      { severity: 'error', rule: 'ai-artifacts/assistant-voice', check: 'ai-artifacts', line: 7, message: "framing 'Certainly!'", remedy: 'Delete the framing sentence.' },
      { severity: 'error', rule: 'front-matter/empty-value', check: 'front-matter', line: 2, message: "'stage' is empty", remedy: 'Fill in the value.' },
      { severity: 'warning', rule: 'bare-urls/bare-url', check: 'bare-urls', line: 9, message: 'bare URL', remedy: 'Wrap it in a descriptive link.' },
      { severity: 'info', rule: 'prose/hedge', check: 'prose', line: 11, message: "hedging: 'should probably'", remedy: 'Commit to the decision.' },
    ],
  },
};

// The script is an IIFE, so evaluating it is all it takes to install the overlay.
new Function(fs.readFileSync(SCRIPT, 'utf8'))();

// ---------- assertions ----------

let failures = 0;
function check(name, condition) {
  if (condition) console.log('  ok   ' + name);
  else { console.log('  FAIL ' + name); failures++; }
}

function byId(id) {
  const walk = (el) => {
    if (el.id === id) return el;
    for (const child of el.children) { const found = walk(child); if (found) return found; }
    return null;
  };
  return walk(body);
}

// Text of an element and everything under it.
function textOf(el) {
  return (el.textContent || '') + el.children.map(textOf).join(' ');
}

function press(key, target) {
  let prevented = false;
  const event = {
    key,
    target: target || body,
    preventDefault() { prevented = true; },
    stopPropagation() {},
  };
  keyHandlers.forEach((fn) => fn(event));
  return prevented;
}

console.log('preview-gate.js:');

const card = byId('sp-gate-meta');
check('renders a metadata card', !!card);
check('puts the card above the document', main.children[0] === card);
check('shows each metadata key and value', /workflow/.test(textOf(card)) && /spec-writer/.test(textOf(card)));
// A blank required value is a finding in its own right; showing it as empty beats a blank row the
// reader has to interpret.
check('marks a blank metadata value rather than leaving a gap', /—/.test(textOf(card)));

const badge = byId('sp-gate-badge');
check('renders a badge', !!badge);
check('badge states the verdict', /GATE FAIL/.test(textOf(badge)));
check('badge summarizes the counts', /2E/.test(textOf(badge)) && /1W/.test(textOf(badge)));
check('badge is not styled as passing', !badge.classList.contains('sp-gate-ok'));
check('badge carries the whole verdict for a screen reader', /threshold error/.test(badge.getAttribute('aria-label') || ''));
check('badge starts collapsed', badge.getAttribute('aria-expanded') === 'false');

const panel = byId('sp-gate-panel');
check('builds the panel closed', !!panel && panel.hidden === true);
check('panel is a labelled modal dialog', panel.getAttribute('role') === 'dialog' && !!panel.getAttribute('aria-labelledby'));
// A pass earned by running fewer checks is a different fact from a clean pass.
check('panel declares reduced coverage', /Reduced coverage/.test(textOf(panel)) && /duplication/.test(textOf(panel)));
check('panel lists every finding', byId('sp-gate-list').children.length === 4);
check('panel shows each severity', ['error', 'warning', 'info'].every((s) => new RegExp(s).test(textOf(panel))));
check('panel shows the fix, not just the finding', /Delete the framing sentence\./.test(textOf(panel)));

check('"v" opens the panel', press('v') && panel.hidden === false);
check('badge reports itself expanded', badge.getAttribute('aria-expanded') === 'true');

const options = byId('sp-gate-list').children;
check('opening selects the first finding', options[0].getAttribute('aria-selected') === 'true');
press('ArrowDown');
check('ArrowDown moves the selection', options[1].getAttribute('aria-selected') === 'true');
press('End');
check('End selects the last finding', options[3].getAttribute('aria-selected') === 'true');
press('Home');
check('Home returns to the first', options[0].getAttribute('aria-selected') === 'true');

// The first finding is at line 7; the target is the last block starting at or before it, because a
// finding is often inside a block rather than at its first line.
press('Enter');
check('Enter closes the panel', panel.hidden === true);
check('Enter scrolls to the block containing the line', heading._scrolled === true);
check('flashes the destination so the jump is visible', heading.classList.contains('sp-gate-flash'));
check('moves focus so keyboard navigation continues there', heading._focused === true);

// The shortcut must never swallow a character being typed into a comment or the find bar.
check('"v" is ignored while typing in a field', !press('v', makeEl('input')) && panel.hidden === true);

press('v');
press('Escape');
check('Escape closes the panel', panel.hidden === true);

console.log(failures === 0 ? '\nall assertions passed' : `\n${failures} assertion(s) failed`);
process.exit(failures === 0 ? 0 : 1);
