<script setup lang="ts">
import { computed } from 'vue';
import type { FlowCanvasOwnerProjection } from './flowCanvasOwner';

const props = defineProps<{
  canvasId: string;
  projection: FlowCanvasOwnerProjection;
}>();

const emit = defineEmits<{
  undo: [];
  redo: [];
  copy: [];
  paste: [];
  duplicate: [];
  toggleDisabled: [];
  delete: [];
  selectAll: [];
  clearSelection: [];
  zoomIn: [];
  zoomOut: [];
  resetView: [];
}>();

const runtime = computed(() => props.projection.runtime);
const readonly = computed(() => props.projection.mutationGate !== 'editable');
const selectedCount = computed(() => runtime.value?.selectedNodeIds.length ?? 0);
const selectedDisabledCount = computed(() => {
  const selected = new Set(runtime.value?.selectedNodeIds ?? []);
  return runtime.value?.nodes.filter(node => selected.has(node.id) && node.disabled).length ?? 0;
});
</script>

<template>
  <section
    class="flow-canvas-surface"
    data-evidence-surface="f03-g2-flow-canvas"
    :data-flow-revision="runtime?.flowRevision ?? 0"
    :data-node-count="runtime?.nodeCount ?? projection.draft.operators.length"
    :data-connection-count="runtime?.connectionCount ?? projection.draft.connections.length"
    :data-selected-count="selectedCount"
    :data-selected-disabled-count="selectedDisabledCount"
    :data-scale="runtime?.scale ?? 1"
    :data-mutation-gate="projection.mutationGate"
  >
    <div
      class="flow-canvas-surface__toolbar"
      aria-label="流程画布命令"
    >
      <button
        type="button"
        data-flow-command="undo"
        :disabled="readonly || !runtime?.canUndo"
        title="撤销 Ctrl+Z"
        aria-label="撤销（Ctrl+Z）"
        @click="emit('undo')"
      >
        ↶
      </button>
      <button
        type="button"
        data-flow-command="redo"
        :disabled="readonly || !runtime?.canRedo"
        title="重做 Ctrl+Y"
        aria-label="重做（Ctrl+Y）"
        @click="emit('redo')"
      >
        ↷
      </button>
      <span aria-hidden="true" />
      <button
        type="button"
        data-flow-command="copy"
        :disabled="selectedCount === 0"
        title="复制 Ctrl+C"
        @click="emit('copy')"
      >
        复制
      </button>
      <button
        type="button"
        data-flow-command="paste"
        :disabled="readonly"
        title="粘贴 Ctrl+V"
        @click="emit('paste')"
      >
        粘贴
      </button>
      <button
        type="button"
        data-flow-command="duplicate"
        :disabled="readonly || selectedCount === 0"
        title="复制节点"
        @click="emit('duplicate')"
      >
        副本
      </button>
      <button
        type="button"
        data-flow-command="toggle-disabled"
        :disabled="readonly || selectedCount === 0"
        title="启用/禁用"
        @click="emit('toggleDisabled')"
      >
        启停
      </button>
      <button
        type="button"
        data-flow-command="delete"
        :disabled="readonly || (selectedCount === 0 && !runtime?.selectedConnectionId)"
        title="删除 Delete"
        @click="emit('delete')"
      >
        删除
      </button>
      <span aria-hidden="true" />
      <button
        type="button"
        data-flow-command="zoom-out"
        title="缩小"
        aria-label="缩小流程画布"
        @click="emit('zoomOut')"
      >
        −
      </button>
      <button
        type="button"
        data-flow-command="reset-view"
        title="重置视图"
        aria-label="重置流程画布缩放"
        @click="emit('resetView')"
      >
        {{ Math.round((runtime?.scale ?? 1) * 100) }}%
      </button>
      <button
        type="button"
        data-flow-command="zoom-in"
        title="放大"
        aria-label="放大流程画布"
        @click="emit('zoomIn')"
      >
        ＋
      </button>
    </div>

    <div class="flow-canvas-surface__stage">
      <canvas
        :id="canvasId"
        tabindex="0"
        aria-label="流程编辑画布"
        data-testid="flow-canvas"
      />
      <div
        v-if="projection.phase === 'idle'"
        class="flow-canvas-surface__loading"
      >
        正在挂载 canonical FlowCanvas…
      </div>
      <div
        v-else-if="projection.phase === 'error'"
        class="flow-canvas-surface__loading is-error"
      >
        {{ projection.error }}
      </div>
    </div>

    <footer class="flow-canvas-surface__status">
      <span>节点 {{ runtime?.nodeCount ?? projection.draft.operators.length }}</span>
      <span>连线 {{ runtime?.connectionCount ?? projection.draft.connections.length }}</span>
      <span>选择 {{ selectedCount }}</span>
      <span>本地草稿 r{{ runtime?.flowRevision ?? 0 }}</span>
      <span class="flow-canvas-surface__spacer" />
      <span
        v-if="projection.feedback"
        :data-tone="projection.feedback.tone"
      >
        {{ projection.feedback.message }}
      </span>
      <span v-else>拖动画布平移 · 滚轮缩放 · Shift/Ctrl 框选</span>
    </footer>
  </section>
</template>

<style scoped>
.flow-canvas-surface { min-width: 0; min-height: 0; display: grid; grid-template-rows: 32px minmax(0, 1fr) 20px; overflow: hidden; background: var(--flow-canvas-background); }
.flow-canvas-surface__toolbar { min-width: 0; display: flex; align-items: center; gap: 2px; padding: 0 var(--cv-space-2); overflow-x: auto; border-bottom: 1px solid var(--cv-border-subtle); background: color-mix(in srgb, var(--cv-surface-raised) 96%, transparent); scrollbar-width: none; }
.flow-canvas-surface__toolbar button { min-width: 26px; height: 24px; padding: 0 6px; border: 1px solid transparent; border-radius: var(--cv-radius-sm); background: transparent; color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); cursor: pointer; white-space: nowrap; }
.flow-canvas-surface__toolbar button:hover:not(:disabled) { border-color: var(--cv-border-subtle); background: var(--cv-interactive-hover); color: var(--cv-text-primary); }
.flow-canvas-surface__toolbar button:disabled { opacity: 0.45; cursor: not-allowed; }
.flow-canvas-surface__toolbar > span { width: 1px; height: 16px; margin-inline: 3px; background: var(--cv-border-subtle); }
.flow-canvas-surface__stage { position: relative; min-width: 0; min-height: 300px; overflow: hidden; }
.flow-canvas-surface__stage canvas { display: block; width: 100%; height: 100%; outline: none; touch-action: none; }
.flow-canvas-surface__stage canvas:focus-visible { box-shadow: inset 0 0 0 2px var(--cv-color-link); }
.flow-canvas-surface__loading { position: absolute; inset: 0; display: grid; place-items: center; background: color-mix(in srgb, var(--flow-canvas-background) 86%, transparent); color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); pointer-events: none; }
.flow-canvas-surface__loading.is-error { color: var(--cv-color-status-ng-strong); }
.flow-canvas-surface__status { min-width: 0; display: flex; align-items: center; gap: var(--cv-space-2); padding: 0 var(--cv-space-2); overflow: hidden; border-top: 1px solid var(--cv-border-subtle); background: var(--cv-surface-page); color: var(--cv-text-muted); font-size: 10px; white-space: nowrap; }
.flow-canvas-surface__status span:last-child { overflow: hidden; text-overflow: ellipsis; }
.flow-canvas-surface__status [data-tone="warning"] { color: var(--cv-color-status-warning-strong); }
.flow-canvas-surface__status [data-tone="error"] { color: var(--cv-color-status-ng-strong); }
.flow-canvas-surface__status [data-tone="success"] { color: var(--cv-color-status-ok-strong); }
.flow-canvas-surface__spacer { flex: 1; }
.flow-canvas-surface__stage :deep(.flow-selection-box) { border: 1px solid var(--flow-canvas-selection-border); background: var(--flow-canvas-selection-background); }
.flow-canvas-surface__stage :deep(.flow-minimap) { border: 1px solid var(--flow-canvas-minimap-border) !important; background: var(--flow-canvas-minimap-background) !important; box-shadow: var(--cv-elevation-2); }
.flow-canvas-surface__stage :deep(.flow-minimap-toggle) { border: 1px solid var(--flow-canvas-minimap-border) !important; background: var(--flow-canvas-minimap-background) !important; color: var(--flow-canvas-node-text) !important; }
</style>
