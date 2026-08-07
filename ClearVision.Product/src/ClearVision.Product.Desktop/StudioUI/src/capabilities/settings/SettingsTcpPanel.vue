<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, shallowRef, watch } from 'vue';
import { CvButton, CvField, CvInlineAlert, CvPanel, CvSelect, CvStatusBadge } from '@/design-system';
import { CvIcon } from '@/design-system/icons';
import {
  TCP_ENCODINGS,
  TCP_FRAME_MODES,
  TCP_LINE_ENDINGS,
  TCP_MODES,
  type DeviceValidationIssueV1,
  type TcpFrameV1,
  type TcpModeV1,
  type TcpProfileStatusV1,
  type TcpProfileV1
} from './deviceContracts';
import type { TcpSendRequestV1 } from './deviceApiAdapter';
import { settingsOperationResultMessage, type SettingsOwner } from './settingsOwner';
import type { SettingsOperationKind } from './contracts';

const props = defineProps<{
  owner: SettingsOwner;
  canWrite: boolean;
}>();

interface TcpDraft {
  id: string;
  name: string;
  enabled: boolean;
  mode: TcpModeV1;
  remoteHost: string;
  remotePort: string;
  localHost: string;
  localPort: string;
  encoding: TcpProfileV1['encoding'];
  frameMode: TcpProfileV1['frameMode'];
  fixedLength: string;
  lineEnding: TcpProfileV1['lineEnding'];
  timeoutMs: string;
  keepAlive: boolean;
  reconnect: boolean;
  connectOnStartup: boolean;
  description: string;
}

const phase = shallowRef<'idle' | 'loading' | 'ready' | 'error'>('idle');
const pendingAction = shallowRef<string | null>(null);
const selectedId = shallowRef('');
const baseline = shallowRef<readonly TcpProfileV1[]>(Object.freeze([]));
const drafts = reactive<TcpDraft[]>([]);
const feedback = shallowRef<{ tone: 'success' | 'warning' | 'error' | 'info'; title: string; message: string } | null>(null);
const validationIssues = shallowRef<readonly DeviceValidationIssueV1[]>([]);
const sendMode = shallowRef<'text' | 'hex'>('text');
const sendPayload = shallowRef('');
const waitResponse = shallowRef(true);
const responseTimeoutMs = shallowRef('5000');
const lastResponse = shallowRef<string | null>(null);

const modeOptions = Object.freeze(TCP_MODES.map(value => ({ value, label: value === 'Client' ? 'Client 客户端' : 'Server 服务端' })));
const encodingOptions = Object.freeze(TCP_ENCODINGS.map(value => ({ value, label: value })));
const frameModeOptions = Object.freeze(TCP_FRAME_MODES.map(value => ({
  value,
  label: value === 'FixedLength' ? 'FixedLength 定长' : value === 'Line' ? 'Line 行' : value === 'Hex' ? 'Hex' : 'Raw 原始'
})));
const lineEndingOptions = Object.freeze(TCP_LINE_ENDINGS.map(value => ({ value, label: value })));
const sendModeOptions = Object.freeze([
  { value: 'text', label: '文本' },
  { value: 'hex', label: 'HEX' }
]);

const selectedDraft = computed(() => drafts.find(item => item.id === selectedId.value) ?? null);
const selectedStatus = computed<TcpProfileStatusV1 | null>(() => {
  const status = props.owner.projection.device.tcpStatuses[selectedId.value];
  return status ?? null;
});
const selectedFrames = computed<readonly TcpFrameV1[]>(() => props.owner.projection.device.tcpFrames[selectedId.value] ?? []);
const isBusy = computed(() => pendingAction.value !== null);
const normalizedDrafts = computed(() => drafts.map(profilePayload));
const dirty = computed(() => JSON.stringify(normalizedDrafts.value) !== JSON.stringify(baseline.value));
const selectedProfileDirty = computed(() => {
  const draft = selectedDraft.value;
  if (!draft) return false;
  const saved = baseline.value.find(profile => profile.id === draft.id);
  return !saved || JSON.stringify(profilePayload(draft)) !== JSON.stringify(saved);
});
const selectedProfileSaved = computed(() => Boolean(selectedDraft.value) && !selectedProfileDirty.value);
const localErrors = computed(() => validateProfile(selectedDraft.value));
const canOperate = computed(() => props.owner.projection.role === 'Admin' || props.owner.projection.role === 'Engineer');
const runtimeLabel = computed(() => {
  if (!selectedStatus.value) return '未读取';
  if (selectedStatus.value.isListening) return '监听中';
  if (selectedStatus.value.isConnected) return '已连接';
  if (selectedStatus.value.lastError) return '错误';
  return '未连接';
});
const runtimeTone = computed(() => runtimeLabel.value === '已连接' || runtimeLabel.value === '监听中' ? 'ok' : runtimeLabel.value === '错误' ? 'error' : 'idle');
const detachPanelState = props.owner.registerPanelState('tcp', () => ({
  dirty: dirty.value,
  pending: isBusy.value
}));

watch([dirty, isBusy], () => props.owner.refreshPanelState());

function profileId(): string {
  if (globalThis.crypto?.randomUUID) return `tcp_${globalThis.crypto.randomUUID().slice(0, 8)}`;
  return `tcp_${Date.now().toString(36)}`;
}

function emptyDraft(mode: TcpModeV1 = 'Client'): TcpDraft {
  return {
    id: profileId(),
    name: mode === 'Client' ? 'TCP 客户端' : 'TCP 服务端',
    enabled: true,
    mode,
    remoteHost: '127.0.0.1',
    remotePort: '9000',
    localHost: '127.0.0.1',
    localPort: '9000',
    encoding: 'UTF8',
    frameMode: 'Raw',
    fixedLength: '0',
    lineEnding: 'None',
    timeoutMs: '5000',
    keepAlive: false,
    reconnect: true,
    connectOnStartup: false,
    description: ''
  };
}

function copyProfile(value: TcpProfileV1): TcpDraft {
  return {
    id: value.id,
    name: value.name,
    enabled: value.enabled,
    mode: value.mode,
    remoteHost: value.remoteHost,
    remotePort: String(value.remotePort),
    localHost: value.localHost,
    localPort: String(value.localPort),
    encoding: value.encoding,
    frameMode: value.frameMode,
    fixedLength: String(value.fixedLength),
    lineEnding: value.lineEnding,
    timeoutMs: String(value.timeoutMs),
    keepAlive: value.keepAlive,
    reconnect: value.reconnect,
    connectOnStartup: value.connectOnStartup,
    description: value.description
  };
}

function copyProfiles(value: readonly TcpProfileV1[]): void {
  baseline.value = Object.freeze(value.map(item => Object.freeze({ ...item })));
  drafts.splice(0, drafts.length, ...value.map(copyProfile));
  if (!drafts.some(item => item.id === selectedId.value)) selectedId.value = drafts[0]?.id ?? '';
}

function profilePayload(value: TcpDraft): TcpProfileV1 {
  return Object.freeze({
    id: value.id.trim(),
    name: value.name.trim(),
    enabled: value.enabled,
    mode: value.mode,
    remoteHost: value.remoteHost.trim(),
    remotePort: Number(value.remotePort),
    localHost: value.localHost.trim(),
    localPort: Number(value.localPort),
    encoding: value.encoding,
    frameMode: value.frameMode,
    fixedLength: Number(value.fixedLength),
    lineEnding: value.lineEnding,
    timeoutMs: Number(value.timeoutMs),
    keepAlive: value.keepAlive,
    reconnect: value.reconnect,
    connectOnStartup: value.connectOnStartup,
    description: value.description.trim()
  });
}

function validateProfile(value: TcpDraft | null): string[] {
  if (!value) return ['请先选择一个 TCP Profile。'];
  const errors: string[] = [];
  if (!value.id.trim()) errors.push('Profile Id 不能为空。');
  if (!value.name.trim()) errors.push('Profile 名称不能为空。');
  const host = value.mode === 'Client' ? value.remoteHost : value.localHost;
  const port = value.mode === 'Client' ? Number(value.remotePort) : Number(value.localPort);
  if (!isValidHost(host)) errors.push(value.mode === 'Client' ? '远端 IP 必须是有效 IP 地址。' : '本地监听 IP 必须是有效 IP 地址。');
  if (!Number.isInteger(port) || port < 1 || port > 65535) errors.push(value.mode === 'Client' ? '远端端口必须在 1-65535 之间。' : '本地监听端口必须在 1-65535 之间。');
  if (!Number.isInteger(Number(value.timeoutMs)) || Number(value.timeoutMs) < 100 || Number(value.timeoutMs) > 600000) errors.push('超时时间必须在 100-600000 ms 之间。');
  if (value.frameMode === 'FixedLength' && (!Number.isInteger(Number(value.fixedLength)) || Number(value.fixedLength) <= 0)) errors.push('FixedLength 报文模式需要配置正整数长度。');
  return errors;
}

function isValidHost(value: string): boolean {
  const normalized = value.trim();
  if (normalized.toLowerCase() === 'localhost') return true;
  const ipv6 = normalized.startsWith('[') && normalized.endsWith(']')
    ? normalized.slice(1, -1)
    : normalized;
  if (ipv6.includes(':')) {
    try {
      new URL(`http://[${ipv6}]`);
      return true;
    } catch {
      return false;
    }
  }
  const parts = normalized.split('.');
  return parts.length === 4 && parts.every(part => /^\d{1,3}$/.test(part) && Number(part) <= 255);
}

function showFeedback(tone: 'success' | 'warning' | 'error' | 'info', title: string, message: string): void {
  feedback.value = { tone, title, message };
}

function resultMessage(result: { status: string; message?: string; error?: unknown; operationKind?: SettingsOperationKind }): string {
  return settingsOperationResultMessage(result);
}

function applyRuntimeResponse(
  result: { status: string; value?: { success: boolean; message: string; errors?: readonly DeviceValidationIssueV1[] }; error?: unknown },
  title: string
): boolean {
  if (result.status !== 'completed' || !result.value) {
    showFeedback('error', title, resultMessage(result));
    return false;
  }
  validationIssues.value = result.value.errors ?? Object.freeze([]);
  if (!result.value.success) {
    showFeedback('warning', title, result.value.message || '服务端未完成该操作。');
    return false;
  }
  showFeedback('success', title, result.value.message || '操作已完成。');
  return true;
}

async function load(): Promise<void> {
  if (isBusy.value) return;
  pendingAction.value = 'load';
  phase.value = 'loading';
  feedback.value = null;
  try {
    const result = await props.owner.readTcpProfiles();
    if (result.status !== 'completed') {
      showFeedback('error', 'TCP Profile 读取失败', resultMessage(result));
      phase.value = 'error';
      return;
    }
    copyProfiles(result.value.profiles);
    phase.value = 'ready';
    if (selectedId.value) await readRuntime(selectedId.value);
  } finally {
    pendingAction.value = null;
  }
}

async function saveProfiles(): Promise<void> {
  if (!props.canWrite || isBusy.value || drafts.length === 0 || localErrors.value.length > 0) return;
  pendingAction.value = 'save-profiles';
  feedback.value = null;
  validationIssues.value = Object.freeze([]);
  try {
    const result = await props.owner.saveTcpProfiles(drafts.map(profilePayload));
    if (result.status !== 'completed') {
      showFeedback('error', 'TCP Profile 未保存', resultMessage(result));
      return;
    }
    validationIssues.value = result.value.errors;
    if (!result.value.success) {
      showFeedback('error', 'TCP Profile 校验失败', result.value.message || '请修正当前 Profile 后重试。');
      return;
    }
    copyProfiles(result.value.profiles);
    showFeedback('success', 'TCP Profile 已保存', 'Profile 已持久化；不会自动连接或启动 Server。');
  } finally {
    pendingAction.value = null;
  }
}

async function refreshRuntime(): Promise<void> {
  if (!selectedId.value || isBusy.value) return;
  pendingAction.value = 'refresh-runtime';
  try {
    await readRuntime(selectedId.value);
  } finally {
    pendingAction.value = null;
  }
}

async function readRuntime(profileId: string): Promise<void> {
  if (!baseline.value.some(profile => profile.id === profileId)) return;
  await Promise.all([
    props.owner.readTcpStatus(profileId),
    props.owner.readTcpFrames(profileId)
  ]);
}

async function runtimeAction(action: 'connect' | 'disconnect' | 'start-server' | 'stop-server'): Promise<void> {
  const requiresSavedProfile = action === 'connect' || action === 'start-server';
  if (!selectedDraft.value || !canOperate.value || isBusy.value) return;
  if (requiresSavedProfile && (localErrors.value.length > 0 || !selectedProfileSaved.value)) return;
  if (requiresSavedProfile && !selectedProfileSaved.value) return;
  pendingAction.value = action;
  feedback.value = null;
  try {
    const ownerAction = action === 'connect'
      ? props.owner.connectTcp
      : action === 'disconnect'
        ? props.owner.disconnectTcp
        : action === 'start-server' ? props.owner.startTcpServer : props.owner.stopTcpServer;
    const result = await ownerAction.call(props.owner, selectedDraft.value.id);
    if (applyRuntimeResponse(result, action === 'connect' ? 'TCP 客户端连接' : action === 'disconnect' ? 'TCP 客户端断开' : action === 'start-server' ? 'TCP Server 启动' : 'TCP Server 停止')) {
      await props.owner.readTcpFrames(selectedDraft.value.id);
    }
  } finally {
    pendingAction.value = null;
  }
}

async function send(): Promise<void> {
  if (!selectedDraft.value || !canOperate.value || isBusy.value || !sendPayload.value || !selectedProfileSaved.value) return;
  pendingAction.value = 'send';
  feedback.value = null;
  lastResponse.value = null;
  try {
    const request: TcpSendRequestV1 = {
      payload: sendPayload.value,
      isHex: sendMode.value === 'hex',
      waitResponse: waitResponse.value,
      responseTimeoutMs: Number(responseTimeoutMs.value) || null
    };
    const result = await props.owner.sendTcp(selectedDraft.value.id, request);
    if (result.status === 'completed' && result.value?.success) {
      lastResponse.value = result.value.response;
    }
    if (applyRuntimeResponse(result, 'TCP 报文发送')) {
      await props.owner.readTcpFrames(selectedDraft.value.id);
    }
  } finally {
    pendingAction.value = null;
  }
}

async function clearFrames(): Promise<void> {
  if (!selectedDraft.value || !canOperate.value || isBusy.value || !selectedProfileSaved.value) return;
  pendingAction.value = 'clear-frames';
  feedback.value = null;
  try {
    const result = await props.owner.clearTcpFrames(selectedDraft.value.id);
    applyRuntimeResponse(result, 'TCP 收发日志清空');
  } finally {
    pendingAction.value = null;
  }
}

function addProfile(mode: TcpModeV1 = 'Client'): void {
  const next = emptyDraft(mode);
  drafts.push(next);
  selectedId.value = next.id;
}

function removeSelectedProfile(): void {
  if (!props.canWrite || !selectedDraft.value || isBusy.value) return;
  const index = drafts.findIndex(item => item.id === selectedDraft.value?.id);
  if (index < 0) return;
  drafts.splice(index, 1);
  selectedId.value = drafts[Math.max(0, index - 1)]?.id ?? '';
}

function frameTime(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.valueOf()) ? value : date.toLocaleTimeString();
}

watch(() => props.owner.projection.device.tcpProfiles, value => {
  if (!dirty.value) copyProfiles(value);
});
watch(selectedId, value => {
  lastResponse.value = null;
  if (value && phase.value === 'ready') void refreshRuntime();
});

onMounted(() => { void load(); });
onBeforeUnmount(() => detachPanelState());
</script>

<template>
  <div
    class="settings-device-workbench"
    data-settings-section="tcp"
  >
    <CvPanel
      title="TCP 连接工作台"
      description="Profile 持久化与 Client/Server 运行操作分离；状态和收发日志只来自运行时 endpoint。"
    >
      <template #actions>
        <CvStatusBadge
          :tone="phase === 'ready' ? 'ok' : phase === 'error' ? 'error' : 'info'"
          :label="phase === 'ready' ? '已读取' : phase === 'loading' ? '读取中' : phase === 'error' ? '读取失败' : '未读取'"
        />
        <CvButton
          variant="quiet"
          size="sm"
          :loading="pendingAction === 'load'"
          @click="load"
        >
          <template #leading>
            <CvIcon
              name="refresh"
              size="sm"
            />
          </template>
          刷新
        </CvButton>
      </template>

      <div class="tcp-workbench">
        <aside
          class="tcp-profile-list"
          aria-label="TCP Profile 列表"
        >
          <div class="tcp-profile-list__header">
            <div><strong>Profiles</strong><small>{{ drafts.length }} 个配置</small></div>
            <div class="tcp-profile-list__actions">
              <button
                class="icon-action"
                type="button"
                aria-label="添加 Client Profile"
                title="添加 Client Profile"
                :disabled="!canWrite || isBusy"
                @click="addProfile('Client')"
              >
                <CvIcon
                  name="plus"
                  size="sm"
                />
              </button>
              <button
                class="icon-action"
                type="button"
                aria-label="添加 Server Profile"
                title="添加 Server Profile"
                :disabled="!canWrite || isBusy"
                @click="addProfile('Server')"
              >
                <CvIcon
                  name="server"
                  size="sm"
                />
              </button>
            </div>
          </div>
          <div class="tcp-profile-list__items">
            <button
              v-for="profile in drafts"
              :key="profile.id"
              class="tcp-profile-row"
              :class="{ 'is-active': profile.id === selectedId }"
              type="button"
              @click="selectedId = profile.id"
            >
              <span class="tcp-profile-row__main"><strong>{{ profile.name || '未命名 Profile' }}</strong><small>{{ profile.mode }} · {{ profile.id }}</small></span>
              <CvStatusBadge
                v-if="props.owner.projection.device.tcpStatuses[profile.id]?.isConnected || props.owner.projection.device.tcpStatuses[profile.id]?.isListening"
                tone="ok"
                label="运行"
              />
            </button>
            <p
              v-if="drafts.length === 0"
              class="tcp-profile-list__empty"
            >
              暂无 Profile；Admin 可添加 Client 或 Server。
            </p>
          </div>
        </aside>

        <div
          v-if="selectedDraft"
          class="tcp-profile-editor"
        >
          <div class="tcp-profile-editor__header">
            <div><h3>Profile 配置</h3><p>当前保存仅更新 Profile，不会建立 socket 或启动监听。</p></div>
            <CvStatusBadge
              :tone="runtimeTone"
              :label="runtimeLabel"
            />
          </div>
          <div class="settings-form-grid">
            <CvField
              v-model="selectedDraft.name"
              label="Profile 名称"
              name="tcpProfileName"
              :readonly="!canWrite"
              required
            />
            <CvField
              v-model="selectedDraft.id"
              label="Profile Id"
              name="tcpProfileId"
              :readonly="true"
              required
            />
            <CvSelect
              v-model="selectedDraft.mode"
              label="角色"
              name="tcpProfileMode"
              :options="modeOptions"
              :disabled="!canWrite"
            />
            <CvField
              v-if="selectedDraft.mode === 'Client'"
              v-model="selectedDraft.remoteHost"
              label="远端 IP"
              name="tcpRemoteHost"
              :readonly="!canWrite"
              required
            />
            <CvField
              v-if="selectedDraft.mode === 'Client'"
              v-model="selectedDraft.remotePort"
              label="远端端口"
              name="tcpRemotePort"
              type="number"
              :readonly="!canWrite"
              required
            />
            <CvField
              v-if="selectedDraft.mode === 'Server'"
              v-model="selectedDraft.localHost"
              label="监听 IP"
              name="tcpLocalHost"
              :readonly="!canWrite"
              required
            />
            <CvField
              v-if="selectedDraft.mode === 'Server'"
              v-model="selectedDraft.localPort"
              label="监听端口"
              name="tcpLocalPort"
              type="number"
              :readonly="!canWrite"
              required
            />
            <CvSelect
              v-model="selectedDraft.encoding"
              label="字符编码"
              name="tcpEncoding"
              :options="encodingOptions"
              :disabled="!canWrite"
            />
            <CvSelect
              v-model="selectedDraft.frameMode"
              label="报文模式"
              name="tcpFrameMode"
              :options="frameModeOptions"
              :disabled="!canWrite"
            />
            <CvField
              v-if="selectedDraft.frameMode === 'FixedLength'"
              v-model="selectedDraft.fixedLength"
              label="定长字节数"
              name="tcpFixedLength"
              type="number"
              :readonly="!canWrite"
            />
            <CvSelect
              v-model="selectedDraft.lineEnding"
              label="行尾"
              name="tcpLineEnding"
              :options="lineEndingOptions"
              :disabled="!canWrite"
            />
            <CvField
              v-model="selectedDraft.timeoutMs"
              label="超时（毫秒）"
              name="tcpTimeoutMs"
              type="number"
              :readonly="!canWrite"
            />
            <label class="check-field"><input
              v-model="selectedDraft.enabled"
              type="checkbox"
              :disabled="!canWrite"
            ><span>启用 Profile</span></label>
            <label class="check-field"><input
              v-model="selectedDraft.keepAlive"
              type="checkbox"
              :disabled="!canWrite"
            ><span>保持连接</span></label>
            <label class="check-field"><input
              v-model="selectedDraft.reconnect"
              type="checkbox"
              :disabled="!canWrite"
            ><span>断线重连</span></label>
            <label class="check-field"><input
              v-model="selectedDraft.connectOnStartup"
              type="checkbox"
              :disabled="!canWrite"
            ><span>启动时连接（由运行时读取）</span></label>
            <CvField
              v-model="selectedDraft.description"
              label="说明"
              name="tcpDescription"
              :readonly="!canWrite"
            />
          </div>
          <CvInlineAlert
            v-if="localErrors.length"
            class="settings-panel__notice"
            tone="warning"
            title="Profile 参数需要修正"
          >
            {{ localErrors.join(' ') }}
          </CvInlineAlert>
          <div class="settings-panel__footer">
            <span class="settings-panel__dirty">{{ dirty ? '有 Profile 草稿修改' : 'Profile 与服务端一致' }}</span>
            <div class="settings-panel__actions">
              <CvButton
                v-if="canWrite"
                variant="danger"
                size="sm"
                :disabled="isBusy"
                @click="removeSelectedProfile"
              >
                <template #leading>
                  <CvIcon
                    name="trash"
                    size="sm"
                  />
                </template>删除本地 Profile
              </CvButton>
              <CvButton
                v-if="canWrite"
                variant="primary"
                size="sm"
                data-tcp-action="save-profiles"
                :loading="pendingAction === 'save-profiles'"
                :disabled="!dirty || localErrors.length > 0"
                @click="saveProfiles"
              >
                <template #leading>
                  <CvIcon
                    name="save"
                    size="sm"
                  />
                </template>保存 Profiles
              </CvButton>
            </div>
          </div>
        </div>
        <CvPageState
          v-else
          kind="empty"
          title="未选择 TCP Profile"
          description="Admin 可从左侧添加 Client 或 Server Profile。"
        />
      </div>
    </CvPanel>

    <section
      v-if="selectedDraft"
      class="settings-subsection"
    >
      <header class="settings-subsection__header">
        <div><h3>运行控制与收发调试</h3><p>运行状态由后端实时投影；发送不会修改 Profile 配置。</p></div>
        <CvButton
          variant="quiet"
          size="sm"
          :loading="pendingAction === 'refresh-runtime'"
          :disabled="isBusy || !selectedProfileSaved"
          @click="refreshRuntime"
        >
          <template #leading>
            <CvIcon
              name="refresh"
              size="sm"
            />
          </template>刷新运行态
        </CvButton>
      </header>
      <div class="tcp-runtime-summary">
        <div><span>状态</span><strong>{{ runtimeLabel }}</strong></div>
        <div><span>本地端点</span><strong>{{ selectedStatus?.localEndpoint ?? '—' }}</strong></div>
        <div><span>远端端点</span><strong>{{ selectedStatus?.remoteEndpoint ?? '—' }}</strong></div>
        <div><span>已连接客户端</span><strong>{{ selectedStatus?.connectedClients ?? 0 }}</strong></div>
      </div>
      <CvInlineAlert
        v-if="selectedStatus?.lastError"
        class="settings-panel__notice"
        tone="error"
        title="TCP 运行错误"
      >
        {{ selectedStatus.lastError }}
      </CvInlineAlert>
      <div class="settings-panel__actions tcp-runtime-actions">
        <CvButton
          v-if="selectedDraft.mode === 'Client'"
          variant="secondary"
          size="sm"
          data-tcp-action="connect"
          :disabled="!canOperate || isBusy || localErrors.length > 0 || !selectedProfileSaved || selectedStatus?.isConnected === true"
          :loading="pendingAction === 'connect'"
          @click="runtimeAction('connect')"
        >
          <template #leading>
            <CvIcon
              name="link"
              size="sm"
            />
          </template>连接
        </CvButton>
        <CvButton
          v-if="selectedDraft.mode === 'Client'"
          variant="quiet"
          size="sm"
          data-tcp-action="disconnect"
          :disabled="!canOperate || isBusy || !selectedStatus?.isConnected"
          :loading="pendingAction === 'disconnect'"
          @click="runtimeAction('disconnect')"
        >
          <template #leading>
            <CvIcon
              name="unlink"
              size="sm"
            />
          </template>断开
        </CvButton>
        <CvButton
          v-if="selectedDraft.mode === 'Server'"
          variant="secondary"
          size="sm"
          data-tcp-action="start-server"
          :disabled="!canOperate || isBusy || localErrors.length > 0 || !selectedProfileSaved || selectedStatus?.isListening === true"
          :loading="pendingAction === 'start-server'"
          @click="runtimeAction('start-server')"
        >
          <template #leading>
            <CvIcon
              name="play"
              size="sm"
            />
          </template>启动 Server
        </CvButton>
        <CvButton
          v-if="selectedDraft.mode === 'Server'"
          variant="quiet"
          size="sm"
          data-tcp-action="stop-server"
          :disabled="!canOperate || isBusy || !selectedStatus?.isListening"
          :loading="pendingAction === 'stop-server'"
          @click="runtimeAction('stop-server')"
        >
          <template #leading>
            <CvIcon
              name="square"
              size="sm"
            />
          </template>停止 Server
        </CvButton>
      </div>
      <div class="tcp-send-form">
        <CvSelect
          v-model="sendMode"
          label="发送格式"
          name="tcpSendMode"
          :options="sendModeOptions"
          :disabled="!canOperate || isBusy"
        />
        <label class="tcp-send-form__payload"><span>发送内容</span><textarea
          v-model="sendPayload"
          name="tcpPayload"
          rows="3"
          :disabled="!canOperate || isBusy"
          :placeholder="sendMode === 'hex' ? '例如：02 01 FF 03' : '输入要发送的文本'"
        /></label>
        <CvField
          v-model="responseTimeoutMs"
          label="响应超时（毫秒）"
          name="tcpResponseTimeoutMs"
          type="number"
          :readonly="!canOperate"
        />
        <label class="check-field"><input
          v-model="waitResponse"
          type="checkbox"
          :disabled="!canOperate || isBusy"
        ><span>等待响应</span></label>
        <CvButton
          variant="primary"
          size="sm"
          data-tcp-action="send"
          :disabled="!canOperate || isBusy || !selectedProfileSaved || !sendPayload"
          :loading="pendingAction === 'send'"
          @click="send"
        >
          <template #leading>
            <CvIcon
              name="send"
              size="sm"
            />
          </template>发送报文
        </CvButton>
      </div>
      <div
        v-if="lastResponse !== null"
        class="tcp-send-response"
        data-tcp-response="latest"
      >
        <span>最近响应</span>
        <code>{{ lastResponse || '未返回响应' }}</code>
      </div>
      <div class="tcp-frame-header">
        <div><strong>有界收发日志</strong><small>仅显示后端运行时返回的最近 {{ selectedFrames.length }} 条记录</small></div><CvButton
          variant="quiet"
          size="sm"
          :disabled="!canOperate || isBusy || !selectedProfileSaved"
          @click="clearFrames"
        >
          <template #leading>
            <CvIcon
              name="trash"
              size="sm"
            />
          </template>清空日志
        </CvButton>
      </div>
      <div class="mapping-table-wrap">
        <table class="mapping-table tcp-frame-table">
          <caption class="sr-only">
            TCP 收发日志
          </caption><thead><tr><th>时间</th><th>方向</th><th>字节</th><th>文本</th><th>HEX</th><th>端点</th></tr></thead><tbody>
            <tr
              v-for="frame in selectedFrames"
              :key="frame.id"
            >
              <td>{{ frameTime(frame.timestampUtc) }}</td><td>
                <CvStatusBadge
                  :tone="frame.direction.toLowerCase().includes('receive') || frame.direction.toLowerCase().includes('in') ? 'info' : 'ok'"
                  :label="frame.direction"
                />
              </td><td>{{ frame.byteCount }}</td><td class="break-cell">
                {{ frame.text || '—' }}
              </td><td class="break-cell mono-cell">
                {{ frame.hex || '—' }}
              </td><td>{{ frame.remoteEndpoint || '—' }}</td>
            </tr><tr v-if="selectedFrames.length === 0">
              <td
                colspan="6"
                class="mapping-table__empty"
              >
                暂无运行时帧。
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>

    <CvInlineAlert
      v-if="feedback"
      :tone="feedback.tone"
      :title="feedback.title"
      data-settings-device-feedback="tcp"
    >
      {{ feedback.message }}
    </CvInlineAlert>
    <ul
      v-if="validationIssues.length"
      class="validation-list"
    >
      <li
        v-for="(issue, index) in validationIssues"
        :key="`${issue.field}-${issue.index}-${index}`"
      >
        {{ issue.message }}
      </li>
    </ul>
  </div>
</template>

<style scoped>
.settings-device-workbench { display: grid; min-width: 0; gap: var(--cv-density-page-gap); }
.tcp-workbench { display: grid; min-width: 0; grid-template-columns: minmax(210px, 0.28fr) minmax(0, 1fr); gap: var(--cv-space-5); }
.tcp-profile-list { min-width: 0; border-right: 1px solid var(--cv-border-subtle); padding-right: var(--cv-space-4); }
.tcp-profile-list__header, .tcp-frame-header { display: flex; align-items: center; justify-content: space-between; gap: var(--cv-space-3); }
.tcp-profile-list__header strong, .tcp-frame-header strong { display: block; color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); }
.tcp-profile-list__header small, .tcp-frame-header small { display: block; margin-top: 2px; color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.tcp-profile-list__actions { display: flex; gap: var(--cv-space-1); }
.tcp-profile-list__items { display: grid; gap: 2px; margin-top: var(--cv-space-3); }
.tcp-profile-row { display: flex; min-width: 0; align-items: center; justify-content: space-between; gap: var(--cv-space-2); padding: var(--cv-space-2); border: 0; border-radius: var(--cv-radius-sm); background: transparent; color: var(--cv-text-secondary); text-align: left; cursor: pointer; }
.tcp-profile-row:hover { background: var(--cv-interactive-hover); color: var(--cv-text-primary); }
.tcp-profile-row.is-active { background: var(--cv-color-brand-soft); color: var(--cv-color-brand-text); }
.tcp-profile-row__main { display: grid; min-width: 0; gap: 2px; }
.tcp-profile-row__main strong, .tcp-profile-row__main small { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.tcp-profile-row__main strong { font-size: var(--cv-font-size-xs); font-weight: var(--cv-font-weight-semibold); }
.tcp-profile-row__main small { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.tcp-profile-list__empty { color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); line-height: var(--cv-line-height-normal); }
.tcp-profile-editor { min-width: 0; }
.tcp-profile-editor__header { display: flex; align-items: flex-start; justify-content: space-between; gap: var(--cv-space-3); margin-bottom: var(--cv-space-4); }
.tcp-profile-editor__header h3, .settings-subsection__header h3 { margin: 0; color: var(--cv-text-primary); font-size: var(--cv-type-section-title-size); }
.tcp-profile-editor__header p, .settings-subsection__header p { margin: var(--cv-space-1) 0 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
.settings-form-grid { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: var(--cv-space-4); }
.check-field { display: inline-flex; min-height: var(--cv-density-control-height); align-items: center; gap: var(--cv-space-2); color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
.check-field input { accent-color: var(--cv-color-brand-500); }
.settings-panel__notice { margin-top: var(--cv-space-4); }
.settings-panel__footer { display: flex; min-width: 0; align-items: center; justify-content: space-between; gap: var(--cv-space-3); margin-top: var(--cv-space-4); }
.settings-panel__dirty { color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); }
.settings-panel__actions { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: var(--cv-space-2); }
.settings-subsection { min-width: 0; border-top: 1px solid var(--cv-border-default); }
.settings-subsection__header { display: flex; align-items: flex-start; justify-content: space-between; gap: var(--cv-space-4); padding: var(--cv-space-4) 0 var(--cv-space-3); }
.tcp-runtime-summary { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: var(--cv-space-2); padding: var(--cv-space-3) 0; border-block: 1px solid var(--cv-border-subtle); }
.tcp-runtime-summary div { display: grid; min-width: 0; gap: 2px; }
.tcp-runtime-summary span { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.tcp-runtime-summary strong { overflow: hidden; color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); text-overflow: ellipsis; white-space: nowrap; }
.tcp-runtime-actions { margin-top: var(--cv-space-3); }
.tcp-send-form { display: grid; grid-template-columns: minmax(140px, 0.35fr) minmax(220px, 1fr) minmax(160px, 0.4fr) auto auto; align-items: end; gap: var(--cv-space-3); margin-top: var(--cv-space-4); }
.tcp-send-form__payload { display: grid; gap: var(--cv-density-field-gap); min-width: 0; }
.tcp-send-form__payload > span { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); font-weight: var(--cv-font-weight-semibold); }
.tcp-send-form textarea { width: 100%; min-height: var(--cv-density-control-height); padding: var(--cv-space-2); resize: vertical; border: 1px solid var(--cv-control-border); border-radius: var(--cv-radius-sm); background: var(--cv-surface-raised); color: var(--cv-text-primary); font: inherit; font-size: var(--cv-font-size-sm); }
.tcp-send-form textarea:focus-visible { outline: 2px solid var(--cv-focus-ring-color); outline-offset: 1px; }
.tcp-send-response { display: grid; grid-template-columns: auto minmax(0, 1fr); align-items: baseline; gap: var(--cv-space-3); margin-top: var(--cv-space-3); padding: var(--cv-space-2) var(--cv-space-3); border: 1px solid var(--cv-border-subtle); border-left-color: var(--cv-color-brand-500); background: var(--cv-surface-raised); }
.tcp-send-response span { color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); font-weight: var(--cv-font-weight-semibold); }
.tcp-send-response code { min-width: 0; overflow: auto; color: var(--cv-text-primary); font: inherit; font-family: var(--cv-font-family-mono); font-size: var(--cv-font-size-sm); white-space: pre-wrap; overflow-wrap: anywhere; }
.tcp-frame-header { margin-top: var(--cv-space-5); padding: var(--cv-space-3) 0 var(--cv-space-2); border-bottom: 1px solid var(--cv-border-subtle); }
.mapping-table-wrap { max-width: 100%; overflow: auto; border-bottom: 1px solid var(--cv-border-subtle); }
.mapping-table { width: 100%; min-width: 720px; border-collapse: collapse; table-layout: fixed; }
.mapping-table th, .mapping-table td { padding: var(--cv-space-2); border-bottom: 1px solid var(--cv-border-subtle); vertical-align: middle; text-align: left; }
.mapping-table th { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); font-weight: var(--cv-font-weight-medium); }
.mapping-table td { color: var(--cv-text-primary); font-size: var(--cv-font-size-xs); }
.mapping-table__empty { color: var(--cv-text-muted); text-align: center !important; }
.break-cell { max-width: 220px; overflow-wrap: anywhere; }
.mono-cell { font-family: ui-monospace, SFMono-Regular, Consolas, monospace; }
.validation-list { display: grid; gap: var(--cv-space-1); margin: var(--cv-space-3) 0 0; padding-left: var(--cv-space-4); color: var(--cv-color-status-ng-strong); font-size: var(--cv-font-size-xs); }
.icon-action { display: inline-grid; width: var(--cv-density-control-height-sm); height: var(--cv-density-control-height-sm); place-items: center; border: 0; border-radius: var(--cv-radius-xs); background: transparent; color: var(--cv-text-secondary); cursor: pointer; }
.icon-action:hover:not(:disabled) { background: var(--cv-interactive-hover); color: var(--cv-text-primary); }
.icon-action:disabled { opacity: 0.48; cursor: not-allowed; }
.sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0, 0, 0, 0); white-space: nowrap; }

@media (max-width: 1100px) {
  .settings-form-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .tcp-send-form { grid-template-columns: repeat(2, minmax(0, 1fr)); }
}

@media (max-width: 780px) {
  .tcp-workbench { grid-template-columns: 1fr; }
  .tcp-profile-list { padding-right: 0; padding-bottom: var(--cv-space-3); border-right: 0; border-bottom: 1px solid var(--cv-border-subtle); }
  .tcp-runtime-summary { grid-template-columns: repeat(2, minmax(0, 1fr)); }
}

@media (max-width: 600px) {
  .settings-form-grid, .tcp-send-form { grid-template-columns: 1fr; }
  .settings-panel__footer, .settings-subsection__header, .tcp-profile-editor__header { align-items: stretch; flex-direction: column; }
  .settings-panel__actions, .tcp-runtime-actions { justify-content: flex-start; }
}
</style>
