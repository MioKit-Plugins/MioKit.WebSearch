<script setup>
import { useRouter } from 'vue-router';
import { HelpCircle } from 'lucide-vue-next';
import { Button } from '@/components/ui/button';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { ScrollArea } from '@/components/ui/scroll-area';

const router = useRouter();

const sample = `await Mio.ready;

// 需开启 storage 权限
await Mio.storage.set('key', { value: 1 });
const data = await Mio.storage.get('key');

// 需 files.read / files.write + 目录授权
const file = await Mio.dialogs.openFile();
if (file) console.log(await Mio.files.readText(file));

await Mio.clipboard.writeText('done');
await Mio.notifications.show('完成', '网页快开');
await Mio.external.open('https://example.com');`;

function openGuide() {
  router.push({ path: '/settings/guide', query: { section: 'scripts' } });
}
</script>

<template>
  <Popover>
    <PopoverTrigger as-child>
      <Button
        variant="ghost"
        size="icon"
        class="size-7"
        title="API 速查"
        aria-label="API 速查"
      >
        <HelpCircle class="size-3.5" />
      </Button>
    </PopoverTrigger>
    <PopoverContent class="w-[min(92vw,520px)] p-0" align="end" side="bottom" :style="{ zIndex: 9999 }">
      <ScrollArea class="h-[min(70vh,420px)]">
        <div class="space-y-3 p-3 text-sm">
          <div>
            <p class="font-semibold text-foreground">脚本与 Mio API</p>
            <p class="mt-1 text-xs leading-relaxed text-muted-foreground">
              四个阶段：文档创建、DOM 就绪、加载完成、关闭前。脚本可直接
              <code class="rounded bg-muted px-1 py-0.5 font-mono text-[11px]">await Mio.*</code>，
              但仍须在「权限」页开启对应能力。未授权会抛出
              <code class="rounded bg-muted px-1 py-0.5 font-mono text-[11px]">permission.denied</code>。
            </p>
          </div>
          <pre
            class="overflow-x-auto rounded-lg border border-border/70 bg-muted/30 p-2.5 font-mono text-[11px] leading-relaxed text-foreground"
          >{{ sample }}</pre>
          <Button variant="link" class="h-auto px-0 text-xs" @click="openGuide">
            查看完整使用说明 →
          </Button>
        </div>
      </ScrollArea>
    </PopoverContent>
  </Popover>
</template>
