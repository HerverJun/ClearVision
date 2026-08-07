<script setup lang="ts">
import { computed, nextTick, ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useAuthLifecycleRoot } from '@/app/auth';
import { resolveSafeReturnRoute } from '@/app/router';
import { CvButton } from '@/design-system/primitives';
import AuthShell from './AuthShell.vue';

const root = useAuthLifecycleRoot();
const route = useRoute();
const router = useRouter();
const username = ref('');
const password = ref('');
const showPassword = ref(false);
const messageRoot = ref<HTMLElement>();
const busy = computed(() => root.auth.projection.phase === 'authenticating' ||
  root.auth.projection.phase === 'protected-transition');

async function submit(): Promise<void> {
  const accepted = await root.auth.login({ username: username.value, password: password.value });
  if (accepted) {
    await router.replace(resolveSafeReturnRoute(route.query.returnTo) ?? '/projects');
    return;
  }
  await nextTick();
  messageRoot.value?.focus();
}

async function retryRecovery(): Promise<void> {
  await root.auth.refreshSession();
  if (root.auth.projection.phase === 'authenticated') {
    await router.replace(resolveSafeReturnRoute(route.query.returnTo) ?? '/projects');
  }
}
</script>

<template>
  <AuthShell
    title="登录"
    description="认证成功并经 /api/auth/me 复核后，产品运行时才会挂载。"
  >
    <form
      class="auth-form"
      data-auth-page="login"
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
        <label for="login-username">用户名</label>
        <input
          id="login-username"
          v-model="username"
          class="auth-form__control"
          name="username"
          autocomplete="username"
          required
          autofocus
        >
      </div>
      <div class="auth-form__field">
        <label for="login-password">密码</label>
        <div class="auth-form__password">
          <input
            id="login-password"
            v-model="password"
            class="auth-form__control"
            name="password"
            :type="showPassword ? 'text' : 'password'"
            autocomplete="current-password"
            required
          >
          <CvButton
            type="button"
            size="sm"
            variant="quiet"
            :aria-pressed="showPassword"
            @click="showPassword = !showPassword"
          >
            {{ showPassword ? '隐藏密码' : '显示密码' }}
          </CvButton>
        </div>
      </div>
      <div class="auth-form__actions">
        <CvButton
          type="submit"
          variant="primary"
          :loading="busy"
          loading-label="正在登录"
        >
          登录
        </CvButton>
        <CvButton
          v-if="root.auth.projection.phase === 'stale' || root.auth.projection.phase === 'protected-transition'"
          type="button"
          @click="retryRecovery"
        >
          重试会话恢复
        </CvButton>
      </div>
    </form>
  </AuthShell>
</template>
