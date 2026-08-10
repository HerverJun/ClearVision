<script setup lang="ts">
import { CvBrand } from '@/design-system/patterns';

defineProps<{
  title: string;
  description: string;
}>();
</script>

<template>
  <main
    class="auth-shell"
    data-auth-shell="ready"
  >
    <section
      class="auth-shell__stage"
      aria-labelledby="auth-shell-title"
    >
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
    </section>
  </main>
</template>

<style scoped>
.auth-shell {
  min-height: 100vh;
  min-height: 100svh;
  display: grid;
  place-items: center;
  padding: var(--cv-space-6);
  background: var(--cv-surface-page);
  color: var(--cv-text-primary);
}
.auth-shell__masthead {
  display: flex;
  justify-content: center;
  align-items: center;
  min-height: 56px;
}
.auth-shell__masthead :deep(.cv-brand__mark) {
  width: 52px;
  height: 52px;
}
.auth-shell__masthead :deep(.cv-brand) { gap: var(--cv-space-3); }
.auth-shell__masthead :deep(.cv-brand__wordmark) { gap: var(--cv-space-2); }
.auth-shell__masthead :deep(.cv-brand__wordmark strong) { font-size: var(--cv-font-size-lg); }
.auth-shell__masthead :deep(.cv-brand__wordmark small) { font-size: var(--cv-font-size-sm); }
.auth-shell__stage {
  width: min(440px, calc(100% - 32px));
  display: grid;
  gap: var(--cv-space-5);
  padding: var(--cv-space-10);
  border: 1px solid transparent;
  border-radius: var(--cv-radius-lg);
  background: var(--cv-surface-raised);
  box-shadow: var(--cv-elevation-2);
}
.auth-shell__header {
  display: grid;
  gap: var(--cv-space-2);
  justify-items: center;
  text-align: center;
}
.auth-shell__header h1 {
  margin: 0;
  font-size: var(--cv-font-size-2xl);
  font-weight: var(--cv-font-weight-semibold);
  line-height: var(--cv-line-height-tight);
  letter-spacing: 0;
}
.auth-shell__description {
  max-width: 36ch;
  margin: 0;
  color: var(--cv-text-secondary);
  font-size: var(--cv-font-size-md);
  line-height: var(--cv-line-height-relaxed);
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
  min-height: 46px;
  padding: 0 var(--cv-space-3);
  border: 1px solid var(--cv-control-border);
  border-radius: var(--cv-radius-sm);
  background: var(--cv-surface-raised);
  color: var(--cv-text-primary);
  font: inherit;
  font-size: var(--cv-font-size-md);
  transition:
    border-color var(--cv-motion-duration-fast) var(--cv-motion-ease-standard),
    box-shadow var(--cv-motion-duration-fast) var(--cv-motion-ease-standard);
}
:deep(.auth-form__control:hover) { border-color: var(--cv-control-border-hover); }
:deep(.auth-form__control:focus-visible) {
  border-color: var(--cv-focus-ring-color);
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
  width: var(--cv-control-check-size);
  height: var(--cv-control-check-size);
  flex: 0 0 auto;
  margin: 0;
  accent-color: var(--cv-color-action);
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
  min-height: 46px;
  border-radius: var(--cv-radius-md);
}
:deep(.auth-form__actions > a) { color: var(--cv-color-link); font-size: var(--cv-font-size-sm); text-decoration: none; }
:deep(.auth-form__actions > a) { justify-self: center; }
:deep(.auth-form__actions > a:hover) { text-decoration: underline; }
@media (max-width: 720px) {
  .auth-shell__stage {
    width: min(440px, calc(100% - 24px));
    padding: var(--cv-space-8);
  }
}
@media (max-height: 700px) {
  .auth-shell__stage { padding-block: var(--cv-space-6); }
}
@media (max-width: 420px) {
  .auth-shell { padding: var(--cv-space-3); }
  .auth-shell__masthead :deep(.cv-brand__mark) { width: 40px; height: 40px; }
  .auth-shell__stage { padding: var(--cv-space-6) var(--cv-space-5); }
}
</style>
