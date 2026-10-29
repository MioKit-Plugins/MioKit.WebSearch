<script setup>
import { computed, nextTick, onMounted, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import {
  Bookmark,
  Code2,
  Globe2,
  Info,
  ListChecks,
  PanelTop,
  Zap,
} from 'lucide-vue-next';
import { cn } from '@/lib/utils';
import SettingsPage from '@/components/settings/SettingsPage.vue';
import { guideSections, guideToc } from '@/lib/usageGuideContent';

const route = useRoute();
const router = useRouter();

const iconMap = {
  Zap,
  Globe2,
  Bookmark,
  PanelTop,
  Code2,
  ListChecks,
  Info,
};

const activeSection = computed(() =>
  typeof route.query.section === 'string' ? route.query.section : guideToc[0]?.id,
);

function sectionId(id) {
  return `guide-${id}`;
}

function sectionIcon(name) {
  return iconMap[name] || Info;
}

async function scrollToSection(id) {
  if (!id) return;
  await nextTick();
  const el = document.getElementById(sectionId(id));
  el?.scrollIntoView({ behavior: 'smooth', block: 'start' });
}

function goSection(id) {
  router.replace({ path: '/settings/guide', query: { section: id } });
  scrollToSection(id);
}

onMounted(() => {
  scrollToSection(typeof route.query.section === 'string' ? route.query.section : '');
});

watch(
  () => route.query.section,
  (section) => {
    if (typeof section === 'string') scrollToSection(section);
  },
);
</script>

<template>
  <SettingsPage class="min-h-0 overflow-y-auto overflow-x-hidden">
    <div class="mx-auto w-full max-w-2xl space-y-7 pb-10">
      <p class="text-sm leading-relaxed text-muted-foreground">
        网站入口、搜索模板、本机收藏夹与内置窗口——按场景说明怎么配置、怎么搜。
      </p>

      <nav
        class="sticky top-0 z-10 -mx-4 flex flex-wrap gap-1.5 border-b border-border/50 bg-background/95 px-4 py-2.5 backdrop-blur-md md:-mx-5 md:px-5"
        aria-label="使用说明目录"
      >
        <button
          v-for="item in guideToc"
          :key="item.id"
          type="button"
          :class="cn(
            'inline-flex items-center gap-1.5 rounded-full px-3 py-1.5 text-xs transition-colors',
            activeSection === item.id
              ? 'bg-primary text-primary-foreground'
              : 'bg-muted/60 text-muted-foreground hover:bg-muted hover:text-foreground',
          )"
          @click="goSection(item.id)"
        >
          <component :is="sectionIcon(item.icon)" class="size-3.5 opacity-90" />
          {{ item.title }}
        </button>
      </nav>

      <div class="space-y-8">
        <section
          v-for="section in guideSections"
          :id="sectionId(section.id)"
          :key="section.id"
          class="scroll-mt-16 space-y-3"
        >
          <div class="flex items-center gap-2.5">
            <span
              class="grid size-8 shrink-0 place-items-center rounded-lg bg-primary/10 text-primary"
            >
              <component :is="sectionIcon(section.icon)" class="size-3.5" />
            </span>
            <h2 class="text-sm font-semibold tracking-tight text-foreground">
              {{ section.title }}
            </h2>
          </div>

          <div class="space-y-2 pl-0.5">
            <p
              v-for="(p, i) in section.paragraphs || []"
              :key="`p-${i}`"
              class="text-sm leading-7 text-muted-foreground"
            >
              {{ p }}
            </p>
            <ul
              v-if="section.bullets?.length"
              class="space-y-1.5 pl-1"
            >
              <li
                v-for="(b, i) in section.bullets"
                :key="`b-${i}`"
                class="flex gap-2 text-sm leading-7 text-muted-foreground"
              >
                <span class="mt-2.5 size-1 shrink-0 rounded-full bg-primary/50" />
                <span class="min-w-0">{{ b }}</span>
              </li>
            </ul>
            <pre
              v-if="section.code"
              class="mt-1 overflow-x-auto rounded-lg border border-border/60 bg-muted/30 p-3 font-mono text-[11px] leading-relaxed text-foreground"
            >{{ section.code }}</pre>
          </div>

          <div
            v-for="(sub, si) in section.subsections || []"
            :key="`sub-${si}`"
            class="rounded-lg border border-border/50 bg-muted/15 px-3.5 py-3"
          >
            <h3 class="text-xs font-semibold text-foreground">{{ sub.title }}</h3>
            <div class="mt-1.5 space-y-1.5">
              <p
                v-for="(p, i) in sub.paragraphs || []"
                :key="`sp-${i}`"
                class="text-sm leading-7 text-muted-foreground"
              >
                {{ p }}
              </p>
              <ul
                v-if="sub.bullets?.length"
                class="space-y-1"
              >
                <li
                  v-for="(b, i) in sub.bullets"
                  :key="`sb-${i}`"
                  class="flex gap-2 text-sm leading-7 text-muted-foreground"
                >
                  <span class="mt-2.5 size-1 shrink-0 rounded-full bg-muted-foreground/40" />
                  <span class="min-w-0">{{ b }}</span>
                </li>
              </ul>
              <ol
                v-if="sub.steps?.length"
                class="space-y-2"
              >
                <li
                  v-for="(s, i) in sub.steps"
                  :key="`ss-${i}`"
                  class="flex items-start gap-2.5 text-sm leading-7 text-muted-foreground"
                >
                  <span
                    class="mt-1 flex size-5 shrink-0 items-center justify-center rounded-full bg-primary/10 text-[10px] font-semibold leading-none tabular-nums text-primary"
                  >
                    {{ i + 1 }}
                  </span>
                  <span class="min-w-0">{{ s }}</span>
                </li>
              </ol>
            </div>
          </div>
        </section>
      </div>
    </div>
  </SettingsPage>
</template>
