<script setup lang="ts">
import { computed } from 'vue';
import { useRoute } from 'vue-router';
import { CvPageState } from '@/design-system/patterns';

const route = useRoute();
const isRouteLoadFailure = computed(() => route.query.reason === 'route-load');

function reloadApplication(): void {
  window.location.reload();
}
</script>

<template>
  <section
    class="product-page-state"
    data-studio-page="not-found"
  >
    <CvPageState
      :kind="isRouteLoadFailure ? 'error' : 'not-found'"
      :title="isRouteLoadFailure ? '页面资源加载失败' : '未找到此页面'"
      :description="isRouteLoadFailure
        ? '页面代码未能从本机资源中加载。请刷新 Studio；若问题仍在，请重新启动应用。'
        : '错误代码 404。该地址不属于当前 Studio 页面。'"
      :heading-level="1"
    >
      <template #actions>
        <button
          v-if="isRouteLoadFailure"
          class="product-page-state__action"
          type="button"
          @click="reloadApplication"
        >
          刷新 Studio
        </button>
        <RouterLink
          v-else
          class="product-page-state__action"
          to="/projects"
        >
          返回概览
        </RouterLink>
      </template>
    </CvPageState>
  </section>
</template>

<style scoped>
.product-page-state { width: min(680px, 100%); margin: 8vh auto 0; }
.product-page-state__action { min-height: var(--cv-density-control-height); padding: 0 var(--cv-space-4); display: inline-flex; align-items: center; border: 1px solid var(--cv-control-border); border-radius: var(--cv-radius-sm); background: var(--cv-surface-raised); color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); font-weight: var(--cv-font-weight-medium); text-decoration: none; }
.product-page-state__action:hover { border-color: var(--cv-control-border-hover); background: var(--cv-interactive-hover); }
</style>
