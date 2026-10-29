<script setup>
defineProps({
  narrow: { type: Boolean, default: false },
  wide: { type: Boolean, default: false },
});
</script>

<template>
  <div class="flex size-full min-h-0 min-w-0 flex-col">
    <div class="flex min-h-0 w-full min-w-0 flex-1 flex-col px-4 py-3 md:px-5 md:py-4">
      <slot />
    </div>
  </div>
</template>
