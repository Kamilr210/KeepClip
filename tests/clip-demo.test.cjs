const assert = require('node:assert/strict');
const {test} = require('node:test');
const {createClipDemoPanel} = require('../frontend/clip-demo.js');

class Element {
  value = '';
  textContent = '';
  title = '';
  hidden = false;
  disabled = false;
  listeners = new Map();
  classes = new Set();
  classList = {toggle: (name, enabled) => enabled ? this.classes.add(name) : this.classes.delete(name)};
  set innerHTML(_) { throw Error('Untrusted demo text must not be rendered as HTML'); }
  addEventListener(name, handler) { this.listeners.set(name, handler); }
  click() { return this.listeners.get('click')?.(); }
}

const empty = id => ({clip_id: id, path: null, filename: null, available: false, size_bytes: null});
const linked = (id, path = 'C:\\CS2\\match.dem', available = true) => ({clip_id: id, path, filename: path.split('\\').at(-1), available, size_bytes: available ? 32 : null});
const response = (data, ok = true) => ({ok, statusText: ok ? 'OK' : 'Error', json: async () => data});
const deferred = () => { let resolve; const promise = new Promise(done => { resolve = done; }); return {promise, resolve}; };

function fixture(respond, pickFile = null) {
  const elements = Object.fromEntries(['path', 'status', 'save', 'remove', 'browse', 'show'].map(name => [name, new Element()]));
  const calls = [];
  const notifications = [];
  const panel = createClipDemoPanel({elements, request: (url, options) => { calls.push({url, options}); return respond(url, options); }, t: key => key + ' {name} {error}', notify: (...args) => notifications.push(args), pickFile});
  return {elements, calls, notifications, panel};
}

test('opening an unassigned clip clears old values and enables manual linking in a browser', async () => {
  const f = fixture(async () => response(empty(7)));
  await f.panel.open(7);
  assert.equal(f.elements.path.value, '');
  assert.equal(f.elements.save.disabled, false);
  assert.equal(f.elements.remove.hidden, true);
  assert.equal(f.elements.show.hidden, true);
  assert.equal(f.elements.browse.hidden, true);
  assert.equal(f.calls[0].url, '/api/clips/7/demo');
});

test('saving uses PUT with the selected local path, then presents the persisted response', async () => {
  const f = fixture(async (_, options) => response(options ? linked(7) : empty(7)));
  await f.panel.open(7);
  f.elements.path.value = 'C:\\CS2\\match.dem';
  await f.elements.save.click();
  const request = f.calls[1];
  assert.equal(request.url, '/api/clips/7/demo');
  assert.equal(request.options.method, 'PUT');
  assert.deepEqual(JSON.parse(request.options.body), {path: 'C:\\CS2\\match.dem'});
  assert.equal(request.options.headers['Content-Type'], 'application/json');
  assert.equal(f.elements.remove.hidden, false);
  assert.equal(f.elements.show.disabled, false);
  assert.match(f.notifications[0][0], /demo.saved/);
});

test('empty paths are rejected before making an HTTP request', async () => {
  const f = fixture(async () => response(empty(7)));
  await f.panel.open(7);
  f.elements.path.value = '   ';
  await f.elements.save.click();
  assert.equal(f.calls.length, 1);
  assert.equal(f.notifications[0][1], 'error');
});

test('a failed change retains the existing association and leaves the attempted path editable', async () => {
  const f = fixture(async (_, options) => options ? response({detail: 'Invalid header'}, false) : response(linked(7)));
  await f.panel.open(7);
  f.elements.path.value = 'C:\\CS2\\bad.dem';
  await f.elements.save.click();
  assert.equal(f.elements.path.value, 'C:\\CS2\\bad.dem');
  assert.match(f.elements.status.textContent, /match.dem/);
  assert.equal(f.elements.save.disabled, false);
  assert.equal(f.elements.remove.hidden, false);
  assert.match(f.notifications[0][0], /Invalid header/);
});

test('detaching calls DELETE on the association and never on the clip', async () => {
  const f = fixture(async (_, options) => response(options ? empty(7) : linked(7)));
  await f.panel.open(7);
  await f.elements.remove.click();
  assert.equal(f.calls[1].url, '/api/clips/7/demo');
  assert.deepEqual(f.calls[1].options, {method: 'DELETE'});
  assert.equal(f.elements.path.value, '');
  assert.equal(f.elements.show.hidden, true);
  assert.match(f.notifications[0][0], /demo.removed/);
});

test('an unavailable file remains visible and can be relinked or detached', async () => {
  const f = fixture(async () => response(linked(7, 'C:\\CS2\\moved.dem', false)));
  await f.panel.open(7);
  assert.equal(f.elements.path.value, 'C:\\CS2\\moved.dem');
  assert.equal(f.elements.show.disabled, true);
  assert.equal(f.elements.remove.hidden, false);
  assert.equal(f.elements.save.disabled, false);
  assert.equal(f.elements.status.classes.has('is-missing'), true);
  assert.match(f.elements.status.textContent, /demo.missing/);
});

test('show in folder uses the saved path rather than an unsaved edit', async () => {
  const f = fixture(async (url) => response(url === '/api/show-in-explorer' ? {ok: true} : linked(7)));
  await f.panel.open(7);
  f.elements.path.value = 'C:\\other.exe';
  await f.elements.show.click();
  assert.equal(f.calls[1].url, '/api/show-in-explorer');
  assert.equal(f.calls[1].options.method, 'POST');
  assert.deepEqual(JSON.parse(f.calls[1].options.body), {path: 'C:\\CS2\\match.dem'});
});

test('file names returned by the server are rendered as literal text', async () => {
  const malicious = '<img src=x onerror=alert(1)>.dem';
  const f = fixture(async () => response({...linked(7), filename: malicious}));
  await f.panel.open(7);
  assert.ok(f.elements.status.textContent.includes(malicious));
});

test('a slow load for the previous clip cannot overwrite the newly opened clip', async () => {
  const first = deferred();
  const f = fixture(url => url.includes('/1/') ? first.promise : Promise.resolve(response(linked(2, 'C:\\CS2\\second.dem'))));
  const old = f.panel.open(1);
  await f.panel.open(2);
  first.resolve(response(linked(1, 'C:\\CS2\\first.dem')));
  await old;
  assert.equal(f.elements.path.value, 'C:\\CS2\\second.dem');
});

test('closing the player invalidates in-flight responses and disables actions', async () => {
  const pending = deferred();
  const f = fixture(() => pending.promise);
  const open = f.panel.open(1);
  f.panel.close();
  pending.resolve(response(linked(1)));
  await open;
  assert.equal(f.elements.path.value, '');
  assert.equal(f.elements.save.disabled, true);
  await f.elements.save.click();
  assert.equal(f.calls.length, 1);
});

test('an in-flight save stays scoped to its original clip and prevents duplicate submission', async () => {
  const saving = deferred();
  const f = fixture((url, options) => options ? saving.promise : Promise.resolve(response(empty(url.includes('/2/') ? 2 : 1))));
  await f.panel.open(1);
  f.elements.path.value = 'C:\\CS2\\first.dem';
  const save = f.elements.save.click();
  await f.elements.save.click();
  assert.equal(f.calls.length, 2);
  await f.panel.open(2);
  saving.resolve(response(linked(1)));
  await save;
  assert.equal(f.calls[1].url, '/api/clips/1/demo');
  assert.equal(f.elements.path.value, '');
  assert.equal(f.notifications.length, 0);
});

test('a native picker result is ignored if the user switches clips while it is open', async () => {
  const picking = deferred();
  const f = fixture(async () => response(empty(1)), () => picking.promise);
  await f.panel.open(1);
  assert.equal(f.elements.browse.hidden, false);
  const choose = f.elements.browse.click();
  await f.panel.open(2);
  picking.resolve('C:\\CS2\\first.dem');
  await choose;
  assert.equal(f.elements.path.value, '');
  assert.equal(f.calls.length, 2);
});

test('native picker selection fills the path without silently saving it', async () => {
  const f = fixture(async () => response(empty(1)), async () => 'C:\\CS2\\picked.dem');
  await f.panel.open(1);
  await f.elements.browse.click();
  assert.equal(f.elements.path.value, 'C:\\CS2\\picked.dem');
  assert.equal(f.calls.length, 1);
  assert.equal(f.elements.save.disabled, false);
});

test('network errors leave the panel usable and do not pretend a demo was loaded', async () => {
  const f = fixture(async () => { throw Error('offline'); });
  await f.panel.open(1);
  assert.match(f.elements.status.textContent, /demo.loadFailed/);
  assert.equal(f.elements.save.disabled, false);
  assert.equal(f.elements.remove.hidden, true);
});

test('canceling the native picker keeps the current path unchanged', async () => {
  const f = fixture(async () => response(linked(1)), async () => null);
  await f.panel.open(1);
  await f.elements.browse.click();
  assert.equal(f.elements.path.value, 'C:\\CS2\\match.dem');
  assert.equal(f.elements.save.disabled, false);
  assert.equal(f.calls.length, 1);
});

test('native picker errors restore the buttons and report a readable error', async () => {
  const f = fixture(async () => response(linked(1)), async () => { throw Error('picker unavailable'); });
  await f.panel.open(1);
  await f.elements.browse.click();
  assert.equal(f.elements.path.value, 'C:\\CS2\\match.dem');
  assert.equal(f.elements.browse.disabled, false);
  assert.match(f.notifications[0][0], /picker unavailable/);
});

test('show in folder reports a file disappearing without deleting the saved link', async () => {
  const f = fixture(async url => url === '/api/show-in-explorer' ? response({detail:'file missing'}, false) : response(linked(1)));
  await f.panel.open(1);
  await f.elements.show.click();
  assert.match(f.notifications[0][0], /file missing/);
  assert.equal(f.elements.path.value, 'C:\\CS2\\match.dem');
  assert.equal(f.elements.remove.hidden, false);
});

test('Enter submits the path while other keys do not trigger a save', async () => {
  const f = fixture(async (_, options) => response(options ? linked(1) : empty(1)));
  await f.panel.open(1);
  f.elements.path.value = 'C:\\CS2\\match.dem';
  f.elements.path.listeners.get('keydown')({key:'a'});
  assert.equal(f.calls.length, 1);
  let prevented = false;
  f.elements.path.listeners.get('keydown')({key:'Enter', preventDefault:() => { prevented = true; }});
  await new Promise(setImmediate);
  assert.equal(prevented, true);
  assert.equal(f.calls[1].options.method, 'PUT');
  assert.equal(f.elements.remove.hidden, false);
});
