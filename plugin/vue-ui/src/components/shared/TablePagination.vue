<script setup>
import { computed } from 'vue';
import { ChevronLeft, ChevronRight, MoreHorizontal } from 'lucide-vue-next';
import { Button } from '@/components/ui/button';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';

const props = defineProps({
  page: { type: Number, required: true },
  pageSize: { type: Number, required: true },
  total: { type: Number, required: true },
  pageSizeOptions: { type: Array, default: () => [10, 20, 50] },
});

const emit = defineEmits(['update:page', 'update:pageSize']);
const totalPages = computed(() => Math.max(1, Math.ceil(props.total / props.pageSize)));
const pageSizeModel = computed({
  get: () => String(props.pageSize),
  set: (value) => emit('update:pageSize', Number(value)),
});
const canPrevious = computed(() => props.page > 1);
const canNext = computed(() => props.page < totalPages.value);
const pageButtons = computed(() => {
  const pageCount = totalPages.value;
  const current = props.page;
  const buttons = [];
  if (pageCount <= 7) {
    for (let page = 1; page <= pageCount; page += 1) buttons.push({ page, isEllipsis: false });
    return buttons;
  }

  const middle = Math.max(4, Math.min(pageCount - 3, current));
  buttons.push({ page: 1, isEllipsis: false });
  buttons.push(middle > 4
    ? { page: -1, isEllipsis: true, jumpTo: Math.max(1, current - 5) }
    : { page: middle - 2, isEllipsis: false });
  buttons.push({ page: middle - 1, isEllipsis: false });
  buttons.push({ page: middle, isEllipsis: false });
  buttons.push({ page: middle + 1, isEllipsis: false });
  buttons.push(middle < pageCount - 3
    ? { page: -1, isEllipsis: true, jumpTo: Math.min(pageCount, current + 5) }
    : { page: middle + 2, isEllipsis: false });
  buttons.push({ page: pageCount, isEllipsis: false });
  return buttons;
});

function goToPage(page) {
  const next = Math.min(Math.max(1, page), totalPages.value);
  if (next !== props.page) emit('update:page', next);
}

function handlePageButtonClick(button) {
  goToPage(button.isEllipsis ? button.jumpTo : button.page);
}
</script>

<template>
  <div class="flex flex-wrap items-center justify-between gap-3 px-3.5 py-2.5 text-sm">
    <div class="flex flex-wrap items-center gap-3">
      <span class="text-muted-foreground">共 {{ total }} 条</span>
      <Select v-if="pageSizeOptions.length > 1" v-model="pageSizeModel">
        <SelectTrigger class="h-8 w-30">
          <SelectValue :placeholder="`${pageSize} 条/页`" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem v-for="size in pageSizeOptions" :key="size" :value="String(size)">
            {{ size }} 条/页
          </SelectItem>
        </SelectContent>
      </Select>
    </div>

    <div v-if="total > 0" class="flex items-center gap-2">
      <span class="hidden text-muted-foreground sm:inline">第 {{ page }} / {{ totalPages }} 页</span>
      <div class="flex items-center gap-1">
        <Button variant="outline" size="icon-sm" class="size-8" :disabled="!canPrevious" title="上一页" @click="goToPage(page - 1)">
          <ChevronLeft class="size-4" /><span class="sr-only">上一页</span>
        </Button>
        <template v-for="(button, index) in pageButtons" :key="index">
          <Button
            v-if="button.isEllipsis"
            variant="ghost"
            size="icon-sm"
            class="size-8"
            title="跳转更多页"
            @click="handlePageButtonClick(button)"
          >
            <MoreHorizontal class="size-4" />
          </Button>
          <Button
            v-else
            :variant="button.page === page ? 'default' : 'ghost'"
            size="icon-sm"
            class="size-8 tabular-nums"
            :title="`第 ${button.page} 页`"
            @click="handlePageButtonClick(button)"
          >
            {{ button.page }}
          </Button>
        </template>
        <Button variant="outline" size="icon-sm" class="size-8" :disabled="!canNext" title="下一页" @click="goToPage(page + 1)">
          <ChevronRight class="size-4" /><span class="sr-only">下一页</span>
        </Button>
      </div>
    </div>
  </div>
</template>
