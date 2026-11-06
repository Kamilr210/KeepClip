const assert = require('node:assert/strict');
const {test} = require('node:test');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync(require.resolve('../frontend/app.js'), 'utf8');

function fixture(fetch) {
  const context = vm.createContext({
    fetch, currentClipId: 7, _playbackRequestToken: 0,
    els: {overlay: {hidden: false}, playerPreparing: {hidden: true}, playerPreparingTitle: {}, playerPreparingDetail: {}},
    t: key => key, toast() {}, wait: async () => {},
    hidden: 0, shown: 0,
  });
  vm.runInContext('function hidePlaybackPreparation() { hidden++; } function showPlaybackPreparation() { shown++; }', context);
  vm.runInContext(source.slice(source.indexOf('function cancelPlaybackPreparation('), source.indexOf('async function openPlayer(')), context);
  vm.runInContext(source.slice(source.indexOf('async function runScan('), source.indexOf('function libraryStatsSignature(')), context);
  return context;
}

test('automatic scans opt into change detection while manual scans always force reconciliation', async () => {
  const requests = [];
  const c = fixture(async (url, options) => {
    requests.push({url, options});
    return {ok: true, json: async () => ({added: 0, removed: 0, updated: 0})};
  });
  await c.runScan({automatic: true});
  await c.runScan();
  assert.deepEqual(requests.map(r => r.url), ['/api/scan?automatic=true', '/api/scan']);
  assert.ok(requests.every(r => r.options.method === 'POST'));
});

test('a late playback response after closing cancels the encoder without updating the player', async () => {
  let complete;
  const requests = [];
  const c = fixture((url, options) => {
    requests.push({url, options});
    if (options?.method === 'DELETE') return Promise.resolve({ok: true});
    return new Promise(resolve => { complete = resolve; });
  });
  const result = c.resolvePlaybackSource(7);
  c.currentClipId = null;
  c.els.overlay.hidden = true;
  c._playbackRequestToken++;
  complete({ok: true, json: async () => ({preparing: true})});
  assert.equal(await result, null);
  assert.equal(requests.at(-1).url, '/api/playback/7');
  assert.equal(requests.at(-1).options.method, 'DELETE');
  assert.equal(c.shown, 0);
  assert.equal(c.hidden, 0);
});

test('reopening the same clip ignores an old response without canceling its new request', async () => {
  let complete;
  const requests = [];
  const c = fixture((url, options) => {
    requests.push({url, options});
    return new Promise(resolve => { complete = resolve; });
  });
  const result = c.resolvePlaybackSource(7);
  c._playbackRequestToken++;
  complete({ok: true, json: async () => ({ready: true, url: '/playback/7'})});
  assert.equal(await result, null);
  assert.equal(requests.length, 1);
  assert.equal(c.hidden, 0);
});

test('a network failure from an old clip cannot overwrite the current player', async () => {
  let fail;
  const c = fixture(() => new Promise((_, reject) => { fail = reject; }));
  const result = c.resolvePlaybackSource(7);
  c.currentClipId = 8;
  fail(Error('old request failed'));
  assert.equal(await result, null);
  assert.equal(c.els.playerPreparing.hidden, true);
});

test('a ready current clip returns its URL and ends preparation', async () => {
  const c = fixture(async () => ({ok: true, json: async () => ({ready: true, url: '/playback/7'})}));
  assert.equal(await c.resolvePlaybackSource(7), '/playback/7');
  assert.equal(c.hidden, 1);
});
