<script setup lang="ts">
import { computed, nextTick, ref } from 'vue';
import { useRouter } from 'vue-router';
import { useAuthLifecycleRoot } from '@/app/auth';
import { CvButton } from '@/design-system/primitives';
import AuthShell from './AuthShell.vue';

const root = useAuthLifecycleRoot();
const router = useRouter();
const username = ref('');
const password = ref('');
const confirmPassword = ref('');
const showPassword = ref(false);
const messageRoot = ref<HTMLElement>();
const busy = computed(() => root.auth.projection.phase === 'authenticating' ||
  root.auth.projection.phase === 'protected-transition');
const passwordHint = computed(() => {
  const policy = root.auth.projection.setupPolicy;
  const requirements = [`至少 ${policy?.passwordMinLength ?? 6} 位`];
  if (policy?.requiresUppercase) requirements.push('包含大写字母');
  if (policy?.requiresLowercase) requirements.push('包含小写字母');
  if (policy?.requiresDigit) requirements.push('包含数字');
  return `密码要求：${requirements.join('，')}。`;
});
const messageTone = computed(() => root.auth.projection.errorCode ? 'error' : 'info');

async function submit(): Promise<void> {
  const accepted = await root.auth.setupAdmin({
    username: username.value,
    password: password.value,
    confirmPassword: confirmPassword.value
  });
  if (accepted) {
    await router.replace('/projects');
    return;
  }
  await nextTick();
  messageRoot.value?.focus();
}
</script>

<template>
  <AuthShell
    title="创建首位管理员"
    description="为这台工作站创建首位管理员。完成后将直接进入工程库，此入口不再出现。"
  >
    <form
      class="auth-form"
      data-auth-page="setup"
      @submit.prevent="submit"
    >
      <p
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
        <label for="setup-username">管理员用户名</label>
        <input
          id="setup-username"
          v-model="username"
          class="auth-form__control"
          name="username"
          autocomplete="username"
          :minlength="root.auth.projection.setupPolicy?.usernameMinLength ?? 3"
          required
          autofocus
        >
      </div>
      <div class="auth-form__field">
        <label for="setup-password">密码</label>
        <div class="auth-form__password">
          <input
            id="setup-password"
            v-model="password"
            class="auth-form__control"
            name="new-password"
            :type="showPassword ? 'text' : 'password'"
            autocomplete="new-password"
            :minlength="root.auth.projection.setupPolicy?.passwordMinLength ?? 6"
            :aria-describedby="'setup-password-hint'"
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
        <small id="setup-password-hint">{{ passwordHint }}</small>
      </div>
      <div class="auth-form__field">
        <label for="setup-confirm-password">确认密码</label>
        <input
          id="setup-confirm-password"
          v-model="confirmPassword"
          class="auth-form__control"
          name="confirm-password"
          :type="showPassword ? 'text' : 'password'"
          autocomplete="new-password"
          required
        >
      </div>
      <div class="auth-form__actions">
        <CvButton
          type="submit"
          variant="primary"
          :loading="busy"
          loading-label="正在初始化"
        >
          创建管理员并进入工程库
        </CvButton>
      </div>
    </form>
  </AuthShell>
</template>
