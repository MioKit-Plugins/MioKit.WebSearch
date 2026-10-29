import test from 'node:test';
import assert from 'node:assert/strict';
import { effectScope, ref } from 'vue';
import { useDelayedBusy } from '../src/composables/useDelayedBusy.js';

test('fast operations do not flash a busy indicator after they have completed', t => {
  t.mock.timers.enable({ apis: ['setTimeout'] });
  const scope = effectScope();
  const busy = ref(false);
  const visible = scope.run(() => useDelayedBusy(busy));
  busy.value = true;
  t.mock.timers.tick(100);
  assert.equal(visible.value, false);
  busy.value = false;
  t.mock.timers.tick(1000);
  assert.equal(visible.value, false);
  scope.stop();
});

test('slow operations show feedback and disposed components cancel pending feedback', t => {
  t.mock.timers.enable({ apis: ['setTimeout'] });
  const scope = effectScope();
  const busy = ref(true);
  const visible = scope.run(() => useDelayedBusy(busy));
  t.mock.timers.tick(300);
  assert.equal(visible.value, true);
  busy.value = false;
  assert.equal(visible.value, false);
  busy.value = true;
  scope.stop();
  t.mock.timers.tick(1000);
  assert.equal(visible.value, false);
});
