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
    <section class="auth-shell__card">
      <header class="auth-shell__header">
        <CvBrand compact />
        <div>
          <p>ClearVision Studio</p>
          <h1>{{ title }}</h1>
        </div>
      </header>
      <p class="auth-shell__description">
        {{ description }}
      </p>
      <slot />
    </section>
  </main>
</template>

<style scoped>
.auth-shell { min-height: 100vh; display: grid; place-items: center; padding: var(--cv-space-6); background: var(--cv-surface-app); }
.auth-shell__card { width: min(440px, 100%); display: grid; gap: var(--cv-space-5); padding: clamp(24px, 4vw, 36px); border: 1px solid var(--cv-border-subtle); border-radius: var(--cv-radius-lg); background: var(--cv-surface-1); box-shadow: var(--cv-elevation-1); }
.auth-shell__header { display: flex; align-items: center; gap: var(--cv-space-4); }
.auth-shell__header p { margin: 0 0 4px; color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); }
.auth-shell__header h1 { margin: 0; font-size: var(--cv-font-size-2xl); letter-spacing: var(--cv-letter-spacing-title); }
.auth-shell__description { margin: 0; color: var(--cv-text-secondary); line-height: 1.6; }
:deep(.auth-form) { display: grid; gap: var(--cv-space-4); }
:deep(.auth-form__field) { display: grid; gap: var(--cv-space-2); }
:deep(.auth-form__field label) { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); font-weight: 600; }
:deep(.auth-form__control) { width: 100%; min-height: 42px; padding: 0 var(--cv-space-3); border: 1px solid var(--cv-control-border); border-radius: var(--cv-radius-sm); background: var(--cv-surface-raised); transition: border-color var(--cv-motion-duration-fast) var(--cv-motion-ease-standard), outline-color var(--cv-motion-duration-fast) var(--cv-motion-ease-standard); }
:deep(.auth-form__control:hover) { border-color: var(--cv-control-border-hover); }
:deep(.auth-form__control:focus-visible) { border-color: var(--cv-focus-ring-color); outline: 2px solid var(--cv-focus-ring-color); outline-offset: 1px; }
:deep(.auth-form__password) { display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: var(--cv-space-2); }
:deep(.auth-form__message) { margin: 0; padding: var(--cv-space-3); border: 1px solid var(--cv-border-default); border-radius: var(--cv-radius-sm); background: var(--cv-surface-2); color: var(--cv-text-secondary); line-height: 1.5; }
:deep(.auth-form__actions) { display: flex; flex-wrap: wrap; gap: var(--cv-space-3); align-items: center; }
@media (max-width: 520px) { .auth-shell { padding: var(--cv-space-3); align-items: start; } .auth-shell__card { margin-top: var(--cv-space-4); } }
</style>
