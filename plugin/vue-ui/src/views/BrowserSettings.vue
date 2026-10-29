<script setup>
import { computed, inject, ref, watch } from 'vue';
import { useRouter } from 'vue-router';
import {
  Bookmark,
  Globe2,
  Loader2,
  RefreshCw,
  Settings2,
} from 'lucide-vue-next';
import { toast } from 'vue-sonner';
import { cn } from '@/lib/utils';
import { useDelayedBusy } from '@/composables/useDelayedBusy';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import SettingsEmptyState from '@/components/settings/SettingsEmptyState.vue';
import SettingsSearchField from '@/components/settings/SettingsSearchField.vue';
import TablePagination from '@/components/shared/TablePagination.vue';
import { pickField, resolveBrowser, resolveProfile } from '@/lib/webSearchItem';

const router = useRouter();
const vm = inject('webSearchVm');
const settingsOpen = ref(false);
const indexRefreshing = ref(false);
const browserFilter = ref('');
const bookmarkFilter = ref('');
const bookmarkPage = ref(1);
const bookmarkPageSize = ref(20);
const bookmarks = ref([]);
const bookmarkTotal = ref(0);
const bookmarksLoading = ref(false);
const bookmarksLoaded = ref(false);
const slowBookmarksLoading = useDelayedBusy(bookmarksLoading);
const openingUrl = ref('');
const failedIconUrls = ref(new Set());
const minSyncLoadingMs = 180;
let bookmarkRequest = 0;

const browsers = computed(() => (vm.Collections.Browsers ?? [])
  .map(resolveBrowser)
  .filter((browser) => browser.Id));
const profiles = computed(() => (vm.Collections.Profiles ?? [])
  .map(resolveProfile)
  .filter((profile) => profile.Path));

const sourceFilters = computed(() => {
  const names = [...new Set(profiles.value.map((p) => p.Browser).filter(Boolean))];
  return [
    { id: '', label: '全部', count: Number(vm.Properties.BookmarkCount ?? 0) },
    ...names.map((name) => ({ id: name, label: name })),
  ];
});

const isIndexBusy = computed(() => indexRefreshing.value || !!vm.Properties.IsSynchronizing);
const lastError = computed(() => String(vm.Properties.LastError ?? '').trim());
watch(lastError, (message) => {
  if (message) toast.error('收藏夹同步异常', { id: 'bookmark-sync-error', description: message });
}, { immediate: true });
const bookmarkCount = computed(() => Number(vm.Properties.BookmarkCount ?? 0));
const lastSyncLabel = computed(() => {
  if (isIndexBusy.value) return '正在同步…';
  if (lastError.value) return '同步异常';
  const at = vm.Properties.LastSynchronizedAt;
  if (!at || at === '尚未同步') return '尚未同步';
  return at;
});
const syncBadge = computed(() => {
  if (isIndexBusy.value) {
    return { text: '同步中', tone: 'busy' };
  }
  if (lastError.value) {
    return { text: '异常', tone: 'error' };
  }
  const at = vm.Properties.LastSynchronizedAt;
  if (!at || at === '尚未同步') {
    return { text: '未同步', tone: 'idle' };
  }
  return { text: '已同步', tone: 'ok' };
});

const defaultBrowserId = computed(() => vm.Properties.DefaultBrowserId ?? 'system');
const defaultBrowserLabel = computed(() => {
  const id = defaultBrowserId.value;
  const match = browsers.value.find((b) => b.Id === id);
  return match?.Name || '系统默认';
});

const bookmarkPageCount = computed(() =>
  Math.max(1, Math.ceil(bookmarkTotal.value / bookmarkPageSize.value)),
);

watch(bookmarkFilter, () => { bookmarkPage.value = 1; });
watch(browserFilter, () => { bookmarkPage.value = 1; });
watch([bookmarkPageCount, bookmarkPageSize], () => {
  bookmarkPage.value = Math.min(bookmarkPage.value, bookmarkPageCount.value);
});

watch(
  () => [
    browserFilter.value,
    bookmarkFilter.value,
    bookmarkPage.value,
    bookmarkPageSize.value,
    vm.Properties.BookmarkCount,
    vm.Properties.LastSynchronizedAt,
    vm.Properties.IsSynchronizing,
    vm.Methods.BrowseBookmarks,
  ],
  () => { void loadBookmarks(); },
  { immediate: true },
);

watch(bookmarks, () => {
  failedIconUrls.value = new Set();
});

function setDefaultBrowser(value) {
  vm.setProperty('DefaultBrowserId', value);
}

function markIconFailed(url) {
  const next = new Set(failedIconUrls.value);
  next.add(url);
  failedIconUrls.value = next;
}

function resolveBookmark(item) {
  return {
    Title: String(pickField(item, 'title', 'Title') || '未命名收藏'),
    Url: String(pickField(item, 'url', 'Url')),
    Browser: String(pickField(item, 'browser', 'Browser')),
    Profile: String(pickField(item, 'profile', 'Profile')),
    Folder: String(pickField(item, 'folder', 'Folder') || ''),
    FaviconDataUrl: String(pickField(item, 'faviconDataUrl', 'FaviconDataUrl') || ''),
  };
}

async function loadBookmarks() {
  if (typeof vm.Methods.BrowseBookmarks !== 'function') return;
  const request = ++bookmarkRequest;
  bookmarksLoading.value = true;
  try {
    const offset = (bookmarkPage.value - 1) * bookmarkPageSize.value;
    const page = await vm.Methods.BrowseBookmarks(
      bookmarkFilter.value.trim(),
      browserFilter.value,
      offset,
      bookmarkPageSize.value,
    );
    if (request !== bookmarkRequest) return;
    const items = pickField(page, 'items', 'Items', []) ?? [];
    bookmarks.value = (Array.isArray(items) ? items : []).map(resolveBookmark);
    bookmarkTotal.value = Number(pickField(page, 'total', 'Total', 0) ?? 0);
    bookmarksLoaded.value = true;
  } catch (error) {
    if (request !== bookmarkRequest) return;
    toast.error(error?.message ?? String(error));
  } finally {
    if (request === bookmarkRequest) bookmarksLoading.value = false;
  }
}

async function openBookmark(bookmark) {
  if (!bookmark.Url || openingUrl.value) return;
  openingUrl.value = bookmark.Url;
  try {
    const error = await vm.Methods.OpenBookmark(bookmark.Url);
    if (error) toast.error(error);
  } catch (error) {
    toast.error(error?.message ?? String(error));
  } finally {
    openingUrl.value = '';
  }
}

async function refreshIndex() {
  if (isIndexBusy.value) return;
  indexRefreshing.value = true;
  const startedAt = Date.now();
  let failed = false;
  try {
    await vm.Methods.RefreshIndex();
    while (vm.Properties.IsSynchronizing) {
      await new Promise((resolve) => setTimeout(resolve, 50));
    }
  } catch (error) {
    failed = true;
    toast.error(error?.message ?? String(error));
  } finally {
    const remaining = minSyncLoadingMs - (Date.now() - startedAt);
    if (remaining > 0) {
      await new Promise((resolve) => setTimeout(resolve, remaining));
    }
    indexRefreshing.value = false;
  }
  if (failed) return;
  if (String(vm.Properties.LastError ?? '').trim()) {
    toast.error('同步未完全成功', { description: String(vm.Properties.LastError) });
  } else {
    toast.success('收藏夹已更新', {
      description: `共 ${vm.Properties.BookmarkCount ?? 0} 条`,
    });
  }
  void loadBookmarks();
}
</script>

<template>
  <div class="flex h-full min-h-0 w-full min-w-0 flex-1 flex-col overflow-hidden bg-background">
    <header class="flex shrink-0 items-center justify-between gap-3 border-b border-border px-4 py-3 md:px-5">
      <div class="min-w-0">
        <h1 class="text-base font-semibold text-foreground">收藏夹</h1>
        <p class="mt-0.5 text-xs text-muted-foreground">浏览本机浏览器收藏</p>
      </div>
      <Button
        size="icon"
        variant="outline"
        class="size-8 shrink-0"
        title="收藏夹设置"
        @click="settingsOpen = true"
      >
        <Settings2 class="size-4" />
      </Button>
    </header>

    <div class="flex shrink-0 flex-wrap items-center gap-2 border-b border-border/70 px-4 py-3">
      <div
        class="flex flex-wrap items-center gap-1 rounded-lg bg-muted/40 p-1"
        role="tablist"
        aria-label="收藏来源"
      >
        <button
          v-for="item in sourceFilters"
          :key="item.id || 'all'"
          type="button"
          role="tab"
          :aria-selected="browserFilter === item.id"
          :class="cn(
            'rounded-md px-2.5 py-1 text-xs transition-colors',
            browserFilter === item.id
              ? 'bg-background font-medium text-foreground shadow-sm'
              : 'text-muted-foreground hover:text-foreground',
          )"
          @click="browserFilter = item.id"
        >
          {{ item.label }}
        </button>
      </div>
      <SettingsSearchField
        v-model="bookmarkFilter"
        class="min-w-[12rem] flex-1"
        placeholder="按标题、网址或文件夹筛选…"
        autocomplete="off"
        name="ws-bookmark-filter"
      />
    </div>

    <SettingsEmptyState
      v-if="bookmarksLoaded && bookmarkTotal === 0 && !bookmarkFilter.trim()"
      title="还没有收藏夹数据"
      description="先同步本机收藏，再在搜索框用 bookmark-web 或 favorites-web 附着后按标题搜索。"
    >
      <template #icon>
        <Bookmark class="size-5" />
      </template>
      <div class="flex flex-wrap items-center justify-center gap-2">
        <Button size="sm" class="gap-1.5" @click="settingsOpen = true">
          <Settings2 class="size-3.5" />
          打开设置同步
        </Button>
        <Button
          size="sm"
          variant="outline"
          @click="router.push({ path: '/settings/guide', query: { section: 'bookmarks' } })"
        >
          查看使用说明
        </Button>
      </div>
    </SettingsEmptyState>

    <SettingsEmptyState
      v-else-if="bookmarksLoaded && bookmarks.length === 0"
      title="没有匹配的收藏"
      description="换个关键词或切换来源筛选后再试。"
    >
      <template #icon>
        <Globe2 class="size-5" />
      </template>
      <Button size="sm" variant="outline" @click="bookmarkFilter = ''; browserFilter = ''">
        清空筛选
      </Button>
    </SettingsEmptyState>

    <div
      v-else
      :class="cn(
        'ws-list-scroll min-h-0 flex-1 overflow-y-auto overflow-x-hidden',
        slowBookmarksLoading && 'opacity-60 transition-opacity',
      )"
    >
      <ul class="divide-y divide-border/50">
        <li
          v-for="bookmark in bookmarks"
          :key="`${bookmark.Url}|${bookmark.Profile}|${bookmark.Title}`"
        >
          <button
            type="button"
            class="flex w-full min-w-0 items-center gap-2.5 px-4 py-2.5 text-left transition-colors hover:bg-muted/40"
            :title="bookmark.Url"
            :disabled="!!openingUrl && openingUrl !== bookmark.Url"
            @click="openBookmark(bookmark)"
          >
            <span
              class="relative grid size-8 shrink-0 place-items-center overflow-hidden rounded-md border border-border/80 bg-muted/40 text-muted-foreground"
            >
              <Loader2
                v-if="openingUrl === bookmark.Url"
                class="size-3.5 animate-spin"
              />
              <template v-else>
                <img
                  v-if="bookmark.FaviconDataUrl && !failedIconUrls.has(bookmark.Url)"
                  :src="bookmark.FaviconDataUrl"
                  alt=""
                  class="absolute inset-0 size-full object-contain p-1"
                  @error="markIconFailed(bookmark.Url)"
                />
                <Globe2 v-else class="size-3.5" />
              </template>
            </span>
            <div class="min-w-0 flex-1">
              <div class="flex min-w-0 items-center gap-2">
                <p class="truncate text-sm font-medium leading-snug text-foreground">
                  {{ bookmark.Title }}
                </p>
                <span
                  class="shrink-0 rounded px-1.5 py-0.5 text-[11px] text-muted-foreground ring-1 ring-border/70"
                >
                  {{ bookmark.Browser || '—' }}
                </span>
              </div>
              <p
                v-if="bookmark.Folder"
                class="mt-0.5 truncate text-[11px] text-muted-foreground"
              >
                {{ bookmark.Folder }}
              </p>
            </div>
          </button>
        </li>
      </ul>
    </div>

    <div
      class="shrink-0 border-t border-border/70"
    >
      <TablePagination
        :page="bookmarkPage"
        :page-size="bookmarkPageSize"
        :total="bookmarkTotal"
        @update:page="bookmarkPage = $event"
        @update:page-size="bookmarkPageSize = $event"
      />
    </div>

    <Dialog v-model:open="settingsOpen">
      <DialogContent class="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>收藏夹设置</DialogTitle>
          <DialogDescription>
            同步本机收藏，并选择打开时使用的浏览器。
          </DialogDescription>
        </DialogHeader>

        <div class="space-y-5 py-1">
          <div class="space-y-2.5">
            <div class="flex items-center justify-between gap-2">
              <p class="text-sm font-medium text-foreground">同步</p>
              <span
                :class="cn(
                  'rounded-md px-1.5 py-0.5 text-[10px] font-medium tabular-nums',
                  syncBadge.tone === 'busy' && 'bg-primary/12 text-primary',
                  syncBadge.tone === 'error' && 'bg-destructive/10 text-destructive',
                  syncBadge.tone === 'ok' && 'bg-emerald-500/12 text-emerald-700 dark:text-emerald-400',
                  syncBadge.tone === 'idle' && 'bg-muted text-muted-foreground',
                )"
              >
                {{ syncBadge.text }}
              </span>
            </div>
            <p class="text-xs leading-relaxed text-muted-foreground">
              启动时同步一次，之后约每 5 分钟自动同步。同步只更新本地索引，不会改写浏览器里的收藏夹。
            </p>
            <p
              :class="cn(
                'text-[11px]',
                lastError ? 'text-destructive' : 'text-muted-foreground',
              )"
            >
              {{ lastError || lastSyncLabel }}
              <span v-if="!lastError"> · 共 {{ bookmarkCount }} 条</span>
            </p>
            <Button
              size="sm"
              class="h-8 w-full gap-1.5"
              :disabled="isIndexBusy"
              @click="refreshIndex"
            >
              <Loader2 v-if="isIndexBusy" class="size-3.5 animate-spin" />
              <RefreshCw v-else class="size-3.5" />
              {{ isIndexBusy ? '同步中…' : '立即同步' }}
            </Button>
          </div>

          <div class="space-y-2">
            <p class="text-sm font-medium text-foreground">打开方式</p>
            <p class="text-xs text-muted-foreground">
              点击收藏后用哪个浏览器打开。所选浏览器不可用时会回退到系统默认。
            </p>
            <Select
              :model-value="defaultBrowserId"
              @update:model-value="setDefaultBrowser"
            >
              <SelectTrigger class="h-9 w-full bg-background">
                <SelectValue placeholder="选择浏览器" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem
                  v-for="browser in browsers"
                  :key="browser.Id"
                  :value="browser.Id"
                  :disabled="!browser.IsAvailable"
                >
                  {{ browser.Name }}{{ browser.IsAvailable ? '' : '（未发现）' }}
                </SelectItem>
              </SelectContent>
            </Select>
            <p class="text-[11px] text-muted-foreground">
              当前：{{ defaultBrowserLabel }}
            </p>
          </div>
        </div>

        <DialogFooter>
          <Button variant="outline" @click="settingsOpen = false">关闭</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
