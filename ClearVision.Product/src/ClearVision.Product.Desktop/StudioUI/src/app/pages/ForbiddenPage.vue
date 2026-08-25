<script setup lang="ts">
import AuthShell from '@/app/pages/auth/AuthShell.vue';
import { CvIcon } from '@/design-system/icons';
</script>

<template>
  <AuthShell
    data-studio-page="forbidden"
    title="无法访问此页面"
    description="当前账号没有访问此页面的权限。返回工程库后仍可继续使用已授权功能。"
    variant="state"
    icon="warning"
  >
    <div
      class="auth-form"
      data-auth-page="forbidden"
    >
      <p
        class="auth-form__message"
        data-tone="warning"
        role="status"
      >
        <CvIcon
          name="info"
          size="md"
          aria-hidden="true"
        />
        <span>登录状态仍然有效。如需更多权限，请联系系统管理员调整账号角色后重新登录。</span>
      </p>
      <div class="auth-form__actions">
        <RouterLink to="/projects">
          返回工程库
          <CvIcon
            name="chevron-right"
            size="md"
            aria-hidden="true"
          />
        </RouterLink>
      </div>
    </div>
  </AuthShell>
</template>

<style scoped>
.auth-form__message { display: flex; align-items: center; gap: var(--cv-space-3); }
.auth-form__message :deep(.cv-icon) { flex: 0 0 auto; color: var(--cv-color-brand-500); }
</style>
