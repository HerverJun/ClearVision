<script setup lang="ts">
import { computed, nextTick, shallowRef } from 'vue';
import { useRouter } from 'vue-router';
import { useAuthLifecycleRoot } from '@/app/auth';
import { CvButton, CvIconButton } from '@/design-system/primitives';
import { CvIcon } from '@/design-system/icons';
import AuthShell from './AuthShell.vue';

const root = useAuthLifecycleRoot();
const router = useRouter();
const username = shallowRef('');
const password = shallowRef('');
const confirmPassword = shallowRef('');
const showPassword = shallowRef(false);
const showConfirmPassword = shallowRef(false);
const messageRoot = shallowRef<HTMLElement>();
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
const prominentMessage = computed(() => Boolean(root.auth.projection.errorCode));

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
          <CvIconButton
            class="auth-form__password-toggle"
            type="button"
            size="sm"
            :label="showPassword ? '隐藏管理员密码' : '显示管理员密码'"
            :aria-pressed="showPassword"
            @click="showPassword = !showPassword"
          >
            <CvIcon
              :name="showPassword ? 'eye-off' : 'eye'"
              size="md"
            />
          </CvIconButton>
        </div>
        <small id="setup-password-hint">{{ passwordHint }}</small>
      </div>
      <div class="auth-form__field">
        <label for="setup-confirm-password">确认密码</label>
        <div class="auth-form__password">
          <input
            id="setup-confirm-password"
            v-model="confirmPassword"
            class="auth-form__control"
            name="confirm-password"
            :type="showConfirmPassword ? 'text' : 'password'"
            autocomplete="new-password"
            required
          >
          <CvIconButton
            class="auth-form__password-toggle"
            type="button"
            size="sm"
            :label="showConfirmPassword ? '隐藏确认密码' : '显示确认密码'"
            :aria-pressed="showConfirmPassword"
            @click="showConfirmPassword = !showConfirmPassword"
          >
            <CvIcon
              :name="showConfirmPassword ? 'eye-off' : 'eye'"
              size="md"
            />
          </CvIconButton>
        </div>
      </div>
      <div class="auth-form__actions">
        <CvButton
          class="auth-form__submit"
          type="submit"
          variant="primary"
          block
          :loading="busy"
          loading-label="正在初始化"
        >
          创建管理员并进入工程库
        </CvButton>
      </div>
    </form>
  </AuthShell>
</template>
