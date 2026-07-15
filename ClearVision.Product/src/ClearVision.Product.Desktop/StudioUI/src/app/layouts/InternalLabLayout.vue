<script setup lang="ts">
import { RouterLink, RouterView } from 'vue-router';
</script>

<template>
  <div
    class="internal-lab-layout"
    data-internal-lab-layout="ready"
  >
    <header>
      <div>
        <strong>ClearVision 内部实验室</strong>
        <span>技术验证路由，不属于正式产品导航</span>
      </div>
      <RouterLink to="/overview">
        返回产品概览
      </RouterLink>
    </header>
    <RouterView />
  </div>
</template>

<style scoped>
.internal-lab-layout { min-height: 100vh; background: var(--cv-surface-app); }
.internal-lab-layout > header {
  min-height: 44px;
  padding: var(--cv-space-2) var(--cv-space-4);
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--cv-space-3);
  border-bottom: 1px solid var(--cv-border-default);
  background: var(--cv-surface-1);
}
.internal-lab-layout > header div { display: flex; align-items: baseline; gap: var(--cv-space-3); }
.internal-lab-layout > header strong { font-size: var(--cv-font-size-sm); }
.internal-lab-layout > header span { color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); }
.internal-lab-layout > header a { font-size: var(--cv-font-size-xs); }
</style>
