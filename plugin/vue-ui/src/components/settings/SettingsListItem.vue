<script setup>
import { cn } from '@/lib/utils';

defineProps({
  class: { type: null, required: false },
  metaClass: { type: null, required: false },
  actionsClass: { type: null, required: false },
});
</script>

<template>
  <li
    :class="cn(
      'group px-3.5 py-3 transition-colors hover:bg-accent/35',
      $props.class,
    )"
  >
    <div class="flex flex-col gap-3 sm:flex-row sm:items-center sm:gap-4">
      <div class="flex min-w-0 flex-1 items-start gap-3">
        <slot v-if="$slots.icon" name="icon" />
        <div class="min-w-0 flex-1">
          <slot />
        </div>
      </div>

      <div
        v-if="$slots.meta"
        :class="cn('min-w-0 text-xs text-muted-foreground', metaClass)"
      >
        <slot name="meta" />
      </div>

      <div
        v-if="$slots.actions"
        :class="cn(
          'flex shrink-0 items-center justify-end gap-2 sm:justify-start',
          actionsClass,
        )"
      >
        <slot name="actions" />
      </div>
    </div>
  </li>
</template>
