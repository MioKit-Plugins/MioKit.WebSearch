import test from 'node:test';
import assert from 'node:assert/strict';
import { getMioScriptTypeDefinitions } from '../src/lib/mio-script-types.js';

test('Mio script types declare the injected API surface', () => {
  const dts = getMioScriptTypeDefinitions();
  assert.match(dts, /declare const Mio:\s*MioApi/);
  for (const name of ['storage', 'files', 'dialogs', 'clipboard', 'notifications', 'external', 'ready']) {
    assert.match(dts, new RegExp(`\\b${name}\\b`), `missing ${name}`);
  }
  for (const method of [
    'get', 'set', 'remove', 'keys', 'clear',
    'readText', 'readBase64', 'writeText', 'writeBase64', 'list', 'createDirectory', 'move', 'delete',
    'openFile', 'saveFile', 'openDirectory',
    'writeText', 'show', 'open',
  ]) {
    assert.match(dts, new RegExp(`\\b${method}\\b`), `missing method ${method}`);
  }
});
