<script setup lang="ts">
import { computed, shallowRef } from 'vue';
import { CvButton, CvStatusBadge, type CvStatusTone } from '@/design-system';
import type { CameraBindingEditorOwner } from './cameraBindingEditorOwner';

const props = defineProps<{
  owner: CameraBindingEditorOwner;
  parameterName: string;
  disabled: boolean;
}>();
const copiedField = shallowRef<string | null>(null);
const selectedBinding = computed(() => props.owner.projection.bindings.find(
  binding => binding.id === props.owner.projection.currentBindingId
) ?? null);
const statusTone = computed<CvStatusTone>(() => {
  if (props.owner.projection.phase === 'error' || props.owner.projection.capturePhase === 'error') return 'error';
  if (props.disabled) return 'warning';
  if (props.owner.projection.capturePhase === 'captured') return 'ok';
  if (props.owner.projection.capturePhase === 'capturing' || props.owner.projection.phase === 'loading') return 'info';
  return 'idle';
});
const statusLabel = computed(() => {
  if (props.disabled) return '只读';
  return ({
    capturing: '正在捕获', captured: '单帧可用', cancelled: '已取消', error: '捕获失败', idle: '等待捕获'
  } as const)[props.owner.projection.capturePhase];
});

function connectionLabel(value: string): string {
  return ({ Connected: '已连接', Disconnected: '未连接', Offline: '离线', Unknown: '状态未知' } as Readonly<Record<string, string>>)[value] ?? value;
}

function triggerLabel(value: string): string {
  return ({ Software: '软件触发', Hardware: '硬件触发', Continuous: '连续采集' } as Readonly<Record<string, string>>)[value] ?? value;
}

async function copyField(key: string, value: string): Promise<void> {
  try {
    await navigator.clipboard.writeText(value);
    copiedField.value = key;
  } catch {
    copiedField.value = null;
  }
}

function select(event: Event): void {
  props.owner.selectBinding(props.parameterName, (event.target as HTMLSelectElement).value);
}
</script>

<template>
  <div
    class="camera-binding-editor cv-workbench"
    data-capability="camera-binding-editor"
    :data-capture-phase="owner.projection.capturePhase"
    :data-frame-id="owner.projection.frame?.frameId ?? ''"
  >
    <div class="cv-workbench-heading">
      <div class="cv-workbench-heading__copy">
        <h4 class="cv-workbench-heading__title">
          相机绑定
        </h4>
        <p class="cv-workbench-heading__description">
          选择工程相机并捕获单帧，作为当前预览链路的输入。
        </p>
      </div>
      <CvStatusBadge
        :tone="statusTone"
        :label="statusLabel"
      />
    </div>
    <select
      :value="owner.projection.currentBindingId ?? ''"
      :name="parameterName"
      :disabled="disabled || owner.projection.phase === 'loading'"
      aria-label="相机绑定"
      @change="select"
    >
      <option value="">
        请选择相机
      </option>
      <option
        v-for="binding in owner.projection.bindings"
        :key="binding.id"
        :value="binding.id"
        :disabled="!binding.isEnabled"
      >
        {{ binding.displayName }} · {{ connectionLabel(binding.connectionStatus) }}
      </option>
    </select>
    <div class="camera-binding-editor__actions">
      <CvButton
        v-if="owner.projection.capturePhase !== 'capturing'"
        size="sm"
        variant="primary"
        :disabled="disabled || !owner.projection.canCapture"
        @click="owner.capture()"
      >
        捕获单帧
      </CvButton>
      <CvButton
        v-else
        size="sm"
        variant="quiet"
        @click="owner.cancelCapture()"
      >
        取消捕获
      </CvButton>
      <CvButton
        size="sm"
        variant="quiet"
        :disabled="owner.projection.phase === 'loading'"
        @click="owner.refreshBindings()"
      >
        刷新
      </CvButton>
    </div>
    <div
      class="cv-workbench-status"
      role="status"
      aria-live="polite"
      :data-tone="statusTone"
    >
      <span>{{ owner.projection.message }}</span>
    </div>
    <details
      v-if="selectedBinding"
      class="cv-technical-detail camera-binding-editor__technical"
    >
      <summary>技术详情</summary>
      <dl>
        <div><dt>触发方式</dt><dd>{{ triggerLabel(selectedBinding.triggerMode) }}</dd></div>
        <div><dt>设备型号</dt><dd>{{ [selectedBinding.manufacturer, selectedBinding.modelName].filter(Boolean).join(' ') || '未提供' }}</dd></div>
        <div>
          <dt>绑定标识</dt><dd class="cv-copyable-value">
            <code translate="no">{{ selectedBinding.id }}</code><CvButton
              size="sm"
              variant="quiet"
              @click="copyField('binding', selectedBinding.id)"
            >
              {{ copiedField === 'binding' ? '已复制' : '复制' }}
            </CvButton>
          </dd>
        </div>
        <div v-if="selectedBinding.deviceId">
          <dt>设备标识</dt><dd class="cv-copyable-value">
            <code translate="no">{{ selectedBinding.deviceId }}</code><CvButton
              size="sm"
              variant="quiet"
              @click="copyField('device', selectedBinding.deviceId!)"
            >
              {{ copiedField === 'device' ? '已复制' : '复制' }}
            </CvButton>
          </dd>
        </div>
        <div v-if="owner.projection.frame">
          <dt>单帧标识</dt><dd class="cv-copyable-value">
            <code translate="no">{{ owner.projection.frame.frameId }}</code><CvButton
              size="sm"
              variant="quiet"
              @click="copyField('frame', owner.projection.frame.frameId)"
            >
              {{ copiedField === 'frame' ? '已复制' : '复制' }}
            </CvButton>
          </dd>
        </div>
      </dl>
    </details>
  </div>
</template>

<style scoped>
.camera-binding-editor { padding: var(--cv-space-2) 0 var(--cv-space-3); border-bottom: 1px solid var(--cv-border-subtle); }
.camera-binding-editor select { font-size: var(--cv-font-size-xs); }
.camera-binding-editor__actions { display: flex; gap: var(--cv-space-1); }
.camera-binding-editor__technical dl { margin: 0; display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); border-top: 1px solid var(--cv-border-subtle); }
.camera-binding-editor__technical dl > div { min-width: 0; padding: var(--cv-space-2); border-bottom: 1px solid var(--cv-border-subtle); }
.camera-binding-editor__technical dt { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.camera-binding-editor__technical dd { margin: 2px 0 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); overflow-wrap: anywhere; }
@media (max-width: 720px) { .camera-binding-editor__technical dl { grid-template-columns: 1fr; } }
</style>
