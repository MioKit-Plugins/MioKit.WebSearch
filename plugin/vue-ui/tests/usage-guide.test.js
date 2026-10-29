import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { guideSections, guideToc } from '../src/lib/usageGuideContent.js';

const root = dirname(fileURLToPath(import.meta.url));

test('usage guide content has stable section ids matching the toc', () => {
  assert.ok(guideSections.length >= 5);
  assert.deepEqual(
    guideToc.map((x) => x.id),
    guideSections.map((x) => x.id),
  );
  for (const id of ['quickstart', 'websites', 'bookmarks', 'embedded', 'scripts', 'examples']) {
    assert.ok(guideSections.some((s) => s.id === id), `missing section ${id}`);
  }
  assert.match(guideSections.find((s) => s.id === 'bookmarks').subsections[0].bullets[0], /bookmark-web/);
  assert.match(guideSections.find((s) => s.id === 'scripts').code, /Mio\.ready/);
});

test('router registers the usage guide path', () => {
  const source = readFileSync(join(root, '../src/router/index.js'), 'utf8');
  assert.match(source, /path:\s*'settings\/guide'/);
  assert.match(source, /name:\s*'Guide'/);
  assert.match(source, /UsageGuideView/);
});
