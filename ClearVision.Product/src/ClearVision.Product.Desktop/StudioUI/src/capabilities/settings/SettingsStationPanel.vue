<script setup lang="ts">
import { computed, onBeforeUnmount, onDeactivated, reactive, shallowRef, watch } from 'vue';
import {
  CvButton,
  CvField,
  CvInlineAlert,
  CvPanel,
  CvSelect,
  CvStatusBadge,
  type CvSelectOption,
  type CvStatusTone
} from '@/design-system';
import type { StationCommunicationProjectionV1 } from './decoder';
import type { SettingsOwner } from './settingsOwner';
import { settingsFeedbackForResult, type SettingsFeedback } from './settingsViewModel';

const props = defineProps<{
  owner: SettingsOwner;
  role: string | null;
}>();

const canManage = computed(() => props.role === 'Admin');
const projection = computed(() => props.owner.projection.station);
const phase = shallowRef<'idle' | 'loading' | 'ready' | 'forbidden' | 'error'>('idle');
const readMessage = shallowRef<string | null>(null);
const feedback = shallowRef<SettingsFeedback | null>(null);
const loadBusy = shallowRef(false);
const mutationBusy = shallowRef(false);
const requestVersion = shallowRef(0);
const tokenMode = shallowRef<'preserve' | 'replace'>('preserve');
const tokenDraft = shallowRef('');

const draft = reactive({
  mode: 'Disabled',
  port: '',
  lanHost: '',
  localStationSyncEnabled: false
});
const baseline = shallowRef(copyProjection(null));

const modeOptions: readonly CvSelectOption[] = Object.freeze([
  { value: 'Disabled', label: 'Disabled（关闭）' },
  { value: 'LocalLoopback', label: 'LocalLoopback（本机）' },
  { value: 'LanController', label: 'LanController（局域网）' }
]);
const tokenModeOptions: readonly CvSelectOption[] = Object.freeze([
  { value: 'preserve', label: '保留当前 token（默认）' },
  { value: 'replace', label: '替换 token' }
]);

const dirty = computed(() =>
  draft.mode !== baseline.value.mode ||
  draft.port !== baseline.value.port ||
  draft.lanHost !== baseline.value.lanHost ||
  draft.localStationSyncEnabled !== baseline.value.localStationSyncEnabled ||
  tokenMode.value === 'replace' ||
  tokenDraft.value.length > 0
);
const pending = computed(() => mutationBusy.value);

let detachPanelState = props.owner.registerPanelState('station', () => ({
  dirty: dirty.value,
  pending: pending.value
}));

const restartSummary = computed(() => {
  const value = projection.value;
  if (!value) return '尚未读取';
  if (value.requiresRestart.studio || value.requiresRestart.localStation) {
    return '已保存，尚未生效，需要重启';
  }
  return '已保存，当前运行配置一致';
});

const restartTone = computed<CvStatusTone>(() => {
  const value = projection.value;
  if (!value) return 'idle';
  return value.requiresRestart.studio || value.requiresRestart.localStation ? 'warning' : 'ok';
});

function copyProjection(value: StationCommunicationProjectionV1 | null) {
  return {
    mode: value?.mode ?? 'Disabled',
    port: value ? String(value.port) : '',
    lanHost: value?.lanHost ?? '',
    localStationSyncEnabled: value?.localStationSyncEnabled ?? false
  };
}

function applyProjection(value: StationCommunicationProjectionV1 | null, forceDraft: boolean): void {
  const wasDirty = dirty.value;
  const next = copyProjection(value);
  baseline.value = next;
  if (forceDraft || !wasDirty) Object.assign(draft, next);
}

function clearTokenInput(): void {
  tokenDraft.value = '';
  tokenMode.value = 'preserve';
}

function resultMessage(result: Parameters<typeof settingsFeedbackForResult>[0]): string {
  return settingsFeedbackForResult(result).message;
}

function bindPanelState(owner: SettingsOwner): void {
  detachPanelState();
  detachPanelState = owner.registerPanelState('station', () => ({
    dirty: dirty.value,
    pending: pending.value
  }));
}

async function loadStation(): Promise<void> {
  const owner = props.owner;
  const version = requestVersion.value + 1;
  requestVersion.value = version;
  clearTokenInput();
  feedback.value = null;
  if (!canManage.value) {
    phase.value = 'forbidden';
    readMessage.value = 'Station 通信设置 endpoint 仅允许 Admin 读取。';
    return;
  }

  phase.value = 'loading';
  loadBusy.value = true;
  try {
    const result = await owner.readStationCommunication();
    if (version !== requestVersion.value || owner !== props.owner) return;
    if (result.status === 'completed') {
      phase.value = 'ready';
      readMessage.value = null;
    } else {
      phase.value = 'error';
      readMessage.value = resultMessage(result);
    }
  } finally {
    if (version === requestVersion.value && owner === props.owner) {
      loadBusy.value = false;
      owner.refreshPanelState();
    }
  }
}

function discard(): void {
  Object.assign(draft, baseline.value);
  clearTokenInput();
  feedback.value = null;
}

function setTokenMode(value: string): void {
  tokenMode.value = value === 'replace' ? 'replace' : 'preserve';
  if (tokenMode.value === 'preserve') tokenDraft.value = '';
}

async function save(): Promise<void> {
  if (!canManage.value || mutationBusy.value || !dirty.value) return;
  if (tokenMode.value === 'replace' && !tokenDraft.value.trim()) {
    feedback.value = {
      kind: 'error',
      message: '选择替换 token 后必须输入新 token。',
      savedLabel: '未保存',
      effectiveLabel: '未生效',
      restartLabel: '不适用'
    };
    return;
  }

  const owner = props.owner;
  mutationBusy.value = true;
  feedback.value = null;
  try {
    const result = await owner.saveStationCommunication({
      mode: draft.mode,
      port: Number(draft.port),
      lanHost: draft.lanHost.trim(),
      localStationSyncEnabled: draft.localStationSyncEnabled,
      ...(tokenMode.value === 'replace' ? { sharedToken: tokenDraft.value } : {})
    });
    if (owner !== props.owner) return;
    feedback.value = settingsFeedbackForResult(result);
    if (result.status === 'completed') {
      applyProjection(result.value, true);
      clearTokenInput();
      phase.value = 'ready';
    }
  } finally {
    clearTokenInput();
    mutationBusy.value = false;
    owner.refreshPanelState();
  }
}

async function regenerateToken(): Promise<void> {
  if (!canManage.value || mutationBusy.value) return;
  if (!window.confirm('确认重新生成 Station token？现有 Station 将需要使用新配置。')) return;
  const owner = props.owner;
  mutationBusy.value = true;
  feedback.value = null;
  try {
    const result = await owner.runStationTokenOperation('regenerate');
    if (owner !== props.owner) return;
    feedback.value = settingsFeedbackForResult(result);
    if (result.status === 'completed') {
      applyProjection(result.value.settings ?? owner.projection.station, true);
      clearTokenInput();
    }
  } finally {
    clearTokenInput();
    mutationBusy.value = false;
    owner.refreshPanelState();
  }
}

function modeLabel(value: string): string {
  return modeOptions.find(item => item.value === value)?.label ?? value;
}

watch(projection, value => {
  applyProjection(value, false);
}, { immediate: true });

watch([() => props.owner, canManage], ([owner]) => {
  bindPanelState(owner);
  void loadStation();
}, { immediate: true });

watch([dirty, pending, tokenDraft, tokenMode], () => props.owner.refreshPanelState());

onBeforeUnmount(() => {
  detachPanelState();
  clearTokenInput();
  requestVersion.value += 1;
});

onDeactivated(() => {
  clearTokenInput();
  props.owner.refreshPanelState();
});
</script>

<template>
  <div
    class="settings-station"
    data-settings-station
  >
    <CvInlineAlert
      v-if="!canManage"
      tone="info"
      title="Station 通信设置仅 Admin 可用"
    >
      当前角色不能读取或修改 Station 系统级通信配置。Station 管理页的运行包、部署和启停能力不在此处提供。
    </CvInlineAlert>

    <template v-else>
      <CvInlineAlert
        v-if="phase === 'error'"
        tone="error"
        title="Station 通信配置读取失败"
      >
        {{ readMessage }}
      </CvInlineAlert>

      <CvPanel
        title="Station 通信"
        description="配置 Studio 与本机/局域网 Station 的通信参数；保存只写入专用 Station endpoint，不会自动重启进程。"
        data-settings-station-communication
      >
        <form
          class="settings-station__form"
          @submit.prevent="save"
        >
          <CvSelect
            v-model="draft.mode"
            label="通信模式"
            name="stationMode"
            :options="modeOptions"
            :disabled="phase === 'loading' || mutationBusy"
          />
          <CvField
            v-model="draft.port"
            label="Studio 端口"
            name="stationPort"
            type="number"
            min="1"
            max="65535"
            :readonly="phase === 'loading' || mutationBusy"
          />
          <CvField
            v-model="draft.lanHost"
            label="局域网地址"
            name="stationLanHost"
            placeholder="LanController 模式使用"
            :readonly="phase === 'loading' || mutationBusy"
          />
          <label class="settings-station__toggle">
            <input
              v-model="draft.localStationSyncEnabled"
              type="checkbox"
              :disabled="phase === 'loading' || mutationBusy"
            >
            <span>
              <strong>启用本机 Station 同步</strong>
              <small>保存后由 Station 自身按 restart-required 结果重新加载。</small>
            </span>
          </label>
        </form>

        <div class="settings-station__token-row">
          <div class="settings-station__token-copy">
            <span class="settings-station__eyebrow">Token</span>
            <strong>{{ projection?.token.hasToken ? (projection.token.mask || '已配置（已掩码）') : '未配置' }}</strong>
            <small>真实 token 不会回显、持久化到前端或进入日志。</small>
          </div>
          <CvSelect
            :model-value="tokenMode"
            label="Token 操作"
            name="stationTokenOperation"
            :options="tokenModeOptions"
            :disabled="mutationBusy"
            data-settings-station-token-operation
            @update:model-value="setTokenMode"
          />
          <CvField
            v-if="tokenMode === 'replace'"
            v-model="tokenDraft"
            label="新 token"
            name="stationToken"
            type="password"
            autocomplete="new-password"
            placeholder="仅在替换时临时输入"
            :readonly="mutationBusy"
          />
          <CvButton
            size="sm"
            variant="quiet"
            :loading="mutationBusy"
            :disabled="mutationBusy || phase === 'loading'"
            loading-label="正在生成"
            data-settings-station-regenerate
            @click="regenerateToken"
          >
            重新生成 token
          </CvButton>
        </div>

        <template #footer>
          <div class="settings-station__footer">
            <CvStatusBadge
              :tone="restartTone"
              :label="restartSummary"
            />
            <div class="settings-station__actions">
              <CvButton
                size="sm"
                variant="quiet"
                :disabled="!dirty || mutationBusy"
                @click="discard"
              >
                放弃修改
              </CvButton>
              <CvButton
                size="sm"
                variant="primary"
                :loading="mutationBusy"
                :disabled="!dirty || mutationBusy || phase === 'loading'"
                loading-label="正在保存 Station 配置"
                data-settings-station-save
                @click="save"
              >
                保存通信配置
              </CvButton>
            </div>
          </div>
        </template>
      </CvPanel>

      <CvPanel
        title="已保存 / 生效状态"
        description="服务端返回的 saved projection 与当前运行配置分开显示。需要重启时不会由 Studio 自动执行。"
        data-settings-station-effective
      >
        <div class="settings-station__state-grid">
          <div class="settings-station__state-item">
            <span>已保存模式</span>
            <strong>{{ projection ? modeLabel(projection.mode) : '尚未读取' }}</strong>
          </div>
          <div class="settings-station__state-item">
            <span>Studio 当前运行</span>
            <strong>{{ projection?.currentRunning.studioEnabled ? `${projection.currentRunning.studioListenMode} : ${projection.currentRunning.studioPort}` : '未启用' }}</strong>
          </div>
          <div class="settings-station__state-item">
            <span>Studio 重启</span>
            <strong>{{ projection?.requiresRestart.studio ? '需要重启' : '无需重启' }}</strong>
          </div>
          <div class="settings-station__state-item">
            <span>本机 Station 重启</span>
            <strong>{{ projection?.requiresRestart.localStation ? '需要重启' : '无需重启' }}</strong>
          </div>
        </div>
        <CvInlineAlert
          v-if="projection?.requiresRestart.studio || projection?.requiresRestart.localStation"
          tone="warning"
          data-settings-station-restart-required
          title="配置已保存，尚未生效"
        >
          请按后端提示重启对应 Studio 或本机 Station；本页面不会自动重启任何进程或服务。
        </CvInlineAlert>
        <CvInlineAlert
          v-else-if="projection"
          tone="success"
          title="当前运行配置与已保存配置一致"
        >
          当前没有后端声明的 restart-required 项。
        </CvInlineAlert>
      </CvPanel>

      <CvPanel
        title="通信诊断"
        description="只展示 Station endpoint 返回的服务可用性提示，不把诊断结果当成配置 mutation 的 authority。"
        data-settings-station-diagnostics
      >
        <ul
          v-if="projection?.diagnostics.length"
          class="settings-station__diagnostics"
        >
          <li
            v-for="item in projection.diagnostics"
            :key="item"
          >
            {{ item }}
          </li>
        </ul>
        <p
          v-else
          class="settings-station__empty"
        >
          尚未返回诊断信息。
        </p>
        <div class="settings-station__endpoints">
          <span>Studio 本地地址：{{ projection?.localStationBaseUrl || '未启用' }}</span>
          <span>Station Hub：{{ projection?.remoteStationHubUrl || '未启用' }}</span>
        </div>
      </CvPanel>
    </template>

    <CvInlineAlert
      v-if="feedback"
      :tone="feedback.kind === 'saved' ? 'success' : feedback.kind === 'unknown' ? 'warning' : 'error'"
      :title="feedback.kind === 'saved' ? 'Station 操作已完成' : feedback.kind === 'unknown' ? 'Station 操作结果未知' : 'Station 操作未完成'"
      data-settings-station-feedback
    >
      {{ feedback.message }}
    </CvInlineAlert>
  </div>
</template>

<style scoped>
.settings-station { display: grid; min-width: 0; gap: var(--cv-density-page-gap); }
.settings-station__form { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); align-items: end; gap: var(--cv-space-4); }
.settings-station__toggle { display: flex; min-height: var(--cv-density-control-height); align-items: center; gap: var(--cv-space-2); color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); }
.settings-station__toggle input { width: 16px; height: 16px; accent-color: var(--cv-color-brand-500); }
.settings-station__toggle span { display: grid; gap: 2px; }
.settings-station__toggle small { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.settings-station__token-row { display: grid; grid-template-columns: minmax(180px, 1fr) minmax(200px, 240px) minmax(220px, 1fr) auto; align-items: end; gap: var(--cv-space-3); margin-top: var(--cv-space-5); padding-top: var(--cv-space-4); border-top: 1px solid var(--cv-border-subtle); }
.settings-station__token-copy { display: grid; min-width: 0; gap: 2px; }
.settings-station__token-copy strong { color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); }
.settings-station__token-copy small { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.settings-station__eyebrow { color: var(--cv-color-brand-text); font-size: var(--cv-font-size-2xs); font-weight: var(--cv-font-weight-semibold); }
.settings-station__footer { display: flex; min-width: 0; align-items: center; justify-content: space-between; gap: var(--cv-space-3); }
.settings-station__actions { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: var(--cv-space-2); }
.settings-station__state-grid { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: var(--cv-space-3); margin-bottom: var(--cv-space-4); }
.settings-station__state-item { display: grid; min-width: 0; gap: var(--cv-space-1); padding: var(--cv-space-3); border: 1px solid var(--cv-border-subtle); border-radius: var(--cv-radius-sm); background: var(--cv-surface-raised); }
.settings-station__state-item span { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.settings-station__state-item strong { overflow: hidden; color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); text-overflow: ellipsis; white-space: nowrap; }
.settings-station__diagnostics { display: grid; gap: var(--cv-space-2); margin: 0; padding-left: 18px; color: var(--cv-text-secondary); font-size: var(--cv-font-size-sm); }
.settings-station__empty { margin: 0; color: var(--cv-text-muted); font-size: var(--cv-font-size-sm); }
.settings-station__endpoints { display: grid; gap: var(--cv-space-1); margin-top: var(--cv-space-4); color: var(--cv-text-muted); font-family: var(--cv-font-family-mono); font-size: var(--cv-font-size-2xs); overflow-wrap: anywhere; }
@media (max-width: 900px) {
  .settings-station__form, .settings-station__token-row { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .settings-station__state-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
}
@media (max-width: 560px) {
  .settings-station__form, .settings-station__token-row, .settings-station__state-grid { grid-template-columns: 1fr; }
  .settings-station__footer { align-items: stretch; flex-direction: column; }
  .settings-station__actions { justify-content: flex-start; }
}
</style>
