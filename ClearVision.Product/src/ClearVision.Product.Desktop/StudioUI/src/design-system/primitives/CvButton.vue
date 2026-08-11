<script setup lang="ts">
import { computed } from 'vue';
import type { CvButtonVariant } from './types';

const props = withDefaults(defineProps<{
  variant?: CvButtonVariant;
  size?: 'sm' | 'md';
  type?: 'button' | 'submit' | 'reset';
  disabled?: boolean;
  loading?: boolean;
  loadingLabel?: string;
  block?: boolean;
}>(), {
  variant: 'secondary',
  size: 'md',
  type: 'button',
  disabled: false,
  loading: false,
  loadingLabel: '加载中',
  block: false
});

const isDisabled = computed(() => props.disabled || props.loading);
</script>

<template>
  <button
    class="cv-button"
    :class="[
      `cv-button--${variant}`,
      `cv-button--${size}`,
      { 'cv-button--block': block, 'cv-button--loading': loading }
    ]"
    :type="type"
    :disabled="isDisabled"
    :aria-busy="loading ? 'true' : undefined"
    data-design-primitive="button"
  >
    <span
      v-if="loading"
      class="cv-button__spinner"
      aria-hidden="true"
    />
    <span
      v-if="$slots.leading && !loading"
      class="cv-button__icon"
      aria-hidden="true"
    >
      <slot name="leading" />
    </span>
    <span class="cv-button__label">
      <span class="cv-button__visual-label"><slot /></span>
      <span
        v-if="loading"
        class="cv-button__sr-only"
      >{{ loadingLabel }}</span>
    </span>
    <span
      v-if="$slots.trailing"
      class="cv-button__icon"
      aria-hidden="true"
    >
      <slot name="trailing" />
    </span>
  </button>
</template>

<style scoped>
.cv-button {
  display: inline-flex;
  min-width: 0;
  align-items: center;
  justify-content: center;
  gap: var(--cv-space-2);
  height: var(--cv-density-control-height);
  padding: 0 var(--cv-space-4);
  border: 1px solid transparent;
  border-radius: var(--cv-radius-sm);
  cursor: pointer;
  font-size: var(--cv-font-size-sm);
  font-weight: var(--cv-font-weight-medium);
  line-height: 1;
  touch-action: manipulation;
  transition:
    background var(--cv-motion-duration-fast) var(--cv-motion-ease-standard),
    border-color var(--cv-motion-duration-fast) var(--cv-motion-ease-standard),
    color var(--cv-motion-duration-fast) var(--cv-motion-ease-standard),
    box-shadow var(--cv-motion-duration-fast) var(--cv-motion-ease-standard);
}

.cv-button:focus-visible { outline: none; box-shadow: var(--cv-focus-ring); }
.cv-button:disabled { cursor: not-allowed; opacity: 0.52; }
.cv-button--sm { height: var(--cv-density-control-height-sm); padding-inline: var(--cv-space-3); }
.cv-button--block { width: 100%; }

.cv-button--primary {
  border-color: var(--cv-color-action);
  background: var(--cv-color-action);
  color: var(--cv-color-on-action);
  box-shadow: none;
  font-weight: var(--cv-font-weight-semibold);
}

.cv-button--primary:hover:not(:disabled) { border-color: var(--cv-color-action-hover); background: var(--cv-color-action-hover); }
.cv-button--primary:active:not(:disabled) { border-color: var(--cv-color-action-active); background: var(--cv-color-action-active); transform: translateY(1px); }

.cv-button--secondary {
  border-color: var(--cv-control-border);
  background: var(--cv-surface-raised);
  color: var(--cv-text-primary);
}

.cv-button--secondary:hover:not(:disabled) { border-color: var(--cv-control-border-hover); background: var(--cv-interactive-hover); }
.cv-button--secondary:active:not(:disabled) { background: var(--cv-interactive-active); }

.cv-button--quiet {
  background: transparent;
  color: var(--cv-text-secondary);
}

.cv-button--quiet:hover:not(:disabled) { background: var(--cv-interactive-hover); color: var(--cv-color-link); }
.cv-button--quiet:active:not(:disabled) { background: var(--cv-interactive-active); }

.cv-button--danger,
.cv-button--destructive {
  border-color: var(--cv-color-destructive-border);
  background: var(--cv-color-destructive-soft);
  color: var(--cv-color-destructive-strong);
}

.cv-button--danger:hover:not(:disabled),
.cv-button--destructive:hover:not(:disabled) { border-color: var(--cv-color-destructive); background: color-mix(in srgb, var(--cv-color-destructive-soft) 78%, var(--cv-color-destructive) 22%); }
.cv-button--danger:active:not(:disabled),
.cv-button--destructive:active:not(:disabled) { background: color-mix(in srgb, var(--cv-color-destructive-soft) 62%, var(--cv-color-destructive) 38%); }

.cv-button__spinner {
  width: 14px;
  height: 14px;
  border: 2px solid currentColor;
  border-right-color: transparent;
  border-radius: 50%;
  animation: cv-button-spin var(--cv-motion-duration-progress) linear infinite;
}

.cv-button__icon { display: inline-flex; }
.cv-button__icon :deep(svg) { width: var(--cv-density-icon-size); height: var(--cv-density-icon-size); }
.cv-button__label { min-width: 0; }
.cv-button__visual-label { white-space: nowrap; }
.cv-button__sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
}

@keyframes cv-button-spin { to { transform: rotate(360deg); } }
</style>
