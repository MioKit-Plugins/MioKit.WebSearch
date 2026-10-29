import test from 'node:test';
import assert from 'node:assert/strict';
import { deferred, editor, flush } from './editor-harness.js';
import { emptyDraft } from '../src/components/website/configuration.js';

test('late editor success cannot replace a newly selected website', async t => {
  const old = deferred();
  const e = await editor(t, { GetWebsiteEditor: key => key === 'a' ? old.promise : Promise.resolve({ id: key, name: key, configuration: { runtime: {} } }) });
  await e.navigate('b');
  old.resolve({ id: 'a' });
  await flush();
  assert.equal(e.state.draft.value.id, 'b');
  assert.equal(e.state.loading.value, false);
});

test('late editor failure cannot display an error on another website', async t => {
  const old = deferred();
  const e = await editor(t, { GetWebsiteEditor: key => key === 'a' ? old.promise : Promise.resolve({ id: key, name: key, configuration: { runtime: {} } }) });
  await e.navigate('b');
  old.reject(new Error('old website failed'));
  await flush();
  assert.equal(e.state.error.value, '');
});

test('a pending save cannot overwrite or show success for another editor', async t => {
  const save = deferred();
  const e = await editor(t, { SaveWebsite: () => save.promise });
  const pending = e.state.save();
  await e.navigate('b');
  save.resolve({ success: true, id: 'a' });
  await pending;
  assert.equal(e.state.draft.value.id, 'b');
  assert.equal(e.notices.length, 0);
});

test('a pending save failure cannot display an error on another website', async t => {
  const save = deferred();
  const e = await editor(t, { SaveWebsite: () => save.promise });
  const pending = e.state.save();
  await e.navigate('b');
  save.reject(new Error('old save failed'));
  await pending;
  assert.equal(e.state.error.value, '');
});

test('directory selection cannot grant a path to a newly selected website', async t => {
  const picker = deferred();
  const e = await editor(t, { PickDirectory: () => picker.promise });
  const pending = e.state.addFileDirectory();
  await e.navigate('b');
  picker.resolve('D:\\private');
  await pending;
  assert.equal(e.state.r.value.fileDirectories.length, 0);
});

test('late picker failure cannot display an error on another editor', async t => {
  const picker = deferred();
  const e = await editor(t, { PickDirectory: () => picker.promise });
  const pending = e.state.pick(e.state.c.value, 'userDataFolder');
  await e.navigate('b');
  picker.reject(new Error('old picker failed'));
  await pending;
  assert.equal(e.state.error.value, '');
});

test('late icon selection cannot change the new draft', async t => {
  const picker = deferred();
  const e = await editor(t, { PickWebsiteIcon: () => picker.promise });
  const pending = e.state.pickIcon();
  await e.navigate('b');
  picker.resolve({ SourcePath: 'D:\\a.png', PreviewDataUrl: 'custom' });
  await pending;
  assert.equal(e.state.draft.value.iconAction, 'Keep');
  assert.equal(e.state.customPreview.value, '');
});

test('unsaved changes prevent open and reopen', async t => {
  let calls = 0;
  const e = await editor(t, { ReopenWebsite: async () => { calls++; } });
  e.state.draft.value.name = 'changed';
  await e.state.run('ReopenWebsite');
  assert.equal(calls, 0);
  assert.equal(e.state.error.value, '请先保存设置。');
});

test('late reopen errors cannot affect another editor', async t => {
  const reopening = deferred();
  const e = await editor(t, { ReopenWebsite: () => reopening.promise });
  const pending = e.state.run('ReopenWebsite');
  await e.navigate('b');
  reopening.resolve('old window failed');
  await pending;
  assert.equal(e.state.error.value, '');
});

test('saving a new draft rehydrates assets, resets dirty state and returns to the list', async t => {
  let submitted;
  const e = await editor(t, {
    SaveWebsite: async value => { submitted = value; return { Success: true, Id: 'saved', RequiresReopen: true }; },
    GetWebsiteEditor: async id => ({ ...emptyDraft(), id, name: 'Saved', requiresReopen: true }),
  }, 'new');
  e.state.draft.value.name = 'new site';
  e.state.draft.value.urlTemplate = 'https://example.test';
  await e.state.save();
  assert.equal(submitted.name, 'new site');
  assert.equal(e.state.draft.value.id, 'saved');
  assert.equal(e.state.dirty.value, false);
  assert.deepEqual(e.navigations, ['/settings/websites']);
  assert.equal(e.notices[0][0], '网站设置已保存');
  assert.match(e.notices[0][1].description, /重新打开网站窗口后生效/);
});

test('retrying a committed new website after readback failures updates the same website', async t => {
  const websites = new Map(), submittedIds = [];
  let reads = 0;
  const e = await editor(t, {
    SaveWebsite: async value => {
      submittedIds.push(value.id);
      const id = value.id || `site-${websites.size + 1}`;
      websites.set(id, { ...value, id });
      return { success: true, id };
    },
    GetWebsiteEditor: async id => {
      if (++reads <= 2) throw new Error('readback failed');
      return websites.get(id);
    },
  }, 'new');
  e.state.draft.value.name = '保留我填写的名称';
  e.state.draft.value.urlTemplate = 'https://example.test/';
  for (let attempt = 0; attempt < 2; attempt++) {
    await e.state.save();
    assert.equal(e.state.draft.value.id, 'site-1');
    assert.equal(e.state.draft.value.name, '保留我填写的名称');
    assert.equal(e.state.draft.value.urlTemplate, 'https://example.test/');
    assert.match(e.state.error.value, /已保存.*重新读取失败.*readback failed/);
    assert.equal(e.state.saving.value, false);
    assert.equal(e.state.canSave.value, true);
    assert.equal(websites.size, 1);
  }
  e.state.draft.value.name = '重试时修改的名称';
  await e.state.save();
  assert.deepEqual(submittedIds, ['', 'site-1', 'site-1']);
  assert.equal(websites.size, 1);
  assert.equal(websites.get('site-1').name, '重试时修改的名称');
  assert.equal(e.state.error.value, '');
  assert.equal(e.state.dirty.value, false);
  assert.deepEqual(e.navigations, ['/settings/websites']);
});

test('a new website keeps its committed ID while readback is pending', async t => {
  const readback = deferred();
  const e = await editor(t, {
    SaveWebsite: async () => ({ success: true, id: 'committed' }),
    GetWebsiteEditor: () => readback.promise,
  }, 'new');
  const saving = e.state.save();
  await flush();
  assert.equal(e.state.draft.value.id, 'committed');
  assert.equal(e.state.saving.value, true);
  readback.resolve({ ...emptyDraft(), id: 'committed', name: 'Saved' });
  await saving;
});

test('a rejected first save does not adopt an uncommitted ID', async t => {
  const e = await editor(t, {
    SaveWebsite: async () => ({ success: false, id: 'rejected', error: '网站保存失败' }),
  }, 'new');
  await e.state.save();
  assert.equal(e.state.draft.value.id, '');
  assert.equal(e.state.error.value, '网站保存失败');
  assert.deepEqual(e.navigations, []);
});

test('an obsolete readback failure cannot overwrite another editors ID or error', async t => {
  const readback = deferred();
  const e = await editor(t, {
    SaveWebsite: async () => ({ success: true, id: 'committed' }),
    GetWebsiteEditor: id => id === 'committed' ? readback.promise : Promise.resolve({ ...emptyDraft(), id }),
  }, 'new');
  const saving = e.state.save();
  await flush();
  await e.navigate('b');
  readback.reject(new Error('old readback failed'));
  await saving;
  assert.equal(e.state.draft.value.id, 'b');
  assert.equal(e.state.error.value, '');
  assert.deepEqual(e.navigations, []);
});

test('a failed save preserves the draft and selects the relevant settings tab', async t => {
  const e = await editor(t, { SaveWebsite: async () => ({ success: false, error: '权限：可信来源无效。' }) });
  e.state.draft.value.name = 'keep my edit';
  await e.state.save();
  assert.equal(e.state.draft.value.name, 'keep my edit');
  assert.equal(e.state.dirty.value, true);
  assert.equal(e.state.tab.value, 'permissions');
  assert.equal(e.state.saving.value, false);
});

test('script reorder and removal preserve selection and execution order', async t => {
  const e = await editor(t);
  e.state.addScript(); e.state.addScript(); e.state.addScript();
  e.state.moveScript(-1);
  assert.equal(e.state.scriptIndex.value, 1);
  assert.equal(e.state.r.value.scripts.map(x => x.name).join(','), '脚本 1,脚本 3,脚本 2');
  e.state.removeScript();
  assert.equal(e.state.script.value.name, '脚本 2');
  e.state.moveScript(1);
  assert.equal(e.state.scriptIndex.value, 1);
});

test('permission toggles are idempotent and disable removes the grant', async t => {
  const e = await editor(t);
  const permissions = e.state.r.value.permissions;
  e.state.toggleList(permissions, 'storage', true);
  e.state.toggleList(permissions, 'storage', true);
  assert.equal(permissions.length, 1);
  e.state.toggleList(permissions, 'storage', false);
  assert.equal(permissions.length, 0);
});

test('an old save finishing cannot clear a newer saves busy state', async t => {
  const first = deferred(), second = deferred();
  let calls = 0;
  const e = await editor(t, { SaveWebsite: () => ++calls === 1 ? first.promise : second.promise });
  const oldSave = e.state.save();
  await e.navigate('b');
  assert.equal(e.state.saving.value, false);
  const newSave = e.state.save();
  first.resolve({ success: true, id: 'a' });
  await oldSave;
  assert.equal(e.state.saving.value, true);
  second.resolve({ success: true, id: 'b' });
  await newSave;
  assert.equal(e.state.saving.value, false);
});

test('a save is blocked while the selected website is still loading', async t => {
  const loading = deferred(); let calls = 0;
  const e = await editor(t, {
    GetWebsiteEditor: () => loading.promise,
    SaveWebsite: async () => { calls++; return { success: false, error: 'unexpected' }; },
  });
  await e.state.save();
  assert.equal(calls, 0);
  loading.resolve({ id: 'a', name: 'a', configuration: { runtime: {} } });
  await flush();
});

test('file permission errors select permissions instead of basic information', async t => {
  const e = await editor(t, { SaveWebsite: async () => ({ success: false, error: '权限：文件授权目录必须存在。' }) });
  await e.state.save();
  assert.equal(e.state.tab.value, 'permissions');
});

test('an obsolete favicon refresh failure cannot overwrite the current URLs state', async t => {
  const refresh = deferred();
  const e = await editor(t, { RefreshFaviconByUrl: () => refresh.promise });
  e.state.draft.value.urlTemplate = 'https://first.test/';
  await flush();
  const pending = e.state.refreshFavicon();
  e.state.draft.value.urlTemplate = 'https://second.test/';
  await flush();
  refresh.reject(new Error('old URL failed'));
  await pending;
  assert.equal(e.state.iconError.value, '');
});

test('unmount discards pending directory selection and leaves its draft untouched', async t => {
  const picker = deferred();
  const e = await editor(t, { PickDirectory: () => picker.promise });
  const pending = e.state.addFileDirectory();
  e.unmount();
  picker.resolve('D:\\private');
  await pending;
  assert.equal(e.state.r.value.fileDirectories.length, 0);
});

test('pending permission changes survive a later presentation-only save until reopen succeeds', async t => {
  let saves = 0, pending = false;
  const e = await editor(t, {
    SaveWebsite: async () => { pending = true; return { success: true, id: 'a', requiresReopen: ++saves === 1 }; },
    GetWebsiteEditor: async id => ({ ...emptyDraft(), id, name: id, requiresReopen: pending }),
    ReopenWebsite: async () => { pending = false; return ''; },
  });
  await e.state.save();
  assert.equal(e.state.requiresReopen.value, true);
  assert.equal(e.notices.at(-1)[0], '网站设置已保存');
  assert.match(e.notices.at(-1)[1].description, /重新打开网站窗口后生效/);
  assert.deepEqual(e.navigations.at(-1), '/settings/websites');
  await e.state.save();
  assert.equal(e.state.requiresReopen.value, true);
  await e.state.run('ReopenWebsite');
  assert.equal(e.state.requiresReopen.value, false);
});

test('reopening failure preserves the pending permission notice and a retry can clear it', async t => {
  let calls = 0;
  const e = await editor(t, {
    GetWebsiteEditor: async id => ({ id, name: id, requiresReopen: true, configuration: { runtime: {} } }),
    ReopenWebsite: async () => ++calls === 1 ? '窗口创建失败' : '',
  });
  assert.equal(e.state.requiresReopen.value, true);
  await e.state.run('ReopenWebsite');
  assert.equal(e.state.requiresReopen.value, true);
  assert.equal(e.state.error.value, '窗口创建失败');
  await e.state.run('ReopenWebsite');
  assert.equal(e.state.requiresReopen.value, false);
  assert.equal(e.state.error.value, '');
});

test('a save requiring reopen preserves its notice even if saved asset readback fails', async t => {
  let reads = 0;
  const e = await editor(t, {
    SaveWebsite: async () => ({ success: true, id: 'a', requiresReopen: true }),
    GetWebsiteEditor: async id => {
      if (++reads > 1) throw new Error('readback failed');
      return { id, name: id, configuration: { runtime: {} } };
    },
  });
  await e.state.save();
  assert.equal(e.state.requiresReopen.value, true);
  assert.match(e.state.error.value, /已保存.*重新读取失败.*readback failed/);
});

test('saving after the old window closes clears its obsolete reopen notice from authoritative readback', async t => {
  let pending = true;
  const e = await editor(t, {
    GetWebsiteEditor: async id => ({ ...emptyDraft(), id, name: id, requiresReopen: pending }),
    SaveWebsite: async () => ({ success: true, id: 'a', requiresReopen: false }),
  });
  assert.equal(e.state.requiresReopen.value, true);
  pending = false;
  await e.state.save();
  assert.equal(e.state.requiresReopen.value, false);
  assert.equal(e.notices.at(-1)[0], '网站设置已保存');
  assert.equal(e.notices.at(-1)[1].description, '');
  assert.deepEqual(e.navigations.at(-1), '/settings/websites');
});
