<script setup lang="ts">
import type { CvStatusTone } from './types';

withDefaults(defineProps<{
  tone?: CvStatusTone;
  label?: string | undefined;
  dot?: boolean;
}>(), {
  tone: 'idle',
  label: undefined,
  dot: true
});
</script>

<template>
  <span
    class="cv-status-badge"
    :class="`cv-status-badge--${tone}`"
    :data-status-tone="tone"
    data-design-primitive="status-badge"
  >
    <span
      v-if="dot"
      class="cv-status-badge__dot"
      aria-hidden="true"
    />
    <span><slot>{{ label }}</slot></span>
  </span>
</template>

<style scoped>
.cv-status-badge {
  display: inline-flex;
  align-items: center;
  gap: var(--cv-space-2);
  min-height: 24px;
  padding: 2px var(--cv-space-2);
  border: 1px solid var(--cv-border-default);
  border-radius: var(--cv-radius-pill);
  background: var(--cv-color-status-idle-soft);
  color: var(--cv-text-secondary);
  font-size: var(--cv-font-size-2xs);
  font-weight: var(--cv-font-weight-semibold);
  line-height: var(--cv-line-height-tight);
  white-space: nowrap;
}
.cv-status-badge__dot { width: 7px; height: 7px; border-radius: 50%; background: var(--cv-color-status-idle); }
.cv-status-badge--ok { border-color: var(--cv-color-status-ok-border); background: var(--cv-color-status-ok-soft); color: var(--cv-color-status-ok-strong); }
.cv-status-badge--ok .cv-status-badge__dot { background: var(--cv-color-status-ok); }
.cv-status-badge--ng { border-color: var(--cv-color-status-ng-border); background: var(--cv-color-status-ng-soft); color: var(--cv-color-status-ng-strong); }
.cv-status-badge--ng .cv-status-badge__dot { background: var(--cv-color-status-ng); }
.cv-status-badge--warning { border-color: var(--cv-color-status-warning-border); background: var(--cv-color-status-warning-soft); color: var(--cv-color-status-warning-strong); }
.cv-status-badge--warning .cv-status-badge__dot { background: var(--cv-color-status-warning); }
.cv-status-badge--info { border-color: var(--cv-color-status-info-border); background: var(--cv-color-status-info-soft); color: var(--cv-color-status-info-strong); }
.cv-status-badge--info .cv-status-badge__dot { background: var(--cv-color-status-info); }
</style>
