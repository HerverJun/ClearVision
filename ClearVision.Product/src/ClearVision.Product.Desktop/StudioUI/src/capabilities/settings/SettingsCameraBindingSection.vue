<script setup lang="ts">
import { computed, reactive, shallowRef, watch } from 'vue';
import { CvButton, CvField, CvSelect, CvStatusBadge } from '@/design-system';
import { CvIcon } from '@/design-system/icons';
import type {
  CameraBindingV1,
  SerialPhotoelectricPortV1
} from './deviceContracts';

const props = withDefaults(defineProps<{
  bindings: readonly CameraBindingV1[];
  activeCameraId: string;
  serialPorts: readonly SerialPhotoelectricPortV1[];
  canWrite: boolean;
  disabled?: boolean;
}>(), {
  disabled: false
});

const emit = defineEmits<{
  save: [bindings: readonly CameraBindingV1[], activeCameraId: string];
}>();

interface CameraBindingDraft {
  id: string;
  displayName: string;
  deviceId: string;
  serialNumber: string;
  ipAddress: string;
  manufacturer: string;
  modelName: string;
  interfaceType: string;
  isEnabled: boolean;
  exposureTimeUs: string;
  gainDb: string;
  pixelFormat: string;
  triggerMode: CameraBindingV1['triggerMode'];
  hardwareTriggerSource: string;
  softwareTriggerSource: CameraBindingV1['softwareTriggerSource'];
  enterPhotoelectricDebounceMs: string;
  enterPhotoelectricTimeoutMs: string;
  ignoreEnterTriggerWhileBusy: boolean;
  enterPhotoelectricDeviceId: string;
  serialPhotoelectricPortName: string;
  serialPhotoelectricBaudRate: string;
  serialPhotoelectricDebounceMs: string;
  serialPhotoelectricTimeoutMs: string;
  ignoreSerialPhotoelectricTriggerWhileBusy: boolean;
  targetFrameRateFps: string;
  connectionStatus: string;
}

const drafts = reactive<CameraBindingDraft[]>([]);
const selectedId = shallowRef('');
const activeCameraIdDraft = shallowRef('');

const selectedDraft = computed(() => drafts.find(item => item.id === selectedId.value) ?? null);
const activeOptions = computed(() => drafts.map(item => ({
  value: item.id,
  label: `${item.displayName || item.id} (${item.id})`
})));
const serialPortOptions = computed(() => props.serialPorts.length > 0
  ? props.serialPorts.map(item => ({
    value: item.portName,
    label: item.isRecommended ? `${item.displayName}（推荐）` : item.displayName
  }))
  : [{ value: '', label: '未发现串口' }]);
const dirty = computed(() => JSON.stringify(payload()) !== JSON.stringify(props.bindings));
const selectedErrors = computed(() => {
  const draft = selectedDraft.value;
  if (!draft) return [];
  const errors: string[] = [];
  if (!draft.displayName.trim()) errors.push('显示名称不能为空。');
  if (!Number.isFinite(Number(draft.exposureTimeUs)) || Number(draft.exposureTimeUs) <= 0) {
    errors.push('曝光时间必须是大于 0 的数值。');
  }
  if (!Number.isFinite(Number(draft.gainDb)) || Number(draft.gainDb) < 0) {
    errors.push('增益必须是大于等于 0 的数值。');
  }
  if (!draft.pixelFormat.trim()) errors.push('像素格式不能为空。');
  if (!Number.isInteger(Number(draft.targetFrameRateFps)) || Number(draft.targetFrameRateFps) < 1 || Number(draft.targetFrameRateFps) > 120) {
    errors.push('目标帧率必须在 1-120 FPS 之间。');
  }
  if (draft.softwareTriggerSource === 'EnterPhotoelectric') {
    if (!Number.isInteger(Number(draft.enterPhotoelectricTimeoutMs)) || Number(draft.enterPhotoelectricTimeoutMs) < 1000) {
      errors.push('Enter 光电超时必须至少为 1000 ms。');
    }
  }
  if (draft.softwareTriggerSource === 'SerialPhotoelectric') {
    if (!draft.serialPhotoelectricPortName.trim()) errors.push('串口光电触发需要选择串口。');
    if (!Number.isInteger(Number(draft.serialPhotoelectricBaudRate)) || Number(draft.serialPhotoelectricBaudRate) <= 0) {
      errors.push('串口波特率必须是正整数。');
    }
  }
  return errors;
});

const triggerModeOptions = Object.freeze([
  { value: 'Software', label: 'Software 软件触发' },
  { value: 'External', label: 'External 外部触发' },
  { value: 'Continuous', label: 'Continuous 连续' }
]);
const softwareSourceOptions = Object.freeze([
  { value: 'Manual', label: 'Manual 手动' },
  { value: 'EnterPhotoelectric', label: 'Enter 光电' },
  { value: 'SerialPhotoelectric', label: '串口光电' }
]);
const pixelFormatOptions = Object.freeze([
  { value: 'Mono8', label: 'Mono8' },
  { value: 'RGB8', label: 'RGB8' },
  { value: 'BGR8', label: 'BGR8' },
  { value: 'BayerRG8', label: 'BayerRG8' },
  { value: 'BayerGB8', label: 'BayerGB8' },
  { value: 'BayerGR8', label: 'BayerGR8' },
  { value: 'BayerBG8', label: 'BayerBG8' }
]);

function copyBinding(value: CameraBindingV1): CameraBindingDraft {
  return {
    id: value.id,
    displayName: value.displayName,
    deviceId: value.deviceId,
    serialNumber: value.serialNumber,
    ipAddress: value.ipAddress,
    manufacturer: value.manufacturer,
    modelName: value.modelName,
    interfaceType: value.interfaceType,
    isEnabled: value.isEnabled,
    exposureTimeUs: String(value.exposureTimeUs),
    gainDb: String(value.gainDb),
    pixelFormat: value.pixelFormat,
    triggerMode: value.triggerMode,
    hardwareTriggerSource: value.hardwareTriggerSource,
    softwareTriggerSource: value.softwareTriggerSource,
    enterPhotoelectricDebounceMs: String(value.enterPhotoelectricDebounceMs),
    enterPhotoelectricTimeoutMs: String(value.enterPhotoelectricTimeoutMs),
    ignoreEnterTriggerWhileBusy: value.ignoreEnterTriggerWhileBusy,
    enterPhotoelectricDeviceId: value.enterPhotoelectricDeviceId,
    serialPhotoelectricPortName: value.serialPhotoelectricPortName,
    serialPhotoelectricBaudRate: String(value.serialPhotoelectricBaudRate),
    serialPhotoelectricDebounceMs: String(value.serialPhotoelectricDebounceMs),
    serialPhotoelectricTimeoutMs: String(value.serialPhotoelectricTimeoutMs),
    ignoreSerialPhotoelectricTriggerWhileBusy: value.ignoreSerialPhotoelectricTriggerWhileBusy,
    targetFrameRateFps: String(value.targetFrameRateFps),
    connectionStatus: value.connectionStatus
  };
}

function copyBindings(value: readonly CameraBindingV1[]): void {
  drafts.splice(0, drafts.length, ...value.map(copyBinding));
  const nextActive = value.some(item => item.id === props.activeCameraId)
    ? props.activeCameraId
    : value.find(item => item.isActive)?.id ?? value[0]?.id ?? '';
  selectedId.value = nextActive || value[0]?.id || '';
  activeCameraIdDraft.value = nextActive || value[0]?.id || '';
}

function payload(): CameraBindingV1[] {
  return drafts.map(draft => ({
    id: draft.id.trim(),
    displayName: draft.displayName.trim(),
    deviceId: draft.deviceId.trim(),
    serialNumber: draft.serialNumber.trim(),
    ipAddress: draft.ipAddress.trim(),
    manufacturer: draft.manufacturer.trim(),
    modelName: draft.modelName.trim(),
    interfaceType: draft.interfaceType.trim(),
    isEnabled: draft.isEnabled,
    isActive: draft.id === activeCameraIdDraft.value,
    exposureTimeUs: Number(draft.exposureTimeUs),
    gainDb: Number(draft.gainDb),
    pixelFormat: draft.pixelFormat.trim(),
    triggerMode: draft.triggerMode,
    hardwareTriggerSource: draft.hardwareTriggerSource.trim(),
    softwareTriggerSource: draft.softwareTriggerSource,
    enterPhotoelectricDebounceMs: Number(draft.enterPhotoelectricDebounceMs),
    enterPhotoelectricTimeoutMs: Number(draft.enterPhotoelectricTimeoutMs),
    ignoreEnterTriggerWhileBusy: draft.ignoreEnterTriggerWhileBusy,
    enterPhotoelectricDeviceId: draft.enterPhotoelectricDeviceId.trim(),
    serialPhotoelectricPortName: draft.serialPhotoelectricPortName.trim(),
    serialPhotoelectricBaudRate: Number(draft.serialPhotoelectricBaudRate),
    serialPhotoelectricDebounceMs: Number(draft.serialPhotoelectricDebounceMs),
    serialPhotoelectricTimeoutMs: Number(draft.serialPhotoelectricTimeoutMs),
    ignoreSerialPhotoelectricTriggerWhileBusy: draft.ignoreSerialPhotoelectricTriggerWhileBusy,
    targetFrameRateFps: Number(draft.targetFrameRateFps),
    connectionStatus: draft.connectionStatus
  }));
}

function save(): void {
  if (!props.canWrite || props.disabled || !selectedDraft.value || selectedErrors.value.length > 0) return;
  emit('save', payload(), activeCameraIdDraft.value);
}

function selectBinding(id: string): void {
  selectedId.value = id;
}

watch(() => props.bindings, value => {
  if (drafts.length === 0 || !dirty.value) copyBindings(value);
}, { immediate: true });
watch(() => props.activeCameraId, value => {
  if (!dirty.value && value && drafts.some(item => item.id === value)) {
    activeCameraIdDraft.value = value;
    selectedId.value = value;
  }
});
</script>

<template>
  <section
    class="camera-section camera-bindings"
    data-camera-section="bindings"
  >
    <header class="camera-section__header">
      <div>
        <h3>相机绑定与采集参数</h3>
        <p>系统级绑定、活动相机和采集参数保存到相机配置 authority。修改活动流中的参数会由后端以 409 拒绝。</p>
      </div>
      <div class="camera-section__actions">
        <CvStatusBadge
          :tone="dirty ? 'warning' : 'idle'"
          :label="dirty ? '有草稿修改' : '与服务端一致'"
        />
        <CvButton
          v-if="canWrite"
          variant="primary"
          size="sm"
          data-camera-action="save-bindings"
          :disabled="disabled || !selectedDraft || selectedErrors.length > 0"
          @click="save"
        >
          <template #leading>
            <CvIcon
              name="save"
              size="sm"
            />
          </template>
          保存绑定
        </CvButton>
      </div>
    </header>

    <div
      v-if="drafts.length > 0"
      class="camera-binding-layout"
    >
      <aside
        class="camera-binding-list"
        aria-label="相机绑定列表"
      >
        <button
          v-for="binding in drafts"
          :key="binding.id"
          class="camera-binding-row"
          :class="{ 'is-active': binding.id === selectedId }"
          type="button"
          :disabled="disabled"
          @click="selectBinding(binding.id)"
        >
          <span class="camera-binding-row__copy">
            <strong>{{ binding.displayName || binding.id }}</strong>
            <small>{{ binding.manufacturer || '未知厂商' }} · {{ binding.serialNumber || binding.id }}</small>
          </span>
          <CvStatusBadge
            :tone="binding.id === activeCameraIdDraft ? 'ok' : binding.isEnabled ? 'info' : 'idle'"
            :label="binding.id === activeCameraIdDraft ? '活动' : binding.isEnabled ? '启用' : '停用'"
          />
        </button>
      </aside>

      <div
        v-if="selectedDraft"
        class="camera-binding-editor"
      >
        <div class="camera-binding-editor__identity">
          <div>
            <strong>{{ selectedDraft.displayName || selectedDraft.id }}</strong>
            <span>{{ selectedDraft.manufacturer || '未知厂商' }} / {{ selectedDraft.modelName || '未知型号' }}</span>
          </div>
          <CvStatusBadge
            :tone="selectedDraft.connectionStatus.toLowerCase().includes('connected') ? 'ok' : 'warning'"
            :label="selectedDraft.connectionStatus || 'Unknown'"
          />
        </div>

        <div class="camera-form-grid">
          <CvField
            v-model="selectedDraft.displayName"
            label="显示名称"
            name="cameraDisplayName"
            :readonly="!canWrite || disabled"
            required
          />
          <CvSelect
            v-model="activeCameraIdDraft"
            label="活动相机"
            name="activeCameraId"
            :options="activeOptions"
            :disabled="!canWrite || disabled"
          />
          <CvField
            v-model="selectedDraft.serialNumber"
            label="序列号"
            name="cameraSerialNumber"
            readonly
          />
          <CvField
            v-model="selectedDraft.ipAddress"
            label="IP 地址"
            name="cameraIpAddress"
            readonly
          />
          <CvField
            v-model="selectedDraft.interfaceType"
            label="接口"
            name="cameraInterface"
            readonly
          />
          <label class="check-field"><input
            v-model="selectedDraft.isEnabled"
            type="checkbox"
            :disabled="!canWrite || disabled"
          ><span>启用绑定</span></label>
          <CvField
            v-model="selectedDraft.exposureTimeUs"
            label="曝光（μs）"
            name="cameraExposure"
            type="number"
            :readonly="!canWrite || disabled"
            :error="selectedErrors.find(item => item.includes('曝光'))"
            required
          />
          <CvField
            v-model="selectedDraft.gainDb"
            label="增益（dB）"
            name="cameraGain"
            type="number"
            :readonly="!canWrite || disabled"
            :error="selectedErrors.find(item => item.includes('增益'))"
            required
          />
          <CvSelect
            v-model="selectedDraft.pixelFormat"
            label="像素格式"
            name="cameraPixelFormat"
            :options="pixelFormatOptions"
            :disabled="!canWrite || disabled"
          />
          <CvField
            v-model="selectedDraft.targetFrameRateFps"
            label="目标帧率（FPS）"
            name="cameraTargetFrameRate"
            type="number"
            :readonly="!canWrite || disabled"
            :error="selectedErrors.find(item => item.includes('帧率'))"
          />
          <CvSelect
            v-model="selectedDraft.triggerMode"
            label="触发模式"
            name="cameraTriggerMode"
            :options="triggerModeOptions"
            :disabled="!canWrite || disabled"
          />
          <CvField
            v-model="selectedDraft.hardwareTriggerSource"
            label="硬件触发源"
            name="cameraHardwareTriggerSource"
            :readonly="!canWrite || disabled"
          />
          <CvSelect
            v-model="selectedDraft.softwareTriggerSource"
            label="软件触发源"
            name="cameraSoftwareTriggerSource"
            :options="softwareSourceOptions"
            :disabled="!canWrite || disabled"
          />
        </div>

        <div class="camera-trigger-config">
          <div class="camera-subheading">
            <strong>Enter 光电</strong><span>仅在软件触发源为 Enter 光电时生效</span>
          </div>
          <div class="camera-form-grid">
            <CvField
              v-model="selectedDraft.enterPhotoelectricDeviceId"
              label="设备 ID"
              name="enterPhotoelectricDeviceId"
              :readonly="!canWrite || disabled"
            />
            <CvField
              v-model="selectedDraft.enterPhotoelectricDebounceMs"
              label="去抖（ms）"
              name="enterDebounce"
              type="number"
              :readonly="!canWrite || disabled"
            />
            <CvField
              v-model="selectedDraft.enterPhotoelectricTimeoutMs"
              label="等待超时（ms）"
              name="enterTimeout"
              type="number"
              :readonly="!canWrite || disabled"
            />
            <label class="check-field"><input
              v-model="selectedDraft.ignoreEnterTriggerWhileBusy"
              type="checkbox"
              :disabled="!canWrite || disabled"
            ><span>忙碌时忽略触发</span></label>
          </div>
        </div>

        <div class="camera-trigger-config">
          <div class="camera-subheading">
            <strong>串口光电</strong><span>实际端口由 trigger-input endpoint 发现和测试</span>
          </div>
          <div class="camera-form-grid">
            <CvSelect
              v-model="selectedDraft.serialPhotoelectricPortName"
              label="串口"
              name="serialPhotoelectricPort"
              :options="serialPortOptions"
              :disabled="!canWrite || disabled"
            />
            <CvField
              v-model="selectedDraft.serialPhotoelectricBaudRate"
              label="波特率"
              name="serialBaudRate"
              type="number"
              :readonly="!canWrite || disabled"
            />
            <CvField
              v-model="selectedDraft.serialPhotoelectricDebounceMs"
              label="去抖（ms）"
              name="serialDebounce"
              type="number"
              :readonly="!canWrite || disabled"
            />
            <CvField
              v-model="selectedDraft.serialPhotoelectricTimeoutMs"
              label="等待超时（ms）"
              name="serialTimeout"
              type="number"
              :readonly="!canWrite || disabled"
            />
            <label class="check-field"><input
              v-model="selectedDraft.ignoreSerialPhotoelectricTriggerWhileBusy"
              type="checkbox"
              :disabled="!canWrite || disabled"
            ><span>忙碌时忽略触发</span></label>
          </div>
        </div>

        <ul
          v-if="selectedErrors.length"
          class="camera-validation"
          aria-live="polite"
        >
          <li
            v-for="error in selectedErrors"
            :key="error"
          >
            {{ error }}
          </li>
        </ul>
      </div>
    </div>
    <div
      v-else
      class="camera-empty"
    >
      当前没有可编辑的系统级 CameraBinding，请先完成 discovery 或检查后端相机配置。
    </div>
  </section>
</template>

<style scoped>
.camera-section { min-width: 0; padding: var(--cv-space-4) 0; border-top: 1px solid var(--cv-border-subtle); }
.camera-section__header { display: flex; min-width: 0; align-items: flex-start; justify-content: space-between; gap: var(--cv-space-4); }
.camera-section__header h3 { margin: 0; color: var(--cv-text-primary); font-size: var(--cv-type-section-title-size); }
.camera-section__header p { max-width: 760px; margin: var(--cv-space-1) 0 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); line-height: var(--cv-line-height-normal); }
.camera-section__actions { display: flex; flex: 0 0 auto; flex-wrap: wrap; align-items: center; justify-content: flex-end; gap: var(--cv-space-2); }
.camera-binding-layout { display: grid; min-width: 0; grid-template-columns: minmax(205px, 0.28fr) minmax(0, 1fr); gap: var(--cv-space-5); margin-top: var(--cv-space-4); }
.camera-binding-list { display: grid; align-content: start; gap: 2px; min-width: 0; padding-right: var(--cv-space-4); border-right: 1px solid var(--cv-border-subtle); }
.camera-binding-row { display: flex; min-width: 0; align-items: center; justify-content: space-between; gap: var(--cv-space-2); padding: var(--cv-space-2); border: 0; border-radius: var(--cv-radius-sm); background: transparent; color: var(--cv-text-secondary); text-align: left; cursor: pointer; }
.camera-binding-row:hover:not(:disabled) { background: var(--cv-interactive-hover); color: var(--cv-text-primary); }
.camera-binding-row.is-active { background: var(--cv-color-brand-soft); color: var(--cv-color-brand-text); }
.camera-binding-row:disabled { cursor: not-allowed; opacity: 0.62; }
.camera-binding-row__copy { display: grid; min-width: 0; gap: 2px; }
.camera-binding-row__copy strong, .camera-binding-row__copy small { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.camera-binding-row__copy strong { font-size: var(--cv-font-size-xs); }
.camera-binding-row__copy small { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.camera-binding-editor { min-width: 0; }
.camera-binding-editor__identity { display: flex; align-items: flex-start; justify-content: space-between; gap: var(--cv-space-3); margin-bottom: var(--cv-space-3); }
.camera-binding-editor__identity div { display: grid; gap: 2px; }
.camera-binding-editor__identity strong { color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); }
.camera-binding-editor__identity span { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.camera-form-grid { display: grid; min-width: 0; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: var(--cv-space-3) var(--cv-space-4); }
.camera-trigger-config { margin-top: var(--cv-space-4); padding-top: var(--cv-space-3); border-top: 1px solid var(--cv-border-subtle); }
.camera-subheading { display: flex; align-items: baseline; gap: var(--cv-space-2); margin-bottom: var(--cv-space-3); }
.camera-subheading strong { color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); }
.camera-subheading span { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.check-field { display: inline-flex; min-height: var(--cv-density-control-height); align-items: center; gap: var(--cv-space-2); color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
.check-field input { accent-color: var(--cv-color-brand-500); }
.camera-validation { display: grid; gap: var(--cv-space-1); margin: var(--cv-space-3) 0 0; padding-left: var(--cv-space-4); color: var(--cv-color-status-ng-strong); font-size: var(--cv-font-size-xs); }
.camera-empty { margin-top: var(--cv-space-4); color: var(--cv-text-muted); font-size: var(--cv-font-size-sm); }
@media (max-width: 1080px) { .camera-form-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
@media (max-width: 780px) { .camera-binding-layout { grid-template-columns: 1fr; } .camera-binding-list { padding-right: 0; padding-bottom: var(--cv-space-3); border-right: 0; border-bottom: 1px solid var(--cv-border-subtle); } }
@media (max-width: 600px) { .camera-form-grid { grid-template-columns: 1fr; } .camera-section__header { align-items: stretch; flex-direction: column; } .camera-section__actions { justify-content: flex-start; } }
</style>
