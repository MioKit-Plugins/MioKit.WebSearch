<script setup>
import { computed, inject, onBeforeUnmount, ref, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import {
  ArrowLeft,
  ChevronDown,
  ChevronUp,
  Code2,
  Database,
  FileCode2,
  FolderOpen,
  FolderPlus,
  Globe2,
  ImageDown,
  ImagePlus,
  Loader2,
  RotateCcw,
  Navigation,
  Plus,
  Settings2,
  Shield,
  Trash2,
} from 'lucide-vue-next';
import { toast } from 'vue-sonner';
import { cn } from '@/lib/utils';
import { useDelayedBusy } from '@/composables/useDelayedBusy';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Switch } from '@/components/ui/switch';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from '@/components/ui/tooltip';
import SettingRow from '@/components/settings/SettingRow.vue';
import SettingsSection from '@/components/settings/SettingsSection.vue';
import ScriptEditor from '@/components/website/ScriptEditor.vue';
import ScriptApiHelp from '@/components/website/ScriptApiHelp.vue';
import {
  browserPermissionOptions,
  camelize,
  emptyDraft,
  permissionOptions,
} from '@/components/website/configuration';

defineOptions({ name: 'WebsiteEditorView' });

const route = useRoute();
const router = useRouter();
const vm = inject('webSearchVm');

const websiteId = computed(() => {
  const id = route.params.id;
  return typeof id === 'string' && id !== 'new' ? id : '';
});

const draft = ref(emptyDraft());
const tab = ref('basic');
const loading = ref(false);
const saving = ref(false);
const activeOperation = ref('');
const requiresReopen = ref(false);
const slowSaving = useDelayedBusy(saving);
const slowLoading = useDelayedBusy(loading);
const error = ref('');
const baseline = ref('');
const scriptIndex = ref(0);
const favicon = ref('');
const faviconBusy = ref(false);
const iconPicking = ref(false);
const iconError = ref('');
const customPreview = ref('');
const failedIconUrl = ref('');
let faviconTimer;
let session = 0;
let faviconRequest = 0;

const iconBusy = computed(() => loading.value || saving.value || faviconBusy.value || iconPicking.value);
const hasCustomIcon = computed(() => draft.value.iconAction === 'Replace'
  || (draft.value.iconAction === 'Keep' && draft.value.hasCustomIcon));
const iconPreview = computed(() => (hasCustomIcon.value
  ? customPreview.value || draft.value.iconDataUrl
  : (c.value.sourceKind === 'Online' ? favicon.value : '')
    || vm.Properties.DefaultWebsiteIconDataUrl || ''));
const iconStatus = computed(() => {
  if (draft.value.iconAction === 'Replace') return '自定义图片 · 保存后生效';
  if (hasCustomIcon.value) return draft.value.iconState || '自定义图片';
  if (draft.value.iconAction === 'Reset') return '已恢复默认 · 保存后生效';
  return c.value.sourceKind === 'Online' && favicon.value ? '自动获取' : '默认图标';
});

const c = computed(() => draft.value.configuration);
const r = computed(() => c.value.runtime);
const script = computed(() => r.value.scripts[scriptIndex.value]);
const embedded = computed(() => c.value.openMode === 'Embedded');
const dirty = computed(() => JSON.stringify(draft.value) !== baseline.value);
const isSearchTemplate = computed(() => /\{query\}/i.test(draft.value.urlTemplate));
const canSave = computed(() => {
  if (!draft.value.name.trim()) return false;
  if (c.value.sourceKind === 'Online') return !!draft.value.urlTemplate.trim();
  return !!c.value.localFilePath.trim();
});
const preview = computed(() => (c.value.sourceKind === 'LocalFile'
  ? (c.value.mapLocalDirectory
    ? `https://${c.value.localHostName || '自动生成本地访问名'}/${c.value.localFilePath.split(/[\\/]/).pop()}`
    : c.value.localFilePath)
  : draft.value.urlTemplate.replace(/\{query\}/gi, encodeURIComponent('MioKit'))));

watch(iconPreview, () => { failedIconUrl.value = ''; });
watch(error, (message) => {
  if (message) toast.error(message, { id: 'website-editor-error', duration: 6000 });
  else toast.dismiss('website-editor-error');
});
watch(iconError, (message) => {
  if (message) toast.error('图标操作失败', { id: 'website-icon-error', description: message });
  else toast.dismiss('website-icon-error');
});

const stages = [
  ['DocumentCreated', '页面开始加载时'],
  ['DomReady', '页面可交互时'],
  ['Loaded', '加载完成时'],
  ['Closing', '窗口关闭前'],
];

const navItems = [
  { id: 'basic', label: '基本信息', icon: Globe2, advanced: false },
  { id: 'navigation', label: '导航与窗口', icon: Navigation, advanced: true },
  { id: 'data', label: '本地数据与文件', icon: Database, advanced: true },
  { id: 'scripts', label: '脚本', icon: Code2, advanced: true },
  { id: 'permissions', label: '权限', icon: Shield, advanced: true },
  { id: 'advanced', label: '高级', icon: Settings2, advanced: true },
];

watch(embedded, (value) => {
  if (!value && tab.value !== 'basic') tab.value = 'basic';
});

watch(() => [draft.value.urlTemplate, c.value.sourceKind], ([url, sourceKind]) => {
  const request = ++faviconRequest;
  const editorSession = session;
  clearTimeout(faviconTimer);
  favicon.value = '';
  if (sourceKind !== 'Online' || !/^https?:\/\//i.test(url)) return;
  faviconTimer = setTimeout(async () => {
    try {
      const value = await vm.Methods.PreviewFavicon(url);
      if (request === faviconRequest && session === editorSession) favicon.value = value;
    } catch { /* optional */ }
  }, 200);
});

onBeforeUnmount(() => {
  clearTimeout(faviconTimer); session++; faviconRequest++;
  toast.dismiss('website-editor-error');
  toast.dismiss('website-icon-error');
});

async function loadEditor() {
  const request = ++session;
  saving.value = false;
  activeOperation.value = '';
  requiresReopen.value = false;
  faviconBusy.value = false;
  iconPicking.value = false;
  if (vm._loading || typeof vm.Methods.GetWebsiteEditor !== 'function') {
    loading.value = true;
    return;
  }
  loading.value = true;
  error.value = '';
  tab.value = 'basic';
  scriptIndex.value = 0;
  iconError.value = '';
  customPreview.value = '';
  try {
    const value = websiteId.value
      ? camelize(await vm.Methods.GetWebsiteEditor(websiteId.value))
      : emptyDraft();
    if (session !== request) return;
    draft.value = value;
    requiresReopen.value = !!value.requiresReopen;
    baseline.value = JSON.stringify(value);
  } catch (e) {
    if (session === request) error.value = e.message || String(e);
  } finally {
    if (session === request) loading.value = false;
  }
}

watch(
  () => [route.fullPath, vm._loading, vm.Methods.GetWebsiteEditor],
  () => { void loadEditor(); },
  { immediate: true },
);

async function refreshFavicon() {
  if (iconBusy.value) return;
  const currentSession = session;
  const url = draft.value.urlTemplate;
  const request = ++faviconRequest;
  faviconBusy.value = true;
  iconError.value = '';
  try {
    const message = await vm.Methods.RefreshFaviconByUrl(url);
    const value = await vm.Methods.PreviewFavicon(url);
    if (currentSession !== session || request !== faviconRequest || url !== draft.value.urlTemplate) return;
    if (value) favicon.value = value;
    if (message) iconError.value = message;
    else toast.success('网站图标已更新');
  } catch (e) {
    if (currentSession === session && request === faviconRequest && url === draft.value.urlTemplate)
      iconError.value = e.message || String(e);
  } finally {
    if (currentSession === session) faviconBusy.value = false;
  }
}

async function pickIcon() {
  if (iconBusy.value) return;
  const currentSession = session;
  iconPicking.value = true;
  iconError.value = '';
  try {
    const result = camelize(await vm.Methods.PickWebsiteIcon());
    if (currentSession !== session || !result) return;
    draft.value.iconAction = 'Replace';
    draft.value.iconSourcePath = result.sourcePath;
    customPreview.value = result.previewDataUrl;
  } catch (e) {
    if (currentSession === session) iconError.value = e.message || String(e);
  } finally {
    if (currentSession === session) iconPicking.value = false;
  }
}

function resetIcon() {
  if (iconBusy.value) return;
  draft.value.iconAction = 'Reset';
  draft.value.iconSourcePath = '';
  customPreview.value = '';
  iconError.value = '';
}

async function pick(target, field, localFile = false) {
  const currentSession = session;
  try {
    const path = await vm.Methods[localFile ? 'PickLocalFile' : 'PickDirectory']();
    if (currentSession === session && path) target[field] = path;
  } catch (e) {
    if (currentSession === session) error.value = e.message || String(e);
  }
}

async function addFileDirectory() {
  const currentSession = session;
  try {
    const path = await vm.Methods.PickDirectory();
    if (currentSession !== session || !path) return;
    r.value.fileDirectories.push({ directory: path, read: true, write: false });
  } catch (e) {
    if (currentSession === session) error.value = e.message || String(e);
  }
}

function addEmptyFileDirectory() {
  r.value.fileDirectories.push({ directory: '', read: true, write: false });
}

async function save() {
  if (iconBusy.value) return;
  const currentSession = session;
  saving.value = true;
  activeOperation.value = 'save';
  error.value = '';
  try {
    const result = camelize(await vm.Methods.SaveWebsite(JSON.parse(JSON.stringify(draft.value))));
    if (currentSession !== session) return;
    if (!result.success) {
      error.value = result.error;
      if (/权限|可信来源/.test(result.error)) tab.value = 'permissions';
      else if (/目录|映射/.test(result.error)) tab.value = 'data';
      else if (/脚本/.test(result.error)) tab.value = 'scripts';
      else if (/导航/.test(result.error)) tab.value = 'navigation';
      else if (/基本|名称|地址|文件|图标/.test(result.error)) tab.value = 'basic';
      return;
    }
    // The write has committed even if refreshing the saved draft fails.
    // Keep the ID before awaiting readback so a retry cannot create another website.
    draft.value.id = result.id;
    requiresReopen.value ||= !!result.requiresReopen;
    let saved;
    try {
      saved = camelize(await vm.Methods.GetWebsiteEditor(result.id));
    } catch (e) {
      if (currentSession === session) {
        error.value = `网站设置已保存，但重新读取失败：${e.message || String(e)}。再次保存将更新此网站。`;
      }
      return;
    }
    if (currentSession !== session) return;
    draft.value = saved;
    customPreview.value = '';
    baseline.value = JSON.stringify(saved);
    requiresReopen.value = !!saved.requiresReopen;
    toast.success('网站设置已保存', {
      id: 'website-editor-save',
      description: requiresReopen.value ? '重新打开网站窗口后生效' : '',
    });
    goBack();
  } catch (e) {
    if (currentSession === session) error.value = e.message || String(e);
  } finally {
    if (currentSession === session) { saving.value = false; activeOperation.value = ''; }
  }
}

async function run(method) {
  if (iconBusy.value) return;
  if (!draft.value.id || dirty.value) {
    error.value = '请先保存设置。';
    return;
  }
  saving.value = true;
  activeOperation.value = method;
  error.value = '';
  const currentSession = session;
  try {
    const result = await vm.Methods[method](draft.value.id);
    if (currentSession !== session) return;
    if (result) error.value = result;
    else if (method === 'ReopenWebsite') {
      requiresReopen.value = false;
      toast.success('窗口已重新打开，已应用最新配置', { id: 'website-editor-save' });
    }
  } catch (e) {
    if (currentSession === session) error.value = e.message || String(e);
  } finally {
    if (currentSession === session) { saving.value = false; activeOperation.value = ''; }
  }
}

function addScript() {
  r.value.scripts.push({
    name: `脚本 ${r.value.scripts.length + 1}`,
    enabled: true,
    stage: 'DomReady',
    code: '// 先在「权限」页开启能力，点标题旁「?」查看 API 速查\n// await Mio.ready;\n// await Mio.storage.set("example", { value: 1 });\n',
  });
  scriptIndex.value = r.value.scripts.length - 1;
  tab.value = 'scripts';
}

function moveScript(delta) {
  const index = scriptIndex.value;
  const next = index + delta;
  if (next < 0 || next >= r.value.scripts.length) return;
  [r.value.scripts[index], r.value.scripts[next]] = [r.value.scripts[next], r.value.scripts[index]];
  scriptIndex.value = next;
}

function removeScript(index = scriptIndex.value) {
  r.value.scripts.splice(index, 1);
  scriptIndex.value = Math.min(index, Math.max(0, r.value.scripts.length - 1));
}

function listHas(list, value) {
  return Array.isArray(list) && list.includes(value);
}

function toggleList(list, value, checked) {
  if (!Array.isArray(list)) return;
  const i = list.indexOf(value);
  if (checked && i < 0) list.push(value);
  if (!checked && i >= 0) list.splice(i, 1);
}

function selectTab(id, requiresEmbedded) {
  if (requiresEmbedded && !embedded.value) return;
  tab.value = id;
}

function goBack() {
  router.push('/settings/websites');
}

function stageLabel(stage) {
  return stages.find(([value]) => value === stage)?.[1] ?? stage;
}
</script>

<template>
  <TooltipProvider :delay-duration="200">
    <div data-website-editor class="relative flex h-full min-h-0 flex-col overflow-hidden bg-background">
      <!-- 上栏：返回 + 保存 -->
      <header
        class="flex shrink-0 items-center gap-3 border-b border-border bg-card px-4 py-3"
      >
        <Button variant="ghost" size="sm" class="h-9 gap-1.5 px-2" :disabled="saving" @click="goBack">
          <ArrowLeft class="size-4" />
          返回
        </Button>
        <p class="min-w-0 flex-1 truncate text-xs text-muted-foreground" role="status" :title="requiresReopen ? '权限或运行配置已保存，重新打开窗口后生效。' : ''">
          {{ requiresReopen ? '配置已保存，重新打开窗口后生效' : '' }}
        </p>
        <div class="ml-auto flex shrink-0 items-center gap-2">
          <Button
            size="sm" variant="outline" class="relative h-9 w-36"
            :class="!requiresReopen && 'invisible pointer-events-none'"
            :disabled="iconBusy || dirty || !requiresReopen" :aria-busy="saving && activeOperation === 'ReopenWebsite'"
            @click="run('ReopenWebsite')"
          >
            <Loader2 v-if="slowSaving && activeOperation === 'ReopenWebsite'" class="absolute left-2 size-3.5 animate-spin" />
            重新打开窗口
          </Button>
          <Button
            size="sm"
            class="relative h-9 w-24 px-4"
            :disabled="iconBusy || loading || !canSave"
            :aria-busy="saving && activeOperation === 'save'"
            @click="save"
          >
            <Loader2 v-if="slowSaving && activeOperation === 'save'" class="absolute left-2 size-3.5 animate-spin" />
            保存
          </Button>
        </div>
      </header>

      <div class="flex min-h-0 flex-1 overflow-hidden" :inert="saving || loading" :aria-busy="saving || loading">
        <!-- 分区导航 -->
        <aside class="flex w-44 shrink-0 flex-col gap-1 border-r border-border/60 bg-muted/15 p-3">
          <p class="px-2 pb-1 text-[11px] font-medium text-muted-foreground">设置分区</p>
          <template v-for="item in navItems" :key="item.id">
            <Tooltip v-if="item.advanced && !embedded">
              <TooltipTrigger as-child>
                <button
                  type="button"
                  disabled
                  class="flex w-full cursor-not-allowed items-center gap-2 rounded-lg px-2.5 py-2 text-left text-sm text-muted-foreground opacity-40"
                >
                  <component :is="item.icon" class="size-3.5 shrink-0" />
                  {{ item.label }}
                </button>
              </TooltipTrigger>
              <TooltipContent side="right">打开方式选「内置窗口」后可配置</TooltipContent>
            </Tooltip>
            <button
              v-else
              type="button"
              :class="cn(
                'flex w-full items-center gap-2 rounded-md px-2.5 py-2 text-left text-sm',
                tab === item.id
                  ? 'bg-accent font-medium text-accent-foreground'
                  : 'text-muted-foreground hover:bg-muted hover:text-foreground',
              )"
              @click="selectTab(item.id, item.advanced)"
            >
              <component :is="item.icon" class="size-3.5 shrink-0" />
              {{ item.label }}
            </button>
          </template>
        </aside>

        <!-- 内容 -->
        <div class="relative flex min-h-0 min-w-0 flex-1 flex-col overflow-hidden">
          <div
            v-if="slowLoading"
            class="pointer-events-none absolute inset-0 z-10 flex items-center justify-center bg-background/90 text-sm text-muted-foreground"
            role="status"
          >
            正在加载设置…
          </div>

          <!-- 基本：舒适表单，可滚动 -->
          <div
            v-if="tab === 'basic'"
            class="ws-editor-scroll min-h-0 flex-1 overflow-y-auto px-5 py-4"
          >
            <div class="flex w-full flex-col gap-6">
              <section class="space-y-3">
                <div>
                  <h2 class="text-sm font-semibold">名称与入口</h2>
                  <p class="mt-0.5 text-xs text-muted-foreground">这是保存网站最少需要的信息</p>
                </div>
                <div class="space-y-1.5">
                  <Label for="site-name">显示名称</Label>
                  <Input
                    id="site-name"
                    v-model="draft.name"
                    class="h-10 rounded-lg"
                    placeholder="例如：Google 搜索"
                    autocomplete="off"
                  />
                </div>
                <div class="grid gap-2 sm:grid-cols-2">
                  <button
                    type="button"
                    :class="cn(
                      'rounded-xl border px-3.5 py-3 text-left transition-all',
                      c.sourceKind === 'Online'
                        ? 'border-primary/40 bg-primary/8 ring-1 ring-primary/20'
                        : 'border-border/70 hover:border-border',
                    )"
                    @click="c.sourceKind = 'Online'"
                  >
                    <p class="flex items-center gap-1.5 text-sm font-medium">
                      <Globe2 class="size-3.5" />在线网页
                    </p>
                    <p class="mt-1 text-xs text-muted-foreground">网址或搜索入口</p>
                  </button>
                  <button
                    type="button"
                    :class="cn(
                      'rounded-xl border px-3.5 py-3 text-left transition-all',
                      c.sourceKind === 'LocalFile'
                        ? 'border-primary/40 bg-primary/8 ring-1 ring-primary/20'
                        : 'border-border/70 hover:border-border',
                    )"
                    @click="c.sourceKind = 'LocalFile'"
                  >
                    <p class="flex items-center gap-1.5 text-sm font-medium">
                      <FileCode2 class="size-3.5" />本地网页
                    </p>
                    <p class="mt-1 text-xs text-muted-foreground">本机网页文件</p>
                  </button>
                </div>
                <div v-if="c.sourceKind === 'Online'" class="space-y-1.5">
                  <Label for="site-url">网页地址</Label>
                  <Input
                    id="site-url"
                    v-model="draft.urlTemplate"
                    class="h-10 rounded-lg font-mono text-sm"
                    placeholder="https://www.google.com/search?q={query}"
                  />
                  <p class="text-xs text-muted-foreground">
                    用 <code class="rounded bg-muted px-1">&#123;query&#125;</code> 表示要搜索的内容
                  </p>
                </div>
                <div v-else class="space-y-1.5">
                  <Label for="site-file">网页文件</Label>
                  <div class="flex gap-2">
                    <Input
                      id="site-file"
                      v-model="c.localFilePath"
                      class="h-10 rounded-lg font-mono text-sm"
                      placeholder="D:\web\index.html"
                    />
                    <Button variant="outline" class="h-10 rounded-lg" @click="pick(c, 'localFilePath', true)">
                      选择
                    </Button>
                  </div>
                </div>
              </section>

              <section class="space-y-3">
                <div>
                  <h2 class="text-sm font-semibold">打开方式</h2>
                  <p class="mt-0.5 text-xs text-muted-foreground">内置窗口可继续设置脚本与权限</p>
                </div>
                <div class="grid gap-2 sm:grid-cols-2">
                  <button
                    type="button"
                    :class="cn(
                      'rounded-xl border px-3.5 py-3 text-left transition-all',
                      c.openMode === 'External'
                        ? 'border-primary/40 bg-primary/8 ring-1 ring-primary/20'
                        : 'border-border/70 hover:border-border',
                    )"
                    @click="c.openMode = 'External'"
                  >
                    <p class="text-sm font-medium">外部浏览器</p>
                    <p class="mt-1 text-xs text-muted-foreground">系统默认浏览器（推荐）</p>
                  </button>
                  <button
                    type="button"
                    :class="cn(
                      'rounded-xl border px-3.5 py-3 text-left transition-all',
                      c.openMode === 'Embedded'
                        ? 'border-primary/40 bg-primary/8 ring-1 ring-primary/20'
                        : 'border-border/70 hover:border-border',
                    )"
                    @click="c.openMode = 'Embedded'"
                  >
                    <p class="text-sm font-medium">内置窗口</p>
                    <p class="mt-1 text-xs text-muted-foreground">脚本、权限与本地数据</p>
                  </button>
                </div>
              </section>

              <section class="space-y-3 rounded-xl border border-border/60 bg-muted/10 p-4">
                <h2 class="text-sm font-semibold">可选信息</h2>
                <div class="space-y-1.5">
                  <Label for="site-description">说明</Label>
                  <Input
                    id="site-description"
                    v-model="draft.description"
                    class="h-9 rounded-lg"
                    placeholder="列表中显示的简短说明"
                  />
                </div>
                <div v-if="c.sourceKind === 'Online' && isSearchTemplate" class="space-y-1.5">
                  <Label>查询来源</Label>
                  <Select v-model="draft.querySourcePolicy">
                    <SelectTrigger class="h-9 w-full rounded-lg">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="InputThenClipboard">输入优先，空时用剪贴板</SelectItem>
                      <SelectItem value="InputOnly">仅输入</SelectItem>
                      <SelectItem value="ClipboardOnly">仅剪贴板</SelectItem>
                    </SelectContent>
                  </Select>
                </div>
                <div
                  class="flex flex-col gap-2 rounded-lg border border-border/60 bg-background px-3 py-2.5"
                  :aria-busy="iconBusy"
                >
                  <div class="flex items-start gap-2.5">
                    <span class="grid size-9 shrink-0 place-items-center overflow-hidden rounded-lg bg-muted text-muted-foreground">
                      <img
                        v-if="iconPreview && failedIconUrl !== iconPreview"
                        :key="iconPreview"
                        :src="iconPreview"
                        alt="网站图标预览"
                        class="size-7 object-contain"
                        @error="failedIconUrl = iconPreview"
                      />
                      <Globe2 v-else class="size-4" />
                    </span>
                    <div class="min-w-0 space-y-0.5">
                      <p class="text-sm font-medium leading-none">网站图标</p>
                      <p class="text-xs leading-snug text-muted-foreground">PNG、JPG/JPEG、WebP、ICO，最大 5 MB</p>
                      <p class="truncate text-xs text-muted-foreground" role="status" :title="iconStatus">{{ iconStatus }}</p>
                    </div>
                  </div>
                  <div class="flex flex-wrap items-center gap-2">
                    <Button size="sm" variant="outline" class="gap-1.5" :disabled="iconBusy" @click="pickIcon">
                      <ImagePlus class="size-3.5" />选择图片
                    </Button>
                    <Button size="sm" variant="ghost" class="gap-1.5" :disabled="iconBusy || !hasCustomIcon" @click="resetIcon">
                      <RotateCcw class="size-3.5" />{{ c.sourceKind === 'Online' ? '恢复自动图标' : '恢复默认图标' }}
                    </Button>
                    <Button
                      size="sm"
                      variant="outline"
                      class="gap-1.5"
                      :disabled="iconBusy || hasCustomIcon || c.sourceKind !== 'Online' || !/^https?:\/\//i.test(draft.urlTemplate)"
                      @click="refreshFavicon"
                    >
                      <ImageDown class="size-3.5" />
                      获取
                    </Button>
                  </div>
                </div>
                <div class="rounded-lg border border-dashed border-border/60 px-3 py-2">
                  <p class="text-xs text-muted-foreground">入口预览</p>
                  <p class="mt-1 truncate font-mono text-xs" :title="preview">{{ preview || '填写地址后预览' }}</p>
                </div>
              </section>
            </div>
          </div>

          <!-- 脚本：左右分栏，编辑器占满高度 -->
          <div
            v-else-if="tab === 'scripts'"
            class="flex min-h-0 flex-1 overflow-hidden"
          >
            <div class="flex w-56 shrink-0 flex-col border-r border-border/60 bg-muted/10">
              <div class="flex items-center justify-between gap-2 border-b border-border/50 px-3 py-2.5">
                <div class="flex min-w-0 items-center gap-0.5">
                  <p class="text-sm font-semibold">脚本列表</p>
                  <ScriptApiHelp />
                </div>
                <Button size="sm" variant="outline" class="h-8 gap-1 rounded-lg px-2" @click="addScript">
                  <Plus class="size-3.5" />添加
                </Button>
              </div>
              <div class="min-h-0 flex-1 overflow-y-auto p-2">
                <p
                  v-if="!r.scripts.length"
                  class="px-2 py-8 text-center text-xs text-muted-foreground"
                >
                  还没有脚本。<br />点上方添加。
                </p>
                <button
                  v-for="(s, i) in r.scripts"
                  :key="i"
                  type="button"
                  :class="cn(
                    'mb-1 flex w-full flex-col gap-0.5 rounded-md px-2.5 py-2 text-left',
                    scriptIndex === i
                      ? 'bg-accent font-medium text-accent-foreground'
                      : 'hover:bg-muted',
                  )"
                  @click="scriptIndex = i"
                >
                  <span class="truncate text-sm font-medium">{{ s.name || `脚本 ${i + 1}` }}</span>
                  <span class="text-[11px] text-muted-foreground">
                    {{ stageLabel(s.stage) }} · {{ s.enabled ? '已启用' : '已禁用' }}
                  </span>
                </button>
              </div>
            </div>

            <div v-if="script" class="flex min-h-0 min-w-0 flex-1 flex-col">
              <div class="flex shrink-0 flex-wrap items-end gap-3 border-b border-border/50 px-4 py-3">
                <div class="min-w-[140px] flex-1 space-y-1">
                  <Label class="text-xs">脚本名称</Label>
                  <Input v-model="script.name" class="h-9 rounded-lg" />
                </div>
                <div class="w-40 space-y-1">
                  <Label class="text-xs">执行阶段</Label>
                  <Select v-model="script.stage">
                    <SelectTrigger class="h-9 rounded-lg">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem v-for="[value, label] in stages" :key="value" :value="value">
                        {{ label }}
                      </SelectItem>
                    </SelectContent>
                  </Select>
                </div>
                <label class="flex h-9 items-center gap-2 text-sm">
                  <Switch v-model="script.enabled" />
                  启用
                </label>
                <div class="flex items-center gap-1">
                  <Button
                    size="icon"
                    variant="outline"
                    class="size-9"
                    :disabled="scriptIndex === 0"
                    title="上移"
                    @click="moveScript(-1)"
                  >
                    <ChevronUp class="size-4" />
                  </Button>
                  <Button
                    size="icon"
                    variant="outline"
                    class="size-9"
                    :disabled="scriptIndex === r.scripts.length - 1"
                    title="下移"
                    @click="moveScript(1)"
                  >
                    <ChevronDown class="size-4" />
                  </Button>
                  <Button
                    size="icon"
                    variant="ghost"
                    class="size-9 text-muted-foreground hover:text-destructive"
                    title="删除"
                    @click="removeScript()"
                  >
                    <Trash2 class="size-4" />
                  </Button>
                </div>
              </div>
              <div class="min-h-0 flex-1 p-3">
                <ScriptEditor :key="scriptIndex" v-model="script.code" fill class="h-full" />
              </div>
            </div>
            <div
              v-else
              class="flex flex-1 flex-col items-center justify-center gap-3 text-sm text-muted-foreground"
            >
              <Code2 class="size-8 opacity-40" />
              <p>选择或添加一个脚本开始编辑</p>
              <Button class="gap-1.5" @click="addScript">
                <Plus class="size-4" />添加脚本
              </Button>
            </div>
          </div>

          <!-- 其它分区：可滚动 -->
          <div
            v-else
            class="ws-editor-scroll min-h-0 flex-1 overflow-y-auto px-5 py-4"
          >
            <div class="w-full space-y-4">
              <template v-if="tab === 'navigation'">
                <SettingsSection title="导航与窗口">
                  <SettingRow title="网页请求新窗口时" description="新窗口可保留登录弹窗等对话。">
                    <template #control>
                      <Select v-model="r.newWindowMode">
                        <SelectTrigger class="h-9 w-full rounded-lg sm:w-64">
                          <SelectValue />
                        </SelectTrigger>
                        <SelectContent>
                          <SelectItem value="NewWindow">打开新窗口（推荐）</SelectItem>
                          <SelectItem value="Current">当前窗口打开</SelectItem>
                          <SelectItem value="External">外部浏览器打开</SelectItem>
                          <SelectItem value="Block">阻止</SelectItem>
                        </SelectContent>
                      </Select>
                    </template>
                  </SettingRow>
                  <SettingRow title="打开未授权网站时">
                    <template #control>
                      <Select v-model="r.currentNavigation">
                        <SelectTrigger class="h-9 w-full rounded-lg sm:w-72">
                          <SelectValue />
                        </SelectTrigger>
                        <SelectContent>
                          <SelectItem value="Current">留在当前窗口，暂停增强功能</SelectItem>
                          <SelectItem value="ExternalUntrusted">不打开，改用外部浏览器</SelectItem>
                        </SelectContent>
                      </Select>
                    </template>
                  </SettingRow>
                  <SettingRow title="允许网页自动弹出窗口">
                    <template #control>
                      <Switch v-model="r.allowAutomaticPopups" />
                    </template>
                  </SettingRow>
                </SettingsSection>
              </template>

              <template v-else-if="tab === 'data'">
                <SettingsSection title="网站数据">
                  <SettingRow
                    title="数据目录"
                    description="保存登录状态与网站数据。留空则使用此网站的独立目录。"
                    layout="stack"
                  >
                    <template #control>
                      <div class="space-y-2">
                        <div class="flex gap-2">
                          <Input
                            v-model="c.userDataFolder"
                            class="h-9 rounded-lg"
                            placeholder="留空：使用此网站的独立目录"
                          />
                          <Button variant="outline" class="h-9 rounded-lg" @click="pick(c, 'userDataFolder')">
                            选择
                          </Button>
                        </div>
                        <p
                          v-if="draft.effectiveDataDirectory"
                          class="break-all font-mono text-xs text-muted-foreground"
                        >
                          当前：{{ draft.effectiveDataDirectory }}
                        </p>
                        <Button
                          v-if="draft.id"
                          size="sm"
                          variant="outline"
                          @click="run('OpenDataDirectory')"
                        >
                          打开数据目录
                        </Button>
                      </div>
                    </template>
                  </SettingRow>
                  <template v-if="c.sourceKind === 'LocalFile'">
                    <SettingRow title="作为本地站点打开" description="便于保存登录状态与网站数据。">
                      <template #control>
                        <Switch v-model="c.mapLocalDirectory" />
                      </template>
                    </SettingRow>
                    <SettingRow
                      v-if="c.mapLocalDirectory"
                      title="本地访问名"
                      description="留空：按此网站自动生成"
                      layout="stack"
                    >
                      <template #control>
                        <Input v-model="c.localHostName" class="h-9 rounded-lg" placeholder="留空自动生成" />
                      </template>
                    </SettingRow>
                  </template>
                </SettingsSection>
                <div class="flex items-center justify-between">
                  <h3 class="text-sm font-medium">额外目录映射</h3>
                  <Button
                    size="sm"
                    variant="outline"
                    @click="r.mappings.push({ enabled: true, host: '', directory: '', access: 'DenyCors' })"
                  >
                    添加映射
                  </Button>
                </div>
                <div
                  v-for="(m, index) in r.mappings"
                  :key="index"
                  class="settings-card divide-y divide-border/50"
                >
                  <SettingRow :title="`映射 ${index + 1}`">
                    <template #control>
                      <div class="flex items-center gap-2">
                        <Switch v-model="m.enabled" />
                        <Button size="sm" variant="ghost" @click="r.mappings.splice(index, 1)">移除</Button>
                      </div>
                    </template>
                  </SettingRow>
                  <SettingRow title="本地访问名" layout="stack">
                    <template #control>
                      <Input v-model="m.host" class="h-9 rounded-lg" placeholder="assets.local" />
                    </template>
                  </SettingRow>
                  <SettingRow title="目录" layout="stack">
                    <template #control>
                      <div class="flex gap-2">
                        <Input v-model="m.directory" class="h-9 rounded-lg" />
                        <Button variant="outline" class="h-9" @click="pick(m, 'directory')">选择</Button>
                      </div>
                    </template>
                  </SettingRow>
                  <SettingRow title="其他网站访问">
                    <template #control>
                      <Select v-model="m.access">
                        <SelectTrigger class="h-9 w-full rounded-lg sm:w-64">
                          <SelectValue />
                        </SelectTrigger>
                        <SelectContent>
                          <SelectItem value="DenyCors">禁止其他网站读取（推荐）</SelectItem>
                          <SelectItem value="Deny">禁止访问</SelectItem>
                          <SelectItem value="Allow">允许访问</SelectItem>
                        </SelectContent>
                      </Select>
                    </template>
                  </SettingRow>
                </div>
              </template>

              <template v-else-if="tab === 'permissions'">
                <SettingsSection
                  title="接口权限"
                  description="默认全部关闭。修改权限并保存后，需重新打开窗口才能应用；网页可通过 window.Mio 调用已授权的能力。"
                >
                  <div class="grid sm:grid-cols-2">
                    <label
                      v-for="[value, title, description] in permissionOptions"
                      :key="value"
                      class="flex cursor-pointer gap-3 border-b border-border/50 px-4 py-3.5 last:border-b-0 even:sm:border-l even:sm:border-border/50 hover:bg-muted/40 sm:[&:nth-last-child(-n+2)]:border-b-0"
                    >
                      <Checkbox
                        class="mt-0.5"
                        :model-value="listHas(r.permissions, value)"
                        @update:model-value="(checked) => toggleList(r.permissions, value, !!checked)"
                      />
                      <span class="min-w-0">
                        <span class="block text-sm font-medium">{{ title }}</span>
                        <span class="mt-0.5 block text-xs leading-relaxed text-muted-foreground">{{ description }}</span>
                      </span>
                    </label>
                  </div>
                </SettingsSection>

                <SettingsSection title="网页功能权限" description="仅对已授权网站生效。">
                  <div class="grid gap-x-4 gap-y-2 px-3 py-3 sm:grid-cols-3">
                    <label
                      v-for="[value, label] in browserPermissionOptions"
                      :key="value"
                      class="flex items-center gap-2 text-sm"
                    >
                      <Checkbox
                        :model-value="listHas(r.browserPermissions, value)"
                        @update:model-value="(checked) => toggleList(r.browserPermissions, value, !!checked)"
                      />
                      {{ label }}
                    </label>
                  </div>
                </SettingsSection>

                <section class="flex flex-col gap-2">
                  <div class="flex flex-wrap items-end justify-between gap-2 px-0.5">
                    <div class="min-w-0">
                      <h3 class="text-sm font-semibold tracking-tight">文件授权目录</h3>
                      <p class="mt-0.5 text-xs leading-relaxed text-muted-foreground">
                        勾选「文件读取 / 写入」后，在此指定可访问的本地目录
                      </p>
                    </div>
                    <div class="flex shrink-0 gap-2">
                      <Button size="sm" variant="outline" class="h-8 gap-1.5" @click="addEmptyFileDirectory">
                        <Plus class="size-3.5" />手动添加
                      </Button>
                      <Button size="sm" class="h-8 gap-1.5" @click="addFileDirectory">
                        <FolderPlus class="size-3.5" />选择目录
                      </Button>
                    </div>
                  </div>

                  <div class="settings-card">
                    <div
                      v-if="!r.fileDirectories.length"
                      class="flex flex-col items-center justify-center gap-2.5 px-4 py-8 text-center"
                    >
                      <span class="grid size-9 place-items-center rounded-lg bg-muted text-muted-foreground">
                        <FolderOpen class="size-4" />
                      </span>
                      <div class="space-y-1">
                        <p class="text-sm font-medium">还没有授权目录</p>
                        <p class="max-w-sm text-xs text-muted-foreground">
                          推荐「选择目录」，一次选好路径并默认开启读取
                        </p>
                      </div>
                      <Button size="sm" class="h-8 gap-1.5" @click="addFileDirectory">
                        <FolderPlus class="size-3.5" />选择目录
                      </Button>
                    </div>

                    <ul v-else class="divide-y divide-border/50">
                      <li
                        v-for="(grant, index) in r.fileDirectories"
                        :key="index"
                        class="flex flex-col gap-2 px-3 py-2.5 sm:flex-row sm:items-center"
                      >
                        <div class="flex min-w-0 flex-1 items-center gap-2">
                          <FolderOpen class="size-4 shrink-0 text-muted-foreground" />
                          <Input
                            v-model="grant.directory"
                            class="h-8 min-w-0 flex-1 rounded-md font-mono text-xs"
                            placeholder="选择或输入目录路径"
                          />
                          <Button
                            variant="outline"
                            size="sm"
                            class="h-8 shrink-0 rounded-md px-2.5"
                            @click="pick(grant, 'directory')"
                          >
                            浏览
                          </Button>
                        </div>
                        <div class="flex shrink-0 items-center gap-3 sm:pl-1">
                          <label class="flex items-center gap-1.5 text-xs text-muted-foreground">
                            <Switch v-model="grant.read" />
                            读取
                          </label>
                          <label class="flex items-center gap-1.5 text-xs text-muted-foreground">
                            <Switch v-model="grant.write" />
                            写入
                          </label>
                          <Button
                            size="icon"
                            variant="ghost"
                            class="size-8 text-muted-foreground hover:text-destructive"
                            title="移除"
                            @click="r.fileDirectories.splice(index, 1)"
                          >
                            <Trash2 class="size-3.5" />
                          </Button>
                        </div>
                      </li>
                    </ul>
                  </div>
                </section>
              </template>

              <template v-else-if="tab === 'advanced'">
                <SettingsSection title="高级">
                  <SettingRow title="开启调试工具" description="允许通过 F12 或网页右键菜单打开开发者工具。窗口始终由网页铺满内容区域。">
                    <template #control>
                      <Switch v-model="c.debugEnabled" />
                    </template>
                  </SettingRow>
                </SettingsSection>
                <div class="flex items-center justify-between">
                  <h3 class="text-sm font-medium">额外可信来源</h3>
                  <Button size="sm" variant="outline" @click="r.trustedOrigins.push('')">添加来源</Button>
                </div>
                <p class="text-xs text-muted-foreground">填写网站根地址，例如 https://example.com，不要带路径。</p>
                <div v-for="(_, index) in r.trustedOrigins" :key="index" class="flex gap-2">
                  <Input
                    v-model="r.trustedOrigins[index]"
                    class="h-9 rounded-lg"
                    placeholder="https://example.com"
                  />
                  <Button size="sm" variant="ghost" @click="r.trustedOrigins.splice(index, 1)">移除</Button>
                </div>
              </template>
            </div>
          </div>
        </div>
      </div>
    </div>
  </TooltipProvider>
</template>
