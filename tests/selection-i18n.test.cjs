const assert = require('node:assert/strict');
const {test} = require('node:test');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
global.window = {};
const i18n = require('../frontend/i18n.js');
const source = fs.readFileSync(path.join(__dirname, '../frontend/app.js'), 'utf8');
const clipsWord = source.match(/function clipsWord\(n\) \{[\s\S]*?\n\}/)[0];

test('selection translations share the placeholders filled by the UI', () => {
  for (const language of Object.values(i18n)) {
    const slots = value => [...value.matchAll(/\{(\w+)\}/g)].map(match => match[1]).sort();
    assert.deepEqual(slots(language.selection.addN), ['n', 'word']);
    assert.deepEqual(slots(language.toast.folderAdded), ['folder', 'n', 'word']);
  }
});

test('English selection labels use the real singular and plural clip words', () => {
  const context = {LANG: 'en', t: key => key.split('.').reduce((value, part) => value[part], i18n.en)};
  vm.runInNewContext(clipsWord, context);
  for (const [count, word] of [[1, 'clip'], [2, 'clips'], [5, 'clips']]) {
    assert.equal(context.clipsWord(count), word);
    assert.equal(i18n.en.selection.addN.replace('{n}', count).replace('{word}', context.clipsWord(count)), `Add ${count} ${word}`);
    assert.equal(i18n.en.toast.folderAdded.replace('{n}', count).replace('{word}', context.clipsWord(count)).replace('{folder}', 'Test'), `Added ${count} ${word} to folder "Test".`);
  }
  assert.match(source, /t\("toast\.folderAdded"\)\.replace\("\{n\}", ids\.length\)\.replace\("\{word\}", clipsWord\(ids\.length\)\)/);
});
