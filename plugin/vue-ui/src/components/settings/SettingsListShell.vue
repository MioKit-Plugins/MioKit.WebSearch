<script setup>
import { cn } from '@/lib/utils';

defineProps({
  /** soft / card 保留兼容，样式已统一为朴素边框容器 */
  variant: {
    type: String,
    default: 'soft',
    validator: (v) => ['soft', 'card'].includes(v),
  },
  class: { type: null, required: false },
});
</script>

<template>
  <div :class="cn('ws-shell', $props.class)">
    <div
      v-if="$slots.toolbar"
      class="ws-shell-toolbar"
    >
      <slot name="toolbar" />
    </div>

    <div class="flex min-h-0 flex-1 flex-col overflow-hidden">
      <slot />
    </div>

    <div
      v-if="$slots.footer"
      class="ws-shell-footer"
    >
      <slot name="footer" />
    </div>
  </div>
</template>
