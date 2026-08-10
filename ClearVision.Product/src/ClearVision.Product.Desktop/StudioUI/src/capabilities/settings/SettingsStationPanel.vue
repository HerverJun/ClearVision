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
  { value: 'Disabled', label: '关闭' },
  { value: 'LocalLoopback', label: '本机通信' },
  { value: 'LanController', label: '局域网控制' }
]);
const tokenModeOptions: readonly CvSelectOption[] = Object.freeze([
  { value: 'preserve', label: '保留当前访问令牌' },
  { value: 'replace', label: '替换访问令牌' }
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
const hasStationUnknown = computed(() => props.owner.projection.unknownOutcomeKeys.includes('station-communication'));
const canRegenerateToken = computed(() =>
  canManage.value &&
  !mutationBusy.value &&
  !loadBusy.value &&
  !dirty.value &&
  !hasStationUnknown.value &&
  draft.mode === 'LocalLoopback' &&
  draft.localStationSyncEnabled
);
const regenerateDisabledReason = computed(() => {
  if (draft.mode === 'LanController') return '局域网控制模式没有获批的安全令牌交接，请手动替换令牌。';
  if (hasStationUnknown.value) return '工作站令牌操作结果未知，请先刷新服务端状态再重试。';
  if (dirty.value) return '工作站通信配置存在未保存修改，请先保存或放弃草稿。';
  if (draft.mode !== 'LocalLoopback' || !draft.localStationSyncEnabled) {
    return '仅启用本机同步时可以重新生成令牌。';
  }
  return '';
});

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
    readMessage.value = '工作站通信设置仅允许管理员读取。';
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

async function refreshAuthority(): Promise<void> {
  if (loadBusy.value || mutationBusy.value) return;
  if (dirty.value) {
    feedback.value = {
      kind: 'error',
      message: '工作站通信配置存在未保存修改，请先保存或放弃草稿再刷新。',
      savedLabel: '未保存',
      effectiveLabel: '未生效',
      restartLabel: '不适用'
    };
    return;
  }
  await loadStation();
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
  if (hasStationUnknown.value) {
    feedback.value = {
      kind: 'unknown',
      message: '工作站通信操作结果未知，请先刷新服务端状态再重试。',
      savedLabel: '未知',
      effectiveLabel: '未知',
      restartLabel: '未知'
    };
    return;
  }
  if (tokenMode.value === 'replace' && !tokenDraft.value.trim()) {
    feedback.value = {
      kind: 'error',
      message: '选择替换访问令牌后，必须输入新的访问令牌。',
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
  if (!canRegenerateToken.value) {
    feedback.value = {
      kind: 'error',
      message: regenerateDisabledReason.value,
      savedLabel: '未保存',
      effectiveLabel: '未生效',
      restartLabel: '不适用'
    };
    return;
  }
  if (!window.confirm('确认重新生成工作站访问令牌？现有工作站需要更新通信配置。')) return;
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

function runningModeLabel(value: string): string {
  if (value === 'Loopback' || value === 'LocalLoopback') return '本机回环';
  if (value === 'Lan' || value === 'LanController') return '局域网';
  return value || '未提供';
}

const stationDiagnosticLabels: Readonly<Record<string, string>> = Object.freeze({
  'Station communication fixture is available.': '测试环境中的工作站通信服务可用。',
  'Studio ingress is configured.': 'Studio 通信入口已配置。',
  'Restart is required to apply the saved ingress.': '需要重启后应用已保存的通信入口。'
});

function stationDiagnosticLabel(value: string): string {
  return stationDiagnosticLabels[value] ?? value;
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
      title="工作站通信设置仅管理员可用"
    >
      当前角色不能读取或修改工作站通信配置。运行包、部署和启停仍在工作站管理页中操作。
    </CvInlineAlert>

    <template v-else>
      <CvInlineAlert
        v-if="phase === 'error'"
        tone="error"
        title="工作站通信配置读取失败"
      >
        {{ readMessage }}
      </CvInlineAlert>

      <CvPanel
        title="工作站通信"
        description="配置 Studio 与本机或局域网工作站的连接方式。保存后不会自动重启 Studio 或工作站。"
        data-settings-station-communication
      >
        <template #actions>
          <CvButton
            size="sm"
            variant="quiet"
            :loading="loadBusy"
            :disabled="loadBusy || mutationBusy || dirty"
            loading-label="正在刷新工作站配置"
            data-settings-station-authority-refresh
            @click="refreshAuthority"
          >
            刷新工作站配置
          </CvButton>
        </template>
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
            placeholder="仅在“局域网控制”模式使用"
            :readonly="phase === 'loading' || mutationBusy"
          />
          <label class="settings-station__toggle">
            <input
              v-model="draft.localStationSyncEnabled"
              type="checkbox"
              :disabled="phase === 'loading' || mutationBusy"
            >
            <span>
              <strong>启用本机工作站同步</strong>
              <small>保存后由工作站根据“需要重启”状态重新加载。</small>
            </span>
          </label>
        </form>

        <div class="settings-station__token-row">
          <div class="settings-station__token-copy">
            <span class="settings-station__eyebrow">访问令牌</span>
            <strong>{{ projection?.token.hasToken ? (projection.token.mask || '已配置（已掩码）') : '未配置' }}</strong>
            <small>完整令牌不会回显，也不会保存在浏览器或写入日志。</small>
          </div>
          <CvSelect
            :model-value="tokenMode"
            label="访问令牌操作"
            name="stationTokenOperation"
            :options="tokenModeOptions"
            :disabled="mutationBusy"
            data-settings-station-token-operation
            @update:model-value="setTokenMode"
          />
          <CvField
            v-if="tokenMode === 'replace'"
            v-model="tokenDraft"
            label="新访问令牌"
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
            :disabled="!canRegenerateToken"
            loading-label="正在生成"
            data-settings-station-regenerate
            @click="regenerateToken"
          >
            重新生成令牌
          </CvButton>
          <small
            v-if="regenerateDisabledReason"
            class="settings-station__token-hint"
            data-settings-station-token-hint
          >
            {{ regenerateDisabledReason }}
          </small>
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
                loading-label="正在保存工作站配置"
                data-settings-station-save
                @click="save"
              >
                保存通信配置
              </CvButton>
            </div>
          </div>
        </template>
      </CvPanel>

      <section
        class="settings-station__section"
        data-settings-station-effective
      >
        <header class="settings-station__section-header">
          <h3>保存与生效状态</h3>
          <p>分别核对已保存配置和当前运行状态；需要重启时，Studio 不会自动执行。</p>
        </header>
        <div class="settings-station__state-grid">
          <div class="settings-station__state-item">
            <span>已保存模式</span>
            <strong>{{ projection ? modeLabel(projection.mode) : '尚未读取' }}</strong>
          </div>
          <div class="settings-station__state-item">
            <span>Studio 当前运行</span>
            <strong>{{ projection?.currentRunning.studioEnabled ? `${runningModeLabel(projection.currentRunning.studioListenMode)} · ${projection.currentRunning.studioPort}` : '未启用' }}</strong>
          </div>
          <div class="settings-station__state-item">
            <span>Studio 重启</span>
            <strong>{{ projection?.requiresRestart.studio ? '需要重启' : '无需重启' }}</strong>
          </div>
          <div class="settings-station__state-item">
            <span>本机工作站重启</span>
            <strong>{{ projection?.requiresRestart.localStation ? '需要重启' : '无需重启' }}</strong>
          </div>
        </div>
        <CvInlineAlert
          v-if="projection?.requiresRestart.studio || projection?.requiresRestart.localStation"
          tone="warning"
          data-settings-station-restart-required
          title="配置已保存，尚未生效"
        >
          请根据提示重启 Studio 或本机工作站。本页面不会自动重启任何进程或服务。
        </CvInlineAlert>
        <CvInlineAlert
          v-else-if="projection"
          tone="success"
          title="当前运行配置与已保存配置一致"
        >
          当前没有需要重启的配置项。
        </CvInlineAlert>
      </section>

      <section
        class="settings-station__section"
        data-settings-station-diagnostics
      >
        <header class="settings-station__section-header">
          <h3>通信诊断</h3>
          <p>通信服务返回的可用性信息仅用于排查问题，不会覆盖已保存配置。</p>
        </header>
        <ul
          v-if="projection?.diagnostics.length"
          class="settings-station__diagnostics"
        >
          <li
            v-for="item in projection.diagnostics"
            :key="item"
          >
            {{ stationDiagnosticLabel(item) }}
          </li>
        </ul>
        <p
          v-else
          class="settings-station__empty"
        >
          尚未返回诊断信息。
        </p>
        <div class="settings-station__endpoints">
          <span>本机工作站地址：{{ projection?.localStationBaseUrl || '未启用' }}</span>
          <span>远程工作站中心：{{ projection?.remoteStationHubUrl || '未启用' }}</span>
        </div>
      </section>
    </template>

    <CvInlineAlert
      v-if="feedback"
      :tone="feedback.kind === 'saved' || feedback.kind === 'completed' ? 'success' : feedback.kind === 'unknown' ? 'warning' : 'error'"
      :title="feedback.kind === 'saved' || feedback.kind === 'completed' ? '工作站操作已完成' : feedback.kind === 'unknown' ? '工作站操作结果未知' : '工作站操作未完成'"
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
.settings-station__token-hint { grid-column: 1 / -1; color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.settings-station__eyebrow { color: var(--cv-color-brand-text); font-size: var(--cv-font-size-2xs); font-weight: var(--cv-font-weight-semibold); }
.settings-station__footer { display: flex; min-width: 0; align-items: center; justify-content: space-between; gap: var(--cv-space-3); }
.settings-station__actions { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: var(--cv-space-2); }
.settings-station__section { min-width: 0; padding: var(--cv-space-5) 0 0; border-top: 1px solid var(--cv-border-subtle); }
.settings-station__section-header h3 { margin: 0; color: var(--cv-text-primary); font-size: var(--cv-type-section-title-size); }
.settings-station__section-header p { max-width: 760px; margin: var(--cv-space-1) 0 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); line-height: var(--cv-line-height-normal); }
.settings-station__state-grid { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: var(--cv-space-4); margin: var(--cv-space-4) 0; padding: var(--cv-space-3) 0; border-block: 1px solid var(--cv-border-subtle); }
.settings-station__state-item { display: grid; min-width: 0; gap: var(--cv-space-1); }
.settings-station__state-item span { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.settings-station__state-item strong { overflow: hidden; color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); text-overflow: ellipsis; white-space: nowrap; }
.settings-station__diagnostics { display: grid; gap: var(--cv-space-2); margin: var(--cv-space-4) 0 0; padding-left: 18px; color: var(--cv-text-secondary); font-size: var(--cv-font-size-sm); }
.settings-station__empty { margin: var(--cv-space-4) 0 0; color: var(--cv-text-muted); font-size: var(--cv-font-size-sm); }
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
