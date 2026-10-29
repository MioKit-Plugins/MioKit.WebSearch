<script setup>
import { cn } from '@/lib/utils';

defineProps({
  title: { type: String, required: true },
  description: { type: String, default: '' },
  stretch: { type: Boolean, default: false },
});
</script>

<template>
  <section :class="cn('flex flex-col gap-2', stretch && 'min-h-0 flex-1')">
    <div class="px-0.5">
      <h2 class="text-sm font-semibold tracking-tight text-foreground">
        {{ title }}
      </h2>
      <p v-if="description" class="mt-0.5 text-xs leading-relaxed text-muted-foreground">
        {{ description }}
      </p>
    </div>
    <div
      :class="cn(
        'settings-card divide-y divide-border/50',
        stretch && 'flex min-h-0 flex-1 flex-col',
      )"
    >
      <slot />
    </div>
  </section>
</template>
