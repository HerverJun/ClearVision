<script setup lang="ts">
import { computed } from 'vue';
import { CvIcon } from '@/design-system/icons';
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
const toggleDisabledLabel = computed(() => {
  if (selectedCount.value === 0) return '切换启用状态';
  if (selectedDisabledCount.value === selectedCount.value) return '启用';
  if (selectedDisabledCount.value === 0) return '禁用';
  return '切换状态';
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
    :data-offset-x="runtime?.offsetX ?? 0"
    :data-offset-y="runtime?.offsetY ?? 0"
    :data-mutation-gate="projection.mutationGate"
  >
    <div
      class="flow-canvas-surface__toolbar"
      role="group"
      aria-label="流程画布命令"
    >
      <div
        class="flow-canvas-surface__tool-group"
        role="group"
        aria-label="历史操作"
      >
        <button
          type="button"
          class="flow-canvas-surface__tool-button is-icon-only"
          data-flow-command="undo"
          :disabled="readonly || !runtime?.canUndo"
          title="撤销（Ctrl+Z）"
          aria-label="撤销"
          aria-keyshortcuts="Control+Z"
          @click="emit('undo')"
        >
          <CvIcon
            name="undo"
            size="sm"
          />
        </button>
        <button
          type="button"
          class="flow-canvas-surface__tool-button is-icon-only"
          data-flow-command="redo"
          :disabled="readonly || !runtime?.canRedo"
          title="重做（Ctrl+Y）"
          aria-label="重做"
          aria-keyshortcuts="Control+Y"
          @click="emit('redo')"
        >
          <CvIcon
            name="redo"
            size="sm"
          />
        </button>
      </div>

      <div
        class="flow-canvas-surface__tool-group"
        role="group"
        aria-label="节点编辑"
      >
        <button
          type="button"
          class="flow-canvas-surface__tool-button"
          data-flow-command="copy"
          :disabled="selectedCount === 0"
          title="复制所选节点（Ctrl+C）"
          aria-label="复制所选节点"
          aria-keyshortcuts="Control+C"
          @click="emit('copy')"
        >
          <CvIcon
            name="copy"
            size="sm"
          />
          <span>复制</span>
        </button>
        <button
          type="button"
          class="flow-canvas-surface__tool-button"
          data-flow-command="paste"
          :disabled="readonly"
          title="粘贴节点（Ctrl+V）"
          aria-label="粘贴节点"
          aria-keyshortcuts="Control+V"
          @click="emit('paste')"
        >
          <CvIcon
            name="paste"
            size="sm"
          />
          <span>粘贴</span>
        </button>
        <button
          type="button"
          class="flow-canvas-surface__tool-button"
          data-flow-command="duplicate"
          :disabled="readonly || selectedCount === 0"
          title="创建所选节点的副本"
          aria-label="创建所选节点的副本"
          @click="emit('duplicate')"
        >
          <CvIcon
            name="duplicate"
            size="sm"
          />
          <span>副本</span>
        </button>
      </div>

      <div
        class="flow-canvas-surface__tool-group"
        role="group"
        aria-label="节点状态"
      >
        <button
          type="button"
          class="flow-canvas-surface__tool-button"
          data-flow-command="toggle-disabled"
          :disabled="readonly || selectedCount === 0"
          :title="`${toggleDisabledLabel}所选节点`"
          :aria-label="`${toggleDisabledLabel}所选节点`"
          @click="emit('toggleDisabled')"
        >
          <CvIcon
            name="power"
            size="sm"
          />
          <span>{{ toggleDisabledLabel }}</span>
        </button>
        <button
          type="button"
          class="flow-canvas-surface__tool-button is-destructive"
          data-flow-command="delete"
          :disabled="readonly || (selectedCount === 0 && !runtime?.selectedConnectionId)"
          title="删除所选对象（Delete）"
          aria-label="删除所选对象"
          aria-keyshortcuts="Delete"
          @click="emit('delete')"
        >
          <CvIcon
            name="trash"
            size="sm"
          />
          <span>删除</span>
        </button>
      </div>

      <div
        class="flow-canvas-surface__tool-group flow-canvas-surface__tool-group--view"
        role="group"
        aria-label="画布视图"
      >
        <button
          type="button"
          class="flow-canvas-surface__tool-button is-icon-only"
          data-flow-command="zoom-out"
          title="缩小流程画布"
          aria-label="缩小流程画布"
          @click="emit('zoomOut')"
        >
          <CvIcon
            name="zoom-out"
            size="sm"
          />
        </button>
        <button
          type="button"
          class="flow-canvas-surface__tool-button is-scale"
          data-flow-command="reset-view"
          title="恢复 100% 并居中显示"
          aria-label="恢复流程画布默认视图"
          @click="emit('resetView')"
        >
          {{ Math.round((runtime?.scale ?? 1) * 100) }}%
        </button>
        <button
          type="button"
          class="flow-canvas-surface__tool-button is-icon-only"
          data-flow-command="zoom-in"
          title="放大流程画布"
          aria-label="放大流程画布"
          @click="emit('zoomIn')"
        >
          <CvIcon
            name="zoom-in"
            size="sm"
          />
        </button>
      </div>
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
        role="status"
      >
        正在加载流程画布…
      </div>
      <div
        v-else-if="projection.phase === 'error'"
        class="flow-canvas-surface__loading is-error"
        role="alert"
      >
        <strong>流程画布加载失败</strong>
        <span>{{ projection.error }}</span>
      </div>
    </div>

    <footer class="flow-canvas-surface__status">
      <span class="flow-canvas-surface__counts">
        节点 {{ runtime?.nodeCount ?? projection.draft.operators.length }} ·
        连线 {{ runtime?.connectionCount ?? projection.draft.connections.length }} ·
        已选 {{ selectedCount }}
      </span>
      <span class="flow-canvas-surface__revision">本地流程修订 {{ runtime?.flowRevision ?? 0 }}</span>
      <span class="flow-canvas-surface__spacer" />
      <span
        v-if="projection.feedback"
        class="flow-canvas-surface__feedback"
        :data-tone="projection.feedback.tone"
        role="status"
        aria-live="polite"
      >
        {{ projection.feedback.message }}
      </span>
      <span
        v-else
        class="flow-canvas-surface__hint"
      >拖动平移 · 滚轮缩放 · Shift/Ctrl 框选</span>
    </footer>
  </section>
</template>

<style scoped>
.flow-canvas-surface { position: relative; min-width: 0; min-height: 0; display: grid; grid-template-rows: minmax(0, 1fr) 22px; overflow: hidden; background: var(--flow-canvas-background); container-name: flow-canvas; container-type: inline-size; }
.flow-canvas-surface__toolbar {
  position: absolute;
  z-index: calc(var(--cv-z-sticky) - 1);
  top: 12px;
  right: 12px;
  left: 12px;
  min-width: 0;
  height: 36px;
  display: flex;
  align-items: center;
  gap: var(--cv-space-1);
  padding: 0 var(--cv-space-2);
  overflow: hidden;
  border: 1px solid var(--cv-border-subtle);
  border-radius: var(--cv-radius-sm);
  background: var(--cv-surface-floating);
  box-shadow: var(--cv-elevation-1);
}
.flow-canvas-surface__tool-group { min-width: 0; display: flex; align-items: center; gap: 2px; }
.flow-canvas-surface__tool-group + .flow-canvas-surface__tool-group { margin-left: var(--cv-space-1); padding-left: var(--cv-space-1); border-left: 1px solid var(--cv-border-subtle); }
.flow-canvas-surface__tool-group--view { margin-left: auto !important; }
.flow-canvas-surface__tool-button {
  min-width: 32px;
  height: 32px;
  padding: 0 6px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 4px;
  border: 1px solid transparent;
  border-radius: var(--cv-radius-sm);
  background: transparent;
  color: var(--cv-text-secondary);
  font: inherit;
  font-size: var(--cv-font-size-2xs);
  cursor: pointer;
  white-space: nowrap;
  touch-action: manipulation;
  transition:
    background var(--cv-motion-duration-fast) var(--cv-motion-ease-standard),
    border-color var(--cv-motion-duration-fast) var(--cv-motion-ease-standard),
    color var(--cv-motion-duration-fast) var(--cv-motion-ease-standard);
}
.flow-canvas-surface__tool-button.is-icon-only { width: 32px; padding: 0; }
.flow-canvas-surface__tool-button.is-scale { min-width: 46px; font-variant-numeric: tabular-nums; }
.flow-canvas-surface__tool-button:hover:not(:disabled) { border-color: var(--cv-border-subtle); background: var(--cv-interactive-hover); color: var(--cv-color-industrial-blue); }
.flow-canvas-surface__tool-button:focus-visible { outline: 2px solid var(--cv-focus-ring-color); outline-offset: 0; }
.flow-canvas-surface__tool-button:active:not(:disabled) { background: var(--cv-interactive-active); }
.flow-canvas-surface__tool-button.is-destructive:hover:not(:disabled) { background: var(--cv-color-status-ng-soft); color: var(--cv-color-status-ng-strong); }
.flow-canvas-surface__tool-button:disabled { color: var(--cv-text-muted); cursor: not-allowed; opacity: 0.42; }
.flow-canvas-surface__stage { position: relative; grid-row: 1; min-width: 0; min-height: 300px; overflow: hidden; }
.flow-canvas-surface__stage canvas { display: block; width: 100%; height: 100%; outline: none; touch-action: none; }
.flow-canvas-surface__stage canvas:focus-visible { box-shadow: inset 0 0 0 2px var(--cv-focus-ring-color); }
.flow-canvas-surface__loading { position: absolute; inset: 0; display: grid; place-content: center; gap: var(--cv-space-1); text-align: center; background: color-mix(in srgb, var(--flow-canvas-background) 86%, transparent); color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); pointer-events: none; }
.flow-canvas-surface__loading strong { color: var(--cv-color-status-ng-strong); font-size: var(--cv-font-size-sm); }
.flow-canvas-surface__loading.is-error { color: var(--cv-color-status-ng-strong); }
.flow-canvas-surface__status { grid-row: 2; min-width: 0; display: flex; align-items: center; gap: var(--cv-space-2); padding: 0 var(--cv-space-2); overflow: hidden; border-top: 1px solid var(--cv-border-subtle); background: var(--cv-surface-raised); color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); font-variant-numeric: tabular-nums; white-space: nowrap; }
.flow-canvas-surface__counts,
.flow-canvas-surface__revision,
.flow-canvas-surface__feedback,
.flow-canvas-surface__hint { overflow: hidden; text-overflow: ellipsis; }
.flow-canvas-surface__revision { color: var(--cv-text-secondary); }
.flow-canvas-surface__feedback[data-tone="warning"] { color: var(--cv-color-status-warning-strong); }
.flow-canvas-surface__feedback[data-tone="error"] { color: var(--cv-color-status-error-strong); }
.flow-canvas-surface__feedback[data-tone="success"] { color: var(--cv-color-status-ok-strong); }
.flow-canvas-surface__spacer { flex: 1; }
.flow-canvas-surface__stage :deep(.flow-selection-box) { border: 1px solid var(--flow-canvas-selection-border); background: var(--flow-canvas-selection-background); }
.flow-canvas-surface__stage :deep(.flow-minimap) { width: 168px !important; height: 112px !important; right: 14px !important; bottom: 14px !important; border: 1px solid var(--flow-canvas-minimap-border) !important; border-radius: var(--cv-radius-sm); background: var(--flow-canvas-minimap-background) !important; box-shadow: var(--cv-elevation-raised); }
.flow-canvas-surface__stage :deep(.flow-minimap > canvas) { width: 168px !important; height: 112px !important; }
.flow-canvas-surface__stage :deep(.flow-minimap-toggle) { border: 1px solid var(--flow-canvas-minimap-border) !important; background: var(--flow-canvas-minimap-background) !important; color: var(--flow-canvas-node-text) !important; }

@media (max-width: 1360px) {
  .flow-canvas-surface__tool-button:not(.is-icon-only):not(.is-scale) span { display: none; }
  .flow-canvas-surface__tool-button:not(.is-icon-only):not(.is-scale) { width: 32px; padding: 0; }
}

@container flow-canvas (max-width: 760px) {
  .flow-canvas-surface__tool-button:not(.is-icon-only):not(.is-scale) span { display: none; }
  .flow-canvas-surface__tool-button:not(.is-icon-only):not(.is-scale) { width: 32px; padding: 0; }
  .flow-canvas-surface__tool-group + .flow-canvas-surface__tool-group { margin-left: 2px; padding-left: 2px; }
}

@media (max-height: 760px) {
  .flow-canvas-surface__toolbar { top: 8px; right: 8px; left: 8px; height: 34px; }
}
</style>
