<script setup>
import { computed, inject, ref, watch } from 'vue';
import { useRouter } from 'vue-router';
import {
  ExternalLink,
  Globe2,
  Loader2,
  Pencil,
  Plus,
  Search,
  Trash2,
  TriangleAlert,
} from 'lucide-vue-next';
import { toast } from 'vue-sonner';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Skeleton } from '@/components/ui/skeleton';
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from '@/components/ui/tooltip';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import SettingsPage from '@/components/settings/SettingsPage.vue';
import SettingsListShell from '@/components/settings/SettingsListShell.vue';
import SettingsSearchField from '@/components/settings/SettingsSearchField.vue';
import SettingsEmptyState from '@/components/settings/SettingsEmptyState.vue';
import TablePagination from '@/components/shared/TablePagination.vue';
import { resolveWebsite, websiteTypeLabel } from '@/lib/webSearchItem';

const router = useRouter();
const vm = inject('webSearchVm');
const websiteFilter = ref('');
const deletingIds = ref(new Set());
const failedFaviconIds = ref(new Set());
const websitePage = ref(1);
const websitePageSize = ref(10);
const deleteTarget = ref(null);
const deleteOpen = computed({
  get: () => !!deleteTarget.value,
  set: (open) => {
    if (!open) deleteTarget.value = null;
  },
});
const isDeletingTarget = computed(() =>
  !!deleteTarget.value && deletingIds.value.has(deleteTarget.value.Id),
);

const websites = computed(() => (vm.Collections.Websites ?? [])
  .map(resolveWebsite)
  .filter((site) => site.Id));
const normalizedFilter = computed(() => websiteFilter.value.trim().toLocaleLowerCase());
const filteredWebsites = computed(() => {
  if (!normalizedFilter.value) return websites.value;
  return websites.value.filter((site) =>
    [site.Name, site.Description, site.UrlTemplate]
      .some((value) => String(value ?? '').toLocaleLowerCase().includes(normalizedFilter.value)),
  );
});
const websitePageCount = computed(() =>
  Math.max(1, Math.ceil(filteredWebsites.value.length / websitePageSize.value)),
);
const pagedWebsites = computed(() => {
  const start = (websitePage.value - 1) * websitePageSize.value;
  return filteredWebsites.value.slice(start, start + websitePageSize.value);
});
const isLoading = computed(() => !!vm._loading);

watch(() => vm.Properties.RequestedWebsiteId, (id) => {
  if (!id) return;
  vm.setProperty('RequestedWebsiteId', '');
  router.push(`/settings/websites/${id}/edit`);
}, { immediate: true });

watch(websiteFilter, () => {
  websitePage.value = 1;
});

watch([websitePageCount, websitePageSize], () => {
  websitePage.value = Math.min(websitePage.value, websitePageCount.value);
});

watch(websites, (current, previous = []) => {
  const old = new Map(previous.map((site) => [site.Id, site.IconDataUrl || site.FaviconDataUrl]));
  const next = new Set(failedFaviconIds.value);
  for (const site of current) {
    if (old.get(site.Id) !== (site.IconDataUrl || site.FaviconDataUrl)) next.delete(site.Id);
  }
  for (const id of next) if (!current.some((site) => site.Id === id)) next.delete(id);
  failedFaviconIds.value = next;
});

function setBusy(target, id, busy) {
  const next = new Set(target.value);
  busy ? next.add(id) : next.delete(id);
  target.value = next;
}

function markFaviconFailed(id) {
  setBusy(failedFaviconIds, id, true);
}

function openAdd() {
  router.push('/settings/websites/new');
}

function openEdit(site) {
  router.push(`/settings/websites/${site.Id}/edit`);
}

async function openWebsite(site) {
  try {
    const error = await vm.Methods.OpenWebsite(site.Id);
    if (error) toast.error(error);
  } catch (error) {
    toast.error(error.message || String(error));
  }
}

function askDelete(site) {
  deleteTarget.value = site;
}

async function confirmDelete() {
  const site = deleteTarget.value;
  if (!site) return;
  setBusy(deletingIds, site.Id, true);
  try {
    const error = await vm.Methods.DeleteWebsite(site.Id);
    if (error) {
      toast.error(error);
      return;
    }
    toast.success(`已删除“${site.Name}”`);
    deleteTarget.value = null;
  } catch (error) {
    toast.error(error?.message ?? String(error));
  } finally {
    setBusy(deletingIds, site.Id, false);
  }
}
</script>

<template>
  <TooltipProvider :delay-duration="250">
    <SettingsPage>
      <div class="flex min-h-0 flex-1 flex-col gap-3">
        <div
          v-if="vm._error"
          class="flex shrink-0 items-start gap-2 rounded-md border border-destructive/30 bg-destructive/5 px-3 py-2 text-sm text-destructive"
          role="alert"
        >
          <TriangleAlert class="mt-0.5 size-4 shrink-0" />
          <span>{{ vm._error }}</span>
        </div>

        <SettingsListShell class="min-h-0 flex-1">
          <template #toolbar>
            <div class="flex items-center gap-2">
              <SettingsSearchField
                v-model="websiteFilter"
                wrapper-class="w-auto min-w-0 flex-1"
                placeholder="按名称、说明或网址筛选…"
                autocomplete="off"
                name="ws-website-filter"
              />
              <Button class="h-9 shrink-0 gap-1.5" size="sm" @click="openAdd">
                <Plus class="size-4" />新增网站
              </Button>
            </div>
          </template>

          <div
            v-if="isLoading && websites.length === 0"
            class="overflow-auto p-3"
          >
            <div v-for="i in 4" :key="i" class="flex items-center gap-3 border-b border-border py-2.5 last:border-b-0">
              <Skeleton class="size-6 shrink-0 rounded" />
              <Skeleton class="h-3.5 w-40" />
              <Skeleton class="ml-auto h-3.5 w-16" />
            </div>
          </div>

          <SettingsEmptyState
            v-else-if="websites.length === 0"
            title="还没有网站入口"
            description="固定网址可直接打开；地址里写 {query} 可当搜索模板。添加后会参与全局搜索。"
          >
            <template #icon>
              <Globe2 class="size-5" />
            </template>
            <div class="flex flex-wrap items-center justify-center gap-2">
              <Button class="gap-1.5" size="sm" @click="openAdd">
                <Plus class="size-4" />添加网站
              </Button>
              <Button
                size="sm"
                variant="outline"
                @click="router.push({ path: '/settings/guide', query: { section: 'websites' } })"
              >
                查看使用说明
              </Button>
            </div>
          </SettingsEmptyState>

          <SettingsEmptyState
            v-else-if="filteredWebsites.length === 0"
            title="没有匹配的网站"
            description="换个关键词，或清空筛选看看全部列表。"
          >
            <template #icon>
              <Search class="size-5" />
            </template>
            <Button size="sm" variant="outline" @click="websiteFilter = ''">
              清空筛选
            </Button>
          </SettingsEmptyState>

          <div v-else class="ws-list-scroll min-h-0 flex-1 overflow-auto">
            <Table>
              <TableHeader>
                <TableRow class="hover:bg-transparent">
                  <TableHead class="h-11 w-[40%] px-4">名称</TableHead>
                  <TableHead class="h-11 px-3">地址</TableHead>
                  <TableHead class="h-11 w-32 px-4 text-right">操作</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow
                  v-for="site in pagedWebsites"
                  :key="site.Id"
                  class="group border-border/70 hover:bg-muted/40"
                >
                  <TableCell class="px-4 py-3.5">
                    <div class="flex min-w-0 items-center gap-3">
                      <span
                        class="relative grid size-9 shrink-0 place-items-center overflow-hidden rounded-lg border border-border/80 bg-muted/40 text-muted-foreground shadow-sm"
                      >
                        <img
                          v-if="(site.IconDataUrl || site.FaviconDataUrl) && !failedFaviconIds.has(site.Id)"
                          :key="site.IconDataUrl || site.FaviconDataUrl"
                          :src="site.IconDataUrl || site.FaviconDataUrl"
                          :title="site.IconState"
                          :alt="`${site.Name} 图标`"
                          class="absolute inset-0 size-full object-contain p-1.5"
                          @error="markFaviconFailed(site.Id)"
                        />
                        <Globe2 v-else class="size-4" />
                      </span>
                      <div class="min-w-0">
                        <div class="flex min-w-0 items-center gap-2">
                          <p class="truncate text-sm font-medium leading-snug text-foreground">
                            {{ site.Name }}
                          </p>
                          <span
                            class="shrink-0 rounded px-1.5 py-0.5 text-[11px] text-muted-foreground ring-1 ring-border/70"
                          >
                            {{ websiteTypeLabel(site) }}
                          </span>
                        </div>
                        <p
                          v-if="site.Description"
                          class="mt-0.5 truncate text-[11px] text-muted-foreground"
                        >
                          {{ site.Description }}
                        </p>
                      </div>
                    </div>
                  </TableCell>
                  <TableCell class="px-3 py-3.5">
                    <Tooltip>
                      <TooltipTrigger as-child>
                        <span class="block max-w-md truncate font-mono text-xs leading-relaxed text-muted-foreground">
                          {{ site.UrlTemplate || '—' }}
                        </span>
                      </TooltipTrigger>
                      <TooltipContent
                        v-if="site.UrlTemplate"
                        class="max-w-md break-all font-mono text-xs"
                      >
                        {{ site.UrlTemplate }}
                      </TooltipContent>
                    </Tooltip>
                  </TableCell>
                  <TableCell class="px-4 py-3.5">
                    <div class="flex items-center justify-end gap-0.5 opacity-80 transition-opacity group-hover:opacity-100">
                      <Tooltip>
                        <TooltipTrigger as-child>
                          <Button
                            size="icon"
                            variant="ghost"
                            class="size-8"
                            @click="openWebsite(site)"
                          >
                            <ExternalLink class="size-3.5" />
                          </Button>
                        </TooltipTrigger>
                        <TooltipContent>打开</TooltipContent>
                      </Tooltip>
                      <Tooltip>
                        <TooltipTrigger as-child>
                          <Button
                            size="icon"
                            variant="ghost"
                            class="size-8"
                            @click="openEdit(site)"
                          >
                            <Pencil class="size-3.5" />
                          </Button>
                        </TooltipTrigger>
                        <TooltipContent>编辑</TooltipContent>
                      </Tooltip>
                      <Tooltip>
                        <TooltipTrigger as-child>
                          <Button
                            size="icon"
                            variant="ghost"
                            class="size-8 text-muted-foreground hover:text-destructive"
                            :disabled="deletingIds.has(site.Id)"
                            @click="askDelete(site)"
                          >
                            <Loader2 v-if="deletingIds.has(site.Id)" class="size-3.5 animate-spin" />
                            <Trash2 v-else class="size-3.5" />
                          </Button>
                        </TooltipTrigger>
                        <TooltipContent>删除</TooltipContent>
                      </Tooltip>
                    </div>
                  </TableCell>
                </TableRow>
              </TableBody>
            </Table>
          </div>

          <template #footer>
            <TablePagination
              :page="websitePage"
              :page-size="websitePageSize"
              :total="filteredWebsites.length"
              @update:page="websitePage = $event"
              @update:page-size="websitePageSize = $event"
            />
          </template>
        </SettingsListShell>
      </div>

      <Dialog v-model:open="deleteOpen">
        <DialogContent class="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>删除网站</DialogTitle>
            <DialogDescription>
              确定删除“{{ deleteTarget?.Name }}”吗？此操作无法撤销。
              登录状态、网站缓存和你的文件会保留，不会删除。
            </DialogDescription>
          </DialogHeader>
          <DialogFooter class="gap-2 sm:gap-2">
            <Button
              variant="outline"
              :disabled="isDeletingTarget"
              @click="deleteTarget = null"
            >
              取消
            </Button>
            <Button
              variant="destructive"
              class="gap-1.5"
              :disabled="isDeletingTarget"
              @click="confirmDelete"
            >
              <Loader2 v-if="isDeletingTarget" class="size-3.5 animate-spin" />
              删除
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </SettingsPage>
  </TooltipProvider>
</template>
