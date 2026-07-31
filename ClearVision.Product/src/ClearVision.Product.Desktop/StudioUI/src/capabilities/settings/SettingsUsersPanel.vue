<script setup lang="ts">
import { onBeforeUnmount, reactive, shallowRef, watch } from 'vue';
import {
  CvButton,
  CvDataTable,
  CvField,
  CvInlineAlert,
  CvModal,
  CvPanel,
  CvSelect,
  type CvDataTableColumn,
  type CvSelectOption
} from '@/design-system';
import type { SettingsOwner } from './settingsOwner';
import type { SettingsUserProjectionV1 } from './decoder';
import { settingsFeedbackForResult, type SettingsFeedback } from './settingsViewModel';

const props = defineProps<{
  owner: SettingsOwner;
  canManage: boolean;
}>();

const users = shallowRef<readonly SettingsUserProjectionV1[]>([]);
const phase = shallowRef<'idle' | 'loading' | 'ready' | 'error'>('idle');
const message = shallowRef<string | null>(null);
const operationFeedback = shallowRef<SettingsFeedback | null>(null);
const createBusy = shallowRef(false);
const editBusy = shallowRef(false);
const resetBusy = shallowRef(false);
const editingId = shallowRef<string | null>(null);
const resetUser = shallowRef<SettingsUserProjectionV1 | null>(null);
const resetPassword = shallowRef('');
let requestVersion = 0;

const createDraft = reactive({
  username: '',
  displayName: '',
  role: '2',
  password: ''
});
const editDraft = reactive({
  displayName: '',
  role: '2',
  isActive: true
});

const roleOptions: readonly CvSelectOption[] = Object.freeze([
  { value: '0', label: 'Admin' },
  { value: '1', label: 'Engineer' },
  { value: '2', label: 'Operator' }
]);
const columns: readonly CvDataTableColumn<SettingsUserProjectionV1>[] = Object.freeze([
  { key: 'username', label: '用户名', width: '18%' },
  { key: 'displayName', label: '显示名称', width: '20%' },
  { key: 'role', label: '角色', width: '15%' },
  { key: 'isActive', label: '状态', width: '12%' },
  { key: 'lastLoginAt', label: '最近登录', width: '20%' },
  { key: 'actions', label: '操作', align: 'end', width: '15%' }
]);

function roleNumber(value: string): number {
  const parsed = Number(value);
  return parsed === 0 || parsed === 1 || parsed === 2 ? parsed : 2;
}

function clearCreateSecret(): void {
  createDraft.password = '';
}

function clearResetSecret(): void {
  resetPassword.value = '';
}

function clearReadState(): void {
  users.value = [];
  phase.value = 'idle';
  message.value = null;
  operationFeedback.value = null;
  editingId.value = null;
  resetUser.value = null;
  clearResetSecret();
}

async function loadUsers(): Promise<void> {
  const owner = props.owner;
  const version = ++requestVersion;
  if (!props.canManage) {
    clearReadState();
    return;
  }
  phase.value = 'loading';
  message.value = null;
  const result = await owner.readUsers();
  if (version !== requestVersion || owner !== props.owner || !props.canManage) return;
  if (result.status === 'completed') {
    users.value = result.value.items;
    phase.value = 'ready';
    return;
  }
  phase.value = 'error';
  message.value = settingsFeedbackForResult(result).message;
}

async function createUser(): Promise<void> {
  if (createBusy.value || !createDraft.username.trim() || !createDraft.password) return;
  const owner = props.owner;
  const version = requestVersion;
  createBusy.value = true;
  operationFeedback.value = null;
  try {
    const result = await owner.createUser({
      username: createDraft.username.trim(),
      displayName: createDraft.displayName.trim(),
      role: roleNumber(createDraft.role),
      password: createDraft.password
    });
    if (version !== requestVersion || owner !== props.owner || !props.canManage) return;
    operationFeedback.value = settingsFeedbackForResult(result);
    if (result.status === 'completed') {
      createDraft.username = '';
      createDraft.displayName = '';
      createDraft.role = '2';
      clearCreateSecret();
      await loadUsers();
    }
  } finally {
    clearCreateSecret();
    createBusy.value = false;
  }
}

function beginEdit(user: SettingsUserProjectionV1): void {
  editingId.value = user.id;
  editDraft.displayName = user.displayName;
  editDraft.role = user.role === 'Admin' ? '0' : user.role === 'Engineer' ? '1' : '2';
  editDraft.isActive = user.isActive;
  operationFeedback.value = null;
}

function cancelEdit(): void {
  editingId.value = null;
}

async function saveEdit(): Promise<void> {
  if (!editingId.value || editBusy.value) return;
  const owner = props.owner;
  const version = requestVersion;
  editBusy.value = true;
  operationFeedback.value = null;
  try {
    const result = await owner.updateUser(editingId.value, {
      displayName: editDraft.displayName.trim(),
      role: roleNumber(editDraft.role),
      isActive: editDraft.isActive
    });
    if (version !== requestVersion || owner !== props.owner || !props.canManage) return;
    operationFeedback.value = settingsFeedbackForResult(result);
    if (result.status === 'completed') {
      editingId.value = null;
      await loadUsers();
    }
  } finally {
    editBusy.value = false;
  }
}

async function deleteUser(user: SettingsUserProjectionV1): Promise<void> {
  if (!window.confirm(`确定删除用户 ${user.username}？`)) return;
  const owner = props.owner;
  const version = requestVersion;
  operationFeedback.value = null;
  const result = await owner.deleteUser(user.id);
  if (version !== requestVersion || owner !== props.owner || !props.canManage) return;
  operationFeedback.value = settingsFeedbackForResult(result);
  if (result.status === 'completed') await loadUsers();
}

function openReset(user: SettingsUserProjectionV1): void {
  resetUser.value = user;
  clearResetSecret();
  operationFeedback.value = null;
}

function closeReset(): void {
  resetUser.value = null;
  clearResetSecret();
}

async function submitReset(): Promise<void> {
  if (!resetUser.value || resetBusy.value || !resetPassword.value) return;
  const owner = props.owner;
  const version = requestVersion;
  resetBusy.value = true;
  operationFeedback.value = null;
  try {
    const result = await owner.resetUserPassword(resetUser.value.id, resetPassword.value);
    if (version !== requestVersion || owner !== props.owner || !props.canManage) return;
    operationFeedback.value = settingsFeedbackForResult(result);
    if (result.status === 'completed') {
      closeReset();
      await loadUsers();
    }
  } finally {
    clearResetSecret();
    resetBusy.value = false;
  }
}

watch([() => props.owner, () => props.canManage], () => {
  void loadUsers();
}, { immediate: true });

onBeforeUnmount(() => {
  clearCreateSecret();
  clearResetSecret();
  requestVersion += 1;
});
</script>

<template>
  <CvPanel
    title="用户管理"
    description="用户记录和密码走各自的后端 authority，不进入 AppConfig 草稿。"
    data-settings-users
  >
    <CvInlineAlert
      v-if="!canManage"
      tone="info"
      title="仅 Admin 可用"
    >
      Engineer 可以修改本人密码，但不能读取或管理用户列表。
    </CvInlineAlert>

    <template v-if="canManage">
      <form
        class="settings-users__create"
        @submit.prevent="createUser"
      >
        <div class="settings-users__create-heading">
          <strong>创建用户</strong>
          <span>密码只在本次请求期间存在于内存。</span>
        </div>
        <CvField
          v-model="createDraft.username"
          label="用户名"
          autocomplete="username"
          required
        />
        <CvField
          v-model="createDraft.displayName"
          label="显示名称"
        />
        <CvSelect
          v-model="createDraft.role"
          label="角色"
          :options="roleOptions"
        />
        <CvField
          v-model="createDraft.password"
          label="初始密码"
          type="password"
          autocomplete="new-password"
          required
        />
        <CvButton
          type="submit"
          size="sm"
          variant="primary"
          :loading="createBusy"
          loading-label="正在创建用户"
        >
          创建用户
        </CvButton>
      </form>

      <CvInlineAlert
        v-if="phase === 'error'"
        tone="error"
        title="用户列表读取失败"
      >
        {{ message }}
      </CvInlineAlert>
      <CvInlineAlert
        v-else-if="phase === 'loading'"
        tone="info"
        title="正在读取用户列表"
      >
        当前用户记录仍以服务端返回为准。
      </CvInlineAlert>

      <CvDataTable
        v-if="phase === 'ready'"
        :rows="users"
        :columns="columns"
        row-key="id"
        caption="Admin 用户列表"
      >
        <template #cell-role="{ row }">
          {{ row.role }}
        </template>
        <template #cell-isActive="{ row }">
          {{ row.isActive ? '启用' : '已停用' }}
        </template>
        <template #cell-lastLoginAt="{ row }">
          {{ row.lastLoginAt ?? '从未登录' }}
        </template>
        <template #cell-actions="{ row }">
          <div class="settings-users__row-actions">
            <CvButton
              size="sm"
              variant="quiet"
              @click="beginEdit(row)"
            >
              编辑
            </CvButton>
            <CvButton
              size="sm"
              variant="quiet"
              @click="openReset(row)"
            >
              重置密码
            </CvButton>
            <CvButton
              size="sm"
              variant="danger"
              @click="deleteUser(row)"
            >
              删除
            </CvButton>
          </div>
        </template>
      </CvDataTable>

      <form
        v-if="editingId"
        class="settings-users__edit"
        @submit.prevent="saveEdit"
      >
        <strong>编辑用户</strong>
        <CvField
          v-model="editDraft.displayName"
          label="显示名称"
        />
        <CvSelect
          v-model="editDraft.role"
          label="角色"
          :options="roleOptions"
        />
        <label class="settings-toggle">
          <input
            v-model="editDraft.isActive"
            type="checkbox"
          >
          <span><strong>账号启用</strong><small>停用账号不会删除历史记录。</small></span>
        </label>
        <div class="settings-users__edit-actions">
          <CvButton
            type="button"
            size="sm"
            variant="quiet"
            @click="cancelEdit"
          >
            取消
          </CvButton>
          <CvButton
            type="submit"
            size="sm"
            variant="primary"
            :loading="editBusy"
          >
            保存用户
          </CvButton>
        </div>
      </form>
    </template>
  </CvPanel>

  <div
    v-if="operationFeedback"
    class="settings-users__feedback"
  >
    <CvInlineAlert
      :tone="operationFeedback.kind === 'saved' ? 'success' : operationFeedback.kind === 'unknown' ? 'warning' : 'error'"
      :title="operationFeedback.kind === 'saved' ? '用户操作已完成' : '用户操作未完成'"
    >
      {{ operationFeedback.message }}
    </CvInlineAlert>
  </div>

  <CvModal
    :open="resetUser !== null"
    title="重置用户密码"
    description="新密码仅提交给既有 Admin endpoint，不会写入用户列表或页面投影。"
    size="sm"
    @close="closeReset"
  >
    <CvField
      v-model="resetPassword"
      label="新密码"
      type="password"
      autocomplete="new-password"
      data-modal-initial-focus
      required
    />
    <template #footer>
      <CvButton
        size="sm"
        variant="quiet"
        @click="closeReset"
      >
        取消
      </CvButton>
      <CvButton
        size="sm"
        variant="danger"
        :loading="resetBusy"
        :disabled="!resetPassword"
        @click="submitReset"
      >
        确认重置
      </CvButton>
    </template>
  </CvModal>
</template>

<style scoped>
.settings-users__create,
.settings-users__edit {
  display: grid;
  grid-template-columns: repeat(5, minmax(0, 1fr));
  align-items: end;
  gap: var(--cv-space-3);
  margin-bottom: var(--cv-space-5);
  padding-bottom: var(--cv-space-4);
  border-bottom: 1px solid var(--cv-border-subtle);
}

.settings-users__create-heading { display: grid; gap: var(--cv-space-1); }
.settings-users__create-heading strong, .settings-users__edit > strong { color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); }
.settings-users__create-heading span { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.settings-users__row-actions { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: 2px; }
.settings-users__edit { grid-template-columns: repeat(3, minmax(0, 1fr)); margin-top: var(--cv-space-5); margin-bottom: 0; padding-top: var(--cv-space-4); border-top: 1px solid var(--cv-border-subtle); border-bottom: 0; }
.settings-users__edit-actions { display: flex; flex-wrap: wrap; gap: var(--cv-space-2); }
.settings-toggle { display: flex; min-width: 0; align-items: flex-start; gap: var(--cv-space-2); padding: var(--cv-space-2); border: 1px solid var(--cv-border-subtle); border-radius: var(--cv-radius-sm); background: var(--cv-surface-page); cursor: pointer; }
.settings-toggle input { width: 18px; height: 18px; margin-top: 2px; accent-color: var(--cv-color-brand-500); }
.settings-toggle span { display: grid; gap: var(--cv-space-1); }
.settings-toggle strong { color: var(--cv-text-primary); font-size: var(--cv-font-size-xs); }
.settings-toggle small { color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); }
.settings-users__feedback { margin-top: var(--cv-space-4); }
@media (max-width: 1020px) {
  .settings-users__create, .settings-users__edit { grid-template-columns: repeat(2, minmax(0, 1fr)); }
}
@media (max-width: 620px) {
  .settings-users__create, .settings-users__edit { grid-template-columns: 1fr; }
  .settings-users__row-actions { justify-content: flex-start; }
}
</style>
