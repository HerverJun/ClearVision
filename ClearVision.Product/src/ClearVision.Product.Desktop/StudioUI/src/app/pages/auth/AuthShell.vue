<script setup lang="ts">
import { CvBrand } from '@/design-system/patterns';
import { CvIcon } from '@/design-system/icons';

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
    <header class="auth-shell__masthead">
      <CvBrand />
      <span>工业视觉工程工作站</span>
    </header>

    <section
      class="auth-shell__stage"
      aria-labelledby="auth-shell-title"
    >
      <div class="auth-shell__identity">
        <CvBrand class="auth-shell__identity-brand" />
        <p class="auth-shell__edition">
          Studio 工程端
        </p>
        <dl class="auth-shell__trust">
          <div>
            <dt><CvIcon name="lock" /> 会话</dt>
            <dd>本机受控</dd>
          </div>
          <div>
            <dt><CvIcon name="server" /> 服务</dt>
            <dd>登录后确认</dd>
          </div>
        </dl>
      </div>

      <div class="auth-shell__panel">
        <header class="auth-shell__header">
          <p>账户访问</p>
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
  display: grid;
  grid-template-rows: auto minmax(0, 1fr);
  background: var(--cv-surface-app);
  color: var(--cv-text-primary);
}
.auth-shell__masthead {
  min-height: 58px;
  padding: 0 var(--cv-space-6);
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--cv-space-4);
  border-bottom: 1px solid var(--cv-border-subtle);
  background: var(--cv-surface-page);
}
.auth-shell__masthead > span { color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); }
.auth-shell__stage {
  width: min(900px, calc(100% - 48px));
  min-height: min(520px, calc(100vh - 90px));
  margin: auto;
  display: grid;
  grid-template-columns: minmax(250px, .78fr) minmax(380px, 1.22fr);
  overflow: hidden;
  border: 1px solid var(--cv-border-default);
  border-radius: var(--cv-radius-lg);
  background: var(--cv-surface-page);
  box-shadow: var(--cv-elevation-1);
}
.auth-shell__identity {
  padding: 44px 36px;
  display: flex;
  flex-direction: column;
  border-right: 1px solid var(--cv-border-subtle);
  background: var(--cv-surface-sunken);
}
.auth-shell__identity-brand { margin-bottom: var(--cv-space-3); }
.auth-shell__identity-brand :deep(.cv-brand__mark) { width: 48px; height: 48px; }
.auth-shell__identity-brand :deep(.cv-brand__wordmark) { display: none; }
.auth-shell__edition { margin: 0; color: var(--cv-text-primary); font-size: var(--cv-font-size-lg); font-weight: var(--cv-font-weight-semibold); }
.auth-shell__trust { margin: auto 0 0; display: grid; gap: var(--cv-space-3); }
.auth-shell__trust div { padding-top: var(--cv-space-3); border-top: 1px solid var(--cv-border-subtle); }
.auth-shell__trust dt { display: flex; align-items: center; gap: var(--cv-space-2); color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); }
.auth-shell__trust dt :deep(svg) { width: 14px; height: 14px; }
.auth-shell__trust dd { margin: var(--cv-space-1) 0 0; color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); font-weight: var(--cv-font-weight-medium); }
.auth-shell__panel { min-width: 0; padding: 44px; display: grid; align-content: center; gap: var(--cv-space-5); }
.auth-shell__header { display: grid; gap: var(--cv-space-2); }
.auth-shell__header > p:first-child { margin: 0; color: var(--cv-color-link); font-size: var(--cv-font-size-xs); font-weight: var(--cv-font-weight-semibold); }
.auth-shell__header h1 { margin: 0; font-size: var(--cv-font-size-2xl); line-height: var(--cv-line-height-tight); letter-spacing: 0; }
.auth-shell__description { margin: 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-sm); line-height: 1.6; }
.auth-shell__form-host { min-width: 0; }
:deep(.auth-form) { display: grid; gap: var(--cv-space-4); }
:deep(.auth-form__field) { display: grid; gap: var(--cv-space-2); }
:deep(.auth-form__field label) { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); font-weight: 600; }
:deep(.auth-form__control) { width: 100%; min-height: 42px; padding: 0 var(--cv-space-3); border: 1px solid var(--cv-control-border); border-radius: var(--cv-radius-sm); background: var(--cv-surface-raised); transition: border-color var(--cv-motion-duration-fast) var(--cv-motion-ease-standard), outline-color var(--cv-motion-duration-fast) var(--cv-motion-ease-standard); }
:deep(.auth-form__control:hover) { border-color: var(--cv-control-border-hover); }
:deep(.auth-form__control:focus-visible) { border-color: var(--cv-focus-ring-color); outline: 2px solid var(--cv-focus-ring-color); outline-offset: 1px; }
:deep(.auth-form__password) { display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: var(--cv-space-2); }
:deep(.auth-form__message) { margin: 0; padding: var(--cv-space-3); border: 1px solid var(--cv-color-status-info); border-radius: var(--cv-radius-sm); background: var(--cv-color-status-info-soft); color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); line-height: 1.5; }
:deep(.auth-form__message[data-tone="error"]) { border-color: var(--cv-color-status-error); background: var(--cv-color-status-error-soft); color: var(--cv-color-status-error-strong); }
:deep(.auth-form__message[data-tone="warning"]) { border-color: var(--cv-color-status-warning); background: var(--cv-color-status-warning-soft); color: var(--cv-color-status-warning-strong); }
:deep(.auth-form__message[data-tone="success"]) { border-color: var(--cv-color-status-ok); background: var(--cv-color-status-ok-soft); color: var(--cv-color-status-ok-strong); }
:deep(.auth-form__actions) { display: flex; flex-wrap: wrap; gap: var(--cv-space-3); align-items: center; }
:deep(.auth-form__actions > a) { color: var(--cv-color-link); font-size: var(--cv-font-size-sm); text-decoration: none; }
:deep(.auth-form__actions > a:hover) { text-decoration: underline; }
@media (max-width: 720px) {
  .auth-shell__masthead { padding-inline: var(--cv-space-4); }
  .auth-shell__masthead > span { display: none; }
  .auth-shell__stage { width: min(520px, calc(100% - 24px)); min-height: 0; margin-block: var(--cv-space-4); grid-template-columns: minmax(0, 1fr); }
  .auth-shell__identity { padding: var(--cv-space-4) var(--cv-space-5); flex-direction: row; align-items: center; gap: var(--cv-space-3); border-right: 0; border-bottom: 1px solid var(--cv-border-subtle); }
  .auth-shell__identity-brand { margin: 0; }
  .auth-shell__identity-brand :deep(.cv-brand__mark) { width: 34px; height: 34px; }
  .auth-shell__trust { display: none; }
  .auth-shell__panel { padding: var(--cv-space-5); }
}
@media (max-height: 700px) and (min-width: 721px) {
  .auth-shell__stage { margin-block: var(--cv-space-3); }
  .auth-shell__identity,
  .auth-shell__panel { padding-block: var(--cv-space-5); }
}
@media (max-width: 420px) {
  .auth-shell__panel { padding: var(--cv-space-4); }
  :deep(.auth-form__password) { grid-template-columns: minmax(0, 1fr); }
  :deep(.auth-form__actions) { align-items: stretch; flex-direction: column; }
  :deep(.auth-form__actions .cv-button) { width: 100%; }
}
</style>
