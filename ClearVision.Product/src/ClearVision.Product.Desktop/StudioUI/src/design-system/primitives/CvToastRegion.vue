<script setup lang="ts">
import { onUnmounted, watch } from 'vue';
import type { CvToastItem } from './types';

const props = withDefaults(defineProps<{
  toasts: readonly CvToastItem[];
  defaultDurationMs?: number;
  label?: string;
}>(), {
  defaultDurationMs: 4500,
  label: '通知'
});

const emit = defineEmits<{
  dismiss: [id: string];
}>();

const timers = new Map<string, number>();

function clearTimer(id: string): void {
  const timer = timers.get(id);
  if (timer === undefined) return;
  window.clearTimeout(timer);
  timers.delete(id);
}

function durationFor(toast: CvToastItem): number {
  return Math.max(0, toast.durationMs ?? props.defaultDurationMs);
}

function schedule(toast: CvToastItem): void {
  clearTimer(toast.id);
  const duration = durationFor(toast);
  if (duration === 0) return;

  const timer = window.setTimeout(() => {
    timers.delete(toast.id);
    emit('dismiss', toast.id);
  }, duration);
  timers.set(toast.id, timer);
}

function synchronizeTimers(): void {
  const activeIds = new Set(props.toasts.map(toast => toast.id));
  for (const id of [...timers.keys()]) {
    if (!activeIds.has(id)) clearTimer(id);
  }
  for (const toast of props.toasts) schedule(toast);
}

function resume(id: string): void {
  const toast = props.toasts.find(item => item.id === id);
  if (toast) schedule(toast);
}

watch(
  () => props.toasts.map(toast => `${toast.id}:${toast.durationMs ?? props.defaultDurationMs}`),
  synchronizeTimers,
  { immediate: true }
);

onUnmounted(() => {
  for (const id of [...timers.keys()]) clearTimer(id);
});
</script>

<template>
  <aside
    class="cv-toast-region"
    :aria-label="label"
    aria-live="polite"
    aria-relevant="additions removals"
    data-design-primitive="toast"
  >
    <TransitionGroup name="cv-toast-list">
      <article
        v-for="toast in toasts"
        :key="toast.id"
        class="cv-toast"
        :class="`cv-toast--${toast.tone ?? 'info'}`"
        :role="toast.tone === 'ng' || toast.tone === 'error' ? 'alert' : 'status'"
        :data-toast-id="toast.id"
        @mouseenter="clearTimer(toast.id)"
        @mouseleave="resume(toast.id)"
        @focusin="clearTimer(toast.id)"
        @focusout="resume(toast.id)"
      >
        <span
          class="cv-toast__indicator"
          aria-hidden="true"
        />
        <div class="cv-toast__content">
          <strong class="cv-toast__title">{{ toast.title }}</strong>
          <p
            v-if="toast.message"
            class="cv-toast__message"
          >
            {{ toast.message }}
          </p>
        </div>
        <button
          type="button"
          class="cv-toast__close"
          :aria-label="`关闭通知：${toast.title}`"
          @click="emit('dismiss', toast.id)"
        >
          ×
        </button>
      </article>
    </TransitionGroup>
  </aside>
</template>

<style scoped>
.cv-toast-region { position: fixed; z-index: var(--cv-z-toast); top: var(--cv-space-4); right: var(--cv-space-4); display: grid; width: min(380px, calc(100vw - (2 * var(--cv-space-4)))); gap: var(--cv-space-2); pointer-events: none; }
.cv-toast { display: grid; grid-template-columns: 4px minmax(0, 1fr) auto; overflow: hidden; border: 0; border-radius: var(--cv-radius-md); background: var(--cv-surface-overlay); box-shadow: var(--cv-elevation-3); pointer-events: auto; }
.cv-toast__indicator { background: var(--cv-color-status-info); }
.cv-toast--ok .cv-toast__indicator { background: var(--cv-color-status-ok); }
.cv-toast--ng .cv-toast__indicator { background: var(--cv-color-status-ng); }
.cv-toast--error .cv-toast__indicator { background: var(--cv-color-status-error); }
.cv-toast--warning .cv-toast__indicator { background: var(--cv-color-status-warning); }
.cv-toast--idle .cv-toast__indicator { background: var(--cv-color-status-idle); }
.cv-toast__content { min-width: 0; padding: var(--cv-space-3) var(--cv-space-4); }
.cv-toast__title { display: block; color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); line-height: var(--cv-line-height-tight); }
.cv-toast__message { margin: var(--cv-space-1) 0 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); line-height: var(--cv-line-height-normal); }
.cv-toast__close { align-self: start; width: var(--cv-density-control-height-sm); height: var(--cv-density-control-height-sm); margin: var(--cv-space-2); padding: 0; border: 0; border-radius: var(--cv-radius-sm); background: transparent; color: var(--cv-text-muted); cursor: pointer; font-size: 18px; }
.cv-toast__close:hover { background: var(--cv-interactive-hover); color: var(--cv-text-primary); }
.cv-toast-list-enter-active, .cv-toast-list-leave-active { transition: opacity var(--cv-motion-duration-normal) var(--cv-motion-ease-standard), transform var(--cv-motion-duration-normal) var(--cv-motion-ease-emphasized); }
.cv-toast-list-enter-from, .cv-toast-list-leave-to { opacity: 0; transform: translateX(var(--cv-motion-distance)); }
</style>
