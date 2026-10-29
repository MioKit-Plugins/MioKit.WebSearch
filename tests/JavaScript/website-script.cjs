const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');

async function main() {
  const { scenario, script, entry } = JSON.parse(fs.readFileSync(0, 'utf8'));
  function target() {
    const listeners = new Map();
    return {
      addEventListener(name, fn, options) {
        if (!listeners.has(name)) listeners.set(name, new Map());
        if (!listeners.get(name).has(fn)) listeners.get(name).set(fn, options?.once);
      },
      removeEventListener(name, fn) { listeners.get(name)?.delete(fn); },
      emit(name, event = {}) {
        for (const [fn, once] of [...(listeners.get(name) || [])]) {
          if (once) listeners.get(name).delete(fn);
          fn(event);
        }
      },
    };
  }
  const document = { ...target(), readyState: 'loading' };
  const messages = [], calls = [];
  let deny = false;
  let hostReady = false;
  const webview = {
    ...target(),
    postMessage(json) {
      const request = JSON.parse(json);
      messages.push(request);
      if (request.type === 'mio-bootstrap') {
        if (hostReady) queueMicrotask(()=>webview.emit('message',{data:JSON.stringify({type:'mio-host-ready'})}));
        return;
      }
      if (request.type !== 'GetService' && request.type !== 'CallMethod') return;
      let result;
      if (request.type === 'GetService') {
        assert.equal(request.serviceName, 'WebsiteCapabilities');
        result = { lifetime: 'Singleton', properties: [], methods: [{ name: 'Invoke' }] };
      } else {
        assert.equal(request.methodName, 'Invoke');
        const [service, operation, json] = request.parameters;
        calls.push({ service, operation, args: JSON.parse(json) });
        result = deny ? { success: false, code: 'permission.denied', message: 'denied by host' }
          : { success: true, value: 'host-result' };
      }
      queueMicrotask(() => webview.emit('message', { data: JSON.stringify({ type: 'methodResult', messageId: request.messageId, success: true, result }) }));
    },
  };
  const sandbox = {
    ...target(), document, chrome: { webview }, trace: [],
    location: new URL(entry), console: { log() {}, warn() {}, error() {} },
    setTimeout, clearTimeout,
  };
  sandbox.window = sandbox.self = sandbox.top = sandbox;
  if (scenario === 'iframe') sandbox.top = {};
  if (scenario === 'untrusted') sandbox.location = new URL('https://entry.test.evil.test/');
  if (scenario === 'extra-origin') sandbox.location = new URL('https://extra.test:8443/page');
  if (scenario === 'local-sibling') sandbox.location = new URL(entry.replace('websearch-script-root/', 'websearch-script-root-sibling/'));
  if (scenario === 'about-blank') sandbox.location = new URL('about:blank');
  if (scenario.startsWith('inherited-blank')) {
    sandbox.location = new URL('about:blank'); sandbox.opener = {};
    sandbox.origin = scenario === 'inherited-blank-untrusted' ? 'https://evil.test' : new URL(entry).origin;
  }
  if (scenario === 'already-complete') document.readyState = 'complete';
  if (scenario === 'already-interactive') document.readyState = 'interactive';
  const context = vm.createContext(sandbox);
  vm.runInContext(script, context, { timeout: 1000 });
  const flush = async () => { for (let i = 0; i < 5; i++) await new Promise(resolve => setImmediate(resolve)); };
  await flush();
  let earlyApi;
  if (scenario.startsWith('inherited-blank')) {
    assert.equal(messages.length, 0, 'Provisional document must not contact the host');
    assert.deepEqual(sandbox.trace, [], 'Provisional document must not run lifecycle scripts');
    if (scenario === 'inherited-blank-untrusted') {assert.equal(sandbox.Mio, undefined); return;}
    assert.ok(sandbox.Mio, 'Early popup script must see Mio before the DOM is parsed');
    earlyApi = sandbox.Mio;
    const pending = sandbox.Mio.storage.get('early-page-call');
    await flush(); assert.equal(messages.length, 0);
    sandbox.location = new URL(scenario === 'inherited-blank-redirect' ? 'https://evil.test/' : entry);
    document.emit('readystatechange'); await flush();
    assert.equal(messages.filter(x=>x.type==='GetService'||x.type==='CallMethod').length, 0, 'Browser URL can change before the host document transport is ready');
    hostReady = true; document.emit('readystatechange'); await flush();
    if (scenario === 'inherited-blank-redirect') {
      document.emit('DOMContentLoaded'); sandbox.emit('load'); await flush();
      assert.equal(sandbox.Mio, undefined, 'Untrusted popup destination must discard the provisional API');
      assert.equal(messages.length, 0, 'Untrusted redirect must not complete provisional handshake'); return;
    }
    assert.equal(await pending, 'host-result');
  }
  if (['iframe', 'untrusted', 'local-sibling'].includes(scenario)) {
    assert.equal(sandbox.Mio, undefined);
    assert.equal(messages.length, 0);
    assert.deepEqual(sandbox.trace, []);
    return;
  }
  if (scenario === 'about-blank') {
    assert.equal(sandbox.Mio, undefined);
    sandbox.location = new URL(entry);
    document.emit('readystatechange');
    await flush();
  }
  document.readyState = 'interactive';
  document.emit('DOMContentLoaded');
  document.readyState = 'complete';
  sandbox.emit('load');
  await flush();
  assert.deepEqual(sandbox.trace, ['DocumentCreated:start', 'DocumentCreated:end', 'DomReady:start', 'DomReady:end', 'Loaded:start', 'Loaded:end']);
  assert.equal(messages.filter(x => x.type === 'GetService').length, 1);
  if (scenario === 'about-blank') {
    document.emit('readystatechange');
    await flush();
    assert.equal(messages.filter(x => x.type === 'GetService').length, 1);
  }
  if (scenario === 'template-tokens') assert.equal(sandbox.tokens, 'ORIGINS LOCALROOT SCRIPTS BRIDGE </script> 中文');
  if (scenario === 'api-contract' || scenario === 'inherited-blank-api-contract') {
    const mio = earlyApi || sandbox.Mio;
    await mio.ready;
    assert.ok(Object.isFrozen(mio));
    const cases = [
      ['storage', 'get', ['键'], { key: '键' }], ['storage', 'set', ['a', { nested: [1, null] }], { key: 'a', value: { nested: [1, null] } }],
      ['storage', 'remove', ['a'], { key: 'a' }], ['storage', 'keys', [], {}], ['storage', 'clear', [], {}],
      ['files', 'readText', ['D:/a'], { path: 'D:/a' }], ['files', 'readBase64', ['D:/a'], { path: 'D:/a' }],
      ['files', 'writeText', ['D:/a', '中文'], { path: 'D:/a', content: '中文' }], ['files', 'writeBase64', ['D:/a', 'AA=='], { path: 'D:/a', content: 'AA==' }],
      ['files', 'list', ['D:/'], { path: 'D:/' }], ['files', 'createDirectory', ['D:/new'], { path: 'D:/new' }],
      ['files', 'move', ['D:/a', 'D:/b'], { path: 'D:/a', target: 'D:/b' }], ['files', 'delete', ['D:/b'], { path: 'D:/b' }],
      ['dialogs', 'openFile', [], {}], ['dialogs', 'saveFile', ['a.json'], { name: 'a.json' }],
      ['dialogs', 'openDirectory', [], { write: false }], ['dialogs', 'openDirectory', [true], { write: true }],
      ['clipboard', 'readText', [], {}], ['clipboard', 'writeText', ['中文'], { text: '中文' }],
      ['notifications', 'show', ['saved'], { message: 'saved', title: '' }], ['external', 'open', ['https://example.test/'], { url: 'https://example.test/' }],
    ];
    for (const [service, operation, args, expected] of cases) {
      assert.ok(Object.isFrozen(mio[service]));
      assert.equal(await mio[service][operation](...args), 'host-result');
      assert.deepEqual(calls.at(-1), { service, operation, args: expected });
    }
  }
  if (scenario === 'api-error') {
    deny = true;
    await assert.rejects(sandbox.Mio.storage.get('secret'), error => error.code === 'permission.denied' && error.message === 'denied by host');
    deny = false;
  }
  await sandbox.__mioClose('test-nonce');
  assert.deepEqual(sandbox.trace.slice(-2), ['Closing:start', 'Closing:end']);
  assert.deepEqual(messages.at(-1), { type: 'mio-lifecycle-close', nonce: 'test-nonce' });
  const errors = messages.filter(x => x.type === 'mio-script-error');
  assert.equal(errors.length, ['script-failure', 'close-failure'].includes(scenario) ? 1 : 0);
  if (errors.length) assert.match(errors[0].message, /failing 中文: Error: expected failure/);
}

main().catch(error => { console.error(error); process.exitCode = 1; });
