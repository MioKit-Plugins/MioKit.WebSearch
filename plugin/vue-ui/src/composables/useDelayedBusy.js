import { ref, watch } from 'vue';

// Fast operations finish without flashing an indicator. Watch cleanup also cancels
// pending indicators when the operation changes or its component is unmounted.
export function useDelayedBusy(source, delay = 300) {
  const visible = ref(false);
  watch(source, (busy, _, onCleanup) => {
    visible.value = false;
    if (!busy) return;
    const timer = setTimeout(() => { visible.value = true; }, delay);
    onCleanup(() => clearTimeout(timer));
  }, { immediate: true, flush: 'sync' });
  return visible;
}
