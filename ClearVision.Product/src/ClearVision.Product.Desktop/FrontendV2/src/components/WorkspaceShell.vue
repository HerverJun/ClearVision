<template>
  <main class="studio2-workspace-shell">
    <header class="studio2-workspace-shell__toolbar">
      <div class="studio2-workspace-shell__brand">
        <span class="studio2-workspace-shell__title">ClearVision Studio 2.0</span>
        <span class="studio2-workspace-shell__meta">{{ model.hostKind }}</span>
      </div>

      <nav
        class="studio2-workspace-shell__modes"
        aria-label="Workspace mode"
      >
        <button
          v-for="mode in modes"
          :key="mode"
          type="button"
          class="studio2-workspace-shell__mode"
          :class="{ 'studio2-workspace-shell__mode--active': shellStore.activeMode === mode }"
          :aria-pressed="shellStore.activeMode === mode"
          @click="setMode(mode)"
        >
          {{ modeLabels[mode] }}
        </button>
      </nav>

      <div class="studio2-workspace-shell__toolbar-status">
        <span>{{ modeLabels[shellStore.activeMode] }}</span>
        <span>{{ model.workspaceState.flowCanvasStatus }}</span>
      </div>
    </header>

    <div
      class="studio2-workspace-shell__body"
      :style="bodyStyle"
    >
      <aside
        class="studio2-workspace-shell__dock studio2-workspace-shell__dock--left"
        :class="{ 'studio2-workspace-shell__dock--collapsed': shellStore.leftDockCollapsed }"
      >
        <button
          type="button"
          class="studio2-workspace-shell__dock-toggle"
          :aria-label="shellStore.leftDockCollapsed ? 'Expand tool dock' : 'Collapse tool dock'"
          @click="toggleLeftDock"
        >
          {{ shellStore.leftDockCollapsed ? '>' : '<' }}
        </button>
        <div class="studio2-workspace-shell__dock-content">
          <FlowEditorPortPanel :model="model" />
          <ProjectPersistencePanel :model="model" />
        </div>
      </aside>

      <section class="studio2-workspace-shell__stage">
        <div
          class="studio2-workspace-shell__pane studio2-workspace-shell__pane--flow"
          :class="{ 'studio2-workspace-shell__pane--hidden': shellStore.activeMode !== 'flow' }"
        >
          <canvas
            :id="flowCanvasId"
            class="studio2-workspace-shell__flow-canvas"
          />
        </div>

        <div
          class="studio2-workspace-shell__pane studio2-workspace-shell__pane--tool"
          :class="{ 'studio2-workspace-shell__pane--hidden': shellStore.activeMode !== 'tool' }"
        >
          <h2>Tool Workspace</h2>
          <p>No business capability is mounted.</p>
        </div>

        <div
          class="studio2-workspace-shell__pane studio2-workspace-shell__pane--review"
          :class="{ 'studio2-workspace-shell__pane--hidden': shellStore.activeMode !== 'review' }"
        >
          <h2>Review Workspace</h2>
          <p>No results capability is mounted.</p>
        </div>
      </section>

      <aside
        class="studio2-workspace-shell__dock studio2-workspace-shell__dock--right"
        :class="{ 'studio2-workspace-shell__dock--collapsed': shellStore.rightDockCollapsed }"
      >
        <button
          type="button"
          class="studio2-workspace-shell__dock-toggle"
          :aria-label="shellStore.rightDockCollapsed ? 'Expand review dock' : 'Collapse review dock'"
          @click="toggleRightDock"
        >
          {{ shellStore.rightDockCollapsed ? '<' : '>' }}
        </button>
        <div class="studio2-workspace-shell__dock-content">
          <h2>Review Dock</h2>
          <p>Review capability pending later goals.</p>
        </div>
      </aside>
    </div>

    <footer class="studio2-workspace-shell__statusbar">
      <span>mode={{ shellStore.activeMode }}</span>
      <span>canvas={{ model.workspaceState.flowCanvasStatus }}</span>
      <span>resize={{ model.workspaceState.resizeCount }}</span>
      <span>listeners={{ model.lifecycle.listenerCount }}</span>
      <span>registry={{ model.lifecycle.registryRegistrationCount }}</span>
    </footer>
  </main>
</template>

<script setup lang="ts">
import { computed, nextTick, onMounted } from 'vue';
import FlowEditorPortPanel from '@/components/FlowEditorPortPanel.vue';
import ProjectPersistencePanel from '@/components/ProjectPersistencePanel.vue';
import type { Studio2FoundationIslandViewModel } from '@/foundation/studio2FoundationIsland';
import {
  DEFAULT_LEFT_DOCK_WIDTH,
  DEFAULT_RIGHT_DOCK_WIDTH,
  useWorkspaceShellStore,
  type WorkspaceShellMode
} from '@/workspace/workspaceShellStore';

const props = defineProps<{
  model: Studio2FoundationIslandViewModel;
}>();

const shellStore = useWorkspaceShellStore();
const flowCanvasId = 'studio2-flow-canvas';
const modes: WorkspaceShellMode[] = ['flow', 'tool', 'review'];
const modeLabels: Record<WorkspaceShellMode, string> = {
  flow: 'Flow',
  tool: 'Tool',
  review: 'Review'
};

const bodyStyle = computed(() => ({
  gridTemplateColumns: `${String(shellStore.leftDockCollapsed ? 48 : shellStore.leftDockWidth)}px minmax(0, 1fr) ${String(shellStore.rightDockCollapsed ? 48 : shellStore.rightDockWidth)}px`
}));

onMounted(async () => {
  props.model.workspaceRuntime.mountFlowCanvas(flowCanvasId);
  props.model.workspaceRuntime.setMode(shellStore.activeMode);
  props.model.refreshLifecycle();
  await nextTick();
  props.model.workspaceRuntime.resizeFlowCanvas('mounted');
  props.model.refreshLifecycle();
});

async function setMode(mode: WorkspaceShellMode): Promise<void> {
  shellStore.setMode(mode);
  props.model.workspaceRuntime.setMode(mode);
  await nextTick();
  if (mode === 'flow') {
    props.model.workspaceRuntime.resizeFlowCanvas('mode-visible');
  }
  props.model.refreshLifecycle();
}

async function toggleLeftDock(): Promise<void> {
  shellStore.toggleLeftDock();
  if (!shellStore.leftDockCollapsed && shellStore.leftDockWidth === 0) {
    shellStore.setLeftDockWidth(DEFAULT_LEFT_DOCK_WIDTH);
  }
  await nextTick();
  props.model.workspaceRuntime.resizeFlowCanvas('left-dock-toggle');
  props.model.refreshLifecycle();
}

async function toggleRightDock(): Promise<void> {
  shellStore.toggleRightDock();
  if (!shellStore.rightDockCollapsed && shellStore.rightDockWidth === 0) {
    shellStore.setRightDockWidth(DEFAULT_RIGHT_DOCK_WIDTH);
  }
  await nextTick();
  props.model.workspaceRuntime.resizeFlowCanvas('right-dock-toggle');
  props.model.refreshLifecycle();
}
</script>

<style scoped>
.studio2-workspace-shell {
  box-sizing: border-box;
  min-height: 100vh;
  height: 100vh;
  display: grid;
  grid-template-rows: 48px minmax(0, 1fr) 28px;
  overflow: hidden;
  background: #141817;
  color: #edf3ef;
  font-family: Inter, "Segoe UI", "Microsoft YaHei", sans-serif;
}

.studio2-workspace-shell *,
.studio2-workspace-shell *::before,
.studio2-workspace-shell *::after {
  box-sizing: border-box;
}

.studio2-workspace-shell__toolbar {
  display: grid;
  grid-template-columns: minmax(220px, 1fr) auto minmax(180px, 1fr);
  align-items: center;
  gap: 12px;
  padding: 0 14px;
  border-bottom: 1px solid #26312d;
  background: #1a211e;
}

.studio2-workspace-shell__brand {
  min-width: 0;
  display: flex;
  align-items: baseline;
  gap: 10px;
}

.studio2-workspace-shell__title {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 15px;
  font-weight: 650;
}

.studio2-workspace-shell__meta {
  overflow: hidden;
  color: #9fb0a8;
  font-size: 12px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.studio2-workspace-shell__modes {
  display: inline-grid;
  grid-auto-flow: column;
  gap: 4px;
  padding: 3px;
  border: 1px solid #31423a;
  border-radius: 6px;
  background: #101411;
}

.studio2-workspace-shell__mode {
  min-width: 72px;
  height: 30px;
  border: 0;
  border-radius: 4px;
  background: transparent;
  color: #b8c8c0;
  font: inherit;
  font-size: 13px;
  cursor: pointer;
}

.studio2-workspace-shell__mode--active {
  background: #2c6f54;
  color: #ffffff;
}

.studio2-workspace-shell__toolbar-status {
  justify-self: end;
  min-width: 0;
  display: flex;
  gap: 10px;
  color: #b8c8c0;
  font-size: 12px;
}

.studio2-workspace-shell__body {
  min-height: 0;
  display: grid;
  gap: 0;
}

.studio2-workspace-shell__dock {
  min-width: 0;
  position: relative;
  display: grid;
  grid-template-rows: 40px minmax(0, 1fr);
  overflow: hidden;
  border-color: #26312d;
  background: #181e1b;
}

.studio2-workspace-shell__dock--left {
  border-right: 1px solid #26312d;
}

.studio2-workspace-shell__dock--right {
  border-left: 1px solid #26312d;
}

.studio2-workspace-shell__dock-toggle {
  width: 32px;
  height: 28px;
  align-self: center;
  justify-self: center;
  border: 1px solid #31423a;
  border-radius: 4px;
  background: #101411;
  color: #d9e6df;
  cursor: pointer;
}

.studio2-workspace-shell__dock-content {
  min-width: 0;
  overflow: hidden;
  padding: 12px;
  color: #aebdb6;
}

.studio2-workspace-shell__dock-content h2,
.studio2-workspace-shell__pane h2 {
  margin: 0 0 8px;
  color: #edf3ef;
  font-size: 14px;
  font-weight: 650;
}

.studio2-workspace-shell__dock-content p,
.studio2-workspace-shell__pane p {
  margin: 0;
  color: #9fb0a8;
  font-size: 12px;
  line-height: 1.5;
}

.studio2-workspace-shell__dock--collapsed .studio2-workspace-shell__dock-content {
  display: none;
}

.studio2-workspace-shell__stage {
  min-width: 0;
  min-height: 0;
  position: relative;
  overflow: hidden;
  background: #101411;
}

.studio2-workspace-shell__pane {
  position: absolute;
  inset: 0;
  min-width: 0;
  min-height: 0;
}

.studio2-workspace-shell__pane--hidden {
  visibility: hidden;
  opacity: 0;
  pointer-events: none;
}

.studio2-workspace-shell__pane--flow {
  display: block;
}

.studio2-workspace-shell__flow-canvas {
  display: block;
  width: 100%;
  height: 100%;
}

.studio2-workspace-shell__pane--tool,
.studio2-workspace-shell__pane--review {
  display: grid;
  align-content: start;
  gap: 8px;
  padding: 16px;
  background: #111613;
}

.studio2-workspace-shell__statusbar {
  min-width: 0;
  display: flex;
  align-items: center;
  gap: 14px;
  overflow: hidden;
  padding: 0 12px;
  border-top: 1px solid #26312d;
  background: #1a211e;
  color: #9fb0a8;
  font-size: 12px;
}

.studio2-workspace-shell__statusbar span {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

@media (max-width: 900px) {
  .studio2-workspace-shell__toolbar {
    grid-template-columns: minmax(160px, 1fr) auto;
  }

  .studio2-workspace-shell__toolbar-status {
    display: none;
  }
}
</style>
