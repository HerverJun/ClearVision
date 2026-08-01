<script setup lang="ts">
import { computed, shallowRef, watch } from 'vue';
import { CvButton, CvInlineAlert, CvSelect, CvStatusBadge } from '@/design-system';
import { CvIcon } from '@/design-system/icons';
import type { CameraBindingV1 } from './deviceContracts';
import { projectSettingsOperationFailure, type SettingsOwner } from './settingsOwner';

const props = withDefaults(defineProps<{
  owner: SettingsOwner;
  bindings: readonly CameraBindingV1[];
  activeCameraId: string;
  canOperate: boolean;
  disabled?: boolean;
}>(), {
  disabled: false
});

const selectedId = shallowRef('');
const pendingAction = shallowRef<string | null>(null);
const feedback = shallowRef<{ tone: 'success' | 'warning' | 'error' | 'info'; title: string; message: string } | null>(null);

const cameraOptions = computed(() => props.bindings.map(item => ({
  value: item.id,
  label: `${item.displayName || item.id} (${item.id})`
})));
const preview = computed(() => props.owner.projection.device.preview);
const isRunning = computed(() => preview.value.phase === 'running' && Boolean(preview.value.sessionId));
const isBusy = computed(() => pendingAction.value !== null);
const selectedBinding = computed(() => props.bindings.find(item => item.id === selectedId.value) ?? null);
const previewTone = computed(() => {
  if (preview.value.phase === 'error') return 'error' as const;
  if (isRunning.value) return 'ok' as const;
  if (preview.value.phase === 'captured') return 'info' as const;
  return 'idle' as const;
});
const previewLabel = computed(() => {
  if (preview.value.phase === 'running') return '连续预览中';
  if (preview.value.phase === 'captured') return '单帧已捕获';
  if (preview.value.phase === 'error') return '预览异常';
  if (preview.value.phase === 'starting') return '启动中';
  return '未运行';
});

function operationMessage(result: { status: string; message?: string; error?: unknown }): string {
  return result.status === 'failed'
    ? projectSettingsOperationFailure(result.error).publicMessage
    : result.message ?? '操作未完成。';
}

function showFeedback(tone: 'success' | 'warning' | 'error' | 'info', title: string, message: string): void {
  feedback.value = { tone, title, message };
}

watch(() => [props.activeCameraId, props.bindings] as const, ([activeId, bindings]) => {
  if (activeId && bindings.some(item => item.id === activeId)) {
    selectedId.value = activeId;
    return;
  }
  if (!bindings.some(item => item.id === selectedId.value)) selectedId.value = bindings[0]?.id ?? '';
}, { immediate: true });

async function capture(): Promise<void> {
  if (!selectedBinding.value || !props.canOperate || props.disabled || isBusy.value) return;
  pendingAction.value = 'capture';
  feedback.value = null;
  try {
    const result = await props.owner.captureSoftTrigger(selectedBinding.value.id);
    if (result.status !== 'completed') {
      showFeedback('error', '单帧捕获失败', operationMessage(result));
      return;
    }
    showFeedback(result.value.success ? 'success' : 'warning', '单帧捕获', result.value.message);
  } finally {
    pendingAction.value = null;
  }
}

async function togglePreview(): Promise<void> {
  if (!selectedBinding.value || !props.canOperate || props.disabled || isBusy.value) return;
  pendingAction.value = isRunning.value ? 'stop-preview' : 'start-preview';
  feedback.value = null;
  try {
    const result = isRunning.value
      ? await props.owner.stopCameraPreview('用户停止连续预览。')
      : await props.owner.startCameraPreview(selectedBinding.value.id);
    if (result.status !== 'completed') {
      showFeedback('error', isRunning.value ? '停止预览失败' : '启动预览失败', operationMessage(result));
      return;
    }
    if ('value' in result && result.value && 'success' in result.value) {
      showFeedback(result.value.success ? 'success' : 'warning', isRunning.value ? '连续预览' : '连续预览', result.value.message);
    } else {
      showFeedback('success', '连续预览', '连续预览已停止。');
    }
  } finally {
    pendingAction.value = null;
  }
}
</script>

<template>
  <section
    class="camera-section camera-preview"
    data-camera-section="preview"
  >
    <header class="camera-section__header">
      <div>
        <h3>调试 Preview</h3>
        <p>Soft capture 和 continuous preview 仅用于调试输入，不写入正式检测结果；离开 Settings 或切换面板时会停止 session。</p>
      </div>
      <CvStatusBadge
        :tone="previewTone"
        :label="previewLabel"
      />
    </header>

    <div
      v-if="bindings.length > 0"
      class="preview-toolbar"
    >
      <CvSelect
        v-model="selectedId"
        label="调试相机"
        name="previewCamera"
        :options="cameraOptions"
        :disabled="disabled || isBusy"
      />
      <div class="preview-actions">
        <CvButton
          variant="secondary"
          size="sm"
          data-camera-action="soft-capture"
          :disabled="!canOperate || disabled || isBusy || !selectedBinding"
          :loading="pendingAction === 'capture'"
          @click="capture"
        >
          <template #leading>
            <CvIcon
              name="camera"
              size="sm"
            />
          </template>
          Soft capture
        </CvButton>
        <CvButton
          :variant="isRunning ? 'quiet' : 'primary'"
          size="sm"
          data-camera-action="toggle-preview"
          :disabled="!canOperate || disabled || isBusy || !selectedBinding"
          :loading="pendingAction === 'start-preview' || pendingAction === 'stop-preview'"
          @click="togglePreview"
        >
          <template #leading>
            <CvIcon
              :name="isRunning ? 'square' : 'play'"
              size="sm"
            />
          </template>
          {{ isRunning ? '停止连续预览' : '开始连续预览' }}
        </CvButton>
      </div>
    </div>
    <div
      v-else
      class="camera-empty"
    >
      没有活动 CameraBinding，无法开始调试预览。
    </div>

    <div
      class="preview-frame"
      :class="{ 'is-running': isRunning }"
    >
      <img
        v-if="preview.imageUrl"
        :src="preview.imageUrl"
        alt="相机调试预览帧"
      >
      <div
        v-else
        class="preview-frame__empty"
      >
        <CvIcon
          name="camera"
          size="lg"
        />
        <strong>{{ preview.message || '尚未接收预览帧' }}</strong>
        <span>当前面板只显示可丢弃调试输入。</span>
      </div>
      <div
        v-if="preview.imageUrl"
        class="preview-frame__meta"
      >
        <span>{{ preview.width ?? '—' }} × {{ preview.height ?? '—' }}</span>
        <span>序号 {{ preview.frameSequence ?? '—' }}</span>
        <span>{{ preview.triggerMode || '—' }}</span>
      </div>
    </div>

    <CvInlineAlert
      v-if="feedback"
      class="camera-preview__alert"
      :tone="feedback.tone"
      :title="feedback.title"
      data-camera-preview-feedback
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
.preview-toolbar { display: grid; grid-template-columns: minmax(220px, 0.45fr) minmax(0, 1fr); align-items: end; gap: var(--cv-space-4); margin-top: var(--cv-space-4); }
.preview-actions { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: var(--cv-space-2); }
.preview-frame { position: relative; display: grid; min-height: 300px; margin-top: var(--cv-space-4); place-items: center; overflow: hidden; border: 1px solid var(--cv-border-subtle); border-radius: var(--cv-radius-sm); background: var(--cv-surface-page); }
.preview-frame.is-running { border-color: var(--cv-color-status-info-border); }
.preview-frame img { display: block; width: 100%; max-height: 520px; object-fit: contain; }
.preview-frame__empty { display: grid; max-width: 460px; justify-items: center; gap: var(--cv-space-2); padding: var(--cv-space-6); color: var(--cv-text-muted); text-align: center; }
.preview-frame__empty strong { color: var(--cv-text-secondary); font-size: var(--cv-font-size-sm); }
.preview-frame__empty span { font-size: var(--cv-font-size-xs); }
.preview-frame__meta { position: absolute; right: var(--cv-space-3); bottom: var(--cv-space-3); display: flex; flex-wrap: wrap; gap: var(--cv-space-2); padding: var(--cv-space-1) var(--cv-space-2); background: color-mix(in srgb, var(--cv-surface-overlay) 90%, transparent); color: var(--cv-text-primary); font-size: var(--cv-font-size-2xs); }
.camera-empty { margin-top: var(--cv-space-4); color: var(--cv-text-muted); font-size: var(--cv-font-size-sm); }
.camera-preview__alert { margin-top: var(--cv-space-4); }
@media (max-width: 700px) { .preview-toolbar { grid-template-columns: 1fr; } .preview-actions { justify-content: flex-start; } .preview-frame { min-height: 220px; } }
</style>
