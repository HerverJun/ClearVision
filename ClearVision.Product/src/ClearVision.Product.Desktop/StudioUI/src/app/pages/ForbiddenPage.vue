<script setup lang="ts">
import { CvPageState } from '@/design-system/patterns';
</script>

<template>
  <main
    class="foundation-page"
    data-studio-page="forbidden"
  >
    <CvPageState
      kind="forbidden"
      title="无权访问"
      description="当前角色或产品 profile 不允许访问此页面。后端权限仍是最终安全边界。"
      :heading-level="1"
    >
      <template #actions>
        <RouterLink to="/projects">
          返回安全产品入口
        </RouterLink>
      </template>
    </CvPageState>
  </main>
</template>
