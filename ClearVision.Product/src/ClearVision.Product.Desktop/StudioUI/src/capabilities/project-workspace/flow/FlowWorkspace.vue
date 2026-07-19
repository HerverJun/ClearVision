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
import { CvSplitter } from '@/design-system';
import type { WorkspaceProjectV1 } from '../workspaceContracts';
import type { WorkspaceOwner } from '../workspaceOwner';
import FlowCanvasSurface from './FlowCanvasSurface.vue';
import OperatorRail from './OperatorRail.vue';
import InspectorPanel from '../inspector/InspectorPanel.vue';
import PreviewPanel from '../preview/PreviewPanel.vue';
import {
  createWorkspaceLayoutOwner,
  workspaceInspectorDefaultWidth,
  workspacePreviewDefaultHeight
} from './workspaceLayoutOwner';

const props = defineProps<{
  workspaceOwner: WorkspaceOwner;
  project: WorkspaceProjectV1;
}>();

const flowOwner = props.workspaceOwner.openFlowCanvas();
const inspectorOwner = flowOwner.openInspector();
const previewWorkbenchOwner = flowOwner.openPreviewWorkbench(inspectorOwner);
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
const canvasId = `flow-canvas-${props.project.id.replaceAll('-', '')}`;
const isReadonly = computed(() => projection.mutationGate !== 'editable');
const workspaceStyle = computed(() => ({
  '--workspace-inspector-width': `${layout.inspectorWidth}px`,
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
  if (!narrowRailOpen.value) return;
  await nextTick();
  focusPane(operatorRailHost.value);
}

async function toggleNarrowInspector(): Promise<void> {
  narrowRailOpen.value = false;
  narrowInspectorOpen.value = !narrowInspectorOpen.value;
  if (!narrowInspectorOpen.value) return;
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

onBeforeUnmount(() => layoutOwner.dispose());

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
    :data-project-id="project.id"
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
    @keydown.esc="closeNarrowPane"
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
        @add="flowOwner.commands.addOperator"
        @refresh="flowOwner.refreshOperators(true)"
      />
    </div>

    <div class="flow-workspace__center">
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

      <CvSplitter
        v-show="!layout.previewCollapsed"
        class="flow-workspace__preview-splitter"
        data-workspace-splitter="preview"
        orientation="horizontal"
        reversed
        :model-value="layout.previewHeight"
        :min="layout.previewMinHeight"
        :max="layout.previewMaxHeight"
        :default-value="workspacePreviewDefaultHeight"
        label="调整预览区高度"
        :value-text="`预览区高度 ${layout.previewHeight} 像素`"
        help-text="拖动调整预览区高度；方向键微调，Shift 加速，Home 最小，End 最大，Enter 或双击恢复默认高度"
        @update:model-value="layoutOwner.setPreviewHeight"
        @resize-end="layoutOwner.commit"
      />

      <PreviewPanel
        class="flow-workspace__preview"
        :owner="previewWorkbenchOwner"
        :collapsed="layout.previewCollapsed"
        @toggle-collapsed="layoutOwner.togglePreviewCollapsed"
      />
    </div>

    <CvSplitter
      class="flow-workspace__inspector-splitter"
      data-workspace-splitter="inspector"
      orientation="vertical"
      reversed
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
      id="workspace-inspector-pane"
      ref="inspectorHost"
      class="flow-workspace__inspector-host"
      tabindex="-1"
    >
      <InspectorPanel :owner="inspectorOwner" />
    </div>

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
    minmax(180px, 210px)
    minmax(600px, 1fr)
    8px
    var(--workspace-inspector-width);
  overflow: hidden;
  background: var(--cv-surface-page);
}
.flow-workspace__rail-host,
.flow-workspace__center,
.flow-workspace__inspector-host { min-width: 0; min-height: 0; }
.flow-workspace__rail-host,
.flow-workspace__inspector-host { overflow: hidden; outline: none; }
.flow-workspace__center {
  display: grid;
  grid-template-rows: minmax(352px, 1fr) 8px var(--workspace-preview-height);
  overflow: hidden;
}
.flow-workspace__canvas { grid-row: 1; }
.flow-workspace__preview-splitter { grid-row: 2; }
.flow-workspace__preview { grid-row: 3; }
.flow-workspace--preview-collapsed .flow-workspace__center {
  grid-template-rows: minmax(352px, 1fr) 0 38px;
}
.flow-workspace__preview-splitter,
.flow-workspace__inspector-splitter { z-index: 2; align-self: stretch; }
.flow-workspace__inspector-host > :deep(.inspector-panel) { width: 100%; height: 100%; }
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
.flow-workspace__narrow-pane-toggle--inspector { right: var(--cv-space-2); }

@media (max-width: 1180px) {
  .flow-workspace {
    grid-template-columns:
      176px
      minmax(520px, 1fr)
      8px
      var(--workspace-inspector-width);
  }
}

@media (max-width: 980px) {
  .flow-workspace { grid-template-columns: 176px minmax(0, 1fr); }
  .flow-workspace__inspector-splitter,
  .flow-workspace__inspector-host { display: none; }
  .flow-workspace__narrow-pane-toggle--inspector { display: inline-flex; align-items: center; }
  .flow-workspace--narrow-inspector-open .flow-workspace__inspector-host {
    position: absolute;
    z-index: var(--cv-z-sticky);
    inset: 34px 0 0 auto;
    width: min(var(--workspace-inspector-width), calc(100% - 48px));
    display: grid;
    overscroll-behavior: contain;
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
    overscroll-behavior: contain;
    box-shadow: var(--cv-elevation-2);
  }
}
</style>
