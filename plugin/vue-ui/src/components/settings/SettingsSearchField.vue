<script setup>
import { Search } from 'lucide-vue-next';
import { Input } from '@/components/ui/input';
import { cn } from '@/lib/utils';

defineOptions({ inheritAttrs: false });

defineProps({
  modelValue: { type: String, default: '' },
  placeholder: { type: String, default: '搜索…' },
  wrapperClass: { type: null, required: false },
  inputClass: { type: null, required: false },
});

const emit = defineEmits(['update:modelValue']);
</script>

<template>
  <div :class="cn('relative min-w-0 w-full', wrapperClass)">
    <Search
      class="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
      aria-hidden="true"
    />
    <Input
      v-bind="$attrs"
      :model-value="modelValue"
      :placeholder="placeholder"
      :class="cn(
        'h-9 border-border/70 bg-background/80 pl-9 focus-visible:ring-primary/30',
        inputClass,
      )"
      @update:model-value="emit('update:modelValue', $event)"
    />
  </div>
</template>
