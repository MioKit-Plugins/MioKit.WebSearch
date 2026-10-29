<script setup>
import { cn } from '@/lib/utils';

defineProps({
  title: { type: String, required: true },
  description: { type: String, default: '' },
  layout: {
    type: String,
    default: 'row',
    validator: (v) => ['row', 'stack'].includes(v),
  },
  highlight: { type: Boolean, default: false },
});
</script>

<template>
  <div
    :class="cn(
      'px-3 py-3',
      layout === 'row'
        ? 'flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between'
        : 'space-y-2',
      highlight && 'bg-muted/40',
    )"
  >
    <div class="min-w-0 flex-1 space-y-0.5">
      <p class="text-sm font-medium text-foreground">{{ title }}</p>
      <p v-if="description" class="text-xs text-muted-foreground">
        {{ description }}
      </p>
      <div v-if="$slots.bottom" class="pt-1.5">
        <slot name="bottom" />
      </div>
    </div>
    <div
      v-if="$slots.control"
      :class="cn('shrink-0', layout === 'row' ? 'w-full sm:w-auto' : 'w-full')"
    >
      <slot name="control" />
    </div>
  </div>
</template>
