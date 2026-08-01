<script setup lang="ts">
import { computed, onBeforeUnmount, onDeactivated, onMounted, shallowRef, watch } from 'vue';
import { CvButton, CvInlineAlert, CvPanel, CvStatusBadge } from '@/design-system';
import { CvIcon } from '@/design-system/icons';
import type { CameraDiscoveryProviderV1 } from './deviceApiAdapter';
import type { CameraBindingV1 } from './deviceContracts';
import { settingsOperationResultMessage, type SettingsOwner } from './settingsOwner';
import type { SettingsOperationKind } from './contracts';
import SettingsCameraBindingSection from './SettingsCameraBindingSection.vue';
import SettingsCameraPreviewSection from './SettingsCameraPreviewSection.vue';
import SettingsCameraTriggerSection from './SettingsCameraTriggerSection.vue';

const props = defineProps<{
  owner: SettingsOwner;
}>();

const phase = shallowRef<'idle' | 'loading' | 'ready' | 'error'>('idle');
const pendingAction = shallowRef<string | null>(null);
const feedback = shallowRef<{ tone: 'success' | 'warning' | 'error' | 'info'; title: string; message: string } | null>(null);
const discoveryProvider = shallowRef<CameraDiscoveryProviderV1>('all');
const bindingDirty = shallowRef(false);
const triggerPending = shallowRef(false);
const previewPending = shallowRef(false);
let leaveStopPromise: Promise<unknown> | undefined;

const device = computed(() => props.owner.projection.device);
const bindings = computed(() => device.value.cameraBindings);
const discovery = computed(() => device.value.cameraDiscovery);
const canOperate = computed(() => props.owner.projection.role === 'Admin' || props.owner.projection.role === 'Engineer');
const canWriteBindings = computed(() => canOperate.value);
const isBusy = computed(() => pendingAction.value !== null);
const detachPanelState = props.owner.registerPanelState('camera', () => ({
  dirty: bindingDirty.value,
  pending: isBusy.value || triggerPending.value || previewPending.value
}));
watch([bindingDirty, triggerPending, previewPending, isBusy], () => props.owner.refreshPanelState());
const cameraStatusTone = computed(() => phase.value === 'ready' ? 'ok' : phase.value === 'error' ? 'error' : 'info');
const cameraStatusLabel = computed(() => phase.value === 'ready' ? '已读取' : phase.value === 'loading' ? '读取中' : phase.value === 'error' ? '读取失败' : '未读取');

function resultMessage(result: { status: string; message?: string; error?: unknown; operationKind?: SettingsOperationKind }): string {
  return settingsOperationResultMessage(result);
}

function showFeedback(tone: 'success' | 'warning' | 'error' | 'info', title: string, message: string): void {
  feedback.value = { tone, title, message };
}

async function load(): Promise<void> {
  if (isBusy.value) return;
  phase.value = 'loading';
  pendingAction.value = 'load';
  feedback.value = null;
  try {
    const [bindingsResult, diagnosticsResult, serialPortsResult] = await Promise.all([
      props.owner.readCameraBindings(),
      props.owner.readTriggerDiagnostics(),
      props.owner.readSerialPhotoelectricPorts()
    ]);
    const failures = [bindingsResult, diagnosticsResult, serialPortsResult]
      .find(result => result.status !== 'completed');
    if (failures) {
      phase.value = 'error';
      showFeedback('error', '相机系统读取失败', resultMessage(failures));
      return;
    }
    phase.value = 'ready';
  } finally {
    pendingAction.value = null;
  }
}

async function discover(provider: CameraDiscoveryProviderV1): Promise<void> {
  if (!canOperate.value || isBusy.value) return;
  discoveryProvider.value = provider;
  pendingAction.value = `discover-${provider}`;
  feedback.value = null;
  try {
    const result = await props.owner.discoverCameras(provider);
    if (result.status !== 'completed') {
      showFeedback('error', '相机 discovery 失败', resultMessage(result));
      return;
    }
    showFeedback('success', '相机 discovery 完成', `已发现 ${result.value.devices.length} 台 ${provider === 'all' ? '相机' : provider} 设备。`);
  } finally {
    pendingAction.value = null;
  }
}

async function saveBindings(nextBindings: readonly CameraBindingV1[], activeCameraId: string): Promise<void> {
  if (!canWriteBindings.value || isBusy.value) return;
  pendingAction.value = 'save-bindings';
  feedback.value = null;
  try {
    const result = await props.owner.saveCameraBindings(nextBindings, activeCameraId);
    if (result.status !== 'completed') {
      showFeedback('error', '相机绑定未保存', resultMessage(result));
      return;
    }
    showFeedback(result.value.success ? 'success' : 'warning', '相机绑定', result.value.message);
  } finally {
    pendingAction.value = null;
  }
}

onMounted(() => { void load(); });
function stopPreviewOnLeave(): void {
  if (leaveStopPromise) return;
  previewPending.value = true;
  props.owner.refreshPanelState();
  leaveStopPromise = props.owner.stopCameraPreview('离开相机 Settings 面板。')
    .finally(() => {
      leaveStopPromise = undefined;
      previewPending.value = false;
      props.owner.refreshPanelState();
    });
}
onDeactivated(stopPreviewOnLeave);
onBeforeUnmount(() => {
  stopPreviewOnLeave();
  detachPanelState();
});
</script>

<template>
  <div
    class="settings-camera-workbench"
    data-settings-section="camera"
    :data-camera-phase="phase"
  >
    <CvPanel
      title="Camera、Trigger 与 Preview"
      description="系统级相机发现、绑定和诊断与 Workspace 工程 CameraBinding 分离；Preview 仅是可丢弃调试输入。"
    >
      <template #actions>
        <CvStatusBadge
          :tone="cameraStatusTone"
          :label="cameraStatusLabel"
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

      <section
        class="camera-section camera-discovery"
        data-camera-section="discovery"
      >
        <header class="camera-section__header">
          <div>
            <h3>相机 Discovery</h3>
            <p>按 provider 调用现有发现 endpoint。发现结果是诊断投影，不会自动写入 binding 或切换活动相机。</p>
          </div>
          <div class="camera-section__actions">
            <CvButton
              variant="quiet"
              size="sm"
              data-camera-discovery="all"
              :disabled="!canOperate || isBusy"
              :loading="pendingAction === 'discover-all'"
              @click="discover('all')"
            >
              <template #leading>
                <CvIcon
                  name="search"
                  size="sm"
                />
              </template>
              全部
            </CvButton>
            <CvButton
              variant="quiet"
              size="sm"
              data-camera-discovery="huaray"
              :disabled="!canOperate || isBusy"
              :loading="pendingAction === 'discover-huaray'"
              @click="discover('huaray')"
            >
              Huaray
            </CvButton>
            <CvButton
              variant="quiet"
              size="sm"
              data-camera-discovery="hikvision"
              :disabled="!canOperate || isBusy"
              :loading="pendingAction === 'discover-hikvision'"
              @click="discover('hikvision')"
            >
              Hikvision
            </CvButton>
          </div>
        </header>

        <div
          v-if="discovery"
          class="discovery-table-wrap"
        >
          <table class="discovery-table">
            <caption class="sr-only">
              相机发现结果
            </caption>
            <thead><tr><th>设备</th><th>厂商 / 型号</th><th>序列号</th><th>地址</th><th>接口</th><th>状态</th></tr></thead>
            <tbody>
              <tr
                v-for="camera in discovery.devices"
                :key="camera.cameraId"
              >
                <td><strong>{{ camera.userDefinedName || camera.name || camera.cameraId }}</strong><small>{{ camera.cameraId }}</small></td>
                <td>{{ camera.manufacturer || '—' }} / {{ camera.model || '—' }}</td>
                <td class="mono-cell">
                  {{ camera.serialNumber || '—' }}
                </td>
                <td class="mono-cell">
                  {{ camera.ipAddress || '—' }}
                </td>
                <td>{{ camera.interfaceType || camera.connectionType || '—' }}</td>
                <td>
                  <CvStatusBadge
                    :tone="camera.isConnected ? 'ok' : 'warning'"
                    :label="camera.isConnected ? '已连接' : '未连接'"
                  />
                </td>
              </tr>
              <tr v-if="discovery.devices.length === 0">
                <td
                  colspan="6"
                  class="discovery-table__empty"
                >
                  当前 provider 没有发现设备。
                </td>
              </tr>
            </tbody>
          </table>
          <div
            v-if="Object.keys(discovery.diagnostics).length"
            class="discovery-diagnostics"
          >
            <span
              v-for="(value, key) in discovery.diagnostics"
              :key="key"
            ><strong>{{ key }}</strong> {{ value ?? '—' }}</span>
          </div>
        </div>
        <div
          v-else
          class="camera-empty"
        >
          尚未执行 discovery。选择 provider 后只读取设备信息。
        </div>
      </section>

      <SettingsCameraBindingSection
        :bindings="bindings"
        :active-camera-id="device.activeCameraId"
        :serial-ports="device.serialPorts"
        :can-write="canWriteBindings"
        :disabled="isBusy"
        @state="bindingDirty = $event.dirty"
        @save="saveBindings"
      />
      <SettingsCameraTriggerSection
        :owner="owner"
        :diagnostics="device.triggerDiagnostics"
        :serial-ports="device.serialPorts"
        :can-operate="canOperate"
        :disabled="isBusy"
        @state="triggerPending = $event.pending"
      />
      <SettingsCameraPreviewSection
        :owner="owner"
        :bindings="bindings"
        :active-camera-id="device.activeCameraId"
        :can-operate="canOperate"
        :disabled="isBusy"
        @state="previewPending = $event.pending"
      />

      <CvInlineAlert
        v-if="feedback"
        class="camera-workbench__feedback"
        :tone="feedback.tone"
        :title="feedback.title"
        data-settings-device-feedback="camera"
      >
        {{ feedback.message }}
      </CvInlineAlert>
    </CvPanel>
  </div>
</template>

<style scoped>
.settings-camera-workbench { display: grid; min-width: 0; gap: var(--cv-density-page-gap); }
.camera-section { min-width: 0; padding: var(--cv-space-4) 0; border-top: 1px solid var(--cv-border-subtle); }
.camera-section__header { display: flex; min-width: 0; align-items: flex-start; justify-content: space-between; gap: var(--cv-space-4); }
.camera-section__header h3 { margin: 0; color: var(--cv-text-primary); font-size: var(--cv-type-section-title-size); }
.camera-section__header p { max-width: 760px; margin: var(--cv-space-1) 0 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); line-height: var(--cv-line-height-normal); }
.camera-section__actions { display: flex; flex: 0 0 auto; flex-wrap: wrap; align-items: center; justify-content: flex-end; gap: var(--cv-space-2); }
.discovery-table-wrap { max-width: 100%; margin-top: var(--cv-space-4); overflow: auto; border-bottom: 1px solid var(--cv-border-subtle); }
.discovery-table { width: 100%; min-width: 820px; border-collapse: collapse; table-layout: fixed; }
.discovery-table th, .discovery-table td { padding: var(--cv-space-2); border-bottom: 1px solid var(--cv-border-subtle); vertical-align: middle; text-align: left; }
.discovery-table th { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); font-weight: var(--cv-font-weight-medium); }
.discovery-table td { color: var(--cv-text-primary); font-size: var(--cv-font-size-xs); }
.discovery-table td:first-child { display: grid; gap: 2px; }
.discovery-table td:first-child small { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.discovery-table__empty { color: var(--cv-text-muted); text-align: center !important; }
.discovery-diagnostics { display: flex; flex-wrap: wrap; gap: var(--cv-space-2) var(--cv-space-4); padding: var(--cv-space-2) 0; color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.discovery-diagnostics strong { color: var(--cv-text-secondary); }
.mono-cell { font-family: ui-monospace, SFMono-Regular, Consolas, monospace; }
.camera-empty { margin-top: var(--cv-space-4); color: var(--cv-text-muted); font-size: var(--cv-font-size-sm); }
.camera-workbench__feedback { margin-top: var(--cv-space-4); }
.sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0, 0, 0, 0); white-space: nowrap; }
@media (max-width: 720px) { .camera-section__header { align-items: stretch; flex-direction: column; } .camera-section__actions { justify-content: flex-start; } }
</style>
