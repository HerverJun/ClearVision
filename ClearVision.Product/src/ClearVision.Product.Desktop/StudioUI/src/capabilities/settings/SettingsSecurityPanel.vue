<script setup lang="ts">
import { computed, onBeforeUnmount, onDeactivated, reactive, shallowRef, watch } from 'vue';
import type { AuthLifecycleOwner } from '@/app/auth';
import { CvButton, CvField, CvInlineAlert, CvPanel } from '@/design-system';
import type { SettingsOwner } from './settingsOwner';
import type { SettingsSecurityProjectionV1 } from './decoder';
import { settingsFeedbackForResult, type SettingsFeedback } from './settingsViewModel';
import SettingsWriteFeedback from './SettingsWriteFeedback.vue';
import SettingsUsersPanel from './SettingsUsersPanel.vue';

const props = defineProps<{
  projection: SettingsSecurityProjectionV1 | null;
  owner: SettingsOwner;
  role: string | null;
  auth: AuthLifecycleOwner | null;
}>();

const canWritePolicy = computed(() => props.role === 'Admin' && props.projection !== null);
const canChangePassword = computed(() => props.role === 'Admin' || props.role === 'Engineer');
const canManageUsers = computed(() => props.role === 'Admin');

const policyDraft = reactive({
  passwordMinLength: '',
  sessionTimeoutMinutes: '',
  loginFailureLockoutCount: ''
});
const policyBaseline = shallowRef({
  passwordMinLength: '',
  sessionTimeoutMinutes: '',
  loginFailureLockoutCount: ''
});
const policyBusy = shallowRef(false);
const policyFeedback = shallowRef<SettingsFeedback | null>(null);
const oldPassword = shallowRef('');
const newPassword = shallowRef('');
const passwordBusy = shallowRef(false);
const passwordFeedback = shallowRef<SettingsFeedback | null>(null);

const policyDirty = computed(() => JSON.stringify(policyDraft) !== JSON.stringify(policyBaseline.value));
const detachPanelState = props.owner.registerPanelState('security', () => ({
  dirty: policyDirty.value,
  pending: policyBusy.value || passwordBusy.value
}));
watch([policyDirty, policyBusy, passwordBusy, oldPassword, newPassword], () => {
  props.owner.refreshPanelState();
});
const policyValidation = computed(() => {
  const passwordMinLength = Number(policyDraft.passwordMinLength);
  if (!Number.isInteger(passwordMinLength) || passwordMinLength < 6) return '密码最小长度不能少于 6 位。';
  for (const [label, raw] of [
    ['失败锁定次数', policyDraft.loginFailureLockoutCount]
  ] as const) {
    const value = Number(raw);
    if (!Number.isInteger(value) || value < 1) return `${label}必须是正整数。`;
  }
  return null;
});

function copyPolicy(value: SettingsSecurityProjectionV1) {
  return {
    passwordMinLength: String(value.passwordMinLength),
    sessionTimeoutMinutes: String(value.sessionTimeoutMinutes),
    loginFailureLockoutCount: String(value.loginFailureLockoutCount)
  };
}

function resetPolicy(value: SettingsSecurityProjectionV1 | null): void {
  if (!value) {
    policyDraft.passwordMinLength = '';
    policyDraft.sessionTimeoutMinutes = '';
    policyDraft.loginFailureLockoutCount = '';
    policyBaseline.value = { ...policyDraft };
    return;
  }
  const next = copyPolicy(value);
  policyBaseline.value = next;
  Object.assign(policyDraft, next);
}

resetPolicy(props.projection);

watch(() => props.projection, value => {
  const wasDirty = policyDirty.value;
  const next = value ? copyPolicy(value) : {
    passwordMinLength: '',
    sessionTimeoutMinutes: '',
    loginFailureLockoutCount: ''
  };
  policyBaseline.value = next;
  if (!wasDirty) Object.assign(policyDraft, next);
  policyFeedback.value = null;
});

async function savePolicy(): Promise<void> {
  if (!canWritePolicy.value || policyBusy.value || !policyDirty.value || policyValidation.value) return;
  policyBusy.value = true;
  policyFeedback.value = null;
  try {
    const result = await props.owner.saveGenericSection('security', {
      passwordMinLength: Number(policyDraft.passwordMinLength),
      loginFailureLockoutCount: Number(policyDraft.loginFailureLockoutCount)
    });
    policyFeedback.value = settingsFeedbackForResult(result);
    if (result.status === 'completed' && result.value.config.sections.security) {
      const next = copyPolicy(result.value.config.sections.security);
      policyBaseline.value = next;
      Object.assign(policyDraft, next);
    }
  } finally {
    policyBusy.value = false;
  }
}

function discardPolicy(): void {
  Object.assign(policyDraft, policyBaseline.value);
  policyFeedback.value = null;
}

function recordPasswordOutcome(accepted: boolean): boolean {
  const auth = props.auth;
  if (!auth) return false;
  const sessionInvalidated = accepted &&
    auth.projection.phase === 'unauthenticated' &&
    auth.session.projection.phase === 'unauthorized';
  if (sessionInvalidated) {
    props.owner.recordChangePasswordSessionResult(true);
    return false;
  }

  const errorCode = auth.projection.errorCode;
  const outcomeUnknown = !accepted && (
    auth.projection.phase !== 'authenticated' ||
    errorCode === 'NETWORK_FAILURE' ||
    errorCode === 'REQUEST_ABORTED' ||
    errorCode === 'AUTH_FAILURE'
  );
  props.owner.recordChangePasswordSessionResult(false, outcomeUnknown);
  return outcomeUnknown;
}

async function changePassword(): Promise<void> {
  if (!canChangePassword.value || passwordBusy.value || !oldPassword.value || !newPassword.value) return;
  passwordFeedback.value = null;
  // Let the shared auth lifecycle pass its protected-transition check before
  // this panel reports the mutation as pending to the shared leave guard.
  const transition = props.auth
    ? props.auth.changePassword({
        oldPassword: oldPassword.value,
        newPassword: newPassword.value
      })
    : Promise.resolve(false);
  passwordBusy.value = true;
  try {
    if (!props.auth) {
      passwordFeedback.value = {
        kind: 'error',
        message: '身份验证流程不可用，密码修改已被拒绝。',
        savedLabel: '未完成',
        effectiveLabel: '未生效',
        restartLabel: '不适用'
      };
      return;
    }
    const accepted = await transition;
    const outcomeUnknown = recordPasswordOutcome(accepted);
    passwordFeedback.value = accepted
      ? {
          kind: 'saved',
          message: '密码修改成功，当前会话已失效，请使用新密码重新登录。',
          savedLabel: '账户操作已完成',
          effectiveLabel: '会话已失效',
          restartLabel: '需要重新登录'
        }
      : {
          kind: outcomeUnknown ? 'unknown' : 'error',
          message: outcomeUnknown
            ? '密码请求结果未知；请等待身份验证流程确认会话状态后再决定是否重试。'
            : props.auth.projection.message,
          savedLabel: outcomeUnknown ? '结果未知' : '未完成',
          effectiveLabel: outcomeUnknown ? '结果未知' : '未生效',
          restartLabel: outcomeUnknown ? '等待会话核对' : '不适用'
        };
  } finally {
    oldPassword.value = '';
    newPassword.value = '';
    passwordBusy.value = false;
  }
}

onBeforeUnmount(() => {
  detachPanelState();
  oldPassword.value = '';
  newPassword.value = '';
});

onDeactivated(() => {
  oldPassword.value = '';
  newPassword.value = '';
  props.owner.refreshPanelState();
});
</script>

<template>
  <div
    class="settings-security"
    data-settings-section="security"
  >
    <CvPanel
      title="安全策略"
      description="设置密码长度和登录失败锁定次数。"
      variant="section"
      data-settings-security-policy
    >
      <div v-if="projection">
        <div class="settings-form-grid">
          <CvField
            v-model="policyDraft.passwordMinLength"
            label="密码最小长度"
            type="number"
            :readonly="!canWritePolicy"
            :error="canWritePolicy ? policyValidation ?? undefined : undefined"
          />
          <CvField
            v-model="policyDraft.sessionTimeoutMinutes"
            label="会话时长（分钟）"
            type="number"
            readonly
            disabled
            hint="兼容字段，只读；当前桌面会话不会按此数值自动退出。"
          />
          <CvField
            v-model="policyDraft.loginFailureLockoutCount"
            label="失败锁定次数"
            type="number"
            :readonly="!canWritePolicy"
            :error="canWritePolicy ? policyValidation ?? undefined : undefined"
          />
        </div>
        <CvInlineAlert
          v-if="!canWritePolicy"
          class="settings-panel__notice"
          tone="info"
          title="当前账户为只读"
        >
          你可以查看当前策略；修改需要管理员权限。
        </CvInlineAlert>
      </div>
      <CvInlineAlert
        v-else
        tone="info"
        title="当前响应未包含安全策略"
      >
        当前账户不能查看安全策略，页面不会显示默认值或提交修改。
      </CvInlineAlert>
      <template
        v-if="projection"
        #footer
      >
        <div class="settings-panel__footer">
          <span class="settings-panel__dirty">{{ policyDirty ? '有未保存修改' : '当前策略已保存' }}</span>
          <div class="settings-panel__actions">
            <CvButton
              v-if="canWritePolicy"
              variant="quiet"
              size="sm"
              :disabled="!policyDirty || policyBusy"
              @click="discardPolicy"
            >
              放弃修改
            </CvButton>
            <CvButton
              v-if="canWritePolicy"
              variant="primary"
              size="sm"
              :loading="policyBusy"
              :disabled="!policyDirty || Boolean(policyValidation)"
              loading-label="正在保存安全策略"
              @click="savePolicy"
            >
              保存安全策略
            </CvButton>
          </div>
        </div>
      </template>
    </CvPanel>

    <SettingsWriteFeedback :feedback="policyFeedback" />

    <CvPanel
      title="修改本人密码"
      description="修改成功后当前会话会退出，需要使用新密码重新登录。"
      variant="section"
      data-settings-change-password
    >
      <form
        v-if="canChangePassword"
        class="settings-password-form"
        autocomplete="off"
        @submit.prevent="changePassword"
      >
        <CvField
          v-model="oldPassword"
          label="当前密码"
          type="password"
          autocomplete="current-password"
          required
        />
        <CvField
          v-model="newPassword"
          label="新密码"
          type="password"
          autocomplete="new-password"
          required
        />
        <CvButton
          type="submit"
          size="sm"
          variant="primary"
          :loading="passwordBusy"
          :disabled="!oldPassword || !newPassword"
          loading-label="正在修改密码"
        >
          修改密码
        </CvButton>
      </form>
      <CvInlineAlert
        v-else
        tone="info"
        title="当前角色不可用"
      >
        操作员不会挂载设置页面。
      </CvInlineAlert>
    </CvPanel>

    <SettingsWriteFeedback :feedback="passwordFeedback" />

    <SettingsUsersPanel
      :owner="owner"
      :can-manage="canManageUsers"
    />
  </div>
</template>

<style scoped>
.settings-security { display: grid; min-width: 0; gap: var(--cv-density-page-gap); }
.settings-form-grid { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: var(--cv-space-4); }
.settings-password-form { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); align-items: end; gap: var(--cv-space-4); }
.settings-password-form > :deep(.cv-button) { justify-self: start; }
.settings-panel__notice { margin-top: var(--cv-space-4); }
.settings-panel__footer { display: flex; min-width: 0; align-items: center; justify-content: space-between; gap: var(--cv-space-3); }
.settings-panel__dirty { color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); }
.settings-panel__actions { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: var(--cv-space-2); }
@media (max-width: 760px) {
  .settings-form-grid, .settings-password-form { grid-template-columns: 1fr; }
  .settings-panel__footer { align-items: stretch; flex-direction: column; }
  .settings-panel__actions { justify-content: flex-start; }
}
</style>
