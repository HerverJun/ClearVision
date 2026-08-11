<script setup lang="ts">
import { CvBrand } from '@/design-system/patterns';
import workspacePreviewUrl from './auth-studio-workspace.png';
</script>

<template>
  <aside
    class="auth-product-preview"
    aria-hidden="true"
  >
    <header class="auth-product-preview__brand">
      <CvBrand />
    </header>
    <div class="auth-product-preview__media">
      <img
        class="auth-product-preview__image"
        :src="workspacePreviewUrl"
        alt=""
        width="1920"
        height="1080"
        decoding="sync"
        fetchpriority="high"
      >
    </div>
  </aside>
</template>

<style scoped>
.auth-product-preview {
  min-width: 0;
  min-height: 100vh;
  min-height: 100svh;
  display: grid;
  grid-template-rows: auto minmax(0, 1fr);
  gap: var(--cv-space-8);
  overflow: hidden;
  padding: 34px 42px 30px;
  background: var(--cv-shell-sidebar);
  color: var(--cv-shell-sidebar-text);
}

.auth-product-preview__brand {
  display: flex;
  align-items: center;
}

.auth-product-preview__brand :deep(.cv-brand__wordmark strong) { color: var(--cv-shell-sidebar-text); }
.auth-product-preview__brand :deep(.cv-brand__wordmark small) { color: var(--cv-shell-sidebar-muted); }

.auth-product-preview__media {
  align-self: center;
  justify-self: center;
  width: min(100%, 960px);
  aspect-ratio: 16 / 9;
  overflow: hidden;
  border-radius: var(--cv-radius-lg);
  background: var(--cv-shell-sidebar-surface);
  box-shadow: var(--cv-elevation-3);
}

.auth-product-preview__image {
  display: block;
  width: 100%;
  height: 100%;
  object-fit: cover;
  object-position: center;
  filter: saturate(0.86) brightness(0.92) contrast(1.03);
}

@media (max-width: 1180px) {
  .auth-product-preview { padding-inline: 28px; }
}

@media (max-height: 700px) {
  .auth-product-preview {
    gap: var(--cv-space-5);
    padding-block: var(--cv-space-6);
  }

  .auth-product-preview__media { width: min(100%, 800px); }
}
</style>
