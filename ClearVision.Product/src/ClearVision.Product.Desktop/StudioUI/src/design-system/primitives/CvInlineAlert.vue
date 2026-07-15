<script setup lang="ts">
import { computed } from 'vue';
import CvIcon from '../icons/CvIcon.vue';
import type { CvIconName } from '../icons/types';
import type { CvInlineAlertTone } from './types';

const props = withDefaults(defineProps<{
  tone?: CvInlineAlertTone;
  title?: string | undefined;
  dismissible?: boolean;
  closeLabel?: string;
}>(), {
  tone: 'info',
  title: undefined,
  dismissible: false,
  closeLabel: '关闭提示'
});

const emit = defineEmits<{
  dismiss: [];
}>();

const iconName = computed<CvIconName>(() => {
  if (props.tone === 'success') return 'success';
  if (props.tone === 'warning') return 'warning';
  if (props.tone === 'error') return 'error';
  return 'info';
});
</script>

<template>
  <aside
    class="cv-inline-alert"
    :class="`cv-inline-alert--${tone}`"
    :role="tone === 'error' ? 'alert' : 'status'"
    :aria-live="tone === 'error' ? 'assertive' : 'polite'"
    data-design-primitive="inline-alert"
  >
    <CvIcon
      class="cv-inline-alert__icon"
      :name="iconName"
    />
    <div class="cv-inline-alert__content">
      <strong
        v-if="title"
        class="cv-inline-alert__title"
      >{{ title }}</strong>
      <div class="cv-inline-alert__message">
        <slot />
      </div>
      <div
        v-if="$slots.actions"
        class="cv-inline-alert__actions"
      >
        <slot name="actions" />
      </div>
    </div>
    <button
      v-if="dismissible"
      class="cv-inline-alert__close"
      type="button"
      :aria-label="closeLabel"
      @click="emit('dismiss')"
    >
      <CvIcon
        name="close"
        size="sm"
      />
    </button>
  </aside>
</template>

<style scoped>
.cv-inline-alert {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr) auto;
  align-items: start;
  gap: var(--cv-space-3);
  padding: var(--cv-space-3);
  border: 1px solid var(--cv-color-status-info-border);
  border-radius: var(--cv-radius-sm);
  background: var(--cv-color-status-info-soft);
  color: var(--cv-color-status-info-strong);
}

.cv-inline-alert--success { border-color: var(--cv-color-status-ok-border); background: var(--cv-color-status-ok-soft); color: var(--cv-color-status-ok-strong); }
.cv-inline-alert--warning { border-color: var(--cv-color-status-warning-border); background: var(--cv-color-status-warning-soft); color: var(--cv-color-status-warning-strong); }
.cv-inline-alert--error { border-color: var(--cv-color-status-ng-border); background: var(--cv-color-status-ng-soft); color: var(--cv-color-status-ng-strong); }
.cv-inline-alert__icon { margin-top: 1px; }
.cv-inline-alert__content { min-width: 0; }
.cv-inline-alert__title { display: block; font-size: var(--cv-font-size-sm); line-height: var(--cv-line-height-tight); }
.cv-inline-alert__message { color: var(--cv-text-primary); font-size: var(--cv-font-size-xs); line-height: var(--cv-line-height-normal); }
.cv-inline-alert__title + .cv-inline-alert__message { margin-top: var(--cv-space-1); }
.cv-inline-alert__actions { display: flex; flex-wrap: wrap; gap: var(--cv-space-2); margin-top: var(--cv-space-2); }

.cv-inline-alert__close {
  display: grid;
  width: var(--cv-density-control-height-sm);
  height: var(--cv-density-control-height-sm);
  place-items: center;
  padding: 0;
  border: 0;
  border-radius: var(--cv-radius-sm);
  background: transparent;
  color: currentColor;
  cursor: pointer;
}

.cv-inline-alert__close:hover { background: var(--cv-interactive-hover); }
</style>
