<script setup>
import { onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { cn } from '@/lib/utils';
import { loadMonaco } from '@/lib/loadMonaco';
import { getMioScriptTypeDefinitions } from '@/lib/mio-script-types';

const props = defineProps({
  modelValue: { type: String, default: '' },
  /** 撑满父级高度（全页脚本区） */
  fill: { type: Boolean, default: false },
});
const emit = defineEmits(['update:modelValue']);
const host = ref(null);
const failed = ref(false);
let editor;
let model;
let disposeChange;
let typesDisposable;
let disposed = false;

const theme = (e) => {
  if (editor) editor.updateOptions({ theme: e.detail?.theme === 'dark' ? 'vs-dark' : 'vs' });
};

function registerMioTypes(monaco) {
  typesDisposable?.dispose();
  const defaults = monaco.languages.typescript.javascriptDefaults;
  defaults.setCompilerOptions({
    allowNonTsExtensions: true,
    allowJs: true,
    checkJs: true,
    noLib: false,
    target: monaco.languages.typescript.ScriptTarget.ESNext,
    lib: ['es2020', 'dom'],
  });
  defaults.setDiagnosticsOptions({
    noSemanticValidation: false,
    noSyntaxValidation: false,
  });
  typesDisposable = defaults.addExtraLib(
    getMioScriptTypeDefinitions(),
    'ts:mio-api.d.ts',
  );
}

onMounted(async () => {
  try {
    const monaco = await loadMonaco();
    if (disposed) return;
    registerMioTypes(monaco);
    model = monaco.editor.createModel(props.modelValue, 'javascript');
    editor = monaco.editor.create(host.value, {
      model,
      automaticLayout: true,
      minimap: { enabled: false },
      fontSize: 13,
      lineHeight: 20,
      padding: { top: 10, bottom: 10 },
      scrollBeyondLastLine: false,
      suggestOnTriggerCharacters: true,
      quickSuggestions: true,
      wordBasedSuggestions: 'off',
      theme: document.documentElement.classList.contains('dark') ? 'vs-dark' : 'vs',
    });
    disposeChange = model.onDidChangeContent(() => emit('update:modelValue', model.getValue()));
    window.addEventListener('theme-changed', theme);
  } catch {
    failed.value = true;
  }
});

watch(() => props.modelValue, (value) => {
  if (model && value !== model.getValue()) model.setValue(value);
});

onBeforeUnmount(() => {
  disposed = true;
  window.removeEventListener('theme-changed', theme);
  disposeChange?.dispose();
  typesDisposable?.dispose();
  typesDisposable = null;
  editor?.dispose();
  model?.dispose();
});
</script>

<template>
  <div
    v-if="!failed"
    ref="host"
    :class="cn(
      'min-w-0 overflow-hidden rounded-lg border border-border/70 bg-background',
      fill ? 'h-full min-h-0 flex-1' : 'h-72',
    )"
    aria-label="脚本编辑器"
  />
  <textarea
    v-else
    :value="modelValue"
    :class="cn(
      'w-full rounded-lg border border-border bg-background p-3 font-mono text-sm',
      fill ? 'h-full min-h-[240px] flex-1 resize-none' : 'h-72',
    )"
    aria-label="脚本编辑器"
    @input="emit('update:modelValue', $event.target.value)"
  />
</template>
