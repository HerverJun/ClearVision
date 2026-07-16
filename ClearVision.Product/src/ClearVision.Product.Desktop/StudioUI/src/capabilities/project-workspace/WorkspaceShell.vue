<script setup lang="ts">
import { computed } from 'vue';
import { RouterLink } from 'vue-router';
import {
  CvButton,
  CvPageState,
  CvStatusBadge
} from '@/design-system';
import type { WorkspaceProjectV1 } from './workspaceContracts';
import type { WorkspaceOwner } from './workspaceOwner';
import FlowWorkspace from './flow/FlowWorkspace.vue';
import type { WorkspaceLifecycleDiagnostics } from './workspaceLifecycleDiagnostics';

export type WorkspaceShellState =
  | 'flag-off'
  | 'loading'
  | 'ready'
  | 'empty'
  | 'unauthorized'
  | 'forbidden'
  | 'readonly'
  | 'not-found'
  | 'decode-error'
  | 'error';

const props = defineProps<{
  state: WorkspaceShellState;
  projectId: string;
  project: WorkspaceProjectV1 | null;
  workspaceOwner: WorkspaceOwner | null;
  message: string | null;
  diagnostics: WorkspaceLifecycleDiagnostics;
}>();

const emit = defineEmits<{
  retry: [];
  refreshSession: [];
}>();

const pageStateKind = computed(() => {
  if (props.state === 'loading') return 'loading';
  if (props.state === 'unauthorized') return 'unauthorized';
  if (props.state === 'forbidden' || props.state === 'flag-off') return 'forbidden';
  if (props.state === 'not-found') return 'not-found';
  if (props.state === 'empty') return 'empty';
  return 'error';
});

const stateTitle = computed(() => {
  switch (props.state) {
    case 'flag-off': return 'Workspace capability 未启用';
    case 'loading': return '正在读取工程工作区';
    case 'empty': return '当前流程为空';
    case 'unauthorized': return '需要预置会话';
    case 'forbidden': return '无权读取此工程';
    case 'not-found': return '工程不存在（404）';
    case 'decode-error': return '工程合同解析失败';
    case 'error': return '无法读取工程工作区';
    default: return '';
  }
});

const stateDescription = computed(() => props.message ?? {
  'flag-off': '启动配置中的 Studio2.Workspace 保持关闭，未创建读取或 Workspace owner。',
  loading: '正在通过唯一只读端口读取正式 Project/Flow persistence envelope。',
  empty: '工程已成功解码。可从算子区点击或拖拽创建本地 Flow draft。',
  unauthorized: '当前仅支持宿主或测试环境预置会话，不提供新的登录入口。',
  forbidden: '后端权限是唯一安全边界；未创建 Workspace owner。',
  'not-found': '该工程可能已删除，或当前链接中的工程标识已失效。',
  'decode-error': '服务响应缺少 required persistence 字段或字段类型非法；未生成伪 Flow。',
  error: '本地服务未返回可用的工程工作区数据。',
  ready: '',
  readonly: ''
}[props.state]);

const isReadySurface = computed(() =>
  props.state === 'ready' || props.state === 'empty' || props.state === 'readonly');
const isReadonly = computed(() => props.state === 'forbidden' || props.state === 'readonly');
const saveCompatibilityTone = computed(() => {
  const status = props.project?.saveCompatibility.status;
  if (status === 'blocked') return 'ng';
  if (status === 'opaque-passthrough') return 'warning';
  return 'ok';
});
const saveCompatibilityLabel = computed(() => {
  const status = props.project?.saveCompatibility.status;
  if (status === 'blocked') return '保存合同：阻断';
  if (status === 'opaque-passthrough') return '保存合同：opaque passthrough';
  return '保存合同：兼容';
});
</script>

<template>
  <section
    class="workspace-shell"
    data-capability="project-workspace"
    data-evidence-surface="f03-workspace-shell"
    :data-workspace-state="state"
    :data-workspace-project-id="projectId"
    :data-workspace-readonly="isReadonly"
    :data-workspace-owner-count="diagnostics.workspaceOwnerCount"
    :data-workspace-inspector-owner-count="diagnostics.inspectorOwnerCount"
    :data-workspace-preview-owner-count="diagnostics.previewOwnerCount"
    :data-workspace-image-owner-count="diagnostics.imageCanvasOwnerCount"
    :data-workspace-roi-owner-count="diagnostics.roiOwnerCount"
    :data-workspace-inspector-draft-count="diagnostics.activeInspectorDrafts"
    :data-workspace-active-subscriptions="diagnostics.activeSubscriptions"
    :data-workspace-in-flight-reads="diagnostics.inFlightReads"
    :data-workspace-save-compatibility="project?.saveCompatibility.status ?? 'unavailable'"
  >
    <header class="workspace-shell__toolbar">
      <div class="workspace-shell__identity">
        <RouterLink
          class="workspace-shell__back"
          :to="`/projects/${projectId}`"
        >
          ← 工程详情
        </RouterLink>
        <span
          class="workspace-shell__divider"
          aria-hidden="true"
        />
        <div>
          <strong>{{ project?.name ?? '工程工作区' }}</strong>
          <small>
            {{ project ? `版本 ${project.version} · revision ${project.persistenceRevision}` : projectId }}
          </small>
        </div>
      </div>

      <div class="workspace-shell__toolbar-status">
        <CvStatusBadge
          v-if="project"
          :tone="saveCompatibilityTone"
          :label="saveCompatibilityLabel"
        />
        <CvStatusBadge
          :tone="isReadonly ? 'warning' : state === 'ready' || state === 'empty' ? 'ok' : 'idle'"
          :label="isReadonly ? '只读预览' : 'G4 Preview / ROI'"
        />
      </div>
    </header>

    <div
      v-if="isReadySurface && project && workspaceOwner"
      class="workspace-shell__work-area"
    >
      <FlowWorkspace
        :key="projectId"
        :workspace-owner="workspaceOwner"
        :project="project"
      />
    </div>

    <div
      v-else
      class="workspace-shell__work-area workspace-shell__work-area--state"
    >
      <aside
        class="workspace-shell__rail"
        aria-label="算子区占位"
      >
        <div class="workspace-shell__pane-heading">
          <strong>算子区</strong>
          <small>等待工程</small>
        </div>
        <div
          class="workspace-shell__placeholder-lines"
          aria-hidden="true"
        >
          <span />
          <span />
          <span />
          <span />
        </div>
        <p>工程读取成功后加载唯一 Operator catalog owner。</p>
      </aside>

      <div class="workspace-shell__center">
        <div class="workspace-shell__canvas-surface">
          <CvPageState
            :kind="pageStateKind"
            :title="stateTitle"
            :description="stateDescription"
          >
            <template
              v-if="state === 'unauthorized' || state === 'error' || state === 'decode-error'"
              #actions
            >
              <CvButton
                v-if="state === 'unauthorized'"
                size="sm"
                @click="emit('refreshSession')"
              >
                重新检查会话
              </CvButton>
              <CvButton
                v-else
                size="sm"
                @click="emit('retry')"
              >
                重试读取
              </CvButton>
            </template>
            <template
              v-else-if="state === 'not-found' || state === 'forbidden' || state === 'flag-off'"
              #actions
            >
              <RouterLink to="/projects">
                返回工程列表
              </RouterLink>
            </template>
          </CvPageState>
        </div>

        <section
          class="workspace-shell__preview"
          aria-label="预览区占位"
        >
          <strong>预览区</strong>
          <span>G4 未启用 · 无 Preview、artifact 或 binary transport</span>
        </section>
      </div>

      <aside
        class="workspace-shell__inspector"
        aria-label="属性区占位"
      >
        <div class="workspace-shell__pane-heading">
          <strong>属性区</strong>
          <small>G3 未启用</small>
        </div>
        <div
          class="workspace-shell__placeholder-field"
          aria-hidden="true"
        />
        <div
          class="workspace-shell__placeholder-field"
          aria-hidden="true"
        />
        <p>没有 Inspector、参数编辑或 Host file picker。</p>
      </aside>
    </div>

    <footer class="workspace-shell__statusbar">
      <span>Workspace owner {{ diagnostics.workspaceOwnerCount }}/1</span>
      <span>读取 {{ diagnostics.inFlightReads }}</span>
      <span>订阅 {{ diagnostics.activeSubscriptions }}</span>
      <span>Inspector {{ diagnostics.inspectorOwnerCount }}/1</span>
      <span>Preview {{ diagnostics.previewOwnerCount }}/1</span>
      <span>Image {{ diagnostics.imageCanvasOwnerCount }}/1</span>
      <span>ROI {{ diagnostics.roiOwnerCount }}/1</span>
      <span>写入 {{ diagnostics.inFlightWrites }}</span>
      <span class="workspace-shell__statusbar-spacer" />
      <span>F03 · G1–G4 Workspace</span>
    </footer>
  </section>
</template>

<style scoped>
.workspace-shell {
  width: 100%;
  height: 100%;
  min-width: 0;
  min-height: 0;
  display: grid;
  grid-template-rows: var(--cv-workspace-toolbar-height, 44px) minmax(0, 1fr) var(--cv-workspace-status-height, 24px);
  overflow: hidden;
  border: 1px solid var(--cv-border-subtle);
  background: var(--cv-surface-page);
}

.workspace-shell__toolbar,
.workspace-shell__statusbar {
  display: flex;
  align-items: center;
  border-color: var(--cv-border-subtle);
  background: var(--cv-surface-raised);
}

.workspace-shell__toolbar {
  min-width: 0;
  justify-content: space-between;
  gap: var(--cv-space-3);
  padding: 0 var(--cv-space-3);
  border-bottom: 1px solid var(--cv-border-subtle);
}

.workspace-shell__identity,
.workspace-shell__toolbar-status {
  min-width: 0;
  display: flex;
  align-items: center;
  gap: var(--cv-space-2);
}

.workspace-shell__identity > div { min-width: 0; }
.workspace-shell__identity strong,
.workspace-shell__identity small { display: block; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.workspace-shell__identity strong { font-size: var(--cv-font-size-sm); font-weight: var(--cv-font-weight-semibold); }
.workspace-shell__identity small { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.workspace-shell__back { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); text-decoration: none; white-space: nowrap; }
.workspace-shell__back:hover { color: var(--cv-color-link); }
.workspace-shell__divider { width: 1px; height: 22px; background: var(--cv-border-subtle); }

.workspace-shell__work-area {
  min-width: 0;
  min-height: 0;
  display: grid;
  grid-template-columns: minmax(196px, 232px) minmax(520px, 1fr) minmax(280px, 320px);
  overflow: hidden;
}
.workspace-shell__work-area > :deep(.flow-workspace) { grid-column: 1 / -1; }
.workspace-shell__work-area--state { grid-template-columns: minmax(196px, 232px) minmax(520px, 1fr) minmax(280px, 320px); }

.workspace-shell__rail,
.workspace-shell__inspector {
  min-width: 0;
  min-height: 0;
  padding: var(--cv-space-3);
  overflow: auto;
  background: var(--cv-surface-raised);
}
.workspace-shell__rail { border-right: 1px solid var(--cv-border-subtle); }
.workspace-shell__inspector { border-left: 1px solid var(--cv-border-subtle); }
.workspace-shell__rail p,
.workspace-shell__inspector p { color: var(--cv-text-muted); font-size: var(--cv-font-size-xs); line-height: var(--cv-line-height-normal); }

.workspace-shell__pane-heading { display: flex; align-items: baseline; justify-content: space-between; gap: var(--cv-space-2); }
.workspace-shell__pane-heading strong { font-size: var(--cv-font-size-sm); }
.workspace-shell__pane-heading small { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }

.workspace-shell__placeholder-lines { margin-top: var(--cv-space-4); display: grid; gap: var(--cv-space-2); }
.workspace-shell__placeholder-lines span,
.workspace-shell__placeholder-field {
  display: block;
  height: 30px;
  border: 1px solid var(--cv-border-subtle);
  border-radius: var(--cv-radius-sm);
  background: var(--cv-surface-sunken);
}
.workspace-shell__placeholder-lines span:nth-child(2) { width: 84%; }
.workspace-shell__placeholder-lines span:nth-child(3) { width: 72%; }
.workspace-shell__placeholder-lines span:nth-child(4) { width: 91%; }
.workspace-shell__placeholder-field { margin-top: var(--cv-space-3); }

.workspace-shell__center {
  min-width: 0;
  min-height: 0;
  display: grid;
  grid-template-rows: minmax(300px, 1fr) minmax(36px, 180px);
  overflow: hidden;
}
.workspace-shell__canvas-surface {
  min-width: 0;
  min-height: 0;
  display: grid;
  place-items: stretch;
  padding: var(--cv-space-4);
  overflow: auto;
  background:
    linear-gradient(var(--flow-canvas-grid) 1px, transparent 1px),
    linear-gradient(90deg, var(--flow-canvas-grid) 1px, transparent 1px),
    var(--flow-canvas-background);
  background-size: 20px 20px;
}
.workspace-shell__canvas-surface :deep(.cv-page-state) { align-self: center; border: 1px solid var(--cv-border-subtle); background: var(--cv-surface-overlay); }

.workspace-shell__decoded-flow {
  align-self: center;
  justify-self: center;
  width: min(680px, 100%);
  padding: var(--cv-space-5);
  display: grid;
  grid-template-columns: auto minmax(0, 1fr);
  gap: var(--cv-space-3);
  border: 1px solid var(--cv-border-default);
  border-radius: var(--cv-radius-lg);
  background: var(--cv-surface-overlay);
  box-shadow: var(--cv-elevation-1);
}
.workspace-shell__decoded-mark { width: 30px; height: 30px; display: grid; place-items: center; border-radius: 50%; background: var(--cv-color-status-ok-soft); color: var(--cv-color-status-ok-strong); font-weight: var(--cv-font-weight-semibold); }
.workspace-shell__decoded-flow strong { font-size: var(--cv-font-size-md); }
.workspace-shell__decoded-flow p { margin: var(--cv-space-1) 0 0; color: var(--cv-text-secondary); font-size: var(--cv-font-size-sm); }
.workspace-shell__decoded-flow dl { grid-column: 2; margin: var(--cv-space-3) 0 0; display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: var(--cv-space-2); }
.workspace-shell__decoded-flow dl div { padding: var(--cv-space-2); border: 1px solid var(--cv-border-subtle); border-radius: var(--cv-radius-sm); background: var(--cv-surface-page); }
.workspace-shell__decoded-flow dt { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.workspace-shell__decoded-flow dd { margin: var(--cv-space-1) 0 0; overflow: hidden; color: var(--cv-text-primary); font-size: var(--cv-font-size-sm); font-weight: var(--cv-font-weight-medium); text-overflow: ellipsis; white-space: nowrap; }

.workspace-shell__preview {
  min-width: 0;
  min-height: 0;
  padding: var(--cv-space-3);
  display: flex;
  align-items: center;
  gap: var(--cv-space-3);
  border-top: 1px solid var(--cv-border-subtle);
  background: var(--cv-surface-raised);
}
.workspace-shell__preview strong { font-size: var(--cv-font-size-xs); }
.workspace-shell__preview span { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }

.workspace-shell__statusbar {
  gap: var(--cv-space-3);
  padding: 0 var(--cv-space-3);
  border-top: 1px solid var(--cv-border-subtle);
  color: var(--cv-text-secondary);
  font-size: var(--cv-font-size-2xs);
  white-space: nowrap;
}
.workspace-shell__statusbar-spacer { flex: 1; }

@media (max-width: 1220px) {
  .workspace-shell__work-area { grid-template-columns: 196px minmax(520px, 1fr); }
  .workspace-shell__inspector { display: none; }
}

@media (max-width: 920px) {
  .workspace-shell__work-area { grid-template-columns: minmax(0, 1fr); }
  .workspace-shell__rail { display: none; }
}

@media (max-height: 650px) {
  .workspace-shell__center { grid-template-rows: minmax(300px, 1fr) 36px; }
  .workspace-shell__preview { padding-block: var(--cv-space-2); }
}
</style>
