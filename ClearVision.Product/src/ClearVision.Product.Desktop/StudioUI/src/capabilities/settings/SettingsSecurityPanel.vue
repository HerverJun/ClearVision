<script setup lang="ts">
import { computed, onBeforeUnmount, reactive, shallowRef, watch } from 'vue';
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
const policyValidation = computed(() => {
  for (const [label, raw] of [
    ['密码最小长度', policyDraft.passwordMinLength],
    ['会话超时', policyDraft.sessionTimeoutMinutes],
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
  resetPolicy(value);
  policyFeedback.value = null;
});

async function savePolicy(): Promise<void> {
  if (!canWritePolicy.value || policyBusy.value || !policyDirty.value || policyValidation.value) return;
  policyBusy.value = true;
  policyFeedback.value = null;
  try {
    const result = await props.owner.saveGenericSection('security', {
      passwordMinLength: Number(policyDraft.passwordMinLength),
      sessionTimeoutMinutes: Number(policyDraft.sessionTimeoutMinutes),
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

async function changePassword(): Promise<void> {
  if (!canChangePassword.value || passwordBusy.value || !oldPassword.value || !newPassword.value) return;
  passwordBusy.value = true;
  passwordFeedback.value = null;
  try {
    const result = await props.owner.changePassword({
      oldPassword: oldPassword.value,
      newPassword: newPassword.value
    });
    passwordFeedback.value = settingsFeedbackForResult(result);
  } finally {
    oldPassword.value = '';
    newPassword.value = '';
    passwordBusy.value = false;
  }
}

onBeforeUnmount(() => {
  oldPassword.value = '';
  newPassword.value = '';
});
</script>

<template>
  <div
    class="settings-security"
    data-settings-section="security"
  >
    <CvPanel
      title="安全策略"
      description="密码策略属于 AppConfig Security；用户记录和密码操作属于独立 authority。"
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
            label="会话超时（分钟）"
            type="number"
            :readonly="!canWritePolicy"
            :error="canWritePolicy ? policyValidation ?? undefined : undefined"
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
          title="当前为安全投影"
        >
          当前角色可以核对已返回策略，但不能执行 Security policy mutation。
        </CvInlineAlert>
      </div>
      <CvInlineAlert
        v-else
        tone="info"
        title="当前响应未包含安全策略"
      >
        服务端返回的是 safe subset；不会用本地默认值补齐策略，也不会发起越权写入。
      </CvInlineAlert>
      <template
        v-if="projection"
        #footer
      >
        <div class="settings-panel__footer">
          <span class="settings-panel__dirty">{{ policyDirty ? '有未保存修改' : '与服务端投影一致' }}</span>
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
      description="密码只提交给既有 authenticated session endpoint，完成或离开分区后立即清空。"
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
          variant="danger"
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
        Operator 不会挂载 Settings route。
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
