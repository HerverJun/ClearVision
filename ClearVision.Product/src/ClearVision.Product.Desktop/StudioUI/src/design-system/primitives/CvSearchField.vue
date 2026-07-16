<script setup lang="ts">
import { computed, ref, useId } from 'vue';
import CvIcon from '../icons/CvIcon.vue';

const props = withDefaults(defineProps<{
  modelValue?: string;
  id?: string | undefined;
  name?: string | undefined;
  label?: string;
  placeholder?: string;
  clearLabel?: string;
  disabled?: boolean;
  hideLabel?: boolean;
  autocomplete?: string;
}>(), {
  modelValue: '',
  id: undefined,
  name: undefined,
  label: '搜索',
  placeholder: '搜索',
  clearLabel: '清除搜索内容',
  disabled: false,
  hideLabel: true,
  autocomplete: 'off'
});

const emit = defineEmits<{
  'update:modelValue': [value: string];
  search: [value: string];
  clear: [];
}>();

const generatedId = useId();
const inputId = computed(() => props.id ?? `cv-search-${generatedId}`);
const input = ref<HTMLInputElement>();

function updateValue(event: Event): void {
  emit('update:modelValue', (event.target as HTMLInputElement).value);
}

function submitSearch(): void {
  emit('search', props.modelValue);
}

function clearSearch(): void {
  if (!props.modelValue) return;
  emit('update:modelValue', '');
  emit('clear');
  input.value?.focus();
}

function handleEscape(event: KeyboardEvent): void {
  if (!props.modelValue) return;
  event.preventDefault();
  event.stopPropagation();
  clearSearch();
}
</script>

<template>
  <label
    class="cv-search-field"
    :class="{ 'cv-search-field--disabled': disabled }"
    :for="inputId"
    data-design-primitive="search-field"
  >
    <span
      class="cv-search-field__label"
      :class="{ 'cv-search-field__label--hidden': hideLabel }"
    >{{ label }}</span>
    <span class="cv-search-field__control-wrap">
      <CvIcon
        class="cv-search-field__search-icon"
        name="search"
        size="sm"
      />
      <input
        :id="inputId"
        ref="input"
        class="cv-search-field__control"
        type="search"
        :name="name"
        :value="modelValue"
        :placeholder="placeholder"
        :disabled="disabled"
        :autocomplete="autocomplete"
        aria-keyshortcuts="Escape"
        @input="updateValue"
        @search="submitSearch"
        @keydown.enter="submitSearch"
        @keydown.esc="handleEscape"
      >
      <button
        v-if="modelValue"
        class="cv-search-field__clear"
        type="button"
        :disabled="disabled"
        :aria-label="clearLabel"
        @mousedown.prevent
        @click="clearSearch"
      >
        <CvIcon
          name="close"
          size="sm"
        />
      </button>
    </span>
  </label>
</template>

<style scoped>
.cv-search-field {
  display: grid;
  min-width: min(100%, 180px);
  gap: var(--cv-density-field-gap);
}

.cv-search-field__label {
  color: var(--cv-text-secondary);
  font-size: var(--cv-font-size-xs);
  font-weight: var(--cv-font-weight-semibold);
}

.cv-search-field__label--hidden {
  position: absolute;
  width: 1px;
  height: 1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
}

.cv-search-field__control-wrap {
  position: relative;
  display: flex;
  min-width: 0;
}

.cv-search-field__search-icon {
  position: absolute;
  z-index: 1;
  top: 50%;
  left: var(--cv-space-3);
  color: var(--cv-text-muted);
  pointer-events: none;
  transform: translateY(-50%);
}

.cv-search-field__control {
  width: 100%;
  height: var(--cv-density-control-height);
  padding: 0 calc(var(--cv-density-control-height-sm) + var(--cv-space-2)) 0 calc(var(--cv-space-3) + 18px);
  border: 1px solid var(--cv-control-border);
  border-radius: var(--cv-radius-sm);
  appearance: none;
  background: var(--cv-surface-raised);
  color: var(--cv-text-primary);
  font-size: var(--cv-font-size-sm);
  transition:
    border-color var(--cv-motion-duration-fast) var(--cv-motion-ease-standard),
    box-shadow var(--cv-motion-duration-fast) var(--cv-motion-ease-standard);
}

.cv-search-field__control::-webkit-search-cancel-button { display: none; }
.cv-search-field__control:hover:not(:disabled) { border-color: var(--cv-control-border-hover); }
.cv-search-field__control:focus-visible { border-color: var(--cv-focus-ring-color); outline: 2px solid var(--cv-focus-ring-color); outline-offset: 1px; box-shadow: none; }
.cv-search-field__control::placeholder { color: var(--cv-text-muted); }

.cv-search-field__clear {
  position: absolute;
  top: 50%;
  right: 2px;
  display: grid;
  width: var(--cv-density-control-height-sm);
  height: var(--cv-density-control-height-sm);
  place-items: center;
  padding: 0;
  border: 0;
  border-radius: var(--cv-radius-sm);
  background: transparent;
  color: var(--cv-text-muted);
  cursor: pointer;
  transform: translateY(-50%);
}

.cv-search-field__clear:hover:not(:disabled) { background: var(--cv-interactive-hover); color: var(--cv-text-primary); }
.cv-search-field--disabled { opacity: 0.54; }
</style>
