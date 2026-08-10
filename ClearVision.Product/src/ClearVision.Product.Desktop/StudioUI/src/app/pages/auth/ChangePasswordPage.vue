<script setup lang="ts">
import { computed, nextTick, shallowRef, watch } from 'vue';
import { useAuthLifecycleRoot } from '@/app/auth';
import { CvButton, CvIconButton } from '@/design-system/primitives';
import { CvIcon } from '@/design-system/icons';
import AuthShell from './AuthShell.vue';

const root = useAuthLifecycleRoot();
const oldPassword = shallowRef('');
const newPassword = shallowRef('');
const confirmNewPassword = shallowRef('');
const showOldPassword = shallowRef(false);
const showNewPassword = shallowRef(false);
const showConfirmNewPassword = shallowRef(false);
const localMessage = shallowRef<string | null>(null);
const messageRoot = shallowRef<HTMLElement>();
const busy = computed(() => root.auth.projection.phase === 'changing-password' ||
  root.auth.projection.phase === 'protected-transition');
const messageTone = computed(() => localMessage.value || root.auth.projection.errorCode ? 'error' : 'info');
const displayMessage = computed(() => localMessage.value ?? root.auth.projection.message);
const prominentMessage = computed(() => Boolean(localMessage.value || root.auth.projection.errorCode));
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
        v-if="prominentMessage"
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
            :type="showOldPassword ? 'text' : 'password'"
            autocomplete="current-password"
            required
            autofocus
          >
          <CvIconButton
            class="auth-form__password-toggle"
            type="button"
            size="sm"
            :label="showOldPassword ? '隐藏当前密码' : '显示当前密码'"
            :aria-pressed="showOldPassword"
            @click="showOldPassword = !showOldPassword"
          >
            <CvIcon
              :name="showOldPassword ? 'eye-off' : 'eye'"
              size="md"
            />
          </CvIconButton>
        </div>
      </div>
      <div class="auth-form__field">
        <label for="new-password">新密码</label>
        <div class="auth-form__password">
          <input
            id="new-password"
            v-model="newPassword"
            class="auth-form__control"
            name="new-password"
            :type="showNewPassword ? 'text' : 'password'"
            autocomplete="new-password"
            :minlength="root.auth.projection.setupPolicy?.passwordMinLength ?? 6"
            aria-describedby="change-password-hint"
            required
          >
          <CvIconButton
            class="auth-form__password-toggle"
            type="button"
            size="sm"
            :label="showNewPassword ? '隐藏新密码' : '显示新密码'"
            :aria-pressed="showNewPassword"
            @click="showNewPassword = !showNewPassword"
          >
            <CvIcon
              :name="showNewPassword ? 'eye-off' : 'eye'"
              size="md"
            />
          </CvIconButton>
        </div>
        <small id="change-password-hint">{{ passwordHint }}</small>
      </div>
      <div class="auth-form__field">
        <label for="confirm-new-password">确认新密码</label>
        <div class="auth-form__password">
          <input
            id="confirm-new-password"
            v-model="confirmNewPassword"
            class="auth-form__control"
            name="confirm-new-password"
            :type="showConfirmNewPassword ? 'text' : 'password'"
            autocomplete="new-password"
            required
          >
          <CvIconButton
            class="auth-form__password-toggle"
            type="button"
            size="sm"
            :label="showConfirmNewPassword ? '隐藏确认新密码' : '显示确认新密码'"
            :aria-pressed="showConfirmNewPassword"
            @click="showConfirmNewPassword = !showConfirmNewPassword"
          >
            <CvIcon
              :name="showConfirmNewPassword ? 'eye-off' : 'eye'"
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
