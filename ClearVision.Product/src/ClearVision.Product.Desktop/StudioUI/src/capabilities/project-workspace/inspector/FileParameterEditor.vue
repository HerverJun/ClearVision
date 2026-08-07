<script setup lang="ts">
import { computed, onBeforeUnmount, shallowRef } from 'vue';
import { CvIcon } from '@/design-system/icons';
import {
  resolveFilePickerFilter,
  type FilePickerPort
} from '@/platform/host';
import type { InspectorParameterProjection } from './inspectorOwner';

const props = defineProps<{
  parameter: InspectorParameterProjection;
  disabled: boolean;
  filePicker: FilePickerPort | null;
  controlId: string;
  describedBy?: string | undefined;
  selectionKey?: string | undefined;
}>();

const emit = defineEmits<{
  commit: [value: unknown];
}>();

const busy = shallowRef(false);
const errorMessage = shallowRef<string | null>(null);
const disposed = shallowRef(false);

const selectionIdentity = computed(() => [
  props.selectionKey ?? '',
  props.parameter.id ?? '',
  props.parameter.name
].join('|'));
const valueText = computed(() => {
  const value = props.parameter.value;
  return value === null || value === undefined ? '' : String(value);
});
const canClear = computed(() => props.parameter.nullable && valueText.value.length > 0 && !busy.value);
const effectiveDisabled = computed(() => props.disabled || busy.value);

async function chooseFile(): Promise<void> {
  if (effectiveDisabled.value || disposed.value) return;
  if (!props.filePicker) {
    errorMessage.value = '文件选择服务尚未就绪，请重新打开工程工作台。';
    return;
  }

  const requestIdentity = selectionIdentity.value;
  busy.value = true;
  errorMessage.value = null;
  try {
    const result = await props.filePicker.pick({
      parameterName: props.parameter.name,
      filter: props.parameter.filePickerFilter ?? resolveFilePickerFilter(props.parameter.name)
    });
    if (disposed.value || requestIdentity !== selectionIdentity.value) return;
    if (result.status === 'selected') {
      emit('commit', result.filePath);
    }
  } catch (error) {
    if (disposed.value || requestIdentity !== selectionIdentity.value) return;
    errorMessage.value = error instanceof Error ? error.message : '文件选择失败，请重试。';
  } finally {
    if (!disposed.value && requestIdentity === selectionIdentity.value) busy.value = false;
  }
}

function clearFile(): void {
  if (!canClear.value || disposed.value) return;
  errorMessage.value = null;
  emit('commit', null);
}

onBeforeUnmount(() => {
  disposed.value = true;
});
</script>

<template>
  <div
    class="file-parameter-editor"
    :data-picker-state="busy ? 'busy' : errorMessage ? 'error' : 'idle'"
  >
    <div class="file-parameter-editor__row">
      <input
        :id="controlId"
        class="file-parameter-editor__path"
        type="text"
        :value="valueText"
        :name="parameter.name"
        readonly
        :disabled="disabled"
        :aria-describedby="describedBy"
        :aria-busy="busy"
        :title="valueText || '尚未选择文件'"
      >
      <button
        type="button"
        class="file-parameter-editor__choose"
        :disabled="effectiveDisabled"
        :aria-describedby="describedBy"
        :title="busy ? '正在等待文件窗口' : '选择文件'"
        @click="chooseFile"
      >
        <span>{{ busy ? '等待中' : '选择文件' }}</span>
      </button>
      <button
        v-if="parameter.nullable"
        type="button"
        class="file-parameter-editor__clear"
        :disabled="!canClear"
        aria-label="清除文件路径"
        title="清除文件路径"
        @click="clearFile"
      >
        <CvIcon
          name="close"
          size="sm"
        />
      </button>
    </div>
    <small
      v-if="busy"
      class="file-parameter-editor__status"
      role="status"
      aria-live="polite"
    >正在等待文件窗口返回结果。</small>
    <small
      v-else-if="errorMessage"
      class="file-parameter-editor__error"
      role="alert"
    >{{ errorMessage }}</small>
  </div>
</template>

<style scoped>
.file-parameter-editor { min-width: 0; display: grid; gap: var(--cv-space-1); }
.file-parameter-editor__row { min-width: 0; display: grid; grid-template-columns: minmax(0, 1fr) auto auto; align-items: center; gap: var(--cv-space-1); }
.file-parameter-editor__path,
.file-parameter-editor__choose,
.file-parameter-editor__clear {
  height: var(--cv-density-control-height);
  border: 1px solid var(--cv-control-border);
  border-radius: var(--cv-radius-sm);
  background: var(--cv-surface-page);
  color: var(--cv-text-primary);
  font: inherit;
  font-size: var(--cv-font-size-xs);
}
.file-parameter-editor__path { min-width: 0; padding: 0 var(--cv-space-2); text-overflow: ellipsis; }
.file-parameter-editor__choose { padding: 0 var(--cv-space-2); white-space: nowrap; cursor: pointer; }
.file-parameter-editor__clear { width: var(--cv-density-control-height); display: grid; place-items: center; padding: 0; cursor: pointer; }
.file-parameter-editor__choose:hover:not(:disabled),
.file-parameter-editor__clear:hover:not(:disabled) { border-color: var(--cv-control-border-hover); background: var(--cv-interactive-hover); }
.file-parameter-editor__path:focus-visible,
.file-parameter-editor__choose:focus-visible,
.file-parameter-editor__clear:focus-visible { outline: 2px solid var(--cv-focus-ring-color); outline-offset: 1px; }
.file-parameter-editor__path:disabled,
.file-parameter-editor__choose:disabled,
.file-parameter-editor__clear:disabled { color: var(--cv-text-muted); cursor: not-allowed; opacity: .62; }
.file-parameter-editor__status,
.file-parameter-editor__error { overflow-wrap: anywhere; font-size: var(--cv-font-size-2xs); line-height: 1.4; }
.file-parameter-editor__status { color: var(--cv-color-status-info-strong); }
.file-parameter-editor__error { color: var(--cv-color-status-ng-strong); }
</style>
