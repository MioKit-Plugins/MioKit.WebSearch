<script setup>
import { cn } from '@/lib/utils';

defineProps({
  title: { type: String, required: true },
  description: { type: String, default: '' },
  class: { type: null, required: false },
});
</script>

<template>
  <div
    :class="cn(
      'flex min-h-40 flex-1 flex-col items-center justify-center gap-3 px-6 py-10 text-center',
      $props.class,
    )"
  >
    <span
      v-if="$slots.icon"
      class="flex size-10 items-center justify-center rounded-md bg-muted text-muted-foreground"
    >
      <slot name="icon" />
    </span>
    <div class="space-y-1">
      <p class="text-sm font-medium text-foreground">{{ title }}</p>
      <p v-if="description" class="max-w-sm text-xs text-muted-foreground">
        {{ description }}
      </p>
    </div>
    <slot />
  </div>
</template>
