<script setup lang="ts">
import {
  computed,
  nextTick,
  onBeforeUnmount,
  onMounted,
  shallowRef,
  useTemplateRef,
  watch,
  type CSSProperties
} from 'vue';
import { useStudioPlatform } from '@/app/studioPlatform';
import { CvSplitter } from '@/design-system';
import type { OperatorCatalogItem } from '@/capabilities/operators-read/operatorContracts';
import type { WorkspaceCanvasProjectV1 } from '../workspaceContracts';
import type { WorkspaceOwner } from '../workspaceOwner';
import type { WorkspaceNewDraftOwner } from '../workspaceNewDraftOwner';
import FlowCanvasSurface from './FlowCanvasSurface.vue';
import OperatorRail from './OperatorRail.vue';
import InspectorPanel from '../inspector/InspectorPanel.vue';
import PreviewPanel from '../preview/PreviewPanel.vue';
import { createCalibrationOwner, type CalibrationOwner } from '../calibration';
import { createLineSequenceOwner, type LineSequenceOwner } from '../line-sequence';
import {
  createWorkspaceLayoutOwner,
  workspaceInspectorDefaultWidth,
  workspacePreviewDefaultWidth
} from './workspaceLayoutOwner';
import type { WorkspaceLifecycleDiagnosticsOwner } from '../workspaceLifecycleDiagnostics';

const props = defineProps<{
  workspaceOwner: WorkspaceOwner | WorkspaceNewDraftOwner;
  project: WorkspaceCanvasProjectV1;
  lifecycleDiagnostics?: WorkspaceLifecycleDiagnosticsOwner | undefined;
}>();

const platform = useStudioPlatform();
const flowOwner = props.workspaceOwner.getFlowCanvasOwner() ?? props.workspaceOwner.openFlowCanvas();
const inspectorOwner = flowOwner.openInspector();
const cameraBindingEditorOwner = props.project.id ? flowOwner.openCameraBindingEditor() : null;
const previewWorkbenchOwner = cameraBindingEditorOwner
  ? flowOwner.openPreviewWorkbench(inspectorOwner)
  : null;
const persistedWorkspaceOwner = 'reconcileExternalProject' in props.workspaceOwner
  ? props.workspaceOwner
  : null;
const calibrationOwner: CalibrationOwner | null = previewWorkbenchOwner && persistedWorkspaceOwner && props.project.id
  ? createCalibrationOwner({
    projectId: props.project.id,
    flowOwner,
    inspectorOwner,
    imageOwner: previewWorkbenchOwner.image,
    api: platform.api,
    getPersistenceRevision: () => persistedWorkspaceOwner.projection.persistence?.persistenceRevision ?? null,
    reconcileAfterSave: () => persistedWorkspaceOwner.reconcileExternalProject(),
    diagnostics: props.lifecycleDiagnostics
  })
  : null;
const lineSequenceOwner: LineSequenceOwner | null = props.project.id
  ? createLineSequenceOwner({
      projectId: props.project.id,
      flowOwner,
      api: platform.api,
      getRecentImageBase64: () => {
        const preview = previewWorkbenchOwner?.preview.projection;
        if (!preview || preview.isStale || !['success', 'empty'].includes(preview.phase)) return null;
        return preview.inputImageBase64 ?? preview.outputImageBase64 ?? null;
      },
      diagnostics: props.lifecycleDiagnostics
    })
  : null;
const unregisterCalibration = calibrationOwner
  ? flowOwner.registerLifecycleParticipant(calibrationOwner)
  : null;
const projection = flowOwner.projection;
const layoutOwner = createWorkspaceLayoutOwner();
const layout = layoutOwner.projection;
const shortcutScope = useTemplateRef<HTMLElement>('shortcutScope');
const operatorRailHost = useTemplateRef<HTMLElement>('operatorRailHost');
const inspectorHost = useTemplateRef<HTMLElement>('inspectorHost');
const narrowRailToggle = useTemplateRef<HTMLButtonElement>('narrowRailToggle');
const narrowInspectorToggle = useTemplateRef<HTMLButtonElement>('narrowInspectorToggle');
const narrowRailOpen = shallowRef(false);
const narrowInspectorOpen = shallowRef(false);
const operatorFlyoutOpen = shallowRef(false);
const canvasId = props.project.id
  ? `flow-canvas-${props.project.id.replaceAll('-', '')}`
  : 'flow-canvas-new-handoff-draft';
const isReadonly = computed(() => projection.mutationGate !== 'editable');
const workspaceStyle = computed(() => ({
  '--workspace-inspector-width': `${layout.inspectorWidth}px`,
  '--workspace-preview-width': `${layout.previewCollapsed ? 44 : layout.previewWidth}px`,
  '--workspace-preview-height': `${layout.previewCollapsed ? 38 : layout.previewHeight}px`
}) as CSSProperties);

function focusPane(host: HTMLElement | null): void {
  const target = host?.querySelector<HTMLElement>([
    'input:not([disabled])',
    'button:not([disabled])',
    'select:not([disabled])',
    'textarea:not([disabled])',
    '[tabindex]:not([tabindex="-1"])'
  ].join(','));
  (target ?? host)?.focus({ preventScroll: true });
}

async function toggleNarrowRail(): Promise<void> {
  narrowInspectorOpen.value = false;
  narrowRailOpen.value = !narrowRailOpen.value;
  if (!narrowRailOpen.value) {
    operatorFlyoutOpen.value = false;
    return;
  }
  await nextTick();
  focusPane(operatorRailHost.value);
}

async function toggleNarrowInspector(): Promise<void> {
  narrowRailOpen.value = false;
  operatorFlyoutOpen.value = false;
  narrowInspectorOpen.value = !narrowInspectorOpen.value;
  if (!narrowInspectorOpen.value) return;
  await nextTick();
  focusPane(inspectorHost.value);
}

async function focusInspectorPane(): Promise<void> {
  narrowRailOpen.value = false;
  operatorFlyoutOpen.value = false;
  if (layout.containerWidth <= 1180) narrowInspectorOpen.value = true;
  await nextTick();
  focusPane(inspectorHost.value);
}

async function closeNarrowPane(): Promise<void> {
  if (narrowInspectorOpen.value) {
    narrowInspectorOpen.value = false;
    await nextTick();
    narrowInspectorToggle.value?.focus({ preventScroll: true });
    return;
  }
  if (narrowRailOpen.value) {
    narrowRailOpen.value = false;
    await nextTick();
    narrowRailToggle.value?.focus({ preventScroll: true });
  }
}

function setOperatorFlyoutOpen(value: boolean): void {
  operatorFlyoutOpen.value = value;
  if (value) narrowInspectorOpen.value = false;
}

function addOperator(operator: OperatorCatalogItem): void {
  const result = flowOwner.commands.addOperator(operator);
  if (result.ok) operatorFlyoutOpen.value = false;
}

function engageCanvas(): void {
  operatorFlyoutOpen.value = false;
}

async function closeTemporaryPane(): Promise<void> {
  if (operatorFlyoutOpen.value) {
    operatorFlyoutOpen.value = false;
    return;
  }
  await closeNarrowPane();
}

watch(
  () => props.workspaceOwner.projection.phase,
  phase => flowOwner.setMutationGate(phase === 'readonly' ? 'readonly' : 'editable'),
  { immediate: true }
);

watch(
  () => layout.containerWidth,
  width => {
    if (width > 980) narrowInspectorOpen.value = false;
    if (width > 760) narrowRailOpen.value = false;
    if (width <= 820 && !narrowRailOpen.value) operatorFlyoutOpen.value = false;
  }
);

onMounted(async () => {
  await nextTick();
  if (!shortcutScope.value || !operatorRailHost.value) return;
  layoutOwner.attach(shortcutScope.value);
  flowOwner.mountCanvas({
    canvasId,
    operatorLibraryElement: operatorRailHost.value,
    shortcutScopeElement: shortcutScope.value
  });
  flowOwner.commands.focus();
});

onBeforeUnmount(() => {
  lineSequenceOwner?.dispose('flow-workspace-unmounted');
  calibrationOwner?.dispose('flow-workspace-unmounted');
  unregisterCalibration?.();
  layoutOwner.dispose();
});

</script>

<template>
  <section
    ref="shortcutScope"
    class="flow-workspace"
    :class="{
      'flow-workspace--narrow-rail-open': narrowRailOpen,
      'flow-workspace--narrow-inspector-open': narrowInspectorOpen,
      'flow-workspace--preview-collapsed': layout.previewCollapsed
    }"
    :style="workspaceStyle"
    data-capability="flow-workspace"
    :data-project-id="project.id ?? 'new'"
    :data-flow-owner-phase="projection.phase"
    :data-flow-owner-count="projection.runtime ? 1 : 0"
    :data-flow-revision="projection.runtime?.flowRevision ?? 0"
    :data-inspector-width="layout.inspectorWidth"
    :data-inspector-min-width="layout.inspectorMinWidth"
    :data-inspector-max-width="layout.inspectorMaxWidth"
    :data-preview-height="layout.previewCollapsed ? 38 : layout.previewHeight"
    :data-preview-min-height="layout.previewMinHeight"
    :data-preview-max-height="layout.previewMaxHeight"
    :data-preview-collapsed="layout.previewCollapsed"
    :data-preview-width="layout.previewCollapsed ? 44 : layout.previewWidth"
    :data-preview-min-width="layout.previewMinWidth"
    :data-preview-max-width="layout.previewMaxWidth"
    :data-container-width="layout.containerWidth"
    :data-container-height="layout.containerHeight"
    :data-operator-flyout-open="operatorFlyoutOpen"
    @keydown.esc="closeTemporaryPane"
  >
    <div
      id="workspace-operator-pane"
      ref="operatorRailHost"
      class="flow-workspace__rail-host"
      tabindex="-1"
    >
      <OperatorRail
        :catalog="projection.catalog"
        :readonly="isReadonly"
        :flyout-open="operatorFlyoutOpen"
        @update:flyout-open="setOperatorFlyoutOpen"
        @add="addOperator"
        @refresh="flowOwner.refreshOperators(true)"
      />
    </div>

    <div
      id="workspace-inspector-pane"
      ref="inspectorHost"
      class="flow-workspace__inspector-host"
      tabindex="-1"
    >
      <InspectorPanel
        :owner="inspectorOwner"
        :camera-owner="cameraBindingEditorOwner"
        :calibration-owner="calibrationOwner"
        :line-sequence-owner="lineSequenceOwner"
      />
    </div>

    <CvSplitter
      class="flow-workspace__inspector-splitter"
      data-workspace-splitter="inspector"
      orientation="vertical"
      :model-value="layout.inspectorWidth"
      :min="layout.inspectorMinWidth"
      :max="layout.inspectorMaxWidth"
      :default-value="workspaceInspectorDefaultWidth"
      label="调整属性检查器宽度"
      :value-text="`属性检查器宽度 ${layout.inspectorWidth} 像素`"
      help-text="拖动调整属性检查器宽度；方向键微调，Shift 加速，Home 最窄，End 最宽，Enter 或双击恢复默认宽度"
      @update:model-value="layoutOwner.setInspectorWidth"
      @resize-end="layoutOwner.commit"
    />

    <div
      class="flow-workspace__center"
      @pointerdown.capture="engageCanvas"
      @drop.capture="engageCanvas"
    >
      <FlowCanvasSurface
        class="flow-workspace__canvas"
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
    </div>

    <CvSplitter
      v-if="previewWorkbenchOwner"
      v-show="!layout.previewCollapsed"
      class="flow-workspace__preview-splitter"
      data-workspace-splitter="preview"
      orientation="vertical"
      reversed
      :model-value="layout.previewWidth"
      :min="layout.previewMinWidth"
      :max="layout.previewMaxWidth"
      :default-value="workspacePreviewDefaultWidth"
      label="调整预览工作台宽度"
      :value-text="`预览工作台宽度 ${layout.previewWidth} 像素`"
      help-text="拖动调整预览工作台宽度；方向键微调，Shift 加速，Home 最窄，End 最宽，Enter 或双击恢复默认宽度"
      @update:model-value="layoutOwner.setPreviewWidth"
      @resize-end="layoutOwner.commit"
    />

    <PreviewPanel
      v-if="previewWorkbenchOwner"
      class="flow-workspace__preview"
      :owner="previewWorkbenchOwner"
      :collapsed="layout.previewCollapsed"
      @toggle-collapsed="layoutOwner.togglePreviewCollapsed"
      @focus-inspector="focusInspectorPane"
    />
    <aside
      v-else
      class="flow-workspace__preview flow-workspace__preview--unavailable"
      data-testid="new-draft-preview-unavailable"
    >
      <strong>预览暂不可用</strong>
      <span>候选仍未创建正式工程；保存后才能访问相机与执行预览。</span>
    </aside>

    <button
      ref="narrowRailToggle"
      type="button"
      class="flow-workspace__narrow-pane-toggle flow-workspace__narrow-pane-toggle--rail"
      :aria-expanded="narrowRailOpen"
      aria-controls="workspace-operator-pane"
      @click="toggleNarrowRail"
    >
      {{ narrowRailOpen ? '关闭算子区' : '打开算子区' }}
    </button>
    <button
      ref="narrowInspectorToggle"
      type="button"
      class="flow-workspace__narrow-pane-toggle flow-workspace__narrow-pane-toggle--inspector"
      :aria-expanded="narrowInspectorOpen"
      aria-controls="workspace-inspector-pane"
      @click="toggleNarrowInspector"
    >
      {{ narrowInspectorOpen ? '关闭属性检查器' : '打开属性检查器' }}
    </button>
  </section>
</template>

<style scoped>
.flow-workspace {
  position: relative;
  min-width: 0;
  min-height: 0;
  display: grid;
  grid-template-columns:
    var(--cv-workspace-operator-rail-width)
    var(--workspace-inspector-width)
    8px
    minmax(680px, 1fr)
    8px
    var(--workspace-preview-width);
  grid-template-rows: minmax(0, 1fr);
  overflow: hidden;
  background: var(--cv-surface-page);
}
.flow-workspace__rail-host,
.flow-workspace__center,
.flow-workspace__inspector-host,
.flow-workspace__preview { min-width: 0; min-height: 0; }
.flow-workspace__rail-host { position: relative; z-index: calc(var(--cv-z-dropdown) - 1); overflow: visible; outline: none; background: var(--cv-shell-sidebar); }
.flow-workspace__inspector-host { overflow: hidden; outline: none; }
.flow-workspace__center {
  display: block;
  overflow: hidden;
}
.flow-workspace__canvas { width: 100%; height: 100%; }
.flow-workspace__preview { border-left: 1px solid var(--cv-border-subtle); }
.flow-workspace__preview--unavailable { grid-column: 6; display: grid; align-content: start; gap: var(--cv-space-2); padding: var(--cv-space-4); color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); line-height: var(--cv-line-height-normal); }
.flow-workspace__preview--unavailable strong { color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); }
.flow-workspace__preview-splitter,
.flow-workspace__inspector-splitter { z-index: 2; align-self: stretch; }
.flow-workspace__inspector-host > :deep(.inspector-panel) { width: 100%; height: 100%; }
.flow-workspace__preview > :deep(.preview-panel) { width: 100%; height: 100%; }
.flow-workspace__rail-host:focus-visible,
.flow-workspace__inspector-host:focus-visible { box-shadow: inset 0 0 0 2px var(--cv-focus-ring-color); }
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
.flow-workspace__narrow-pane-toggle--inspector {
  top: calc(var(--cv-space-1) + 32px);
  right: var(--cv-space-2);
}

@media (max-width: 1180px) {
  .flow-workspace {
    grid-template-columns:
      var(--cv-workspace-operator-rail-width)
      minmax(560px, 1fr)
      8px
      var(--workspace-preview-width);
  }
  .flow-workspace__inspector-splitter,
  .flow-workspace__inspector-host { display: none; }
  .flow-workspace__preview--unavailable { grid-column: 4; }
  .flow-workspace__narrow-pane-toggle--inspector { display: inline-flex; align-items: center; }
  .flow-workspace--narrow-inspector-open .flow-workspace__inspector-host {
    position: absolute;
    z-index: calc(var(--cv-z-sticky) + 2);
    inset: 36px auto 0 var(--cv-workspace-operator-rail-width);
    width: min(var(--workspace-inspector-width), calc(100% - var(--cv-workspace-operator-rail-width) - 48px));
    display: grid;
    overscroll-behavior: contain;
    border-right: 1px solid var(--cv-border-default);
    background: var(--cv-surface-raised);
    box-shadow: var(--cv-elevation-floating);
  }
}

@media (max-width: 820px) {
  .flow-workspace {
    grid-template-columns: minmax(0, 1fr);
    grid-template-rows: minmax(320px, 1fr) 8px var(--workspace-preview-height);
  }
  .flow-workspace__rail-host { display: none; }
  .flow-workspace__center { grid-row: 1; }
  .flow-workspace__preview-splitter {
    grid-row: 2;
    display: grid !important;
    width: auto;
    height: 8px;
  }
  .flow-workspace__preview { grid-row: 3; width: 100%; border-top: 1px solid var(--cv-border-subtle); border-left: 0; }
  .flow-workspace__preview--unavailable { grid-column: 1; }
  .flow-workspace__narrow-pane-toggle--rail { display: inline-flex; align-items: center; }
  .flow-workspace--narrow-rail-open .flow-workspace__rail-host {
    position: absolute;
    z-index: calc(var(--cv-z-sticky) + 2);
    inset: 36px auto 0 0;
    width: var(--cv-workspace-operator-rail-width);
    display: block;
    overscroll-behavior: contain;
    box-shadow: var(--cv-elevation-floating);
  }
  .flow-workspace--preview-collapsed { grid-template-rows: minmax(320px, 1fr) 0 38px; }
}
</style>
