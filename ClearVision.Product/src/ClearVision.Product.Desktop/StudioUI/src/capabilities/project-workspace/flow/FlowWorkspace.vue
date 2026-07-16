<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import type { WorkspaceProjectV1 } from '../workspaceContracts';
import type { WorkspaceOwner } from '../workspaceOwner';
import FlowCanvasSurface from './FlowCanvasSurface.vue';
import OperatorRail from './OperatorRail.vue';

const props = defineProps<{
  workspaceOwner: WorkspaceOwner;
  project: WorkspaceProjectV1;
}>();

const flowOwner = props.workspaceOwner.openFlowCanvas();
const projection = flowOwner.projection;
const shortcutScope = ref<HTMLElement | null>(null);
const operatorRailHost = ref<HTMLElement | null>(null);
const canvasId = `flow-canvas-${props.project.id.replaceAll('-', '')}`;
const isReadonly = computed(() => projection.mutationGate !== 'editable');

watch(
  () => props.workspaceOwner.projection.phase,
  phase => flowOwner.setMutationGate(phase === 'readonly' ? 'readonly' : 'editable'),
  { immediate: true }
);

onMounted(async () => {
  await nextTick();
  if (!shortcutScope.value || !operatorRailHost.value) return;
  flowOwner.mountCanvas({
    canvasId,
    operatorLibraryElement: operatorRailHost.value,
    shortcutScopeElement: shortcutScope.value
  });
  flowOwner.commands.focus();
});

onBeforeUnmount(() => {
  flowOwner.dispose('flow-workspace-unmounted');
});
</script>

<template>
  <section
    ref="shortcutScope"
    class="flow-workspace"
    data-capability="flow-workspace"
    :data-project-id="project.id"
    :data-flow-owner-phase="projection.phase"
    :data-flow-owner-count="projection.runtime ? 1 : 0"
    :data-flow-revision="projection.runtime?.flowRevision ?? 0"
  >
    <div
      ref="operatorRailHost"
      class="flow-workspace__rail-host"
    >
      <OperatorRail
        :catalog="projection.catalog"
        :readonly="isReadonly"
        @add="flowOwner.commands.addOperator"
        @refresh="flowOwner.refreshOperators(true)"
      />
    </div>

    <div class="flow-workspace__center">
      <FlowCanvasSurface
        :canvas-id="canvasId"
        :projection="projection"
        @undo="flowOwner.commands.undo"
        @redo="flowOwner.commands.redo"
        @copy="flowOwner.commands.copySelection"
        @paste="flowOwner.commands.pasteSelection"
        @duplicate="flowOwner.commands.duplicateSelection"
        @toggle-disabled="flowOwner.commands.toggleSelectedDisabled"
        @delete="flowOwner.commands.deleteSelection"
        @select-all="flowOwner.commands.selectAll"
        @clear-selection="flowOwner.commands.clearSelection"
        @zoom-in="flowOwner.commands.zoomIn"
        @zoom-out="flowOwner.commands.zoomOut"
        @reset-view="flowOwner.commands.resetView"
      />

      <section
        class="flow-workspace__preview"
        aria-label="预览区占位"
      >
        <strong>预览区</strong>
        <span>G4 未启用 · 无 Preview、artifact 或 binary transport</span>
      </section>
    </div>

    <aside
      class="flow-workspace__inspector"
      aria-label="属性区占位"
    >
      <div>
        <strong>属性区</strong>
        <small>G3 未启用</small>
      </div>
      <p v-if="projection.runtime?.selectedNodeIds.length">
        已选择 {{ projection.runtime.selectedNodeIds.length }} 个节点；G2 仅输出 selection projection。
      </p>
      <p v-else>
        选择节点后，G3 将在此消费同一 selection 与 Flow draft。
      </p>
      <dl>
        <div><dt>flowRevision</dt><dd>{{ projection.runtime?.flowRevision ?? 0 }}</dd></div>
        <div><dt>缩放</dt><dd>{{ Math.round((projection.runtime?.scale ?? 1) * 100) }}%</dd></div>
        <div><dt>模式</dt><dd>{{ projection.mutationGate }}</dd></div>
      </dl>
    </aside>
  </section>
</template>

<style scoped>
.flow-workspace { min-width: 0; min-height: 0; display: grid; grid-template-columns: minmax(196px, 232px) minmax(520px, 1fr) minmax(280px, 320px); overflow: hidden; }
.flow-workspace__rail-host, .flow-workspace__center, .flow-workspace__inspector { min-width: 0; min-height: 0; }
.flow-workspace__rail-host { overflow: hidden; }
.flow-workspace__center { display: grid; grid-template-rows: minmax(300px, 1fr) 44px; overflow: hidden; }
.flow-workspace__preview { padding: 0 var(--cv-space-3); display: flex; align-items: center; gap: var(--cv-space-3); border-top: 1px solid var(--cv-border-subtle); background: var(--cv-surface-raised); }
.flow-workspace__preview strong { font-size: var(--cv-font-size-xs); }
.flow-workspace__preview span { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.flow-workspace__inspector { padding: var(--cv-space-3); overflow: auto; border-left: 1px solid var(--cv-border-subtle); background: var(--cv-surface-raised); }
.flow-workspace__inspector > div { display: flex; align-items: baseline; justify-content: space-between; gap: var(--cv-space-2); }
.flow-workspace__inspector strong { font-size: var(--cv-font-size-sm); }
.flow-workspace__inspector small, .flow-workspace__inspector p { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); line-height: 1.5; }
.flow-workspace__inspector dl { margin: var(--cv-space-4) 0 0; display: grid; gap: var(--cv-space-2); }
.flow-workspace__inspector dl div { padding: var(--cv-space-2); display: flex; justify-content: space-between; gap: var(--cv-space-2); border: 1px solid var(--cv-border-subtle); border-radius: var(--cv-radius-sm); background: var(--cv-surface-page); }
.flow-workspace__inspector dt { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.flow-workspace__inspector dd { margin: 0; color: var(--cv-text-primary); font-size: var(--cv-font-size-xs); }
@media (max-width: 1220px) { .flow-workspace { grid-template-columns: 196px minmax(520px, 1fr); } .flow-workspace__inspector { display: none; } }
@media (max-width: 920px) { .flow-workspace { grid-template-columns: minmax(0, 1fr); } .flow-workspace__rail-host { display: none; } }
@media (max-height: 650px) { .flow-workspace__center { grid-template-rows: minmax(300px, 1fr) 36px; } }
</style>
