<script setup lang="ts">
import { computed, useId } from 'vue';
import CvIcon from '../icons/CvIcon.vue';
import type { CvSelectOption } from './types';

const props = withDefaults(defineProps<{
  modelValue?: string;
  options: readonly CvSelectOption[];
  id?: string | undefined;
  label: string;
  name?: string | undefined;
  hint?: string | undefined;
  error?: string | undefined;
  disabled?: boolean;
  required?: boolean;
}>(), {
  modelValue: '',
  id: undefined,
  name: undefined,
  hint: undefined,
  error: undefined,
  disabled: false,
  required: false
});

const emit = defineEmits<{
  'update:modelValue': [value: string];
}>();

const generatedId = useId();
const selectId = computed(() => props.id ?? `cv-select-${generatedId}`);
const hintId = computed(() => `${selectId.value}-hint`);
const errorId = computed(() => `${selectId.value}-error`);
const describedBy = computed(() => {
  const ids: string[] = [];
  if (props.hint) ids.push(hintId.value);
  if (props.error) ids.push(errorId.value);
  return ids.length > 0 ? ids.join(' ') : undefined;
});

function updateValue(event: Event): void {
  emit('update:modelValue', (event.target as HTMLSelectElement).value);
}
</script>

<template>
  <label
    class="cv-select"
    :class="{ 'cv-select--error': error, 'cv-select--disabled': disabled }"
    :for="selectId"
    data-design-primitive="select"
  >
    <span class="cv-select__label">
      {{ label }}
      <span
        v-if="required"
        class="cv-select__required"
        aria-hidden="true"
      >*</span>
    </span>
    <span class="cv-select__control-wrap">
      <select
        :id="selectId"
        class="cv-select__control"
        :value="modelValue"
        :name="name ?? selectId"
        :disabled="disabled"
        :required="required"
        :aria-invalid="error ? 'true' : undefined"
        :aria-describedby="describedBy"
        @change="updateValue"
      >
        <option
          v-for="option in options"
          :key="option.value"
          :value="option.value"
          :disabled="option.disabled"
        >{{ option.label }}</option>
      </select>
      <CvIcon
        class="cv-select__chevron"
        name="chevron-right"
        size="sm"
      />
    </span>
    <span
      v-if="hint"
      :id="hintId"
      class="cv-select__hint"
    >{{ hint }}</span>
    <span
      v-if="error"
      :id="errorId"
      class="cv-select__error"
      role="alert"
    >{{ error }}</span>
  </label>
</template>

<style scoped>
.cv-select { display: grid; gap: var(--cv-density-field-gap); min-width: 0; }
.cv-select__label { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); font-weight: var(--cv-font-weight-semibold); }
.cv-select__required { color: var(--cv-color-status-error); }
.cv-select__control-wrap { position: relative; display: flex; min-width: 0; }
.cv-select__control {
  width: 100%;
  height: var(--cv-density-control-height);
  padding: 0 calc(var(--cv-space-6) + var(--cv-space-2)) 0 var(--cv-space-3);
  border: 1px solid var(--cv-control-border);
  border-radius: var(--cv-radius-sm);
  appearance: none;
  background: var(--cv-surface-raised);
  color: var(--cv-text-primary);
  font-size: var(--cv-font-size-sm);
  transition: border-color var(--cv-motion-duration-fast) var(--cv-motion-ease-standard), box-shadow var(--cv-motion-duration-fast) var(--cv-motion-ease-standard);
}
.cv-select__control:hover:not(:disabled) { border-color: var(--cv-control-border-hover); }
.cv-select__control:focus-visible { border-color: var(--cv-focus-ring-color); outline: 0; box-shadow: var(--cv-focus-ring); }
.cv-select--error .cv-select__control { border-color: var(--cv-color-status-error); }
.cv-select--disabled { opacity: 0.54; }
.cv-select__chevron { position: absolute; right: var(--cv-space-3); top: 50%; transform: translateY(-50%) rotate(90deg); color: var(--cv-text-muted); pointer-events: none; }
.cv-select__hint, .cv-select__error { font-size: var(--cv-font-size-2xs); line-height: var(--cv-line-height-normal); }
.cv-select__hint { color: var(--cv-text-muted); }
.cv-select__error { color: var(--cv-color-status-error-strong); }

@media (forced-colors: active) {
  .cv-select__control:focus-visible {
    border-color: Highlight;
    outline: 2px solid Highlight;
    outline-offset: 2px;
    box-shadow: none;
  }
}
</style>
