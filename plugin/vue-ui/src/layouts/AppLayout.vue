<script setup>
import { computed, onMounted, provide, shallowRef, watch } from 'vue';
import { useRoute, RouterLink } from 'vue-router';
import { BookOpen, Bookmark, Globe2, PanelTop, Search, Sparkles } from 'lucide-vue-next';
import { cn } from '@/lib/utils';
import { useBridgeService } from '@/composables/useDotnetBridge';
import WebsitesSettings from '@/views/WebsitesSettings.vue';
import BrowserSettings from '@/views/BrowserSettings.vue';
import UsageGuideView from '@/views/UsageGuideView.vue';
import WebsiteEditorView from '@/views/WebsiteEditorView.vue';

const route = useRoute();
const webSearchVm = useBridgeService('WebSearchService');
provide('webSearchVm', webSearchVm);

const isEditor = computed(() => !!route.meta?.editor);

const navGroups = [
  {
    label: '管理',
    items: [
      {
        path: '/settings/websites',
        label: '网站入口',
        icon: Globe2,
        title: '网站入口',
        description: '固定网址与搜索模板，一键跳转',
      },
      {
        path: '/settings/browser',
        label: '收藏夹',
        icon: Bookmark,
        title: '收藏夹',
        description: '浏览本机浏览器收藏并同步',
      },
    ],
  },
  {
    label: '帮助',
    items: [
      {
        path: '/settings/guide',
        label: '使用说明',
        icon: BookOpen,
        title: '使用说明',
        description: '网站入口、收藏夹、内置窗口与脚本',
      },
    ],
  },
];

const allItems = navGroups.flatMap((group) => group.items);
const activePath = computed(() => {
  if (route.path.startsWith('/settings/websites')) return '/settings/websites';
  return route.path;
});
const hidePageHeader = computed(() => activePath.value === '/settings/browser');
const currentPage = computed(
  () => allItems.find((item) => item.path === activePath.value) ?? {
    title: '网页快开',
    description: '',
  },
);

function viewForPath(path) {
  if (path === '/settings/browser') return BrowserSettings;
  if (path === '/settings/guide') return UsageGuideView;
  return WebsitesSettings;
}

/** 编辑页底下保持列表挂载，返回时不闪 */
const listView = shallowRef(WebsitesSettings);
watch(
  activePath,
  (path) => {
    listView.value = viewForPath(path);
  },
  { immediate: true },
);

onMounted(async () => {
  try {
    await webSearchVm.Methods.Initialize?.();
  } catch {
    /* bridge offline in browser preview */
  }
});
</script>

<template>
  <div class="relative flex h-dvh min-h-0 w-full flex-col overflow-hidden bg-background md:flex-row">
    <aside
      class="relative flex w-full shrink-0 flex-col border-b border-border/50 md:h-full md:w-[212px] md:border-b-0 md:border-r"
    >
      <div
        class="pointer-events-none absolute inset-0 bg-gradient-to-b from-primary/[0.07] via-card/80 to-card/40"
        aria-hidden="true"
      />
      <div class="relative flex shrink-0 items-center gap-3 px-4 py-5 md:px-4">
        <div
          class="relative grid size-10 place-items-center rounded-2xl bg-primary text-primary-foreground shadow-md"
          style="box-shadow: 0 8px 18px color-mix(in srgb, var(--primary) 32%, transparent)"
        >
          <PanelTop class="size-5" />
          <span
            class="absolute -bottom-1 -right-1 grid size-4 place-items-center rounded-full bg-background text-primary shadow-sm ring-2 ring-card"
          >
            <Search class="size-2.5" />
          </span>
        </div>
        <div class="min-w-0">
          <p class="truncate text-[15px] font-semibold tracking-tight text-foreground">网页快开</p>
          <p class="mt-0.5 flex items-center gap-1 text-[11px] text-muted-foreground">
            <Sparkles class="size-3 text-primary/70" />
            搜索与收藏快捷入口
          </p>
        </div>
      </div>

      <nav
        class="relative flex min-h-0 gap-1.5 overflow-x-auto px-3 pb-4 md:flex-1 md:flex-col md:gap-3 md:overflow-y-auto md:overflow-x-hidden"
      >
        <div v-for="group in navGroups" :key="group.label" class="md:space-y-1.5">
          <p class="hidden px-2.5 pb-1 text-[11px] font-medium text-muted-foreground/75 md:block">
            {{ group.label }}
          </p>
          <div class="flex gap-1.5 md:flex-col">
            <RouterLink
              v-for="item in group.items"
              :key="item.path"
              :to="item.path"
              :title="item.label"
              :class="cn(
                'group relative flex shrink-0 items-center gap-2.5 rounded-xl px-2.5 py-2.5 text-sm transition-all duration-200 md:w-full',
                activePath === item.path
                  ? 'bg-background/90 font-medium text-foreground shadow-sm ring-1 ring-border/60'
                  : 'text-muted-foreground hover:bg-background/55 hover:text-foreground',
              )"
            >
              <span
                v-if="activePath === item.path"
                class="absolute left-1 top-1/2 hidden h-6 w-1 -translate-y-1/2 rounded-full bg-primary md:block"
              />
              <span
                :class="cn(
                  'flex size-8 shrink-0 items-center justify-center rounded-lg transition-colors',
                  activePath === item.path
                    ? 'bg-primary text-primary-foreground'
                    : 'bg-muted/80 text-muted-foreground group-hover:bg-primary/12 group-hover:text-primary',
                )"
              >
                <component :is="item.icon" class="size-3.5" />
              </span>
              <span class="truncate">{{ item.label }}</span>
            </RouterLink>
          </div>
        </div>
      </nav>
    </aside>

    <div class="flex min-h-0 min-w-0 flex-1 flex-col overflow-hidden bg-background">
      <header
        v-if="!hidePageHeader"
        class="shrink-0 border-b border-border px-4 py-3 md:px-5"
      >
        <h1 class="text-base font-semibold text-foreground">
          {{ currentPage.title }}
        </h1>
        <p
          v-if="currentPage.description"
          class="mt-0.5 text-xs text-muted-foreground"
        >
          {{ currentPage.description }}
        </p>
      </header>

      <main class="relative flex min-h-0 min-w-0 flex-1 overflow-hidden">
        <div
          v-if="webSearchVm._error"
          class="flex size-full items-center justify-center p-6"
        >
          <p class="rounded-md border border-destructive/30 bg-destructive/5 px-3 py-2 text-sm text-destructive">
            {{ webSearchVm._error }}
          </p>
        </div>

        <div
          v-else
          :inert="webSearchVm._loading || isEditor"
          class="settings-detail-viewport flex min-h-0 min-w-0 flex-1 flex-col overflow-hidden"
        >
          <KeepAlive>
            <component
              :is="listView"
              :key="activePath"
              class="min-h-0 min-w-0 flex-1"
            />
          </KeepAlive>
        </div>
      </main>
    </div>

    <!-- 编辑页全屏覆盖：上顶栏 / 下内容，不再占左侧竖条 -->
    <Transition name="ws-editor-slide">
      <div
        v-if="isEditor"
        class="absolute inset-0 z-30 flex min-h-0 flex-col overflow-hidden bg-background"
      >
        <WebsiteEditorView :key="route.fullPath" />
      </div>
    </Transition>
  </div>
</template>
