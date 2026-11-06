const assert = require('node:assert/strict');
const {test} = require('node:test');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync(require.resolve('../frontend/app.js'), 'utf8');

function fixture(state) {
  let apply;
  const requests = [];
  const els = {
    replayNavDot: {}, replayBox: {}, replayOverlay: {hidden: false}, replaySaveNow: {}, replayDot: {},
    replayStatusText: {}, replayError: {}, replayHotkeyWarn: {}, replayEnabled: {}, replayBackground: {},
    replayGamesOnly: {}, replayMic: {}, replayDuration: {}, replayFps: {}, replayQuality: {}, replayHotkey: {},
    replayAudioOutput: {}, replayAudioInput: {}, replayApply: {addEventListener(_, handler) { apply = handler; }},
  };
  const c = vm.createContext({
    els, REPLAY_ENC_LABELS: {}, t: key => key, toast() {}, clearInterval() {}, setInterval: () => 1,
    populateAudioDevices: async () => {}, backendErrorMessage: () => 'error',
    fetch: async (url, options) => { requests.push({url, options}); return {ok: true, json: async () => state}; },
  });
  vm.runInContext(source.slice(source.indexOf('let _replayPoll ='), source.indexOf('function closeReplayModal(')), c);
  vm.runInContext(source.slice(source.indexOf('els.replayApply.addEventListener('), source.indexOf('els.replaySaveNow.addEventListener(')), c);
  return {c, els, requests, apply: () => apply()};
}

test('waiting for a game is shown as a pause and cannot save an empty buffer', () => {
  const {c, els} = fixture();
  c.renderReplayStatus({enabled: true, waiting_for_game: true, running: false, can_save: false});
  assert.equal(els.replayStatusText.textContent, 'replay.stWaitingForGame');
  assert.equal(els.replaySaveNow.disabled, true);
  assert.equal(els.replayNavDot.hidden, true);
});

test('a paused buffer remains saveable and still warns about an unavailable hotkey', () => {
  const {c, els} = fixture();
  c.renderReplayStatus({enabled: true, waiting_for_game: true, running: false, can_save: true, hotkey_active: false});
  assert.equal(els.replaySaveNow.disabled, false);
  assert.equal(els.replayHotkeyWarn.hidden, false);
  c.renderReplayStatus({enabled: true, waiting_for_game: true, running: false, can_save: false, saving: true});
  assert.equal(els.replaySaveNow.disabled, true);
  assert.equal(els.replayStatusText.textContent, 'replay.stSaving');
});

test('opening settings restores games-only and applying sends its actual checkbox value', async () => {
  const {c, els, requests, apply} = fixture({enabled: true, games_only: true, waiting_for_game: true, duration_s: 120, fps: 60, quality: 'high', hotkey: 'Alt+F10'});
  await c.openReplayModal();
  assert.equal(els.replayGamesOnly.checked, true);
  els.replayGamesOnly.checked = false;
  await apply();
  const request = requests.find(r => r.url === '/api/replay/config');
  assert.equal(request.options.method, 'POST');
  assert.equal(JSON.parse(request.options.body).games_only, false);
  assert.equal(els.replayApply.disabled, false);
});

test('a capture failure remains visible instead of being presented as normal waiting', () => {
  const {c, els} = fixture();
  c.renderReplayStatus({enabled: true, waiting_for_game: true, error: 'Encoder failure'});
  assert.equal(els.replayError.textContent, 'Encoder failure');
  assert.equal(els.replayError.hidden, false);
  assert.equal(els.replayDot.className, 'replay-dot err');
});