<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue';
import type { InspectorParameterProjection } from './inspectorOwner';

const props = defineProps<{
  parameter: InspectorParameterProjection;
  disabled: boolean;
}>();

const emit = defineEmits<{
  commit: [value: unknown];
  draftActive: [active: boolean];
}>();

const draftText = ref('');
const draftBoolean = ref(false);
const nullSelected = ref(false);

const effectiveDisabled = computed(() =>
  props.disabled || props.parameter.disabledByConstraint || props.parameter.ignored ||
  props.parameter.editorKind === 'unsupported' || props.parameter.editorKind === 'extension');

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
  () => [
    props.parameter.id,
    props.parameter.name,
    props.parameter.value,
    props.parameter.valueSource,
    props.parameter.editorKind
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
      <label :for="`inspector-param-${parameter.id ?? parameter.name}`">
        {{ parameter.label }}
        <span v-if="parameter.isRequired">*</span>
      </label>
      <small v-if="parameter.valueSource === 'metadata-default'">使用参数默认值</small>
      <small v-else-if="parameter.valueSource === 'undefined'">未定义</small>
      <small v-if="parameter.deprecated">deprecated</small>
    </div>

    <p
      v-if="parameter.description"
      class="parameter-editor__description"
    >
      {{ parameter.description }}
    </p>

    <div
      v-if="parameter.editorKind === 'extension' || parameter.editorKind === 'unsupported'"
      class="parameter-editor__extension"
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
          :disabled="effectiveDisabled"
          @change="toggleNull"
        >
        <span>Use default value (null)</span>
      </label>

      <input
        v-if="parameter.editorKind === 'text'"
        :id="`inspector-param-${parameter.id ?? parameter.name}`"
        v-model="draftText"
        type="text"
        :disabled="effectiveDisabled || nullSelected"
        :aria-invalid="parameter.errors.length > 0"
        @blur="commit"
        @keydown.enter.stop.prevent="commit"
        @keydown.escape.stop.prevent="reset"
      >

      <input
        v-else-if="parameter.editorKind === 'number'"
        :id="`inspector-param-${parameter.id ?? parameter.name}`"
        v-model="draftText"
        type="number"
        :min="typeof parameter.minValue === 'number' ? parameter.minValue : undefined"
        :max="typeof parameter.maxValue === 'number' ? parameter.maxValue : undefined"
        :step="parameter.integer ? 1 : 'any'"
        :disabled="effectiveDisabled || nullSelected"
        :aria-invalid="parameter.errors.length > 0"
        @blur="commit"
        @keydown.enter.stop.prevent="commit"
        @keydown.escape.stop.prevent="reset"
      >

      <label
        v-else-if="parameter.editorKind === 'boolean'"
        class="parameter-editor__boolean"
      >
        <input
          :id="`inspector-param-${parameter.id ?? parameter.name}`"
          v-model="draftBoolean"
          type="checkbox"
          :disabled="effectiveDisabled || nullSelected"
          @change="commit"
        >
        <span>{{ draftBoolean ? '是' : '否' }}</span>
      </label>

      <select
        v-else-if="parameter.editorKind === 'enum'"
        :id="`inspector-param-${parameter.id ?? parameter.name}`"
        v-model="draftText"
        :disabled="effectiveDisabled || nullSelected"
        :aria-invalid="parameter.errors.length > 0"
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
          :id="`inspector-param-${parameter.id ?? parameter.name}`"
          v-model="draftText"
          type="range"
          :min="Number(parameter.minValue)"
          :max="Number(parameter.maxValue)"
          :step="parameter.integer ? 1 : 'any'"
          :disabled="effectiveDisabled || nullSelected"
          @change="commit"
        >
        <input
          v-model="draftText"
          type="number"
          :min="Number(parameter.minValue)"
          :max="Number(parameter.maxValue)"
          :step="parameter.integer ? 1 : 'any'"
          :disabled="effectiveDisabled || nullSelected"
          @blur="commit"
          @keydown.enter.stop.prevent="commit"
          @keydown.escape.stop.prevent="reset"
        >
      </div>
    </template>

    <ul
      v-if="parameter.errors.length"
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
.parameter-editor { display: grid; gap: 6px; padding: var(--cv-space-2); border: 1px solid var(--cv-border-subtle); border-radius: var(--cv-radius-sm); background: var(--cv-surface-page); }
.parameter-editor.is-invalid { border-color: var(--cv-color-status-ng-strong); }
.parameter-editor.is-disabled { opacity: 0.72; }
.parameter-editor__label { display: flex; align-items: baseline; flex-wrap: wrap; gap: 5px; }
.parameter-editor__label label { color: var(--cv-text-primary); font-size: var(--cv-font-size-xs); font-weight: var(--cv-font-weight-medium); }
.parameter-editor__label label span { color: var(--cv-color-status-ng-strong); }
.parameter-editor__label small { padding: 1px 4px; border-radius: 999px; background: var(--cv-surface-sunken); color: var(--cv-text-muted); font-size: 9px; }
.parameter-editor__description { margin: 0; color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); line-height: 1.35; }
.parameter-editor input[type="text"], .parameter-editor input[type="number"], .parameter-editor select { width: 100%; min-width: 0; height: 30px; padding: 0 var(--cv-space-2); border: 1px solid var(--cv-border-default); border-radius: var(--cv-radius-sm); background: var(--cv-surface-raised); color: var(--cv-text-primary); font: inherit; font-size: var(--cv-font-size-xs); }
.parameter-editor input:focus-visible, .parameter-editor select:focus-visible { outline: 2px solid var(--cv-color-link); outline-offset: 1px; }
.parameter-editor__nullable, .parameter-editor__boolean { display: inline-flex; align-items: center; gap: var(--cv-space-2); color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); }
.parameter-editor__slider { display: grid; grid-template-columns: minmax(0, 1fr) 72px; align-items: center; gap: var(--cv-space-2); }
.parameter-editor__slider input[type="range"] { width: 100%; min-width: 0; }
.parameter-editor__extension { padding: var(--cv-space-2); border-radius: var(--cv-radius-sm); background: var(--cv-color-status-info-soft); color: var(--cv-color-status-info-strong); font-size: var(--cv-font-size-2xs); line-height: 1.4; }
.parameter-editor__errors { margin: 0; padding-left: 16px; color: var(--cv-color-status-ng-strong); font-size: var(--cv-font-size-2xs); line-height: 1.35; }
</style>
