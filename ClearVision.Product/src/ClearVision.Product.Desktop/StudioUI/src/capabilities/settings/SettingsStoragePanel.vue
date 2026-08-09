<script setup lang="ts">
import { computed, onBeforeUnmount, reactive, shallowRef, watch } from 'vue';
import { CvButton, CvField, CvInlineAlert, CvPanel } from '@/design-system';
import type { SettingsOwner } from './settingsOwner';
import type { SettingsDiskUsageProjectionV1, SettingsStorageProjectionV1 } from './decoder';
import {
  formatSettingsBytes,
  settingsFeedbackForResult,
  type SettingsFeedback
} from './settingsViewModel';
import SettingsWriteFeedback from './SettingsWriteFeedback.vue';

const props = defineProps<{
  projection: SettingsStorageProjectionV1;
  owner: SettingsOwner;
  canWrite: boolean;
}>();

interface StorageDraft {
  imageSavePath: string;
  savePolicy: string;
  retentionDays: string;
  minFreeSpaceGb: string;
}

function copy(value: SettingsStorageProjectionV1): StorageDraft {
  return {
    imageSavePath: value.imageSavePath,
    savePolicy: value.savePolicy,
    retentionDays: String(value.retentionDays),
    minFreeSpaceGb: String(value.minFreeSpaceGb)
  };
}

const draft = reactive<StorageDraft>(copy(props.projection));
const baseline = shallowRef<StorageDraft>(copy(props.projection));
const busy = shallowRef(false);
const diskBusy = shallowRef(false);
const feedback = shallowRef<SettingsFeedback | null>(null);
const diskUsage = shallowRef<SettingsDiskUsageProjectionV1 | null>(null);
const diskError = shallowRef<string | null>(null);
const dirty = computed(() => JSON.stringify(draft) !== JSON.stringify(baseline.value));
const detachPanelState = props.owner.registerPanelState('storage', () => ({
  dirty: dirty.value,
  pending: busy.value || diskBusy.value
}));
watch([dirty, busy, diskBusy], () => props.owner.refreshPanelState());
const validationMessage = computed(() => {
  const retention = Number(draft.retentionDays);
  const minFree = Number(draft.minFreeSpaceGb);
  if (!Number.isInteger(retention) || retention < 0) return '保留天数必须是非负整数。';
  if (!Number.isFinite(minFree) || minFree < 0) return '最低可用空间必须是非负数。';
  return null;
});

watch(() => props.projection, value => {
  const wasDirty = dirty.value;
  const next = copy(value);
  baseline.value = next;
  if (!wasDirty) Object.assign(draft, next);
  feedback.value = null;
  diskUsage.value = null;
  diskError.value = null;
});

function discard(): void {
  Object.assign(draft, baseline.value);
  feedback.value = null;
}

async function save(): Promise<void> {
  if (!props.canWrite || busy.value || !dirty.value || validationMessage.value) return;
  busy.value = true;
  feedback.value = null;
  try {
    const result = await props.owner.saveGenericSection('storage', {
      imageSavePath: draft.imageSavePath,
      savePolicy: draft.savePolicy,
      retentionDays: Number(draft.retentionDays),
      minFreeSpaceGb: Number(draft.minFreeSpaceGb)
    });
    feedback.value = settingsFeedbackForResult(result);
    if (result.status === 'completed') {
      const next = copy(result.value.config.sections.storage!);
      baseline.value = next;
      Object.assign(draft, next);
    }
  } finally {
    busy.value = false;
  }
}

async function inspectDiskUsage(): Promise<void> {
  if (!props.canWrite || diskBusy.value) return;
  diskBusy.value = true;
  diskError.value = null;
  diskUsage.value = null;
  try {
    const result = await props.owner.readDiskUsage(draft.imageSavePath);
    if (result.status === 'completed') {
      diskUsage.value = result.value;
      return;
    }
    diskError.value = settingsFeedbackForResult(result).message;
  } finally {
    diskBusy.value = false;
  }
}

onBeforeUnmount(() => detachPanelState());
</script>

<template>
  <CvPanel
    title="存储"
    description="保存路径与保留策略由对应范围的应用配置管理；路径选择器和立即清理当前没有可用合同。"
    data-settings-section="storage"
  >
    <div class="settings-form-grid">
      <CvField
        v-model="draft.imageSavePath"
        label="图像保存路径"
        name="imageSavePath"
        :readonly="!canWrite"
        hint="当前没有可用的宿主路径选择器，请手工输入并让服务端校验。"
      />
      <CvField
        v-model="draft.savePolicy"
        label="保存策略"
        name="savePolicy"
        :readonly="!canWrite"
        hint="保持后端既有策略值，不在前端猜测枚举。"
      />
      <CvField
        v-model="draft.retentionDays"
        label="保留天数"
        name="retentionDays"
        type="number"
        :readonly="!canWrite"
      />
      <CvField
        v-model="draft.minFreeSpaceGb"
        label="最低可用空间（GB）"
        name="minFreeSpaceGb"
        type="number"
        :readonly="!canWrite"
        :error="canWrite ? validationMessage ?? undefined : undefined"
      />
    </div>

    <div class="settings-storage__tools">
      <div>
        <strong>磁盘占用</strong>
        <p>仅在点击检查时读取服务端结果，不把路径检查伪装成保存或清理。</p>
      </div>
      <CvButton
        v-if="canWrite"
        size="sm"
        :loading="diskBusy"
        loading-label="正在读取磁盘占用"
        @click="inspectDiskUsage"
      >
        读取磁盘占用
      </CvButton>
    </div>

    <CvInlineAlert
      v-if="diskError"
      tone="error"
      title="磁盘占用读取失败"
    >
      {{ diskError }}
    </CvInlineAlert>
    <dl
      v-if="diskUsage"
      class="settings-storage__usage"
      data-settings-disk-usage
    >
      <div><dt>检查路径</dt><dd>{{ diskUsage.sourcePath }}</dd></div>
      <div><dt>可访问</dt><dd>{{ diskUsage.isAccessible ? '是' : '否' }}</dd></div>
      <div><dt>可写入</dt><dd>{{ diskUsage.canWrite ? '是' : '否' }}</dd></div>
      <div><dt>已使用</dt><dd>{{ diskUsage.usedGb.toFixed(2) }} GB / {{ diskUsage.totalGb.toFixed(2) }} GB</dd></div>
      <div><dt>剩余</dt><dd>{{ diskUsage.freeGb.toFixed(2) }} GB（{{ formatSettingsBytes(diskUsage.freeBytes) }}）</dd></div>
      <div><dt>占用率</dt><dd>{{ diskUsage.usedPercent.toFixed(2) }}%</dd></div>
    </dl>

    <CvInlineAlert
      v-if="!canWrite"
      class="settings-panel__notice"
      tone="info"
      title="当前为安全视图"
    >
      当前角色不能读取仅管理员可见的磁盘诊断，也不能保存存储设置。
    </CvInlineAlert>

    <template #footer>
      <div class="settings-panel__footer">
        <span class="settings-panel__dirty">{{ dirty ? '有未保存修改' : '与服务端配置一致' }}</span>
        <div class="settings-panel__actions">
          <CvButton
            v-if="canWrite"
            variant="quiet"
            size="sm"
            :disabled="!dirty || busy"
            @click="discard"
          >
            放弃修改
          </CvButton>
          <CvButton
            v-if="canWrite"
            variant="primary"
            size="sm"
            :loading="busy"
            loading-label="正在保存存储设置"
            :disabled="!dirty || Boolean(validationMessage)"
            @click="save"
          >
            保存存储设置
          </CvButton>
        </div>
      </div>
    </template>
  </CvPanel>

  <SettingsWriteFeedback :feedback="feedback" />
</template>

<style scoped>
.settings-form-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--cv-space-4); }
.settings-storage__tools {
  display: flex;
  min-width: 0;
  align-items: center;
  justify-content: space-between;
  gap: var(--cv-space-4);
  margin-top: var(--cv-space-5);
  padding-top: var(--cv-space-4);
  border-top: 1px solid var(--cv-border-subtle);
}
.settings-storage__tools strong { color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); }
.settings-storage__tools p { margin: var(--cv-space-1) 0 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
.settings-storage__usage { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: var(--cv-space-2); margin: var(--cv-space-4) 0 0; }
.settings-storage__usage > div { min-width: 0; padding: var(--cv-space-2) var(--cv-space-3); background: var(--cv-surface-page); border: 1px solid var(--cv-border-subtle); border-radius: var(--cv-radius-sm); }
.settings-storage__usage dt { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.settings-storage__usage dd { margin: var(--cv-space-1) 0 0; color: var(--cv-text-primary); font-size: var(--cv-font-size-xs); overflow-wrap: anywhere; }
.settings-panel__notice { margin-top: var(--cv-space-4); }
.settings-panel__footer { display: flex; min-width: 0; align-items: center; justify-content: space-between; gap: var(--cv-space-3); }
.settings-panel__dirty { color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); }
.settings-panel__actions { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: var(--cv-space-2); }
@media (max-width: 720px) {
  .settings-form-grid, .settings-storage__usage { grid-template-columns: 1fr; }
  .settings-storage__tools, .settings-panel__footer { align-items: stretch; flex-direction: column; }
  .settings-panel__actions { justify-content: flex-start; }
}
</style>
