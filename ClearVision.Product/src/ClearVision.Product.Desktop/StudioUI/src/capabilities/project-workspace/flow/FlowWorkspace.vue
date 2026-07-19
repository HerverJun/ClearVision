<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue';
import type { WorkspaceProjectV1 } from '../workspaceContracts';
import type { WorkspaceOwner } from '../workspaceOwner';
import FlowCanvasSurface from './FlowCanvasSurface.vue';
import OperatorRail from './OperatorRail.vue';
import InspectorPanel from '../inspector/InspectorPanel.vue';
import PreviewPanel from '../preview/PreviewPanel.vue';

const props = defineProps<{
  workspaceOwner: WorkspaceOwner;
  project: WorkspaceProjectV1;
}>();

const flowOwner = props.workspaceOwner.openFlowCanvas();
const inspectorOwner = flowOwner.openInspector();
const previewWorkbenchOwner = flowOwner.openPreviewWorkbench(inspectorOwner);
const projection = flowOwner.projection;
const shortcutScope = ref<HTMLElement | null>(null);
const operatorRailHost = ref<HTMLElement | null>(null);
const narrowRailOpen = ref(false);
const narrowInspectorOpen = ref(false);
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

</script>

<template>
  <section
    ref="shortcutScope"
    class="flow-workspace"
    :class="{
      'flow-workspace--narrow-rail-open': narrowRailOpen,
      'flow-workspace--narrow-inspector-open': narrowInspectorOpen
    }"
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

      <PreviewPanel :owner="previewWorkbenchOwner" />
    </div>

    <InspectorPanel :owner="inspectorOwner" />

    <button
      type="button"
      class="flow-workspace__narrow-pane-toggle flow-workspace__narrow-pane-toggle--rail"
      :aria-expanded="narrowRailOpen"
      @click="narrowRailOpen = !narrowRailOpen"
    >
      {{ narrowRailOpen ? '关闭算子区' : '打开算子区' }}
    </button>
    <button
      type="button"
      class="flow-workspace__narrow-pane-toggle flow-workspace__narrow-pane-toggle--inspector"
      :aria-expanded="narrowInspectorOpen"
      @click="narrowInspectorOpen = !narrowInspectorOpen"
    >
      {{ narrowInspectorOpen ? '关闭属性检查器' : '打开属性检查器' }}
    </button>
  </section>
</template>

<style scoped>
.flow-workspace { position: relative; min-width: 0; min-height: 0; display: grid; grid-template-columns: minmax(180px, 210px) minmax(600px, 1fr) minmax(260px, 296px); overflow: hidden; background: var(--cv-surface-page); }
.flow-workspace__rail-host, .flow-workspace__center { min-width: 0; min-height: 0; }
.flow-workspace__rail-host { overflow: hidden; }
.flow-workspace__center { display: grid; grid-template-rows: minmax(320px, 1fr) minmax(160px, 220px); overflow: hidden; }
.flow-workspace__narrow-pane-toggle {
  position: absolute;
  z-index: calc(var(--cv-z-sticky) + 1);
  top: var(--cv-space-1);
  display: none;
  height: 26px;
  padding: 0 var(--cv-space-2);
  border: 1px solid var(--cv-border-default);
  border-radius: var(--cv-radius-sm);
  background: var(--cv-surface-floating);
  box-shadow: var(--cv-elevation-1);
  color: var(--cv-text-secondary);
  font: inherit;
  font-size: var(--cv-font-size-2xs);
  cursor: pointer;
}
.flow-workspace__narrow-pane-toggle--rail { left: var(--cv-space-2); }
.flow-workspace__narrow-pane-toggle--inspector { right: var(--cv-space-2); }

@media (max-width: 1180px) {
  .flow-workspace { grid-template-columns: 176px minmax(520px, 1fr) 248px; }
}

@media (max-width: 980px) {
  .flow-workspace { grid-template-columns: 176px minmax(0, 1fr); }
  .flow-workspace > :deep(.inspector-panel) { display: none; }
  .flow-workspace__narrow-pane-toggle--inspector { display: inline-flex; align-items: center; }
  .flow-workspace--narrow-inspector-open > :deep(.inspector-panel) {
    position: absolute;
    z-index: var(--cv-z-sticky);
    inset: 34px 0 0 auto;
    width: min(296px, calc(100% - 48px));
    display: grid;
    box-shadow: var(--cv-elevation-2);
  }
}

@media (max-width: 760px) {
  .flow-workspace { grid-template-columns: minmax(0, 1fr); }
  .flow-workspace__rail-host { display: none; }
  .flow-workspace__narrow-pane-toggle--rail { display: inline-flex; align-items: center; }
  .flow-workspace--narrow-rail-open .flow-workspace__rail-host {
    position: absolute;
    z-index: var(--cv-z-sticky);
    inset: 34px auto 0 0;
    width: min(220px, calc(100% - 48px));
    display: block;
    box-shadow: var(--cv-elevation-2);
  }
}

@media (max-height: 760px) { .flow-workspace__center { grid-template-rows: minmax(300px, 1fr) minmax(140px, 160px); } }
@media (max-height: 650px) { .flow-workspace__center { grid-template-rows: minmax(280px, 1fr) 38px; } }
</style>
