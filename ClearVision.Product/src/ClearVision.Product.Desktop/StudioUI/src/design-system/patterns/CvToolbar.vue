<script setup lang="ts">
import { ref } from 'vue';

const props = withDefaults(defineProps<{
  label?: string;
  orientation?: 'horizontal' | 'vertical';
  wrap?: boolean;
}>(), {
  label: '页面工具栏',
  orientation: 'horizontal',
  wrap: true
});

const root = ref<HTMLElement>();
const focusableSelector = [
  'a[href]',
  'button:not([disabled])',
  'input:not([disabled])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  '[tabindex]:not([tabindex="-1"])'
].join(',');

function focusableElements(): HTMLElement[] {
  if (!root.value) return [];
  return [...root.value.querySelectorAll<HTMLElement>(focusableSelector)]
    .filter(element => element.getAttribute('aria-hidden') !== 'true' && !element.hasAttribute('hidden'));
}

function handleKeydown(event: KeyboardEvent): void {
  const eventTarget = event.target;
  if (!(eventTarget instanceof HTMLElement)) return;
  if (eventTarget.matches('input, textarea, select')) return;

  const previousKey = props.orientation === 'horizontal' ? 'ArrowLeft' : 'ArrowUp';
  const nextKey = props.orientation === 'horizontal' ? 'ArrowRight' : 'ArrowDown';
  if (![previousKey, nextKey, 'Home', 'End'].includes(event.key)) return;

  const elements = focusableElements();
  if (elements.length === 0) return;
  const activeElement = eventTarget.closest<HTMLElement>(focusableSelector) ?? eventTarget;
  const activeIndex = Math.max(0, elements.indexOf(activeElement));
  let nextIndex = activeIndex;
  if (event.key === previousKey) nextIndex = (activeIndex - 1 + elements.length) % elements.length;
  if (event.key === nextKey) nextIndex = (activeIndex + 1) % elements.length;
  if (event.key === 'Home') nextIndex = 0;
  if (event.key === 'End') nextIndex = elements.length - 1;
  event.preventDefault();
  elements[nextIndex]?.focus();
}
</script>

<template>
  <div
    ref="root"
    class="cv-toolbar"
    :class="[
      `cv-toolbar--${orientation}`,
      { 'cv-toolbar--wrap': wrap }
    ]"
    role="toolbar"
    :aria-label="label"
    :aria-orientation="orientation"
    data-design-pattern="toolbar"
    @keydown="handleKeydown"
  >
    <div class="cv-toolbar__primary">
      <slot />
    </div>
    <div
      v-if="$slots.secondary"
      class="cv-toolbar__secondary"
    >
      <slot name="secondary" />
    </div>
  </div>
</template>

<style scoped>
.cv-toolbar {
  display: flex;
  min-width: 0;
  align-items: center;
  justify-content: space-between;
  gap: var(--cv-space-3);
  padding: var(--cv-space-2) 0;
}

.cv-toolbar--wrap { flex-wrap: wrap; }
.cv-toolbar--vertical { align-items: stretch; flex-direction: column; }
.cv-toolbar__primary,
.cv-toolbar__secondary { display: flex; min-width: 0; align-items: center; gap: var(--cv-space-2); }
.cv-toolbar--wrap .cv-toolbar__primary,
.cv-toolbar--wrap .cv-toolbar__secondary { flex-wrap: wrap; }
.cv-toolbar__secondary { margin-left: auto; }
.cv-toolbar--vertical .cv-toolbar__secondary { margin-left: 0; }
</style>
