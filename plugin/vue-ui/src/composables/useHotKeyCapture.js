import { onUnmounted } from 'vue';
import { isBridgeAvailable, useBridgeService } from '@/composables/useDotnetBridge';

export const HOT_KEY_CAPTURE_SERVICE = 'HotKeyCaptureService';

export function useHotKeyCapture() {
  const captureVm = useBridgeService(HOT_KEY_CAPTURE_SERVICE);

  function beginCapture(baselineValueText, excludeOwnerId) {
    captureVm?.Methods?.BeginCapture?.(baselineValueText ?? '0', excludeOwnerId ?? '');
  }

  function cancelCapture() {
    captureVm?.Methods?.CancelCapture?.();
  }

  function clearDraft() {
    captureVm?.Methods?.ClearDraft?.();
  }

  onUnmounted(() => {
    cancelCapture();
  });

  return {
    captureVm,
    bridgeAvailable: isBridgeAvailable(),
    beginCapture,
    cancelCapture,
    clearDraft,
  };
}
