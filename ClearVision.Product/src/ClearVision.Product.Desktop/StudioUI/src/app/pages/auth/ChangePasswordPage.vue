<script setup lang="ts">
import { computed, nextTick, ref } from 'vue';
import { useAuthLifecycleRoot } from '@/app/auth';
import { CvButton } from '@/design-system/primitives';
import AuthShell from './AuthShell.vue';

const root = useAuthLifecycleRoot();
const oldPassword = ref('');
const newPassword = ref('');
const showPassword = ref(false);
const messageRoot = ref<HTMLElement>();
const busy = computed(() => root.auth.projection.phase === 'changing-password' ||
  root.auth.projection.phase === 'protected-transition');

async function submit(): Promise<void> {
  const accepted = await root.auth.changePassword({
    oldPassword: oldPassword.value,
    newPassword: newPassword.value
  });
  if (!accepted) {
    await nextTick();
    messageRoot.value?.focus();
  }
}
</script>

<template>
  <AuthShell
    title="修改密码"
    description="修改前会先检查保存、执行与未知结果；成功后旧会话立即失效。"
  >
    <form
      class="auth-form"
      data-auth-page="change-password"
      @submit.prevent="submit"
    >
      <p
        ref="messageRoot"
        class="auth-form__message"
        role="status"
        aria-live="polite"
        tabindex="-1"
        data-auth-message
      >
        {{ root.auth.projection.message }}
      </p>
      <div class="auth-form__field">
        <label for="old-password">当前密码</label>
        <input
          id="old-password"
          v-model="oldPassword"
          class="auth-form__control"
          name="current-password"
          :type="showPassword ? 'text' : 'password'"
          autocomplete="current-password"
          required
          autofocus
        >
      </div>
      <div class="auth-form__field">
        <label for="new-password">新密码</label>
        <input
          id="new-password"
          v-model="newPassword"
          class="auth-form__control"
          name="new-password"
          :type="showPassword ? 'text' : 'password'"
          autocomplete="new-password"
          required
        >
      </div>
      <div class="auth-form__actions">
        <CvButton
          type="button"
          size="sm"
          variant="quiet"
          :aria-pressed="showPassword"
          @click="showPassword = !showPassword"
        >
          {{ showPassword ? '隐藏密码' : '显示密码' }}
        </CvButton>
        <CvButton
          type="submit"
          variant="danger"
          :loading="busy"
          loading-label="正在修改密码"
        >
          修改密码并退出
        </CvButton>
        <RouterLink to="/projects">
          取消并返回概览
        </RouterLink>
      </div>
    </form>
  </AuthShell>
</template>
