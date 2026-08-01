<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, shallowRef, watch } from 'vue';
import { CvButton, CvField, CvInlineAlert, CvPanel, CvSelect, CvStatusBadge } from '@/design-system';
import { CvIcon } from '@/design-system/icons';
import type {
  DeviceValidationIssueV1,
  PlcMappingV1,
  PlcProfileV1,
  PlcProtocolV1,
  PlcSettingsV1
} from './deviceContracts';
import { PLC_PROTOCOLS } from './deviceContracts';
import type { PlcTestConnectionRequestV1 } from './deviceApiAdapter';
import {
  settingsOperationResultMessage,
  type SettingsOwner
} from './settingsOwner';
import type { SettingsOperationKind } from './contracts';

const props = defineProps<{
  owner: SettingsOwner;
  canWrite: boolean;
}>();

interface PlcDraft {
  ipAddress: string;
  port: string;
  cpuType: string;
  rack: string;
  slot: string;
  heartbeatIntervalMs: string;
}

interface MappingDraft {
  name: string;
  address: string;
  dataType: string;
  description: string;
  canWrite: boolean;
}

type ProtocolMap<T> = Record<PlcProtocolV1, T>;

const protocol = shallowRef<PlcProtocolV1>('S7');
const phase = shallowRef<'idle' | 'loading' | 'ready' | 'error'>('idle');
const pendingAction = shallowRef<string | null>(null);
const feedback = shallowRef<{ tone: 'success' | 'warning' | 'error' | 'info'; title: string; message: string } | null>(null);
const validationIssues = shallowRef<readonly DeviceValidationIssueV1[]>([]);
const baseline = shallowRef<PlcSettingsV1 | null>(null);
const drafts = reactive<ProtocolMap<PlcDraft>>({
  S7: emptyDraft('S7'),
  MC: emptyDraft('MC'),
  FINS: emptyDraft('FINS')
});
const mappingDrafts = reactive<ProtocolMap<MappingDraft[]>>({
  S7: [],
  MC: [],
  FINS: []
});

const protocolOptions = Object.freeze([
  { value: 'S7', label: 'Siemens S7' },
  { value: 'MC', label: 'Mitsubishi MC' },
  { value: 'FINS', label: 'Omron FINS' }
]);
const dataTypeOptions = Object.freeze([
  { value: 'Bool', label: 'Bool' },
  { value: 'Byte', label: 'Byte' },
  { value: 'Word', label: 'Word' },
  { value: 'DWord', label: 'DWord' },
  { value: 'Int', label: 'Int' },
  { value: 'Float', label: 'Float' },
  { value: 'String', label: 'String' }
]);

const activeDraft = computed(() => drafts[protocol.value]);
const activeMappings = computed(() => mappingDrafts[protocol.value]);
const activeBaselineProfile = computed(() => profileFor(baseline.value, protocol.value));
const protocolMismatch = computed(() => baseline.value !== null && baseline.value.activeProtocol !== protocol.value);
const settingsDirty = computed(() => {
  const source = baseline.value;
  if (!source) return false;
  const draft = activeDraft.value;
  const profile = activeBaselineProfile.value;
  if (!profile) return true;
  return draft.ipAddress !== profile.ipAddress || Number(draft.port) !== profile.port ||
    (protocol.value === 'S7' && (
      draft.cpuType !== (profile.cpuType ?? 'S7-1200') ||
      Number(draft.rack) !== (profile.rack ?? 0) ||
      Number(draft.slot) !== (profile.slot ?? 1)
    )) || Number(draft.heartbeatIntervalMs) !== source.heartbeatIntervalMs ||
    source.activeProtocol !== protocol.value;
});
const mappingDirty = computed(() => JSON.stringify(activeMappings.value) !== JSON.stringify(activeBaselineProfile.value?.mappings ?? []));
const activeProtocolLabel = computed(() => protocolOptions.find(item => item.value === protocol.value)?.label ?? protocol.value);
const localErrors = computed(() => {
  const errors: string[] = [];
  const draft = activeDraft.value;
  if (!draft.ipAddress.trim()) errors.push('PLC IP 地址不能为空。');
  if (!Number.isInteger(Number(draft.port)) || Number(draft.port) < 1 || Number(draft.port) > 65535) {
    errors.push('端口必须在 1-65535 之间。');
  }
  if (protocol.value === 'S7') {
    if (!draft.cpuType.trim()) errors.push('S7 CPU 类型不能为空。');
    if (!Number.isInteger(Number(draft.rack)) || Number(draft.rack) < 0 || Number(draft.rack) > 15) errors.push('Rack 必须在 0-15 之间。');
    if (!Number.isInteger(Number(draft.slot)) || Number(draft.slot) < 0 || Number(draft.slot) > 15) errors.push('Slot 必须在 0-15 之间。');
  }
  return errors;
});
const canTest = computed(() => props.owner.projection.role === 'Admin' || props.owner.projection.role === 'Engineer');
const isBusy = computed(() => pendingAction.value !== null);
const detachPanelState = props.owner.registerPanelState('plc', () => ({
  dirty: settingsDirty.value || mappingDirty.value,
  pending: isBusy.value
}));
watch([settingsDirty, mappingDirty, isBusy], () => props.owner.refreshPanelState());

function emptyDraft(protocolName: PlcProtocolV1): PlcDraft {
  return {
    ipAddress: '',
    port: protocolName === 'S7' ? '102' : protocolName === 'MC' ? '5002' : '9600',
    cpuType: 'S7-1200',
    rack: '0',
    slot: '1',
    heartbeatIntervalMs: '1000'
  };
}

function profileFor(settings: PlcSettingsV1 | null, protocolName: PlcProtocolV1): PlcProfileV1 | null {
  if (!settings) return null;
  return protocolName === 'S7' ? settings.s7 : protocolName === 'MC' ? settings.mc : settings.fins;
}

function copyMappings(value: readonly PlcMappingV1[]): MappingDraft[] {
  return value.map(item => ({
    name: item.name,
    address: item.address,
    dataType: item.dataType,
    description: item.description,
    canWrite: item.canWrite
  }));
}

function copySettings(value: PlcSettingsV1): void {
  baseline.value = Object.freeze({
    ...value,
    s7: Object.freeze({ ...value.s7, mappings: Object.freeze(value.s7.mappings.map(item => Object.freeze({ ...item })))}),
    mc: Object.freeze({ ...value.mc, mappings: Object.freeze(value.mc.mappings.map(item => Object.freeze({ ...item })))}),
    fins: Object.freeze({ ...value.fins, mappings: Object.freeze(value.fins.mappings.map(item => Object.freeze({ ...item })))}),
  });
  protocol.value = value.activeProtocol;
  for (const item of PLC_PROTOCOLS) {
    const profile = profileFor(value, item)!;
    drafts[item].ipAddress = profile.ipAddress;
    drafts[item].port = String(profile.port);
    drafts[item].cpuType = profile.cpuType ?? 'S7-1200';
    drafts[item].rack = String(profile.rack ?? 0);
    drafts[item].slot = String(profile.slot ?? 1);
    drafts[item].heartbeatIntervalMs = String(value.heartbeatIntervalMs);
    mappingDrafts[item].splice(0, mappingDrafts[item].length, ...copyMappings(profile.mappings));
  }
}

function settingsPayload(): PlcSettingsV1 {
  const source = baseline.value;
  const buildProfile = (protocolName: PlcProtocolV1): PlcProfileV1 => {
    const draft = drafts[protocolName];
    const original = profileFor(source, protocolName);
    return Object.freeze({
      ipAddress: draft.ipAddress.trim(),
      port: Number(draft.port),
      mappings: original?.mappings ?? [],
      cpuType: protocolName === 'S7' ? draft.cpuType.trim() : null,
      rack: protocolName === 'S7' ? Number(draft.rack) : null,
      slot: protocolName === 'S7' ? Number(draft.slot) : null
    });
  };
  return Object.freeze({
    activeProtocol: protocol.value,
    heartbeatIntervalMs: Number(activeDraft.value.heartbeatIntervalMs),
    s7: buildProfile('S7'),
    mc: buildProfile('MC'),
    fins: buildProfile('FINS')
  });
}

function mappingPayload(): PlcMappingV1[] {
  return activeMappings.value.map(item => Object.freeze({ ...item }));
}

function showFeedback(tone: 'success' | 'warning' | 'error' | 'info', title: string, message: string): void {
  feedback.value = { tone, title, message };
}

function resultMessage(result: { status: string; message?: string; error?: unknown; operationKind?: SettingsOperationKind }): string {
  return settingsOperationResultMessage(result);
}

async function load(): Promise<void> {
  if (isBusy.value) return;
  phase.value = 'loading';
  pendingAction.value = 'load';
  feedback.value = null;
  try {
    const settings = await props.owner.readPlcSettings();
    if (settings.status !== 'completed' || !settings.value.settings) {
      showFeedback('error', 'PLC 读取失败', resultMessage(settings));
      phase.value = 'error';
      return;
    }
    copySettings(settings.value.settings);
    const mappings = await props.owner.readPlcMappings();
    if (mappings.status === 'completed') {
      mappingDrafts[protocol.value].splice(
        0,
        mappingDrafts[protocol.value].length,
        ...copyMappings(mappings.value.mappings)
      );
    }
    phase.value = 'ready';
  } finally {
    pendingAction.value = null;
  }
}

async function saveSettings(): Promise<void> {
  if (!props.canWrite || isBusy.value || localErrors.value.length > 0) return;
  pendingAction.value = 'save-settings';
  feedback.value = null;
  validationIssues.value = Object.freeze([]);
  try {
    const result = await props.owner.savePlcSettings(settingsPayload());
    if (result.status !== 'completed') {
      showFeedback('error', 'PLC 设置未保存', resultMessage(result));
      return;
    }
    validationIssues.value = result.value.errors;
    if (!result.value.success) {
      showFeedback('error', 'PLC 配置校验失败', result.value.message || '请修正高亮字段后重试。');
      return;
    }
    const reread = await props.owner.readPlcSettings();
    if (reread.status !== 'completed' || !reread.value.settings) {
      showFeedback('warning', 'PLC 设置已提交', '协议保存响应已返回，但重新读取服务端投影失败；请刷新后确认。');
      return;
    }
    copySettings(reread.value.settings);
    showFeedback('success', 'PLC 设置已保存', '配置已持久化；不会自动建立 PLC 长期连接。');
  } finally {
    pendingAction.value = null;
  }
}

async function saveMappings(): Promise<void> {
  if (!props.canWrite || isBusy.value || protocolMismatch.value) {
    if (protocolMismatch.value) {
      showFeedback('warning', 'PLC 映射暂未保存', '当前本地协议与服务端 ActiveProtocol 不一致，请先保存协议设置。');
    }
    return;
  }
  pendingAction.value = 'save-mappings';
  feedback.value = null;
  validationIssues.value = Object.freeze([]);
  try {
    const result = await props.owner.savePlcMappings(mappingPayload());
    if (result.status !== 'completed') {
      showFeedback('error', 'PLC 映射未保存', resultMessage(result));
      return;
    }
    validationIssues.value = result.value.errors;
    if (!result.value.success) {
      showFeedback('error', 'PLC 映射校验失败', result.value.message || '请修正变量名、地址或数据类型。');
      return;
    }
    const current = mappingDrafts[protocol.value];
    const source = baseline.value;
    if (source) {
      const updated = {
        ...source,
        [protocol.value === 'S7' ? 's7' : protocol.value === 'MC' ? 'mc' : 'fins']: {
          ...profileFor(source, protocol.value),
          mappings: result.value.mappings
        }
      } as PlcSettingsV1;
      baseline.value = Object.freeze(updated);
      current.splice(0, current.length, ...copyMappings(result.value.mappings));
    }
    showFeedback('success', 'PLC 映射已保存', '映射已持久化；连接测试仍需单独执行。');
  } finally {
    pendingAction.value = null;
  }
}

async function testConnection(): Promise<void> {
  if (!canTest.value || isBusy.value || localErrors.value.length > 0) return;
  pendingAction.value = 'test-connection';
  feedback.value = null;
  try {
    const draft = activeDraft.value;
    const request: PlcTestConnectionRequestV1 = {
      protocol: protocol.value,
      ipAddress: draft.ipAddress.trim(),
      port: Number(draft.port),
      ...(protocol.value === 'S7'
        ? { cpuType: draft.cpuType.trim(), rack: Number(draft.rack), slot: Number(draft.slot) }
        : {})
    };
    const result = await props.owner.testPlcConnection(request);
    if (result.status !== 'completed') {
      showFeedback('error', 'PLC 连接测试失败', resultMessage(result));
      return;
    }
    showFeedback(result.value.success ? 'success' : 'warning', result.value.success ? '连接测试成功' : '连接测试失败', result.value.message);
  } finally {
    pendingAction.value = null;
  }
}

function addMapping(): void {
  mappingDrafts[protocol.value].push({ name: '', address: '', dataType: 'Bool', description: '', canWrite: false });
}

function removeMapping(index: number): void {
  mappingDrafts[protocol.value].splice(index, 1);
}

function mappingIssue(index: number, field: string): string | undefined {
  return validationIssues.value.find(item => item.index === index && item.field.toLowerCase() === field.toLowerCase())?.message;
}

watch(() => props.owner.projection.device.plcSettings, value => {
  if (value && !settingsDirty.value && !mappingDirty.value) copySettings(value);
});

onMounted(() => { void load(); });
onBeforeUnmount(() => detachPanelState());
</script>

<template>
  <div
    class="settings-device-workbench"
    data-settings-section="plc"
  >
    <CvPanel
      title="PLC 通讯"
      description="协议草稿彼此隔离；保存配置、保存映射和连接测试是三个独立操作。"
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

      <div class="plc-toolbar">
        <CvSelect
          v-model="protocol"
          label="当前协议草稿"
          name="plcProtocol"
          :options="protocolOptions"
          :disabled="isBusy"
          hint="切换协议不会覆盖其他协议的本地草稿。"
        />
        <CvField
          v-model="activeDraft.heartbeatIntervalMs"
          label="心跳间隔（毫秒）"
          name="plcHeartbeatIntervalMs"
          type="number"
          :readonly="!canWrite"
        />
        <div class="plc-toolbar__scope">
          <span>当前工作协议</span>
          <strong>{{ activeProtocolLabel }}</strong>
          <small>{{ settingsDirty ? '有配置草稿修改' : '与服务端投影一致' }}</small>
        </div>
      </div>

      <div class="settings-form-grid">
        <CvField
          v-model="activeDraft.ipAddress"
          label="PLC IP 地址"
          name="plcIpAddress"
          :readonly="!canWrite"
          :error="localErrors.find(item => item.includes('IP'))"
          required
        />
        <CvField
          v-model="activeDraft.port"
          label="端口"
          name="plcPort"
          type="number"
          :readonly="!canWrite"
          :error="localErrors.find(item => item.includes('端口'))"
          required
        />
        <CvField
          v-if="protocol === 'S7'"
          v-model="activeDraft.cpuType"
          label="S7 CPU 类型"
          name="plcCpuType"
          :readonly="!canWrite"
          required
        />
        <CvField
          v-if="protocol === 'S7'"
          v-model="activeDraft.rack"
          label="Rack"
          name="plcRack"
          type="number"
          :readonly="!canWrite"
        />
        <CvField
          v-if="protocol === 'S7'"
          v-model="activeDraft.slot"
          label="Slot"
          name="plcSlot"
          type="number"
          :readonly="!canWrite"
        />
      </div>

      <CvInlineAlert
        v-if="!canWrite"
        class="settings-panel__notice"
        tone="info"
        title="Engineer 诊断权限"
      >
        当前角色可以读取和测试 PLC，但不能保存协议设置或映射。
      </CvInlineAlert>

      <CvInlineAlert
        v-if="localErrors.length > 0"
        class="settings-panel__notice"
        tone="warning"
        title="先修正连接参数"
      >
        {{ localErrors.join(' ') }}
      </CvInlineAlert>

      <CvInlineAlert
        v-if="protocolMismatch"
        class="settings-panel__notice"
        tone="warning"
        title="当前协议尚未保存"
      >
        当前本地协议为 {{ activeProtocolLabel }}，服务端 ActiveProtocol 仍为 {{ baseline?.activeProtocol }}；请先保存协议设置，再保存映射。
      </CvInlineAlert>

      <template #footer>
        <div class="settings-panel__footer">
          <span class="settings-panel__dirty">保存不会自动连接 PLC</span>
          <div class="settings-panel__actions">
            <CvButton
              variant="secondary"
              size="sm"
              data-plc-action="test-connection"
              :loading="pendingAction === 'test-connection'"
              :disabled="!canTest || localErrors.length > 0"
              @click="testConnection"
            >
              <template #leading>
                <CvIcon
                  name="diagnostics"
                  size="sm"
                />
              </template>
              测试连接
            </CvButton>
            <CvButton
              v-if="canWrite"
              variant="primary"
              size="sm"
              data-plc-action="save-settings"
              :loading="pendingAction === 'save-settings'"
              :disabled="!settingsDirty || localErrors.length > 0"
              @click="saveSettings"
            >
              <template #leading>
                <CvIcon
                  name="save"
                  size="sm"
                />
              </template>
              保存协议设置
            </CvButton>
          </div>
        </div>
      </template>
    </CvPanel>

    <section class="settings-subsection">
      <header class="settings-subsection__header">
        <div>
          <h3>地址映射</h3>
          <p>映射保存只作用于当前协议；切换协议不会读取或覆盖其他协议草稿。</p>
        </div>
        <CvButton
          v-if="canWrite"
          variant="secondary"
          size="sm"
          :disabled="isBusy"
          @click="addMapping"
        >
          <template #leading>
            <CvIcon
              name="plus"
              size="sm"
            />
          </template>
          添加映射
        </CvButton>
      </header>
      <div class="mapping-table-wrap">
        <table class="mapping-table">
          <caption class="sr-only">
            {{ activeProtocolLabel }} PLC 地址映射
          </caption>
          <thead>
            <tr><th>变量名</th><th>地址</th><th>数据类型</th><th>说明</th><th>可写</th><th><span class="sr-only">操作</span></th></tr>
          </thead>
          <tbody v-if="activeMappings.length">
            <tr
              v-for="(mapping, index) in activeMappings"
              :key="`${protocol}-${index}`"
            >
              <td>
                <input
                  v-model="mapping.name"
                  :class="{ 'has-error': mappingIssue(index, 'name') }"
                  :disabled="!canWrite"
                  aria-label="变量名"
                >
              </td>
              <td>
                <input
                  v-model="mapping.address"
                  :class="{ 'has-error': mappingIssue(index, 'address') }"
                  :disabled="!canWrite"
                  aria-label="PLC 地址"
                >
              </td>
              <td>
                <CvSelect
                  v-model="mapping.dataType"
                  label="数据类型"
                  :options="dataTypeOptions"
                  :disabled="!canWrite"
                />
              </td>
              <td>
                <input
                  v-model="mapping.description"
                  :disabled="!canWrite"
                  aria-label="说明"
                >
              </td>
              <td>
                <input
                  v-model="mapping.canWrite"
                  type="checkbox"
                  :disabled="!canWrite"
                  aria-label="允许写入"
                >
              </td>
              <td>
                <button
                  v-if="canWrite"
                  class="icon-action"
                  type="button"
                  aria-label="删除映射"
                  title="删除映射"
                  @click="removeMapping(index)"
                >
                  <CvIcon
                    name="trash"
                    size="sm"
                  />
                </button>
              </td>
            </tr>
          </tbody>
          <tbody v-else>
            <tr>
              <td
                colspan="6"
                class="mapping-table__empty"
              >
                当前协议暂无映射。
              </td>
            </tr>
          </tbody>
        </table>
      </div>
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
      <div class="settings-panel__footer mapping-footer">
        <span class="settings-panel__dirty">{{ mappingDirty ? '映射草稿未保存' : '映射与服务端一致' }}</span>
        <CvButton
          v-if="canWrite"
          variant="primary"
          size="sm"
          data-plc-action="save-mappings"
          :loading="pendingAction === 'save-mappings'"
          :disabled="!mappingDirty || protocolMismatch"
          @click="saveMappings"
        >
          <template #leading>
            <CvIcon
              name="save"
              size="sm"
            />
          </template>
          保存当前映射
        </CvButton>
      </div>
    </section>

    <CvInlineAlert
      v-if="feedback"
      :tone="feedback.tone"
      :title="feedback.title"
      data-settings-device-feedback="plc"
    >
      {{ feedback.message }}
    </CvInlineAlert>
  </div>
</template>

<style scoped>
.settings-device-workbench { display: grid; min-width: 0; gap: var(--cv-density-page-gap); }
.plc-toolbar { display: grid; grid-template-columns: minmax(190px, 0.9fr) minmax(180px, 0.75fr) minmax(180px, 1fr); align-items: end; gap: var(--cv-space-4); margin-bottom: var(--cv-space-4); }
.plc-toolbar__scope { display: grid; gap: var(--cv-space-1); min-height: var(--cv-density-control-height); align-content: center; padding: 0 var(--cv-space-3); border: 1px solid var(--cv-border-subtle); border-radius: var(--cv-radius-sm); background: var(--cv-surface-page); }
.plc-toolbar__scope span, .plc-toolbar__scope small { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.plc-toolbar__scope strong { color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); }
.settings-form-grid { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: var(--cv-space-4); }
.settings-panel__notice { margin-top: var(--cv-space-4); }
.settings-panel__footer { display: flex; min-width: 0; align-items: center; justify-content: space-between; gap: var(--cv-space-3); }
.settings-panel__dirty { color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); }
.settings-panel__actions { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: var(--cv-space-2); }
.settings-subsection { min-width: 0; border-top: 1px solid var(--cv-border-default); }
.settings-subsection__header { display: flex; align-items: flex-start; justify-content: space-between; gap: var(--cv-space-4); padding: var(--cv-space-4) 0 var(--cv-space-3); }
.settings-subsection__header h3 { margin: 0; color: var(--cv-text-primary); font-size: var(--cv-type-section-title-size); }
.settings-subsection__header p { margin: var(--cv-space-1) 0 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
.mapping-table-wrap { max-width: 100%; overflow: auto; border-block: 1px solid var(--cv-border-subtle); }
.mapping-table { width: 100%; min-width: 720px; border-collapse: collapse; table-layout: fixed; }
.mapping-table th, .mapping-table td { padding: var(--cv-space-2); border-bottom: 1px solid var(--cv-border-subtle); vertical-align: middle; text-align: left; }
.mapping-table th { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); font-weight: var(--cv-font-weight-medium); }
.mapping-table td { color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); }
.mapping-table input:not([type="checkbox"]) { width: 100%; min-width: 0; height: var(--cv-density-control-height-sm); padding: 0 var(--cv-space-2); border: 1px solid var(--cv-control-border); border-radius: var(--cv-radius-xs); background: var(--cv-surface-raised); color: var(--cv-text-primary); }
.mapping-table input:focus-visible { outline: 2px solid var(--cv-focus-ring-color); outline-offset: 1px; }
.mapping-table input.has-error { border-color: var(--cv-color-status-ng); }
.mapping-table :deep(.cv-select) { gap: 0; }
.mapping-table :deep(.cv-select__label), .mapping-table :deep(.cv-select__hint) { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0, 0, 0, 0); }
.mapping-table :deep(.cv-select__control) { height: var(--cv-density-control-height-sm); font-size: var(--cv-font-size-xs); }
.mapping-table__empty { color: var(--cv-text-muted); text-align: center !important; }
.mapping-footer { padding: var(--cv-space-3) 0 0; }
.validation-list { display: grid; gap: var(--cv-space-1); margin: var(--cv-space-3) 0 0; padding-left: var(--cv-space-4); color: var(--cv-color-status-ng-strong); font-size: var(--cv-font-size-xs); }
.icon-action { display: inline-grid; width: var(--cv-density-control-height-sm); height: var(--cv-density-control-height-sm); place-items: center; border: 0; border-radius: var(--cv-radius-xs); background: transparent; color: var(--cv-color-status-ng-strong); cursor: pointer; }
.icon-action:hover { background: var(--cv-color-status-ng-soft); }
.sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0, 0, 0, 0); white-space: nowrap; }

@media (max-width: 980px) {
  .plc-toolbar, .settings-form-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .plc-toolbar__scope { grid-column: 1 / -1; }
}

@media (max-width: 620px) {
  .plc-toolbar, .settings-form-grid { grid-template-columns: 1fr; }
  .settings-panel__footer, .settings-subsection__header { align-items: stretch; flex-direction: column; }
  .settings-panel__actions { justify-content: flex-start; }
}
</style>
