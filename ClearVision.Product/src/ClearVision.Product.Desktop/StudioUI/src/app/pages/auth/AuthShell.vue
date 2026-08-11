<script setup lang="ts">
import { CvBrand } from '@/design-system/patterns';
import AuthProductPreview from './AuthProductPreview.vue';

defineProps<{
  title: string;
  description: string;
}>();
</script>

<template>
  <main
    class="auth-shell"
    data-auth-shell="ready"
    aria-label="ClearVision Studio"
  >
    <AuthProductPreview />
    <section
      class="auth-shell__stage"
      aria-labelledby="auth-shell-title"
    >
      <div class="auth-shell__frame">
        <header class="auth-shell__masthead">
          <CvBrand />
        </header>
        <header class="auth-shell__header">
          <h1 id="auth-shell-title">
            {{ title }}
          </h1>
          <p class="auth-shell__description">
            {{ description }}
          </p>
        </header>
        <div class="auth-shell__form-host">
          <slot />
        </div>
      </div>
    </section>
  </main>
</template>

<style scoped>
.auth-shell {
  min-height: 100vh;
  min-height: 100svh;
  display: grid;
  grid-template-columns: minmax(0, 1.12fr) minmax(420px, 0.88fr);
  background: var(--cv-surface-page);
  color: var(--cv-text-primary);
}

.auth-shell__stage {
  min-width: 0;
  min-height: 100vh;
  min-height: 100svh;
  display: grid;
  place-items: center;
  overflow: auto;
  overscroll-behavior: contain;
  padding: var(--cv-space-12) clamp(32px, 5vw, 80px);
  background: var(--cv-surface-raised);
}

.auth-shell__frame {
  width: min(420px, 100%);
  display: grid;
  gap: var(--cv-space-8);
}

.auth-shell__masthead {
  display: none;
  align-items: center;
}

.auth-shell__header {
  display: grid;
  gap: var(--cv-space-3);
}

.auth-shell__header h1 {
  margin: 0;
  font-size: var(--cv-type-auth-title-size);
  font-weight: var(--cv-font-weight-semibold);
  line-height: var(--cv-line-height-tight);
  letter-spacing: 0;
  text-wrap: balance;
}

.auth-shell__description {
  max-width: 42ch;
  margin: 0;
  color: var(--cv-text-secondary);
  font-size: var(--cv-font-size-md);
  line-height: var(--cv-line-height-relaxed);
  text-wrap: pretty;
}

.auth-shell__form-host { min-width: 0; }

:deep(.auth-form) { display: grid; gap: var(--cv-space-5); }
:deep(.auth-form__field) { display: grid; gap: var(--cv-space-2); }
:deep(.auth-form__field label) {
  color: var(--cv-text-secondary);
  font-size: var(--cv-font-size-sm);
  font-weight: var(--cv-font-weight-medium);
}
:deep(.auth-form__control) {
  width: 100%;
  min-height: 44px;
  padding: 0 var(--cv-space-4);
  border: 1px solid var(--cv-control-border);
  border-radius: var(--cv-radius-md);
  background: var(--cv-surface-raised);
  color: var(--cv-text-primary);
  font: inherit;
  font-size: var(--cv-font-size-md);
  transition:
    border-color var(--cv-motion-duration-fast) var(--cv-motion-ease-standard),
    background var(--cv-motion-duration-fast) var(--cv-motion-ease-standard),
    box-shadow var(--cv-motion-duration-fast) var(--cv-motion-ease-standard);
}
:deep(.auth-form__control:hover) { border-color: var(--cv-control-border-hover); }
:deep(.auth-form__control:focus-visible) {
  border-color: var(--cv-focus-ring-color);
  background: var(--cv-surface-raised);
  outline: none;
  box-shadow: var(--cv-focus-ring);
}
:deep(.auth-form__password) { position: relative; }
:deep(.auth-form__password .auth-form__control) { padding-right: 48px; }
:deep(.auth-form__password-toggle) {
  position: absolute;
  top: 50%;
  right: 6px;
  transform: translateY(-50%);
}
:deep(.auth-form__password-toggle[aria-pressed="true"]) {
  background: var(--cv-interactive-active);
  color: var(--cv-color-link);
}
:deep(.auth-form__password-toggle svg) { width: 18px; height: 18px; }
:deep(.auth-form__message) {
  margin: 0;
  padding: var(--cv-space-3);
  border: 1px solid var(--cv-color-status-info);
  border-radius: var(--cv-radius-sm);
  background: var(--cv-color-status-info-soft);
  color: var(--cv-text-secondary);
  font-size: var(--cv-font-size-sm);
  line-height: 1.5;
}
:deep(.auth-form__message[data-tone="error"]) { border-color: var(--cv-color-status-error); background: var(--cv-color-status-error-soft); color: var(--cv-color-status-error-strong); }
:deep(.auth-form__message[data-tone="warning"]) { border-color: var(--cv-color-status-warning); background: var(--cv-color-status-warning-soft); color: var(--cv-color-status-warning-strong); }
:deep(.auth-form__message[data-tone="success"]) { border-color: var(--cv-color-status-ok); background: var(--cv-color-status-ok-soft); color: var(--cv-color-status-ok-strong); }
:deep(.auth-form__options) {
  min-height: 20px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--cv-space-3);
}
:deep(.auth-form__remember) {
  display: inline-flex;
  align-items: center;
  gap: var(--cv-space-2);
  color: var(--cv-text-secondary);
  font-size: var(--cv-font-size-sm);
  cursor: pointer;
  user-select: none;
}
:deep(.auth-form__remember input) {
  appearance: none;
  width: var(--cv-control-check-size);
  height: var(--cv-control-check-size);
  flex: 0 0 auto;
  display: grid;
  margin: 0;
  place-content: center;
  border: 1px solid var(--cv-control-border);
  border-radius: var(--cv-radius-xs);
  background: var(--cv-surface-page);
  transition:
    border-color var(--cv-motion-duration-fast) var(--cv-motion-ease-standard),
    background var(--cv-motion-duration-fast) var(--cv-motion-ease-standard);
}
:deep(.auth-form__remember input::before) {
  width: 8px;
  height: 4px;
  border-bottom: 2px solid var(--cv-color-on-action);
  border-left: 2px solid var(--cv-color-on-action);
  content: '';
  transform: translateY(-1px) rotate(-45deg) scale(0);
  transform-origin: center;
  transition: transform var(--cv-motion-duration-fast) var(--cv-motion-ease-emphasized);
}
:deep(.auth-form__remember input:checked) {
  border-color: var(--cv-color-action);
  background: var(--cv-color-action);
}
:deep(.auth-form__remember input:checked::before) {
  transform: translateY(-1px) rotate(-45deg) scale(1);
}
:deep(.auth-form__remember input:focus-visible) {
  outline: none;
  box-shadow: var(--cv-focus-ring);
}
:deep(.auth-form__actions) {
  display: grid;
  gap: var(--cv-space-3);
  align-items: center;
}
:deep(.auth-form__submit.cv-button) {
  width: 100%;
  min-height: 44px;
  border-radius: var(--cv-radius-md);
  font-size: var(--cv-font-size-md);
}
:deep(.auth-form__actions > a) { color: var(--cv-color-link); font-size: var(--cv-font-size-sm); text-decoration: none; }
:deep(.auth-form__actions > a) { justify-self: center; }
:deep(.auth-form__actions > a:hover) { text-decoration: underline; }

@media (max-width: 1080px) {
  .auth-shell__stage { padding-inline: 36px; }
}

@media (max-height: 700px) {
  .auth-shell__stage { place-items: start center; padding-block: var(--cv-space-6); }
  .auth-shell__frame { gap: var(--cv-space-5); }
  .auth-shell__header { gap: var(--cv-space-2); }
  .auth-shell__header h1 { font-size: var(--cv-font-size-2xl); }
  :deep(.auth-form) { gap: var(--cv-space-4); }
}

@media (max-width: 920px) {
  .auth-shell { grid-template-columns: minmax(0, 1fr); }
  .auth-shell > :deep(.auth-product-preview) { display: none; }
  .auth-shell__stage { padding: var(--cv-space-8); }
  .auth-shell__masthead { display: flex; }
  .auth-shell__masthead :deep(.cv-brand__mark) { width: 38px; height: 38px; }
}

@media (max-width: 480px) {
  .auth-shell__stage { place-items: start center; padding: var(--cv-space-6) var(--cv-space-5); }
  .auth-shell__frame { gap: var(--cv-space-6); }
  .auth-shell__header h1 { font-size: var(--cv-font-size-2xl); }
  .auth-shell__description { font-size: var(--cv-font-size-md); }
}

@media (forced-colors: active) {
  :deep(.auth-form__remember input) {
    appearance: auto;
    border: initial;
    background: initial;
    box-shadow: none;
  }

  :deep(.auth-form__remember input:focus-visible) {
    outline: 2px solid Highlight;
    outline-offset: 2px;
  }

  :deep(.auth-form__remember input::before) { content: none; }
}
</style>
