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
      <div class="auth-product-preview__object">
        <span>当前工程</span>
        <strong>瓶盖检测 A · 流程编辑</strong>
      </div>
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
    <footer class="auth-product-preview__status">
      <span><strong>ROI Rectangle</strong> · 节点 1 · 连线 0</span>
      <span class="auth-product-preview__ready">预览完成</span>
      <span>已保存 · 正式运行就绪</span>
    </footer>
  </aside>
</template>

<style scoped>
.auth-product-preview {
  min-width: 0;
  min-height: 100vh;
  min-height: 100svh;
  display: grid;
  grid-template-rows: 64px minmax(0, 1fr) 44px;
  overflow: hidden;
  border-right: 1px solid var(--cv-border-subtle);
  background: var(--cv-surface-canvas);
  color: var(--cv-text-primary);
}

.auth-product-preview__brand {
  display: flex;
  min-width: 0;
  align-items: center;
  justify-content: space-between;
  gap: var(--cv-space-6);
  padding: 0 var(--cv-space-6);
  border-bottom: 1px solid var(--cv-border-subtle);
  background: var(--cv-surface-raised);
}

.auth-product-preview__object { display: grid; min-width: 0; justify-items: end; gap: var(--cv-space-1); }
.auth-product-preview__object span { color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); }
.auth-product-preview__object strong { max-width: 360px; overflow: hidden; font-size: var(--cv-font-size-sm); text-overflow: ellipsis; white-space: nowrap; }

.auth-product-preview__media {
  min-width: 0;
  min-height: 0;
  overflow: hidden;
  background: var(--cv-surface-canvas);
}

.auth-product-preview__image {
  display: block;
  width: 100%;
  height: 100%;
  object-fit: cover;
  object-position: center top;
}

.auth-product-preview__status {
  display: flex;
  min-width: 0;
  align-items: center;
  gap: var(--cv-space-5);
  padding: 0 var(--cv-space-6);
  border-top: 1px solid var(--cv-border-subtle);
  background: var(--cv-surface-raised);
  color: var(--cv-text-secondary);
  font-size: var(--cv-font-size-xs);
}
.auth-product-preview__status > span:first-child { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.auth-product-preview__status > span:last-child { margin-left: auto; white-space: nowrap; }
.auth-product-preview__ready { color: var(--cv-color-status-ok-strong); font-weight: var(--cv-font-weight-semibold); white-space: nowrap; }

@media (max-width: 1180px) {
  .auth-product-preview__object { display: none; }
}

@media (max-height: 700px) {
  .auth-product-preview { grid-template-rows: 54px minmax(0, 1fr) 36px; }
  .auth-product-preview__status { gap: var(--cv-space-3); }
}
</style>
