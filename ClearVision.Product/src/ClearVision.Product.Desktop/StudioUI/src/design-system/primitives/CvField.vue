<script setup lang="ts">
import { computed, useId } from 'vue';

const props = withDefaults(defineProps<{
  modelValue?: string;
  id?: string | undefined;
  label: string;
  type?: 'text' | 'search' | 'number' | 'password' | 'email';
  name?: string | undefined;
  placeholder?: string | undefined;
  hint?: string | undefined;
  error?: string | undefined;
  autocomplete?: string | undefined;
  disabled?: boolean;
  readonly?: boolean;
  required?: boolean;
}>(), {
  modelValue: '',
  id: undefined,
  type: 'text',
  name: undefined,
  placeholder: undefined,
  hint: undefined,
  error: undefined,
  autocomplete: undefined,
  disabled: false,
  readonly: false,
  required: false
});

const emit = defineEmits<{
  'update:modelValue': [value: string];
}>();

const generatedId = useId();
const inputId = computed(() => props.id ?? `cv-field-${generatedId}`);
const hintId = computed(() => `${inputId.value}-hint`);
const errorId = computed(() => `${inputId.value}-error`);
const describedBy = computed(() => {
  const ids: string[] = [];
  if (props.hint) ids.push(hintId.value);
  if (props.error) ids.push(errorId.value);
  return ids.length > 0 ? ids.join(' ') : undefined;
});

function updateValue(event: Event): void {
  emit('update:modelValue', (event.target as HTMLInputElement).value);
}
</script>

<template>
  <label
    class="cv-field"
    :class="{ 'cv-field--error': error, 'cv-field--disabled': disabled }"
    :for="inputId"
    data-design-primitive="field"
  >
    <span class="cv-field__label">
      {{ label }}
      <span
        v-if="required"
        class="cv-field__required"
        aria-hidden="true"
      >*</span>
    </span>
    <span class="cv-field__control-wrap">
      <input
        :id="inputId"
        class="cv-field__control"
        :value="modelValue"
        :type="type"
        :name="name"
        :placeholder="placeholder"
        :autocomplete="autocomplete"
        :disabled="disabled"
        :readonly="readonly"
        :required="required"
        :aria-invalid="error ? 'true' : undefined"
        :aria-describedby="describedBy"
        @input="updateValue"
      >
      <span
        v-if="$slots.trailing"
        class="cv-field__trailing"
        aria-hidden="true"
      ><slot name="trailing" /></span>
    </span>
    <span
      v-if="hint"
      :id="hintId"
      class="cv-field__hint"
    >{{ hint }}</span>
    <span
      v-if="error"
      :id="errorId"
      class="cv-field__error"
      role="alert"
    >{{ error }}</span>
  </label>
</template>

<style scoped>
.cv-field { display: grid; gap: var(--cv-density-field-gap); min-width: 0; }
.cv-field__label { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); font-weight: var(--cv-font-weight-semibold); }
.cv-field__required { color: var(--cv-color-status-ng); }
.cv-field__control-wrap { position: relative; display: flex; min-width: 0; }
.cv-field__control {
  width: 100%;
  height: var(--cv-density-control-height);
  padding: 0 var(--cv-space-3);
  border: 1px solid var(--cv-border-default);
  border-radius: var(--cv-radius-sm);
  background: var(--cv-surface-1);
  color: var(--cv-text-primary);
  font-size: var(--cv-font-size-sm);
  transition: border-color var(--cv-motion-duration-fast) var(--cv-motion-ease-standard), box-shadow var(--cv-motion-duration-fast) var(--cv-motion-ease-standard);
}
.cv-field__control:hover:not(:disabled):not(:read-only) { border-color: var(--cv-border-strong); }
.cv-field__control:focus-visible { border-color: var(--cv-focus-ring-color); outline: none; box-shadow: var(--cv-focus-ring); }
.cv-field__control::placeholder { color: var(--cv-text-muted); }
.cv-field--error .cv-field__control { border-color: var(--cv-color-status-ng); }
.cv-field--disabled { opacity: 0.58; }
.cv-field__trailing { position: absolute; right: var(--cv-space-3); top: 50%; display: inline-flex; transform: translateY(-50%); color: var(--cv-text-muted); }
.cv-field__hint, .cv-field__error { font-size: var(--cv-font-size-2xs); line-height: var(--cv-line-height-normal); }
.cv-field__hint { color: var(--cv-text-muted); }
.cv-field__error { color: var(--cv-color-status-ng-strong); }
</style>
