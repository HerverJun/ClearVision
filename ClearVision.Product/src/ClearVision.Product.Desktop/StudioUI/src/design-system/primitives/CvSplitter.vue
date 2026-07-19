<script setup lang="ts">
import { computed, onUnmounted, shallowRef } from 'vue';

const props = withDefaults(defineProps<{
  modelValue: number;
  min?: number;
  max?: number;
  step?: number;
  defaultValue?: number;
  orientation?: 'vertical' | 'horizontal';
  label?: string;
  valueText?: string | undefined;
  helpText?: string | undefined;
  reversed?: boolean;
  disabled?: boolean;
}>(), {
  min: 160,
  max: 560,
  step: 8,
  defaultValue: 300,
  orientation: 'vertical',
  label: '调整面板大小',
  valueText: undefined,
  helpText: undefined,
  reversed: false,
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
const dragging = shallowRef(false);
const resolvedValueText = computed(() => props.valueText ?? `${props.modelValue} 像素`);

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
  dragging.value = false;
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
  dragging.value = true;
  captureTarget?.setPointerCapture?.(event.pointerId);
  window.addEventListener('pointermove', handlePointerMove);
  window.addEventListener('pointerup', handlePointerEnd);
  window.addEventListener('pointercancel', handlePointerEnd);
  emit('resizeStart');
}

function handlePointerMove(event: PointerEvent): void {
  if (pointerId === null || event.pointerId !== pointerId) return;
  const direction = props.reversed ? -1 : 1;
  emit('update:modelValue', clamp(startValue + (coordinate(event) - startCoordinate) * direction));
}

function handlePointerEnd(event: PointerEvent): void {
  if (pointerId === null || event.pointerId !== pointerId) return;
  finishDrag(true);
}

function handleKeydown(event: KeyboardEvent): void {
  if (props.disabled) return;
  const multiplier = event.shiftKey ? 4 : 1;
  const delta = props.step * multiplier;
  const direction = props.reversed ? -1 : 1;
  let nextValue: number | undefined;

  if (event.key === 'Home') nextValue = props.min;
  if (event.key === 'End') nextValue = props.max;
  if (event.key === 'Enter') nextValue = props.defaultValue;
  if (props.orientation === 'vertical' && event.key === 'ArrowLeft') nextValue = props.modelValue - delta * direction;
  if (props.orientation === 'vertical' && event.key === 'ArrowRight') nextValue = props.modelValue + delta * direction;
  if (props.orientation === 'horizontal' && event.key === 'ArrowUp') nextValue = props.modelValue - delta * direction;
  if (props.orientation === 'horizontal' && event.key === 'ArrowDown') nextValue = props.modelValue + delta * direction;

  if (nextValue === undefined) return;
  event.preventDefault();
  emit('update:modelValue', clamp(nextValue));
  emit('resizeEnd');
}

function resetToDefault(): void {
  if (props.disabled) return;
  emit('update:modelValue', clamp(props.defaultValue));
  emit('resizeEnd');
}

onUnmounted(() => finishDrag(false));
</script>

<template>
  <div
    class="cv-splitter"
    :class="[
      `cv-splitter--${orientation}`,
      { 'cv-splitter--disabled': disabled, 'cv-splitter--dragging': dragging }
    ]"
    role="separator"
    :tabindex="disabled ? -1 : 0"
    :aria-label="label"
    :aria-orientation="orientation"
    :aria-valuemin="min"
    :aria-valuemax="max"
    :aria-valuenow="modelValue"
    :aria-valuetext="resolvedValueText"
    :aria-disabled="disabled ? 'true' : undefined"
    :title="helpText ?? `${label}；方向键微调，Shift 加速，Home/End 跳到边界，Enter 或双击恢复默认值`"
    data-design-primitive="splitter"
    @pointerdown="handlePointerDown"
    @keydown="handleKeydown"
    @dblclick="resetToDefault"
  >
    <span
      class="cv-splitter__grip"
      aria-hidden="true"
    />
  </div>
</template>

<style scoped>
.cv-splitter { position: relative; display: grid; flex: 0 0 auto; place-items: center; touch-action: none; user-select: none; color: var(--cv-border-strong); }
.cv-splitter::before { position: absolute; content: ''; border-radius: var(--cv-radius-pill); background: transparent; transition: background var(--cv-motion-duration-fast) var(--cv-motion-ease-standard); }
.cv-splitter:hover::before, .cv-splitter--dragging::before { background: color-mix(in srgb, var(--cv-focus-ring-color) 12%, transparent); }
.cv-splitter--vertical { width: 8px; cursor: col-resize; }
.cv-splitter--vertical::before { inset: 0 -2px; }
.cv-splitter--horizontal { height: 8px; cursor: row-resize; }
.cv-splitter--horizontal::before { inset: -2px 0; }
.cv-splitter__grip { position: relative; z-index: 1; border-radius: var(--cv-radius-pill); background: currentColor; }
.cv-splitter--vertical .cv-splitter__grip { width: 2px; height: 32px; }
.cv-splitter--horizontal .cv-splitter__grip { width: 32px; height: 2px; }
.cv-splitter:focus-visible { outline: none; color: var(--cv-focus-ring-color); }
.cv-splitter:focus-visible .cv-splitter__grip { box-shadow: 0 0 0 2px var(--cv-surface-page), 0 0 0 4px var(--cv-focus-ring-color); }
.cv-splitter--disabled { cursor: not-allowed; opacity: 0.42; }
</style>
