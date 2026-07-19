<script setup lang="ts">
import { computed, onBeforeUnmount, shallowRef, watch } from 'vue';
import type { InspectorParameterProjection } from './inspectorOwner';

const props = defineProps<{
  parameter: InspectorParameterProjection;
  disabled: boolean;
}>();

const emit = defineEmits<{
  commit: [value: unknown];
  draftActive: [active: boolean];
}>();

const draftText = shallowRef('');
const draftBoolean = shallowRef(false);
const nullSelected = shallowRef(false);

const controlId = computed(() => `inspector-param-${props.parameter.id ?? props.parameter.name}`);
const descriptionId = computed(() => `${controlId.value}-description`);
const errorsId = computed(() => `${controlId.value}-errors`);
const describedBy = computed(() => [
  props.parameter.description ? descriptionId.value : null,
  props.parameter.errors.length > 0 ? errorsId.value : null
].filter(Boolean).join(' ') || undefined);
const effectiveDisabled = computed(() =>
  props.disabled || props.parameter.disabledByConstraint || props.parameter.ignored ||
  props.parameter.editorKind === 'unsupported' || props.parameter.editorKind === 'extension');
const disabledReason = computed(() => {
  if (props.parameter.ignored) return '当前条件下忽略';
  if (props.parameter.disabledByConstraint) return '当前条件下不可编辑';
  if (props.parameter.editorKind === 'unsupported') return '暂不支持编辑';
  if (props.parameter.editorKind === 'extension') return '使用专用编辑器';
  return null;
});

function reset(): void {
  const value = props.parameter.value;
  draftText.value = value === null || value === undefined ? '' : String(value);
  draftBoolean.value = value === true;
  nullSelected.value = value === null && props.parameter.nullable;
}

const dirty = computed(() => {
  if (nullSelected.value) return props.parameter.value !== null;
  if (props.parameter.editorKind === 'boolean') return draftBoolean.value !== props.parameter.value;
  if (props.parameter.editorKind === 'number' || props.parameter.editorKind === 'slider') {
    if (String(draftText.value).trim() === '') return props.parameter.value !== undefined && props.parameter.value !== null;
    const parsed = Number(draftText.value);
    return !Number.isFinite(parsed) || !Object.is(parsed, props.parameter.value);
  }
  return draftText.value !== (props.parameter.value === null || props.parameter.value === undefined
    ? ''
    : String(props.parameter.value));
});

watch(
  [
    () => props.parameter.id,
    () => props.parameter.name,
    () => props.parameter.value,
    () => props.parameter.valueSource,
    () => props.parameter.editorKind
  ],
  reset,
  { immediate: true }
);

watch(dirty, active => emit('draftActive', active), { immediate: true });

function commit(): void {
  if (effectiveDisabled.value) return;
  if (nullSelected.value) {
    emit('commit', null);
    return;
  }
  if (props.parameter.editorKind === 'boolean') {
    emit('commit', draftBoolean.value);
    return;
  }
  if (props.parameter.editorKind === 'number' || props.parameter.editorKind === 'slider') {
    const raw = String(draftText.value).trim();
    emit('commit', raw === '' ? '' : Number(raw));
    return;
  }
  emit('commit', draftText.value);
}

function toggleNull(): void {
  if (nullSelected.value) commit();
  else if (props.parameter.editorKind === 'boolean') draftBoolean.value = false;
  else if (props.parameter.editorKind === 'number' || props.parameter.editorKind === 'slider') {
    draftText.value = typeof props.parameter.defaultValue === 'number'
      ? String(props.parameter.defaultValue)
      : '0';
  } else {
    draftText.value = typeof props.parameter.defaultValue === 'string'
      ? props.parameter.defaultValue
      : '';
  }
}

onBeforeUnmount(() => emit('draftActive', false));
</script>

<template>
  <div
    v-if="parameter.visible"
    class="parameter-editor"
    :class="{ 'is-invalid': parameter.errors.length > 0, 'is-disabled': effectiveDisabled }"
    :data-parameter-name="parameter.name"
    :data-editor-kind="parameter.editorKind"
    :data-value-source="parameter.valueSource"
    :data-dirty="dirty"
  >
    <div class="parameter-editor__label">
      <label
        :for="controlId"
        :title="parameter.label"
      >
        {{ parameter.label }}
        <template v-if="parameter.isRequired">
          <span aria-hidden="true">*</span>
          <span class="parameter-editor__sr-only">必填</span>
        </template>
      </label>
      <span class="parameter-editor__states">
        <small v-if="parameter.valueSource === 'metadata-default'">默认值</small>
        <small v-else-if="parameter.valueSource === 'undefined'">未定义</small>
        <small
          v-if="parameter.deprecated"
          data-tone="warning"
        >已弃用</small>
        <small
          v-if="disabledReason"
          data-tone="muted"
        >{{ disabledReason }}</small>
      </span>
    </div>

    <p
      v-if="parameter.description"
      :id="descriptionId"
      class="parameter-editor__description"
      :title="parameter.description"
    >
      {{ parameter.description }}
    </p>

    <div
      v-if="parameter.editorKind === 'extension' || parameter.editorKind === 'unsupported'"
      class="parameter-editor__extension"
      role="status"
    >
      {{ parameter.extensionMessage }}
    </div>

    <template v-else>
      <label
        v-if="parameter.nullable"
        class="parameter-editor__nullable"
      >
        <input
          v-model="nullSelected"
          type="checkbox"
          :name="`${parameter.name}-use-default`"
          :disabled="effectiveDisabled"
          @change="toggleNull"
        >
        <span>使用默认值（空值）</span>
      </label>

      <input
        v-if="parameter.editorKind === 'text'"
        :id="controlId"
        v-model="draftText"
        type="text"
        :name="parameter.name"
        autocomplete="off"
        :disabled="effectiveDisabled || nullSelected"
        :aria-invalid="parameter.errors.length > 0"
        :aria-describedby="describedBy"
        @blur="commit"
        @keydown.enter.stop.prevent="commit"
        @keydown.escape.stop.prevent="reset"
      >

      <input
        v-else-if="parameter.editorKind === 'number'"
        :id="controlId"
        v-model="draftText"
        type="number"
        inputmode="decimal"
        :name="parameter.name"
        autocomplete="off"
        :min="typeof parameter.minValue === 'number' ? parameter.minValue : undefined"
        :max="typeof parameter.maxValue === 'number' ? parameter.maxValue : undefined"
        :step="parameter.integer ? 1 : 'any'"
        :disabled="effectiveDisabled || nullSelected"
        :aria-invalid="parameter.errors.length > 0"
        :aria-describedby="describedBy"
        @blur="commit"
        @keydown.enter.stop.prevent="commit"
        @keydown.escape.stop.prevent="reset"
      >

      <label
        v-else-if="parameter.editorKind === 'boolean'"
        class="parameter-editor__boolean"
        :for="controlId"
      >
        <input
          :id="controlId"
          v-model="draftBoolean"
          type="checkbox"
          :name="parameter.name"
          :disabled="effectiveDisabled || nullSelected"
          :aria-describedby="describedBy"
          @change="commit"
        >
        <span>{{ draftBoolean ? '是' : '否' }}</span>
      </label>

      <select
        v-else-if="parameter.editorKind === 'enum'"
        :id="controlId"
        v-model="draftText"
        :name="parameter.name"
        :disabled="effectiveDisabled || nullSelected"
        :aria-invalid="parameter.errors.length > 0"
        :aria-describedby="describedBy"
        @change="commit"
      >
        <option
          v-for="option in parameter.options ?? []"
          :key="option.value"
          :value="option.value"
        >
          {{ option.label }}
        </option>
      </select>

      <div
        v-else-if="parameter.editorKind === 'slider'"
        class="parameter-editor__slider"
      >
        <input
          :id="controlId"
          v-model="draftText"
          type="range"
          :name="parameter.name"
          :min="Number(parameter.minValue)"
          :max="Number(parameter.maxValue)"
          :step="parameter.integer ? 1 : 'any'"
          :disabled="effectiveDisabled || nullSelected"
          :aria-describedby="describedBy"
          @change="commit"
        >
        <input
          v-model="draftText"
          type="number"
          inputmode="decimal"
          :name="`${parameter.name}-value`"
          autocomplete="off"
          :aria-label="`${parameter.label}数值`"
          :min="Number(parameter.minValue)"
          :max="Number(parameter.maxValue)"
          :step="parameter.integer ? 1 : 'any'"
          :disabled="effectiveDisabled || nullSelected"
          :aria-invalid="parameter.errors.length > 0"
          :aria-describedby="describedBy"
          @blur="commit"
          @keydown.enter.stop.prevent="commit"
          @keydown.escape.stop.prevent="reset"
        >
      </div>
    </template>

    <ul
      v-if="parameter.errors.length"
      :id="errorsId"
      class="parameter-editor__errors"
      aria-live="polite"
    >
      <li
        v-for="error in parameter.errors"
        :key="`${error.code}-${error.message}`"
      >
        {{ error.message }}
      </li>
    </ul>
  </div>
</template>

<style scoped>
.parameter-editor {
  min-width: 0;
  display: grid;
  gap: var(--cv-space-1);
  padding: var(--cv-space-2) 0 var(--cv-space-3);
  border-bottom: 1px solid var(--cv-border-subtle);
}
.parameter-editor.is-invalid { padding-inline: var(--cv-space-2); border-bottom-color: var(--cv-color-status-ng-border); border-radius: var(--cv-radius-sm); background: var(--cv-color-status-ng-soft); }
.parameter-editor__label { min-width: 0; display: flex; align-items: baseline; justify-content: space-between; flex-wrap: wrap; gap: var(--cv-space-1) var(--cv-space-2); }
.parameter-editor__label > label { min-width: 0; color: var(--cv-text-primary); font-size: var(--cv-font-size-xs); font-weight: var(--cv-font-weight-medium); line-height: 1.4; overflow-wrap: anywhere; }
.parameter-editor__label > label span { color: var(--cv-color-status-ng-strong); }
.parameter-editor__states { min-width: 0; display: inline-flex; align-items: center; flex-wrap: wrap; gap: var(--cv-space-1); }
.parameter-editor__states small { color: var(--cv-text-muted); font-size: 9px; line-height: 1.3; }
.parameter-editor__states small + small::before { content: "·"; margin-right: var(--cv-space-1); color: var(--cv-border-strong); }
.parameter-editor__states small[data-tone="warning"] { color: var(--cv-color-status-warning-strong); }
.parameter-editor__sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
}
.parameter-editor__description {
  margin: 0;
  display: -webkit-box;
  overflow: hidden;
  color: var(--cv-text-muted);
  font-size: var(--cv-font-size-2xs);
  line-height: 1.4;
  overflow-wrap: anywhere;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 2;
}
.parameter-editor input[type="text"],
.parameter-editor input[type="number"],
.parameter-editor select {
  width: 100%;
  min-width: 0;
  height: var(--cv-density-control-height);
  padding: 0 var(--cv-space-2);
  border: 1px solid var(--cv-control-border);
  border-radius: var(--cv-radius-sm);
  background: var(--cv-surface-page);
  color: var(--cv-text-primary);
  font: inherit;
  font-size: var(--cv-font-size-xs);
}
.parameter-editor select { background-color: var(--cv-surface-page); }
.parameter-editor input:hover:not(:disabled),
.parameter-editor select:hover:not(:disabled) { border-color: var(--cv-control-border-hover); }
.parameter-editor input:focus-visible,
.parameter-editor select:focus-visible { outline: 2px solid var(--cv-focus-ring-color); outline-offset: 1px; }
.parameter-editor input:disabled,
.parameter-editor select:disabled { color: var(--cv-text-muted); cursor: not-allowed; opacity: 0.62; }
.parameter-editor__nullable,
.parameter-editor__boolean { min-height: 24px; display: inline-flex; align-items: center; gap: var(--cv-space-2); color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); cursor: pointer; }
.parameter-editor__nullable input,
.parameter-editor__boolean input { margin: 0; }
.parameter-editor__slider { display: grid; grid-template-columns: minmax(0, 1fr) 72px; align-items: center; gap: var(--cv-space-2); }
.parameter-editor__slider input[type="range"] { width: 100%; min-width: 0; accent-color: var(--cv-color-link); }
.parameter-editor__extension { padding: var(--cv-space-2); border-radius: var(--cv-radius-sm); background: var(--cv-color-status-info-soft); color: var(--cv-color-status-info-strong); font-size: var(--cv-font-size-2xs); line-height: 1.45; overflow-wrap: anywhere; }
.parameter-editor__errors { margin: var(--cv-space-1) 0 0; padding-left: 16px; color: var(--cv-color-status-ng-strong); font-size: var(--cv-font-size-2xs); line-height: 1.4; overflow-wrap: anywhere; }
</style>
