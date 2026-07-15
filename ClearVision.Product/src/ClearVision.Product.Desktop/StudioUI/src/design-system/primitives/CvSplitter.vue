<script setup lang="ts">
import { onUnmounted } from 'vue';

const props = withDefaults(defineProps<{
  modelValue: number;
  min?: number;
  max?: number;
  step?: number;
  orientation?: 'vertical' | 'horizontal';
  label?: string;
  disabled?: boolean;
}>(), {
  min: 160,
  max: 560,
  step: 8,
  orientation: 'vertical',
  label: 'Resize panel',
  disabled: false
});

const emit = defineEmits<{
  'update:modelValue': [value: number];
  resizeStart: [];
  resizeEnd: [];
}>();

let pointerId: number | null = null;
let startCoordinate = 0;
let startValue = 0;
let captureTarget: HTMLElement | null = null;

function clamp(value: number): number {
  return Math.min(props.max, Math.max(props.min, Math.round(value)));
}

function coordinate(event: PointerEvent): number {
  return props.orientation === 'vertical' ? event.clientX : event.clientY;
}

function removeWindowListeners(): void {
  window.removeEventListener('pointermove', handlePointerMove);
  window.removeEventListener('pointerup', handlePointerEnd);
  window.removeEventListener('pointercancel', handlePointerEnd);
}

function finishDrag(emitEvent: boolean): void {
  if (pointerId === null) return;
  if (captureTarget?.hasPointerCapture?.(pointerId)) captureTarget.releasePointerCapture(pointerId);
  pointerId = null;
  captureTarget = null;
  removeWindowListeners();
  if (emitEvent) emit('resizeEnd');
}

function handlePointerDown(event: PointerEvent): void {
  if (props.disabled || !event.isPrimary || event.button !== 0) return;
  event.preventDefault();
  finishDrag(false);
  pointerId = event.pointerId;
  startCoordinate = coordinate(event);
  startValue = props.modelValue;
  captureTarget = event.currentTarget instanceof HTMLElement ? event.currentTarget : null;
  captureTarget?.setPointerCapture?.(event.pointerId);
  window.addEventListener('pointermove', handlePointerMove);
  window.addEventListener('pointerup', handlePointerEnd);
  window.addEventListener('pointercancel', handlePointerEnd);
  emit('resizeStart');
}

function handlePointerMove(event: PointerEvent): void {
  if (pointerId === null || event.pointerId !== pointerId) return;
  emit('update:modelValue', clamp(startValue + coordinate(event) - startCoordinate));
}

function handlePointerEnd(event: PointerEvent): void {
  if (pointerId === null || event.pointerId !== pointerId) return;
  finishDrag(true);
}

function handleKeydown(event: KeyboardEvent): void {
  if (props.disabled) return;
  const multiplier = event.shiftKey ? 4 : 1;
  const delta = props.step * multiplier;
  let nextValue: number | undefined;

  if (event.key === 'Home') nextValue = props.min;
  if (event.key === 'End') nextValue = props.max;
  if (props.orientation === 'vertical' && event.key === 'ArrowLeft') nextValue = props.modelValue - delta;
  if (props.orientation === 'vertical' && event.key === 'ArrowRight') nextValue = props.modelValue + delta;
  if (props.orientation === 'horizontal' && event.key === 'ArrowUp') nextValue = props.modelValue - delta;
  if (props.orientation === 'horizontal' && event.key === 'ArrowDown') nextValue = props.modelValue + delta;

  if (nextValue === undefined) return;
  event.preventDefault();
  emit('update:modelValue', clamp(nextValue));
}

onUnmounted(() => finishDrag(false));
</script>

<template>
  <div
    class="cv-splitter"
    :class="[`cv-splitter--${orientation}`, { 'cv-splitter--disabled': disabled }]"
    role="separator"
    :tabindex="disabled ? -1 : 0"
    :aria-label="label"
    :aria-orientation="orientation"
    :aria-valuemin="min"
    :aria-valuemax="max"
    :aria-valuenow="modelValue"
    :aria-disabled="disabled ? 'true' : undefined"
    data-design-primitive="splitter"
    @pointerdown="handlePointerDown"
    @keydown="handleKeydown"
  >
    <span
      class="cv-splitter__grip"
      aria-hidden="true"
    />
  </div>
</template>

<style scoped>
.cv-splitter { position: relative; display: grid; flex: 0 0 auto; place-items: center; touch-action: none; color: var(--cv-border-strong); }
.cv-splitter::before { position: absolute; content: ''; border-radius: var(--cv-radius-pill); background: transparent; transition: background var(--cv-motion-duration-fast) var(--cv-motion-ease-standard); }
.cv-splitter:hover::before, .cv-splitter:focus-visible::before { background: color-mix(in srgb, var(--cv-focus-ring-color) 12%, transparent); }
.cv-splitter--vertical { width: 8px; cursor: col-resize; }
.cv-splitter--vertical::before { inset: 0 -2px; }
.cv-splitter--horizontal { height: 8px; cursor: row-resize; }
.cv-splitter--horizontal::before { inset: -2px 0; }
.cv-splitter__grip { position: relative; z-index: 1; border-radius: var(--cv-radius-pill); background: currentColor; }
.cv-splitter--vertical .cv-splitter__grip { width: 2px; height: 32px; }
.cv-splitter--horizontal .cv-splitter__grip { width: 32px; height: 2px; }
.cv-splitter:focus-visible { outline-offset: 1px; color: var(--cv-focus-ring-color); }
.cv-splitter--disabled { cursor: not-allowed; opacity: 0.42; }
</style>
