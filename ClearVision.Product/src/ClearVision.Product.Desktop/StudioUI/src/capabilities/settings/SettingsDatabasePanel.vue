<script setup lang="ts">
import { computed, onBeforeUnmount, shallowRef, watch } from 'vue';
import { CvButton, CvInlineAlert, CvPanel, CvStatusBadge, type CvStatusTone } from '@/design-system';
import type { SettingsOwner } from './settingsOwner';
import type { SettingsDatabaseBackupProjectionV1, SettingsDatabaseStatusProjectionV1 } from './decoder';
import { formatSettingsBytes, settingsFeedbackForResult, type SettingsFeedback } from './settingsViewModel';

const props = defineProps<{ owner: SettingsOwner; role: string | null }>();
const isAdmin = computed(() => props.role === 'Admin');
const phase = shallowRef<'idle' | 'loading' | 'ready' | 'error'>('idle');
const status = shallowRef<SettingsDatabaseStatusProjectionV1 | null>(null);
const backup = shallowRef<SettingsDatabaseBackupProjectionV1 | null>(null);
const backupBusy = shallowRef(false);
const feedback = shallowRef<SettingsFeedback | null>(null);
const message = shallowRef<string | null>(null);
let requestVersion = 0;

const detachPanelState = props.owner.registerPanelState('database', () => ({
  dirty: false,
  pending: backupBusy.value
}));
watch(backupBusy, () => props.owner.refreshPanelState());

function clearReadState(): void {
  status.value = null;
  backup.value = null;
  phase.value = 'idle';
  message.value = null;
  feedback.value = null;
}

function statusTone(value: string): CvStatusTone {
  if (value === 'Healthy') return 'ok';
  if (value === 'PendingMigration' || value === 'NeedsRepair') return 'warning';
  if (value === 'Missing' || value === 'Corrupt' || value === 'Error') return 'error';
  return 'idle';
}

async function loadStatus(): Promise<void> {
  const owner = props.owner;
  const version = ++requestVersion;
  if (!isAdmin.value) {
    clearReadState();
    return;
  }
  phase.value = 'loading';
  const result = await owner.readDatabaseStatus();
  if (version !== requestVersion || owner !== props.owner || !isAdmin.value) return;
  if (result.status === 'completed') {
    status.value = result.value;
    phase.value = 'ready';
    return;
  }
  phase.value = 'error';
  message.value = settingsFeedbackForResult(result).message;
}

async function createBackup(): Promise<void> {
  if (!isAdmin.value || backupBusy.value) return;
  if (!window.confirm('确认创建当前数据库备份？本轮不会提供 restore、cleanup 或 repair。')) return;
  const owner = props.owner;
  const version = requestVersion;
  backupBusy.value = true;
  feedback.value = null;
  backup.value = null;
  try {
    const result = await owner.backupDatabase();
    if (version !== requestVersion || owner !== props.owner || !isAdmin.value) return;
    feedback.value = settingsFeedbackForResult(result);
    if (result.status === 'completed') backup.value = result.value;
  } finally {
    backupBusy.value = false;
  }
}

watch([() => props.owner, () => props.role], () => {
  void loadStatus();
}, { immediate: true });

onBeforeUnmount(() => {
  requestVersion += 1;
  detachPanelState();
  clearReadState();
});
</script>

<template>
  <CvPanel
    title="数据库维护"
    description="首轮只读取 status 与创建 backup；restore、cleanup、repair、全局 reset 延期。"
    data-settings-section="database"
    data-settings-database-no-path="true"
  >
    <CvInlineAlert
      v-if="!isAdmin"
      tone="info"
      title="仅 Admin 可用"
    >
      数据库维护 endpoint 保持现有 Admin policy，Engineer 不会发起请求。
    </CvInlineAlert>
    <template v-else>
      <CvInlineAlert
        v-if="phase === 'loading'"
        tone="info"
        title="正在读取数据库状态"
      >
        正在读取服务端维护投影。
      </CvInlineAlert>
      <CvInlineAlert
        v-else-if="phase === 'error'"
        tone="error"
        title="数据库状态读取失败"
      >
        {{ message }}
      </CvInlineAlert>
      <template v-if="status">
        <div class="settings-database__heading">
          <div>
            <strong>当前状态</strong>
            <p>路径字段由 decoder 丢弃，不进入设置投影或截图。</p>
          </div>
          <CvStatusBadge
            :tone="statusTone(status.state)"
            :label="status.state"
          />
        </div>
        <dl class="settings-database__grid">
          <div><dt>数据库存在</dt><dd>{{ status.exists ? '是' : '否' }}</dd></div>
          <div><dt>Schema</dt><dd>{{ status.schemaVersion }} / {{ status.currentSchemaVersion }}</dd></div>
          <div><dt>完整性检查</dt><dd>{{ status.integrityCheck || '未运行' }}</dd></div>
          <div><dt>外键违规</dt><dd>{{ status.foreignKeyViolationCount }}</dd></div>
          <div><dt>数据库大小</dt><dd>{{ formatSettingsBytes(status.databaseSizeBytes) }}</dd></div>
          <div><dt>WAL 大小</dt><dd>{{ formatSettingsBytes(status.walSizeBytes) }}</dd></div>
          <div><dt>运行包文件</dt><dd>{{ status.packageFileCount }}</dd></div>
          <div><dt>待处理迁移</dt><dd>{{ status.pendingMigrations.length }}</dd></div>
        </dl>
        <CvInlineAlert
          v-if="status.issues.length || status.pendingMigrations.length || status.missingSchemaItems.length"
          class="settings-database__issues"
          tone="warning"
          title="服务端报告待处理问题"
        >
          {{ [...status.issues, ...status.pendingMigrations, ...status.missingSchemaItems].join('；') }}
        </CvInlineAlert>
      </template>
      <div class="settings-database__backup">
        <div>
          <strong>创建数据库备份</strong>
          <p>结果只显示时间和大小，不显示或保存 backup path。</p>
        </div>
        <CvButton
          variant="primary"
          size="sm"
          :loading="backupBusy"
          loading-label="正在创建数据库备份"
          @click="createBackup"
        >
          创建备份
        </CvButton>
      </div>
      <dl
        v-if="backup"
        class="settings-database__backup-result"
        data-settings-backup-result
      >
        <div><dt>创建时间</dt><dd>{{ backup.createdAtUtc }}</dd></div>
        <div><dt>备份大小</dt><dd>{{ formatSettingsBytes(backup.sizeBytes) }}</dd></div>
        <div><dt>数据库大小</dt><dd>{{ formatSettingsBytes(backup.databaseSizeBytes) }}</dd></div>
        <div><dt>运行包</dt><dd>{{ backup.packageFileCount }} 个文件 / {{ formatSettingsBytes(backup.packageBytes) }}</dd></div>
      </dl>
      <CvInlineAlert
        class="settings-database__deferred"
        tone="info"
        title="延期操作"
      >
        restore、cleanup、repair 和全局 reset 本轮没有入口，也不会由前端直接操作 SQLite。
      </CvInlineAlert>
    </template>
  </CvPanel>
  <div
    v-if="feedback"
    class="settings-database__feedback"
  >
    <CvInlineAlert
      :tone="feedback.kind === 'saved' ? 'success' : feedback.kind === 'unknown' ? 'warning' : 'error'"
      :title="feedback.kind === 'saved' ? '备份操作已完成' : '备份操作未完成'"
    >
      {{ feedback.message }}
    </CvInlineAlert>
  </div>
</template>

<style scoped>
.settings-database__heading, .settings-database__backup { display: flex; min-width: 0; align-items: center; justify-content: space-between; gap: var(--cv-space-4); }
.settings-database__heading strong, .settings-database__backup strong { color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); }
.settings-database__heading p, .settings-database__backup p { margin: var(--cv-space-1) 0 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
.settings-database__grid, .settings-database__backup-result { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: var(--cv-space-2); margin: var(--cv-space-4) 0 0; }
.settings-database__grid > div, .settings-database__backup-result > div { min-width: 0; padding: var(--cv-space-2) var(--cv-space-3); border: 1px solid var(--cv-border-subtle); border-radius: var(--cv-radius-sm); background: var(--cv-surface-page); }
.settings-database__grid dt, .settings-database__backup-result dt { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.settings-database__grid dd, .settings-database__backup-result dd { margin: var(--cv-space-1) 0 0; color: var(--cv-text-primary); font-size: var(--cv-font-size-xs); overflow-wrap: anywhere; }
.settings-database__issues, .settings-database__deferred, .settings-database__feedback { margin-top: var(--cv-space-4); }
.settings-database__backup { margin-top: var(--cv-space-5); padding-top: var(--cv-space-4); border-top: 1px solid var(--cv-border-subtle); }
@media (max-width: 900px) { .settings-database__grid, .settings-database__backup-result { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
@media (max-width: 560px) { .settings-database__heading, .settings-database__backup { align-items: stretch; flex-direction: column; } .settings-database__grid, .settings-database__backup-result { grid-template-columns: 1fr; } }
</style>
