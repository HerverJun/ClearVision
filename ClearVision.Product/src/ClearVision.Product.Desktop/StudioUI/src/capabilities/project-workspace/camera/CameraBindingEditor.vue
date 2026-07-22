<script setup lang="ts">
import type { CameraBindingEditorOwner } from './cameraBindingEditorOwner';

const props = defineProps<{
  owner: CameraBindingEditorOwner;
  parameterName: string;
  disabled: boolean;
}>();

function select(event: Event): void {
  props.owner.selectBinding(props.parameterName, (event.target as HTMLSelectElement).value);
}
</script>

<template>
  <div
    class="camera-binding-editor"
    data-capability="camera-binding-editor"
    :data-capture-phase="owner.projection.capturePhase"
    :data-frame-id="owner.projection.frame?.frameId ?? ''"
  >
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
        {{ binding.displayName }} · {{ binding.connectionStatus }}
      </option>
    </select>
    <div class="camera-binding-editor__actions">
      <button
        v-if="owner.projection.capturePhase !== 'capturing'"
        type="button"
        :disabled="disabled || !owner.projection.canCapture"
        @click="owner.capture()"
      >
        捕获单帧
      </button>
      <button
        v-else
        type="button"
        @click="owner.cancelCapture()"
      >
        取消
      </button>
      <button
        type="button"
        :disabled="owner.projection.phase === 'loading'"
        @click="owner.refreshBindings()"
      >
        刷新
      </button>
    </div>
    <p
      role="status"
      :data-tone="owner.projection.capturePhase === 'error' || owner.projection.phase === 'error' ? 'error' : 'info'"
    >
      {{ owner.projection.message }}
    </p>
  </div>
</template>

<style scoped>
.camera-binding-editor { display: grid; gap: var(--cv-space-2); padding: var(--cv-space-2) 0 var(--cv-space-3); border-bottom: 1px solid var(--cv-border-subtle); }
.camera-binding-editor select { width: 100%; min-width: 0; height: var(--cv-density-control-height); padding: 0 var(--cv-space-2); border: 1px solid var(--cv-control-border); border-radius: var(--cv-radius-sm); background: var(--cv-surface-page); color: var(--cv-text-primary); font: inherit; font-size: var(--cv-font-size-xs); }
.camera-binding-editor__actions { display: flex; gap: var(--cv-space-1); }
.camera-binding-editor button { min-height: 26px; padding: 0 var(--cv-space-2); border: 1px solid var(--cv-control-border); border-radius: var(--cv-radius-sm); background: var(--cv-surface-raised); color: var(--cv-text-secondary); font: inherit; font-size: var(--cv-font-size-2xs); cursor: pointer; }
.camera-binding-editor button:first-child { border-color: var(--cv-color-industrial-blue); color: var(--cv-color-industrial-blue); }
.camera-binding-editor button:disabled { opacity: .45; cursor: not-allowed; }
.camera-binding-editor p { margin: 0; color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); line-height: 1.4; }
.camera-binding-editor p[data-tone="error"] { color: var(--cv-color-status-ng-strong); }
</style>
