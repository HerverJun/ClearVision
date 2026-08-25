<script setup lang="ts">
import { CvIcon } from '@/design-system/icons';
import type { CvIconName } from '@/design-system/icons';
import { CvBrand } from '@/design-system/patterns';

withDefaults(defineProps<{
  title: string;
  description: string;
  variant?: 'form' | 'state';
  icon?: CvIconName | undefined;
}>(), {
  variant: 'form',
  icon: undefined
});
</script>

<template>
  <main
    class="auth-shell"
    :class="`auth-shell--${variant}`"
    data-auth-shell="ready"
    :data-auth-shell-variant="variant"
    aria-label="ClearVision Studio"
  >
    <header class="auth-shell__masthead">
      <CvBrand />
    </header>
    <section
      class="auth-shell__stage"
      aria-labelledby="auth-shell-title"
      aria-describedby="auth-shell-description"
    >
      <div class="auth-shell__frame">
        <div
          v-if="icon"
          class="auth-shell__state-icon"
          aria-hidden="true"
        >
          <CvIcon
            :name="icon"
            size="lg"
          />
        </div>
        <header class="auth-shell__header">
          <h1 id="auth-shell-title">
            {{ title }}
          </h1>
          <p
            id="auth-shell-description"
            class="auth-shell__description"
          >
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
  position: relative;
  width: 100%;
  min-height: 100vh;
  min-height: 100svh;
  overflow: hidden;
  background: var(--cv-auth-shell-surface);
  color: var(--cv-text-primary);
}

.auth-shell__masthead {
  position: absolute;
  z-index: 1;
  top: 34px;
  left: 28px;
  display: flex;
  align-items: center;
}

.auth-shell__masthead :deep(.cv-brand__mark) { width: 42px; height: 42px; }
.auth-shell__masthead :deep(.cv-brand__wordmark) { gap: 10px; }
.auth-shell__masthead :deep(.cv-brand__wordmark strong) { font-size: 24px; }
.auth-shell__masthead :deep(.cv-brand__wordmark small) { font-size: 16px; }

.auth-shell__stage {
  width: 100%;
  min-height: 100vh;
  min-height: 100svh;
  display: grid;
  place-items: safe center;
  overflow-x: hidden;
  overflow-y: auto;
  overscroll-behavior: contain;
  padding: var(--cv-space-8) var(--cv-space-6);
}

.auth-shell__frame {
  width: min(556px, 100%);
  padding: 54px 40px 44px;
  display: grid;
  gap: var(--cv-space-4);
  border: 1px solid var(--cv-border-default);
  border-radius: var(--cv-radius-md);
  background: var(--cv-surface-raised);
  box-shadow: var(--cv-elevation-2);
}

.auth-shell__header {
  display: grid;
  gap: var(--cv-space-3);
}

.auth-shell__header h1 {
  margin: 0;
  font-size: 34px;
  font-weight: var(--cv-font-weight-semibold);
  line-height: var(--cv-line-height-tight);
  letter-spacing: 0;
  text-wrap: balance;
}

.auth-shell__description {
  max-width: 42ch;
  margin: 0;
  color: var(--cv-text-secondary);
  font-size: var(--cv-font-size-sm);
  line-height: var(--cv-line-height-relaxed);
  text-wrap: pretty;
}

.auth-shell__form-host { min-width: 0; }

:deep(.auth-form) { display: grid; gap: var(--cv-space-10); }
:deep(.auth-form__field) { display: grid; gap: var(--cv-space-2); }
:deep(.auth-form__field label) {
  color: var(--cv-text-secondary);
  font-size: var(--cv-font-size-md);
  font-weight: var(--cv-font-weight-medium);
}
:deep(.auth-form__control) {
  width: 100%;
  min-height: 58px;
  padding: 0 var(--cv-space-4);
  border: 1px solid var(--cv-control-border);
  border-radius: 6px;
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
  min-height: 58px;
  border-radius: 6px;
  font-size: var(--cv-font-size-lg);
}
:deep(.auth-form__actions > a) { color: var(--cv-color-link); font-size: var(--cv-font-size-sm); text-decoration: none; }
:deep(.auth-form__actions > a) { justify-self: center; }
:deep(.auth-form__actions > a:hover) { text-decoration: underline; }

.auth-shell--state .auth-shell__masthead {
  position: relative;
  top: auto;
  left: auto;
  min-height: 78px;
  padding: 0 34px;
  border-bottom: 1px solid var(--cv-border-subtle);
  background: var(--cv-surface-raised);
}

.auth-shell--state .auth-shell__stage {
  min-height: calc(100vh - 78px);
  min-height: calc(100svh - 78px);
  place-items: start center;
  padding: 218px var(--cv-space-6) var(--cv-space-8);
}

.auth-shell--state .auth-shell__frame {
  width: min(752px, 100%);
  min-height: 448px;
  padding: 52px 62px 48px;
  align-content: start;
  gap: var(--cv-space-8);
}

.auth-shell__state-icon {
  width: 76px;
  height: 76px;
  display: grid;
  place-items: center;
  color: var(--cv-color-brand-500);
}

.auth-shell__state-icon :deep(.cv-icon) { width: 48px; height: 48px; }
.auth-shell--state .auth-shell__header { gap: var(--cv-space-4); }
.auth-shell--state .auth-shell__header h1 { font-size: 36px; }
.auth-shell--state .auth-shell__description { max-width: none; font-size: var(--cv-font-size-md); }
.auth-shell--state .auth-shell__form-host { margin-top: var(--cv-space-4); }
.auth-shell--state :deep(.auth-form) { gap: var(--cv-space-4); }
.auth-shell--state :deep(.auth-form__message) {
  padding: var(--cv-space-4) var(--cv-space-5);
  border-color: var(--cv-color-brand-border);
  background: var(--cv-color-brand-soft);
  color: var(--cv-text-secondary);
  font-size: var(--cv-font-size-md);
}
.auth-shell--state :deep(.auth-form__actions) { justify-items: start; }
.auth-shell--state :deep(.auth-form__actions > a) {
  min-height: 36px;
  display: inline-flex;
  align-items: center;
  gap: var(--cv-space-2);
  color: var(--cv-color-link);
  font-size: var(--cv-font-size-lg);
  font-weight: var(--cv-font-weight-semibold);
}

@media (max-height: 700px) {
  .auth-shell__stage { place-items: start center; padding-block: var(--cv-space-6); }
  .auth-shell__frame { padding-block: var(--cv-space-6); gap: var(--cv-space-5); }
  .auth-shell__header { gap: var(--cv-space-2); }
  .auth-shell__header h1 { font-size: var(--cv-font-size-2xl); }
  :deep(.auth-form) { gap: var(--cv-space-4); }
}

@media (max-height: 820px) {
  .auth-shell--state .auth-shell__stage {
    place-items: center;
    padding-block: var(--cv-space-6);
  }

  .auth-shell--state .auth-shell__frame {
    min-height: 0;
    padding-block: var(--cv-space-6);
    gap: var(--cv-space-5);
  }

  .auth-shell--state :deep(.auth-form) { gap: var(--cv-space-5); }
}

@media (max-width: 480px) {
  .auth-shell__masthead { top: var(--cv-space-5); left: var(--cv-space-5); }
  .auth-shell__stage { padding-inline: var(--cv-space-5); }
  .auth-shell__frame { padding-inline: var(--cv-space-5); gap: var(--cv-space-6); }
  .auth-shell__header h1 { font-size: var(--cv-font-size-2xl); }
  .auth-shell__description { font-size: var(--cv-font-size-md); }
}

@media (max-width: 720px) {
  .auth-shell--state .auth-shell__masthead { padding-inline: var(--cv-space-5); }
  .auth-shell--state .auth-shell__frame { min-height: 0; padding: var(--cv-space-8) var(--cv-space-5); }
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
