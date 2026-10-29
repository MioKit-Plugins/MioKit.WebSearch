import test from 'node:test';
import assert from 'node:assert/strict';
import { camelize, emptyDraft, emptyConfiguration, permissionOptions } from '../src/components/website/configuration.js';
import { resolveWebsite, resolveBrowser, resolveProfile, websiteTypeLabel, pickField } from '../src/lib/webSearchItem.js';

test('drafts own their permission, mapping and script arrays', () => {
  const a = emptyDraft(), b = emptyDraft();
  a.configuration.runtime.permissions.push('storage');
  a.configuration.runtime.scripts.push({ code: 'example' });
  assert.deepEqual(b.configuration, emptyConfiguration());
  assert.equal(b.iconAction, 'Keep');
  assert.equal(b.configuration.openMode, 'External');
  assert.equal(b.configuration.runtime.allowAutomaticPopups, false);
});

test('backend PascalCase DTOs become nested editor fields without changing values', () => {
  assert.deepEqual(camelize({ Configuration: { Runtime: { Scripts: [{ Code: 'const ORIGINS = "中文";', Enabled: false }], TrustedOrigins: ['https://EXAMPLE.test'] } }, HasCustomIcon: false, Value: null }),
    { configuration: { runtime: { scripts: [{ code: 'const ORIGINS = "中文";', enabled: false }], trustedOrigins: ['https://EXAMPLE.test'] } }, hasCustomIcon: false, value: null });
  assert.equal(camelize(null), null);
});

test('both bridge casings normalize to the same website including icon fields', () => {
  const value = { Id: 'a', Name: '中文', SourceKind: 'LocalFile', HasCustomIcon: true, IconDataUrl: 'custom', FaviconDataUrl: 'auto', IsSearchTemplate: true, OpenMode: 'Embedded' };
  assert.deepEqual(resolveWebsite(value), resolveWebsite(camelize(value)));
  assert.equal(websiteTypeLabel(resolveWebsite(value)), '本地网页');
  assert.equal(websiteTypeLabel(resolveWebsite({ IsSearchTemplate: true })), '搜索入口');
  assert.equal(websiteTypeLabel(resolveWebsite({})), '固定网址');
});

test('nullish bridge fields use compatible defaults while explicit false and empty survive', () => {
  assert.equal(pickField({ enabled: false, Enabled: true }, 'enabled', 'Enabled', true), false);
  assert.equal(pickField({ name: '', Name: 'legacy' }, 'name', 'Name'), '');
  assert.equal(resolveWebsite(null).QuerySourcePolicy, 'InputThenClipboard');
  assert.equal(resolveWebsite(null).HasCustomIcon, false);
  assert.deepEqual(resolveBrowser({ id: 'edge', isAvailable: false }), { Id: 'edge', Name: '', IsAvailable: false, Path: '' });
  assert.deepEqual(resolveProfile({ Browser: 'Edge', Name: 'Main', Path: 'D:\\profile' }), { Browser: 'Edge', Name: 'Main', Path: 'D:\\profile' });
  assert.equal(permissionOptions.some(([id]) => ['dialogs', 'external'].includes(id)), false);
});
