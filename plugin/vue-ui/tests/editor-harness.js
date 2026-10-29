import { readFile } from 'node:fs/promises';
import { SourceTextModule, SyntheticModule, createContext } from 'node:vm';
import { compileScript, parse } from 'vue/compiler-sfc';
import * as vue from 'vue';
import * as configuration from '../src/components/website/configuration.js';
import * as delayedBusy from '../src/composables/useDelayedBusy.js';

export function deferred() {
  let resolve, reject;
  const promise = new Promise((yes, no) => { resolve = yes; reject = no; });
  return { promise, resolve, reject };
}

export async function flush() {
  await vue.nextTick();
  await new Promise(resolve => setImmediate(resolve));
  await vue.nextTick();
}

// Compile the real SFC setup. Vue reactivity is real; route, bridge, pickers and
// presentation components are substitutes. This deliberately does not test DOM layout.
export async function editor(t, methods = {}, id = 'a') {
  const source = await readFile(new URL('../src/views/WebsiteEditorView.vue', import.meta.url), 'utf8');
  const { descriptor } = parse(source);
  const code = compileScript(descriptor, { id: 'editor-regression' }).content;
  const route = vue.reactive({ params: { id }, fullPath: `/settings/websites/${id}/edit` });
  const vm = vue.reactive({
    _loading: false, Properties: { DefaultWebsiteIconDataUrl: 'default-icon' },
    Methods: {
      GetWebsiteEditor: async key => ({ ...configuration.emptyDraft(), id: key, name: key }),
      PreviewFavicon: async () => '', ...methods,
    },
  });
  const notices = [], navigations = [], unmount = [];
  const context = createContext({ console, setTimeout, clearTimeout });
  const imports = [...code.matchAll(/import\s+([\s\S]*?)\s+from\s+['"]([^'"]+)['"];?/g)];
  const requested = new Map(imports.map(([, names, path]) => [path, names.trim().startsWith('{')
    ? names.replace(/[{}]/g, '').split(',').map(s => s.trim()).filter(Boolean) : ['default']]));
  const module = new SourceTextModule(code, { context });
  await module.link(path => {
    let exports = {};
    if (path === 'vue') exports = { ...vue, inject: () => vm, onBeforeUnmount: fn => unmount.push(fn) };
    else if (path === 'vue-router') exports = {
      useRoute: () => route,
      useRouter: () => ({ push: value => navigations.push(value), replace: async value => navigations.push(value) }),
    };
    else if (path.endsWith('/website/configuration')) exports = configuration;
    else if (path.endsWith('/useDelayedBusy')) exports = delayedBusy;
    else if (path === 'vue-sonner') exports = { toast: {
      success: (...args) => notices.push(args), error: (...args) => notices.push(args), dismiss() {},
    } };
    else exports = Object.fromEntries(requested.get(path).map(name => [name, () => null]));
    return new SyntheticModule(Object.keys(exports), function () {
      for (const [key, value] of Object.entries(exports)) this.setExport(key, value);
    }, { context });
  });
  await module.evaluate();
  const scope = vue.effectScope();
  const state = scope.run(() => module.namespace.default.setup({}, { expose() {} }));
  t.after(() => { unmount.forEach(fn => fn()); scope.stop(); });
  await flush();
  return {
    state, vm, route, notices, navigations,
    async navigate(key) {
      route.params.id = key;
      route.fullPath = `/settings/websites/${key}/edit`;
      await flush();
    },
    unmount() { unmount.forEach(fn => fn()); scope.stop(); },
  };
}
