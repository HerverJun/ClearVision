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
  </section>
</template>

<style scoped>
.flow-workspace { min-width: 0; min-height: 0; display: grid; grid-template-columns: minmax(196px, 232px) minmax(520px, 1fr) minmax(280px, 320px); overflow: hidden; }
.flow-workspace__rail-host, .flow-workspace__center { min-width: 0; min-height: 0; }
.flow-workspace__rail-host { overflow: hidden; }
.flow-workspace__center { display: grid; grid-template-rows: minmax(300px, 1fr) minmax(180px, 280px); overflow: hidden; }
@media (max-width: 1220px) { .flow-workspace { grid-template-columns: 196px minmax(520px, 1fr); } .flow-workspace > :deep(.inspector-panel) { display: none; } }
@media (max-width: 920px) { .flow-workspace { grid-template-columns: minmax(0, 1fr); } .flow-workspace__rail-host { display: none; } }
@media (max-height: 650px) { .flow-workspace__center { grid-template-rows: minmax(300px, 1fr) minmax(160px, 180px); } }
</style>
