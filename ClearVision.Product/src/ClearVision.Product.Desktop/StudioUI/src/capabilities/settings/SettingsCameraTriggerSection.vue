<script setup lang="ts">
import { computed, shallowRef, watch } from 'vue';
import { CvButton, CvField, CvInlineAlert, CvSelect, CvStatusBadge } from '@/design-system';
import { CvIcon } from '@/design-system/icons';
import type {
  SerialPhotoelectricPortV1,
  TriggerDiagnosticsV1
} from './deviceContracts';
import type { SerialPhotoelectricTestRequestV1 } from './deviceApiAdapter';
import {
  settingsOperationResultMessage,
  type SettingsDeviceOperationResult,
  type SettingsOwner
} from './settingsOwner';
import type { SettingsOperationKind } from './contracts';

const props = withDefaults(defineProps<{
  owner: SettingsOwner;
  diagnostics: TriggerDiagnosticsV1 | null;
  serialPorts: readonly SerialPhotoelectricPortV1[];
  canOperate: boolean;
  disabled?: boolean;
}>(), {
  disabled: false
});

const emit = defineEmits<{
  state: [state: { readonly pending: boolean }];
}>();

const pendingAction = shallowRef<string | null>(null);
const feedback = shallowRef<{ tone: 'success' | 'warning' | 'error' | 'info'; title: string; message: string } | null>(null);
const portName = shallowRef('');
const baudRate = shallowRef('9600');
const debounceMs = shallowRef('200');
const timeoutMs = shallowRef('10000');

const serialPortOptions = computed(() => props.serialPorts.length > 0
  ? props.serialPorts.map(item => ({
    value: item.portName,
    label: item.isRecommended ? `${item.displayName}（推荐）` : item.displayName
  }))
  : [{ value: '', label: '未发现串口' }]);
const isBusy = computed(() => pendingAction.value !== null);
watch(isBusy, value => emit('state', { pending: value }), { immediate: true });
const diagnosticsTone = computed(() => {
  if (!props.diagnostics) return 'idle' as const;
  if (props.diagnostics.lastError) return 'error' as const;
  return props.diagnostics.isAvailable ? 'ok' as const : 'warning' as const;
});
const diagnosticsLabel = computed(() => {
  if (!props.diagnostics) return '未读取';
  if (props.diagnostics.lastError) return '异常';
  return props.diagnostics.isAvailable ? '可用' : '不可用';
});

function operationMessage(result: { status: string; message?: string; error?: unknown; operationKind?: SettingsOperationKind }): string {
  return settingsOperationResultMessage(result);
}

function showFeedback(tone: 'success' | 'warning' | 'error' | 'info', title: string, message: string): void {
  feedback.value = { tone, title, message };
}

function applyOperation(
  result: { status: string; value?: SettingsDeviceOperationResult; message?: string; error?: unknown },
  title: string
): void {
  if (result.status !== 'completed' || !result.value) {
    showFeedback('error', title, operationMessage(result));
    return;
  }
  showFeedback(result.value.success ? 'success' : 'warning', title, result.value.message);
}

async function testSerial(): Promise<void> {
  if (!props.canOperate || props.disabled || isBusy.value) return;
  if (!portName.value.trim()) {
    showFeedback('warning', '串口光电测试', '请先选择已发现的串口。');
    return;
  }
  pendingAction.value = 'serial-test';
  feedback.value = null;
  const request: SerialPhotoelectricTestRequestV1 = {
    portName: portName.value.trim(),
    baudRate: Number(baudRate.value),
    debounceMs: Number(debounceMs.value),
    timeoutMs: Number(timeoutMs.value)
  };
  try {
    const result = await props.owner.testSerialPhotoelectric(request);
    applyOperation(result, '串口光电测试');
  } finally {
    pendingAction.value = null;
  }
}

async function learnEnterDevice(): Promise<void> {
  if (!props.canOperate || props.disabled || isBusy.value) return;
  pendingAction.value = 'enter-learn';
  feedback.value = null;
  try {
    const result = await props.owner.learnEnterPhotoelectricDevice(Number(timeoutMs.value));
    applyOperation(result, 'Enter 光电识别');
  } finally {
    pendingAction.value = null;
  }
}

watch(() => props.serialPorts, ports => {
  if (portName.value && ports.some(item => item.portName === portName.value)) return;
  portName.value = ports.find(item => item.isRecommended)?.portName ?? ports[0]?.portName ?? '';
}, { immediate: true });
</script>

<template>
  <section
    class="camera-section camera-trigger"
    data-camera-section="trigger"
  >
    <header class="camera-section__header">
      <div>
        <h3>Trigger 输入与诊断</h3>
        <p>Enter、串口光电和诊断只调用现有 trigger-input endpoint。测试等待的是输入信号，不会写入正式检测结果。</p>
      </div>
      <CvStatusBadge
        :tone="diagnosticsTone"
        :label="`输入 ${diagnosticsLabel}`"
      />
    </header>

    <dl class="diagnostics-grid">
      <div><dt>监听类型</dt><dd>{{ diagnostics?.listenerType || '—' }}</dd></div>
      <div><dt>等待者</dt><dd>{{ diagnostics?.pendingWaiterCount ?? 0 }}</dd></div>
      <div>
        <dt>窗口句柄</dt><dd class="mono-value">
          {{ diagnostics?.attachedWindowHandle || '—' }}
        </dd>
      </div>
      <div><dt>最后设备</dt><dd>{{ diagnostics?.lastDeviceId || '—' }}</dd></div>
      <div><dt>最后信号</dt><dd>{{ diagnostics?.lastSignalUtc || '—' }}</dd></div>
    </dl>

    <div class="trigger-test-grid">
      <div class="trigger-test-block">
        <div class="camera-subheading">
          <strong>Enter 光电</strong><span>学习下一个可用设备</span>
        </div>
        <CvField
          v-model="timeoutMs"
          label="等待超时（ms）"
          name="enterLearnTimeout"
          type="number"
          :readonly="!canOperate || disabled"
        />
        <CvButton
          variant="secondary"
          size="sm"
          :disabled="!canOperate || disabled || isBusy"
          :loading="pendingAction === 'enter-learn'"
          @click="learnEnterDevice"
        >
          <template #leading>
            <CvIcon
              name="camera"
              size="sm"
            />
          </template>
          识别 Enter 设备
        </CvButton>
      </div>
      <div class="trigger-test-block">
        <div class="camera-subheading">
          <strong>串口光电</strong><span>等待一帧输入信号</span>
        </div>
        <CvSelect
          v-model="portName"
          label="串口"
          name="serialTestPort"
          :options="serialPortOptions"
          :disabled="!canOperate || disabled"
        />
        <div class="compact-grid">
          <CvField
            v-model="baudRate"
            label="波特率"
            name="serialTestBaudRate"
            type="number"
            :readonly="!canOperate || disabled"
          />
          <CvField
            v-model="debounceMs"
            label="去抖（ms）"
            name="serialTestDebounce"
            type="number"
            :readonly="!canOperate || disabled"
          />
        </div>
        <CvButton
          variant="secondary"
          size="sm"
          :disabled="!canOperate || disabled || isBusy || !portName"
          :loading="pendingAction === 'serial-test'"
          @click="testSerial"
        >
          <template #leading>
            <CvIcon
              name="diagnostics"
              size="sm"
            />
          </template>
          测试串口光电
        </CvButton>
      </div>
    </div>

    <CvInlineAlert
      v-if="diagnostics?.lastError"
      class="camera-trigger__alert"
      tone="error"
      title="Trigger 输入异常"
    >
      {{ diagnostics.lastError }}
    </CvInlineAlert>
    <CvInlineAlert
      v-if="feedback"
      class="camera-trigger__alert"
      :tone="feedback.tone"
      :title="feedback.title"
      data-camera-trigger-feedback
    >
      {{ feedback.message }}
    </CvInlineAlert>
  </section>
</template>

<style scoped>
.camera-section { min-width: 0; padding: var(--cv-space-4) 0; border-top: 1px solid var(--cv-border-subtle); }
.camera-section__header { display: flex; min-width: 0; align-items: flex-start; justify-content: space-between; gap: var(--cv-space-4); }
.camera-section__header h3 { margin: 0; color: var(--cv-text-primary); font-size: var(--cv-type-section-title-size); }
.camera-section__header p { max-width: 760px; margin: var(--cv-space-1) 0 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); line-height: var(--cv-line-height-normal); }
.diagnostics-grid { display: grid; grid-template-columns: repeat(5, minmax(0, 1fr)); gap: var(--cv-space-2); margin: var(--cv-space-4) 0 0; padding: var(--cv-space-3) 0; border-block: 1px solid var(--cv-border-subtle); }
.diagnostics-grid div { display: grid; min-width: 0; gap: 2px; }
.diagnostics-grid dt { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.diagnostics-grid dd { overflow: hidden; margin: 0; color: var(--cv-text-primary); font-size: var(--cv-font-size-xs); text-overflow: ellipsis; white-space: nowrap; }
.mono-value { font-family: ui-monospace, SFMono-Regular, Consolas, monospace; }
.trigger-test-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--cv-space-5); margin-top: var(--cv-space-4); }
.trigger-test-block { display: grid; align-content: start; gap: var(--cv-space-3); min-width: 0; }
.camera-subheading { display: flex; align-items: baseline; gap: var(--cv-space-2); }
.camera-subheading strong { color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); }
.camera-subheading span { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.compact-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--cv-space-3); }
.camera-trigger__alert { margin-top: var(--cv-space-4); }
@media (max-width: 900px) { .diagnostics-grid { grid-template-columns: repeat(3, minmax(0, 1fr)); } .trigger-test-grid { grid-template-columns: 1fr; } }
@media (max-width: 560px) { .diagnostics-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); } .camera-section__header { align-items: stretch; flex-direction: column; } }
</style>
