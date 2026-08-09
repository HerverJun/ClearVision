<script setup lang="ts">
import { useId } from 'vue';

export interface CvViewTabOption {
  readonly value: string;
  readonly label: string;
  readonly description?: string;
  readonly id: string;
  readonly controls: string;
}

const props = defineProps<{
  modelValue: string;
  options: readonly CvViewTabOption[];
  label: string;
}>();

const emit = defineEmits<{
  'update:modelValue': [value: string];
}>();

const tabListId = useId();

function select(value: string): void {
  if (value !== props.modelValue) emit('update:modelValue', value);
}

function moveFocus(event: KeyboardEvent, direction: -1 | 1 | 'first' | 'last'): void {
  if (props.options.length === 0) return;
  const currentIndex = Math.max(0, props.options.findIndex(option => option.value === props.modelValue));
  const nextIndex = direction === 'first'
    ? 0
    : direction === 'last'
      ? props.options.length - 1
      : (currentIndex + direction + props.options.length) % props.options.length;
  const option = props.options[nextIndex];
  if (!option) return;
  event.preventDefault();
  select(option.value);
  const target = event.currentTarget as HTMLElement | null;
  target?.parentElement?.querySelectorAll<HTMLButtonElement>('[role="tab"]')[nextIndex]?.focus();
}
</script>

<template>
  <div
    :id="tabListId"
    class="cv-view-tabs"
    role="tablist"
    :aria-label="label"
    data-design-primitive="view-tabs"
  >
    <button
      v-for="option in options"
      :id="option.id"
      :key="option.value"
      type="button"
      class="cv-view-tabs__tab"
      :class="{ 'is-active': option.value === modelValue }"
      role="tab"
      :aria-controls="option.controls"
      :aria-selected="option.value === modelValue"
      :tabindex="option.value === modelValue ? 0 : -1"
      :title="option.description"
      @click="select(option.value)"
      @keydown.left="moveFocus($event, -1)"
      @keydown.right="moveFocus($event, 1)"
      @keydown.home="moveFocus($event, 'first')"
      @keydown.end="moveFocus($event, 'last')"
    >
      {{ option.label }}
    </button>
  </div>
</template>

<style scoped>
.cv-view-tabs {
  min-width: 0;
  display: flex;
  align-items: end;
  gap: var(--cv-space-4);
  border-bottom: 1px solid var(--cv-border-subtle);
}

.cv-view-tabs__tab {
  position: relative;
  min-height: 36px;
  padding: 0 var(--cv-space-1);
  border: 0;
  background: transparent;
  color: var(--cv-text-secondary);
  cursor: pointer;
  font: inherit;
  font-size: var(--cv-font-size-sm);
  font-weight: var(--cv-font-weight-medium);
  letter-spacing: 0;
  white-space: nowrap;
}

.cv-view-tabs__tab::after {
  position: absolute;
  right: 0;
  bottom: -1px;
  left: 0;
  height: 2px;
  background: transparent;
  content: '';
}

.cv-view-tabs__tab:hover { color: var(--cv-text-primary); }
.cv-view-tabs__tab.is-active { color: var(--cv-color-brand-500); }
.cv-view-tabs__tab.is-active::after { background: var(--cv-color-brand-500); }
.cv-view-tabs__tab:focus-visible { outline: 2px solid var(--cv-focus-ring-color); outline-offset: -2px; }

@media (prefers-reduced-motion: reduce) {
  .cv-view-tabs__tab { scroll-behavior: auto; }
}
</style>
