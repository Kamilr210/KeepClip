const assert = require('node:assert/strict');
const {test} = require('node:test');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const template = fs.readFileSync(path.join(__dirname, '../scripts/quality-report.template.html'), 'utf8');
const scripts = [...template.matchAll(/<script\b([^>]*)>([\s\S]*?)<\/script>/gi)].filter(match => !/application\/json/i.test(match[1]));
assert.equal(scripts.length, 1, 'The report must expose one executable inline script for this DOM fixture.');
const script = new vm.Script(scripts[0][2], {filename: 'quality-report.template.html'});

class Element {
  children = [];
  listeners = new Map();
  attributes = new Map();
  dataset = {};
  className = '';
  value = '';
  hidden = false;
  disabled = false;
  open = false;
  focused = false;
  selected = false;
  selection = null;
  content = '';
  constructor(tagName) { this.tagName = tagName; }
  get textContent() { return this.content + this.children.map(child => child.textContent).join(''); }
  set textContent(value) { this.content = String(value); this.children = []; }
  set innerHTML(_) { throw Error('Report data must be rendered as literal text, never HTML.'); }
  append(...nodes) { for (const node of nodes) this.children.push(...(node.tagName === '#fragment' ? node.children.splice(0) : [node])); }
  replaceChildren(...nodes) { this.content = ''; this.children = []; this.append(...nodes); }
  setAttribute(name, value) { this.attributes.set(name, String(value)); }
  getAttribute(name) { return this.attributes.get(name) ?? null; }
  addEventListener(name, handler) { this.listeners.set(name, handler); }
  click() { if (!this.disabled) return this.listeners.get('click')?.(); }
  focus() { this.focused = true; }
  select() { this.selected = true; }
  setSelectionRange(start, end) { this.selection = [start, end]; }
}

const descendants = node => node.children.flatMap(child => [child, ...descendants(child)]);
const hasClass = (node, name) => node.className.split(/\s+/).includes(name);
const result = (name, status, extra = {}) => ({name, status, suite: 'Release', module: 'KeepClip.Tests.ScannerTests', durationSeconds: 0.015, ...extra});
const step = (name, status, extra = {}) => ({name, status, seconds: 0.5, ...extra});
const repairPrompt = 'Napraw test Scan_replaced.\nBłąd: plik został zmieniony.\nLog: release-tests/results.trx\nUruchom scripts/quality-check.ps1 -Mode Full.';
const successful = extra => ({
  runId: 'synthetic-ui-run', mode: 'Full', overallStatus: 'passed', exitCode: 0,
  startedUtc: '2026-10-29T12:00:00Z', finishedUtc: '2026-10-29T12:01:00Z',
  tests: [result('Scan_unchanged', 'passed'), result('Scan_unchanged', 'passed', {suite: 'PublicRelease'})],
  steps: [step('test-release', 'passed'), step('desktop-tests', 'not-requested')],
  issues: [], repairPrompt: 'Brak błędów wymagających naprawy.', coverageLink: 'coverage.html',
  artifacts: {summary: 'summary.json', prompt: 'repair-prompt.txt'}, ...extra,
});
const failed = extra => successful({
  overallStatus: 'failed', exitCode: 1, repairPrompt,
  tests: [
    result('Scan_unchanged', 'passed'),
    result('Scan_replaced', 'failed', {message: 'Brak ścieżki do nagrania', stackTrace: 'at Scanner.Scan in C:\\tests\\ScannerTests.cs:line 42', sourceFile: 'C:\\tests\\ScannerTests.cs', sourceLine: 42}),
    result('Native_video', 'skipped', {suite: 'FFmpeg', module: 'MediaTests', message: 'Narzędzie niedostępne'}),
  ],
  steps: [step('build-release-analyzers', 'passed'), step('test-release', 'failed', {detail: 'One test failed', log: 'test-release.log', logText: 'Scan_replaced failed'}), step('desktop-tests', 'not-requested')],
  ...extra,
});

function fixture(model, options = {}) {
  const elements = {};
  const roots = [];
  for (const match of template.matchAll(/<([a-z][\w-]*)\b([^>]*)>/gi)) {
    const attributes = match[2];
    const id = attributes.match(/\bid="([^"]+)"/)?.[1];
    const className = attributes.match(/\bclass="([^"]+)"/)?.[1] || '';
    const filter = attributes.match(/\bdata-filter="([^"]+)"/)?.[1];
    if (!id && filter === undefined && !className.split(/\s+/).includes('copy-prompt')) continue;
    const node = new Element(match[1]);
    node.className = className;
    node.hidden = /\bhidden(?:\s|$|=)/.test(attributes);
    node.disabled = /\bdisabled(?:\s|$|=)/.test(attributes);
    if (filter !== undefined) node.dataset.filter = filter;
    if (id) elements[id] = node;
    roots.push(node);
  }
  elements['report-data'].textContent = options.rawData ?? JSON.stringify(model);
  const allNodes = () => roots.flatMap(node => [node, ...descendants(node)]);
  const nodes = className => allNodes().filter(node => hasClass(node, className));
  const filters = Object.fromEntries(roots.filter(node => node.dataset.filter !== undefined).map(node => [node.dataset.filter, node]));
  const timers = new Map();
  let nextTimer = 0;
  const clipboardWrites = [];
  const fallbackCalls = [];
  const navigator = options.clipboard === 'missing' ? {} : {clipboard: {writeText: async text => {
    clipboardWrites.push(text);
    if (options.clipboard === 'rejected') throw Error('Clipboard permission denied');
  }}};
  const document = {
    getElementById: id => { assert.ok(elements[id], `Unknown report element: ${id}`); return elements[id]; },
    createElement: tag => new Element(tag),
    createDocumentFragment: () => new Element('#fragment'),
    querySelectorAll: selector => {
      if (selector === '[data-filter]') return Object.values(filters);
      if (selector === '.copy-prompt') return nodes('copy-prompt');
      if (selector === '#test-list .suite, #test-list .group') return descendants(elements['test-list']).filter(node => hasClass(node, 'suite') || hasClass(node, 'group'));
      throw Error(`Unsupported selector in report DOM fixture: ${selector}`);
    },
    execCommand: command => { fallbackCalls.push(command); if (options.fallback === 'throws') throw Error('Copy command blocked'); return options.fallback === true; },
  };
  // Allow cold locale initialization on shared Windows runners while bounding runaway scripts.
  script.runInNewContext({document, navigator, setTimeout: callback => { const id = ++nextTimer; timers.set(id, callback); return id; }, clearTimeout: id => timers.delete(id)}, {timeout: 10000});
  const flush = () => { for (const [id, callback] of [...timers]) { timers.delete(id); callback(); } };
  const search = query => { elements['test-search'].value = query; elements['test-search'].listeners.get('input')(); };
  return {elements, filters, nodes, search, flush, clipboardWrites, fallbackCalls, names: () => nodes('test-name').map(node => node.textContent), copy: () => nodes('copy-prompt')[0].click()};
}

test('a successful run presents actual test counts and keeps Release variants separate', async () => {
  const f = fixture(successful({totals: {passed: 999, total: 999}}));
  assert.equal(f.elements['passed-count'].textContent, '2');
  assert.equal(f.elements['failed-count'].textContent, '0');
  assert.equal(f.elements['skipped-count'].textContent, '0');
  assert.equal(f.elements['steps-count'].textContent, '1 / 1');
  assert.equal(f.elements['report-title'].className, 'good');
  assert.deepEqual(f.nodes('suite-name').map(node => node.textContent), ['Release', 'PublicRelease']);
  assert.deepEqual(f.names(), ['Scan_unchanged', 'Scan_unchanged']);
  assert.equal(f.elements['prompt-panel'].hidden, true);
  assert.ok(f.nodes('copy-prompt').every(node => node.disabled));
  await f.copy();
  assert.deepEqual(f.clipboardWrites, []);
  assert.equal(f.elements['summary-link'].href, 'summary.json');
});

test('failed tests expose their message, source location and repair prompt', () => {
  const f = fixture(failed({tests: [result('Scan_replaced', 'failed', {message: 'Broken video', sourceFile: 'C:\\tests\\ScannerTests.cs', sourceLine: 42, stackTrace: 'Scanner.Scan()'})]}));
  assert.equal(f.elements['report-title'].className, 'bad');
  assert.equal(f.elements['failed-count'].textContent, '1');
  assert.equal(f.elements['steps-count'].textContent, '1 / 2');
  assert.deepEqual(f.names(), ['Scan_replaced']);
  assert.equal(f.nodes('message')[0].textContent, 'Broken video');
  assert.equal(f.nodes('location')[0].textContent, 'C:\\tests\\ScannerTests.cs:42');
  assert.equal(f.elements['repair-prompt'].textContent, repairPrompt);
  assert.equal(f.elements['prompt-panel'].hidden, false);
  assert.ok(f.nodes('test').every(node => node.open));
  assert.equal(f.nodes('step')[0].open, true);
});

test('incomplete or unknown results cannot be presented as a successful run', () => {
  const f = fixture(successful({overallStatus: 'incomplete', issues: ['Brak public-tests/results.trx'], tests: [result('Scan_pending', 'unrecognized')]}));
  assert.equal(f.elements['report-title'].className, 'warn');
  assert.equal(f.elements['passed-count'].textContent, '0');
  assert.equal(f.elements['failed-count'].textContent, '1');
  assert.equal(f.elements.issues.hidden, false);
  assert.equal(f.elements['issue-list'].textContent, 'Brak public-tests/results.trx');
  assert.deepEqual(f.names(), ['Scan_pending']);
  assert.equal(f.elements['prompt-panel'].hidden, false);
});

test('skipped results override a contradictory successful summary', () => {
  const f = fixture(successful({tests: [result('Native_video', 'skipped')]}));
  assert.equal(f.elements['passed-count'].textContent, '0');
  assert.equal(f.elements['skipped-count'].textContent, '1');
  assert.notEqual(f.elements['report-title'].className, 'good');
  assert.equal(f.elements['prompt-panel'].hidden, false);
});

test('status filters show only matching test results and update the visible count', () => {
  const f = fixture(failed());
  assert.deepEqual(f.names(), ['Scan_replaced']);
  assert.equal(f.filters.failed.getAttribute('aria-pressed'), 'true');
  f.filters.all.click();
  assert.deepEqual(f.names(), ['Scan_replaced', 'Scan_unchanged', 'Native_video']);
  assert.equal(f.elements['visible-count'].textContent, 'Widoczne: 3 / 3');
  f.filters.passed.click();
  assert.deepEqual(f.names(), ['Scan_unchanged']);
  assert.equal(f.filters.all.getAttribute('aria-pressed'), 'false');
  f.filters.skipped.click();
  assert.deepEqual(f.names(), ['Native_video']);
  assert.equal(f.elements['visible-count'].textContent, 'Widoczne: 1 / 3');
  f.filters.failed.click();
  assert.deepEqual(f.names(), ['Scan_replaced']);
});

test('search matches diagnostic text and source files while respecting the selected status', () => {
  const f = fixture(failed());
  f.filters.all.click();
  f.search('native');
  f.search(' ŚCIEŻKI ');
  f.flush();
  assert.deepEqual(f.names(), ['Scan_replaced']);
  assert.ok(f.nodes('group').every(node => node.open));
  f.search('scannertests.cs');
  f.flush();
  assert.deepEqual(f.names(), ['Scan_replaced']);
  f.filters.passed.click();
  assert.deepEqual(f.names(), []);
  assert.equal(f.elements['visible-count'].textContent, 'Widoczne: 0 / 3');
  assert.ok(f.nodes('empty').length > 0);
  f.search('scan_unchanged');
  f.flush();
  assert.deepEqual(f.names(), ['Scan_unchanged']);
});

test('expand and collapse control every visible suite and group without hiding test data', () => {
  const f = fixture(failed());
  f.filters.all.click();
  const names = f.names();
  f.elements['collapse-tests'].click();
  assert.ok([...f.nodes('suite'), ...f.nodes('group')].every(node => !node.open));
  f.elements['expand-tests'].click();
  assert.ok([...f.nodes('suite'), ...f.nodes('group')].every(node => node.open));
  assert.deepEqual(f.names(), names);
});

test('clipboard success copies the exact prompt and avoids the manual fallback', async () => {
  const f = fixture(failed());
  await f.copy();
  assert.deepEqual(f.clipboardWrites, [repairPrompt]);
  assert.deepEqual(f.fallbackCalls, []);
  assert.equal(f.elements['manual-copy'].hidden, true);
  assert.match(f.elements['copy-status'].textContent, /Prompt skopiowany/);
});

test('clipboard denial uses a successful legacy copy fallback', async () => {
  const f = fixture(failed(), {clipboard: 'rejected', fallback: true});
  await f.copy();
  assert.deepEqual(f.clipboardWrites, [repairPrompt]);
  assert.deepEqual(f.fallbackCalls, ['copy']);
  assert.equal(f.elements['manual-copy'].value, repairPrompt);
  assert.equal(f.elements['manual-copy'].hidden, true);
  assert.match(f.elements['copy-status'].textContent, /Prompt skopiowany/);
});

for (const options of [{clipboard: 'missing', fallback: false}, {clipboard: 'rejected', fallback: 'throws'}]) {
  test(`blocked copying leaves the prompt selected and never claims success (${options.clipboard}, ${options.fallback})`, async () => {
    const f = fixture(failed(), options);
    await f.copy();
    assert.equal(f.elements['prompt-panel'].open, true);
    assert.equal(f.elements['manual-copy'].hidden, false);
    assert.equal(f.elements['manual-copy'].value, repairPrompt);
    assert.equal(f.elements['manual-copy'].focused, true);
    assert.equal(f.elements['manual-copy'].selected, true);
    assert.deepEqual(f.elements['manual-copy'].selection, [0, repairPrompt.length]);
    assert.match(f.elements['copy-status'].textContent, /Ctrl\+C/);
    assert.doesNotMatch(f.elements['copy-status'].textContent, /Prompt skopiowany/);
  });
}

test('untrusted names, diagnostics and prompts remain literal text', async () => {
  const payload = '</script><img src=x onerror=alert(1)>';
  const f = fixture(failed({tests: [result(payload, 'failed', {module: payload, message: payload})], repairPrompt: payload, issues: [payload], steps: [step('test-release', 'failed', {detail: payload, logText: payload})]}));
  assert.deepEqual(f.names(), [payload]);
  assert.equal(f.nodes('group-name')[0].textContent, payload);
  assert.equal(f.nodes('message')[0].textContent, payload);
  assert.equal(f.elements['issue-list'].textContent, payload);
  assert.equal(f.elements['repair-prompt'].textContent, payload);
  assert.ok(f.nodes('step-body')[0].textContent.includes(payload));
  await f.copy();
  assert.deepEqual(f.clipboardWrites, [payload]);
});

test('links to external URLs or parent directories are not made clickable', () => {
  const f = fixture(failed({coverageLink: 'javascript:alert(1)', artifacts: {summary: '../private.json', prompt: 'https://example.test/prompt'}, steps: [step('test-release', 'failed', {log: '%2e%2e/private.log'})]}));
  for (const id of ['coverage-link', 'summary-link', 'prompt-link']) assert.equal(f.elements[id].hidden, true);
  assert.equal(descendants(f.elements.steps).filter(node => node.tagName === 'a').length, 0);
});

test('missing individual results and malformed embedded data do not report success', () => {
  const empty = fixture(successful({tests: [], steps: []}));
  assert.notEqual(empty.elements['report-title'].className, 'good');
  assert.equal(empty.elements['passed-count'].textContent, '0');
  assert.deepEqual(empty.names(), []);
  const corrupt = fixture(null, {rawData: '{invalid'});
  assert.equal(corrupt.elements['report-title'].className, 'bad');
  assert.match(corrupt.elements['report-title'].textContent, /Nie można odczytać/);
  assert.equal(corrupt.nodes('copy-prompt')[0].disabled, true);
});
