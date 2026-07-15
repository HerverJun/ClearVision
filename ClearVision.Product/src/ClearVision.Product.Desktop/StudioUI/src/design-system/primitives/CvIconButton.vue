<script setup lang="ts">
import { computed } from 'vue';

const props = withDefaults(defineProps<{
  label: string;
  title?: string | undefined;
  type?: 'button' | 'submit' | 'reset';
  variant?: 'secondary' | 'quiet';
  size?: 'sm' | 'md';
  disabled?: boolean;
  loading?: boolean;
}>(), {
  title: undefined,
  type: 'button',
  variant: 'quiet',
  size: 'md',
  disabled: false,
  loading: false
});

const isDisabled = computed(() => props.disabled || props.loading);
</script>

<template>
  <button
    class="cv-icon-button"
    :class="[`cv-icon-button--${variant}`, `cv-icon-button--${size}`]"
    :type="type"
    :disabled="isDisabled"
    :aria-label="label"
    :aria-busy="loading ? 'true' : undefined"
    :title="title ?? label"
    data-design-primitive="icon-button"
  >
    <span
      v-if="loading"
      class="cv-icon-button__spinner"
      aria-hidden="true"
    />
    <span
      v-else
      class="cv-icon-button__icon"
      aria-hidden="true"
    >
      <slot />
    </span>
  </button>
</template>

<style scoped>
.cv-icon-button {
  display: inline-grid;
  width: var(--cv-density-control-height);
  height: var(--cv-density-control-height);
  place-items: center;
  padding: 0;
  border: 1px solid transparent;
  border-radius: var(--cv-radius-sm);
  background: transparent;
  color: var(--cv-text-secondary);
  cursor: pointer;
  transition:
    background var(--cv-motion-duration-fast) var(--cv-motion-ease-standard),
    color var(--cv-motion-duration-fast) var(--cv-motion-ease-standard),
    transform var(--cv-motion-duration-instant) var(--cv-motion-ease-standard);
}

.cv-icon-button--sm {
  width: var(--cv-density-control-height-sm);
  height: var(--cv-density-control-height-sm);
}

.cv-icon-button--secondary { border-color: var(--cv-border-default); background: var(--cv-surface-1); }
.cv-icon-button:hover:not(:disabled) { background: var(--cv-interactive-hover); color: var(--cv-color-link); transform: translateY(-1px); }
.cv-icon-button:active:not(:disabled) { background: var(--cv-interactive-active); transform: translateY(0); }
.cv-icon-button:disabled { cursor: not-allowed; opacity: 0.48; }
.cv-icon-button__icon { display: inline-flex; }
.cv-icon-button__icon :deep(svg) { width: var(--cv-density-icon-size); height: var(--cv-density-icon-size); }
.cv-icon-button__spinner {
  width: 14px;
  height: 14px;
  border: 2px solid currentColor;
  border-right-color: transparent;
  border-radius: 50%;
  animation: cv-icon-button-spin var(--cv-motion-duration-slow) linear infinite;
}

@keyframes cv-icon-button-spin { to { transform: rotate(360deg); } }
</style>
