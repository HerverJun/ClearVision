<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue';
import { useAuthLifecycleRoot } from '@/app/auth';
import { CvButton } from '@/design-system/primitives';
import AuthShell from './AuthShell.vue';

const root = useAuthLifecycleRoot();
const oldPassword = ref('');
const newPassword = ref('');
const confirmNewPassword = ref('');
const showPassword = ref(false);
const localMessage = ref<string | null>(null);
const messageRoot = ref<HTMLElement>();
const busy = computed(() => root.auth.projection.phase === 'changing-password' ||
  root.auth.projection.phase === 'protected-transition');
const messageTone = computed(() => localMessage.value || root.auth.projection.errorCode ? 'error' : 'info');
const displayMessage = computed(() => localMessage.value ?? root.auth.projection.message);
const passwordHint = computed(() => {
  const policy = root.auth.projection.setupPolicy;
  const requirements = [`至少 ${policy?.passwordMinLength ?? 6} 位`];
  if (policy?.requiresUppercase) requirements.push('包含大写字母');
  if (policy?.requiresLowercase) requirements.push('包含小写字母');
  if (policy?.requiresDigit) requirements.push('包含数字');
  return `新密码要求：${requirements.join('，')}。`;
});

watch([oldPassword, newPassword, confirmNewPassword], () => {
  localMessage.value = null;
});

async function submit(): Promise<void> {
  if (newPassword.value !== confirmNewPassword.value) {
    localMessage.value = '两次输入的新密码不一致。';
    await nextTick();
    messageRoot.value?.focus();
    return;
  }
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
    description="离开前会确认当前工程和运行状态；保存成功后需要使用新密码重新登录。"
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
        :data-tone="messageTone"
      >
        {{ displayMessage }}
      </p>
      <div class="auth-form__field">
        <label for="old-password">当前密码</label>
        <div class="auth-form__password">
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
      <div class="auth-form__field">
        <label for="new-password">新密码</label>
        <input
          id="new-password"
          v-model="newPassword"
          class="auth-form__control"
          name="new-password"
          :type="showPassword ? 'text' : 'password'"
          autocomplete="new-password"
          :minlength="root.auth.projection.setupPolicy?.passwordMinLength ?? 6"
          aria-describedby="change-password-hint"
          required
        >
        <small id="change-password-hint">{{ passwordHint }}</small>
      </div>
      <div class="auth-form__field">
        <label for="confirm-new-password">确认新密码</label>
        <input
          id="confirm-new-password"
          v-model="confirmNewPassword"
          class="auth-form__control"
          name="confirm-new-password"
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
          loading-label="正在修改密码"
        >
          保存新密码并重新登录
        </CvButton>
        <RouterLink to="/projects">
          取消并返回工程库
        </RouterLink>
      </div>
    </form>
  </AuthShell>
</template>
