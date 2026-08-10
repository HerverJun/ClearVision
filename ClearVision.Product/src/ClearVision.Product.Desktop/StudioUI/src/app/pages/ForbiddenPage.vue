<script setup lang="ts">
import AuthShell from '@/app/pages/auth/AuthShell.vue';
</script>

<template>
  <AuthShell
    data-studio-page="forbidden"
    title="无法访问此页面"
    description="当前账号没有访问此页面的权限。返回工程库后仍可继续使用已授权功能。"
  >
    <div
      class="auth-form"
      data-auth-page="forbidden"
    >
      <p
        class="auth-form__message"
        data-tone="warning"
      >
        登录状态仍然有效。如需更多权限，请联系系统管理员调整账号角色后重新登录。
      </p>
      <div class="auth-form__actions">
        <RouterLink to="/projects">
          返回工程库
        </RouterLink>
      </div>
    </div>
  </AuthShell>
</template>
