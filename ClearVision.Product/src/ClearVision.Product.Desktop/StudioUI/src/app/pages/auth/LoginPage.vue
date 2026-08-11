<script setup lang="ts">
import { computed, nextTick, shallowRef } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useAuthLifecycleRoot } from '@/app/auth';
import { resolveSafeReturnRoute } from '@/app/router';
import { CvButton, CvIconButton } from '@/design-system/primitives';
import { CvIcon } from '@/design-system/icons';
import AuthShell from './AuthShell.vue';

const root = useAuthLifecycleRoot();
const route = useRoute();
const router = useRouter();
const username = shallowRef(root.preferences.projection.rememberedUsername ?? '');
const password = shallowRef('');
const showPassword = shallowRef(false);
const rememberUsername = shallowRef(root.preferences.projection.rememberedUsername !== null);
const messageRoot = shallowRef<HTMLElement>();
const busy = computed(() => root.auth.projection.phase === 'authenticating' ||
  root.auth.projection.phase === 'protected-transition');
const pageTitle = computed(() => route.query.reason === 'expired' || route.query.reason === 'change-password'
  ? '重新登录'
  : '登录');
const pageDescription = computed(() => {
  if (route.query.reason === 'expired') return '会话已结束，请重新登录后继续原来的工作。';
  if (route.query.reason === 'change-password') return '密码已更新，请使用新密码重新登录。';
  if (route.query.reason === 'logout') return '你已安全退出，可再次登录进入工程配置与调试环境。';
  return '使用工作站账号进入工程配置与调试环境。';
});
const messageTone = computed(() => {
  if (root.auth.projection.phase === 'stale' || root.auth.projection.phase === 'expired') return 'warning';
  if (root.auth.projection.errorCode) return 'error';
  if (route.query.reason === 'change-password' || route.query.reason === 'logout') return 'success';
  if (root.auth.projection.phase === 'authenticated') return 'success';
  return 'info';
});
const prominentMessage = computed(() => messageTone.value !== 'info' ||
  root.auth.projection.phase === 'protected-transition');

function persistRememberedUsername(): void {
  root.preferences.setRememberedUsername(rememberUsername.value ? username.value : null);
}

async function submit(): Promise<void> {
  const accepted = await root.auth.login({ username: username.value, password: password.value });
  if (accepted) {
    persistRememberedUsername();
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
    :title="pageTitle"
    :description="pageDescription"
  >
    <form
      class="auth-form"
      data-auth-page="login"
      @submit.prevent="submit"
    >
      <p
        v-if="prominentMessage"
        ref="messageRoot"
        class="auth-form__message"
        role="status"
        aria-live="polite"
        tabindex="-1"
        data-auth-message
        :data-tone="messageTone"
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
          spellcheck="false"
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
          <CvIconButton
            class="auth-form__password-toggle"
            type="button"
            size="sm"
            :label="showPassword ? '隐藏登录密码' : '显示登录密码'"
            :aria-pressed="showPassword"
            @click="showPassword = !showPassword"
          >
            <CvIcon
              :name="showPassword ? 'eye-off' : 'eye'"
              size="md"
            />
          </CvIconButton>
        </div>
      </div>
      <div class="auth-form__options">
        <label class="auth-form__remember">
          <input
            v-model="rememberUsername"
            type="checkbox"
            name="rememberUsername"
          >
          <span>记住账号</span>
        </label>
      </div>
      <div class="auth-form__actions">
        <CvButton
          class="auth-form__submit"
          type="submit"
          variant="primary"
          block
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
          重新确认会话
        </CvButton>
      </div>
    </form>
  </AuthShell>
</template>
