<script setup lang="ts">
import { computed, ref, shallowRef } from 'vue';
import { RouterLink } from 'vue-router';
import {
  CvButton,
  CvPageState,
  CvStatusBadge
} from '@/design-system';
import { CvIcon } from '@/design-system/icons';
import type { WorkspaceProjectV1 } from './workspaceContracts';
import type { WorkspaceOwner } from './workspaceOwner';
import FlowWorkspace from './flow/FlowWorkspace.vue';
import type { WorkspaceLifecycleDiagnostics } from './workspaceLifecycleDiagnostics';
import { GlobalVariablesWorkbench, type WorkspaceGlobalVariablesOwner } from './global-variables';
import { FinalDecisionWorkbench, type FinalDecisionOwner } from './final-decision';
import type { FlowCanvasOwner } from './flow';
import { RuntimePackageExportDialog, type RuntimePackageExportOwner } from './runtime-package';
import { formatInspectionOutcome } from '@/shared/inspectionOutcome';

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
  userRole?: string | null | undefined;
}>();

const emit = defineEmits<{
  retry: [];
  refreshSession: [];
}>();
const variablesOpen = ref(false);
const decisionOpen = ref(false);
const variablesOwner = shallowRef<WorkspaceGlobalVariablesOwner | null>(null);
const decisionOwner = shallowRef<FinalDecisionOwner | null>(null);
const modalFlowOwner = shallowRef<FlowCanvasOwner | null>(null);
const packageOwner = shallowRef<RuntimePackageExportOwner | null>(null);
const packageOpen = ref(false);

function openVariables(): void {
  variablesOwner.value = props.workspaceOwner?.getGlobalVariablesOwner() ?? null;
  modalFlowOwner.value = props.workspaceOwner?.getFlowCanvasOwner() ?? null;
  variablesOpen.value = Boolean(variablesOwner.value && modalFlowOwner.value);
}

function openDecision(): void {
  decisionOwner.value = props.workspaceOwner?.getFinalDecisionOwner() ?? null;
  decisionOpen.value = decisionOwner.value !== null;
}

function openRuntimePackage(): void {
  packageOwner.value = props.workspaceOwner?.getRuntimePackageExportOwner() ?? null;
  packageOpen.value = packageOwner.value !== null;
}

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
    case 'flag-off': return '工程工作区未启用';
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
  'flag-off': '当前启动配置未开放工程编辑工作区。',
  loading: '正在读取工程、流程与资源信息。',
  empty: '当前流程为空，可从左侧算子区点击或拖拽添加算子。',
  unauthorized: '当前仅支持宿主或测试环境预置会话，不提供新的登录入口。',
  forbidden: '当前账户没有读取此工程的权限。',
  'not-found': '该工程可能已删除，或当前链接中的工程标识已失效。',
  'decode-error': '工程数据不完整或格式不受支持，未创建临时流程。',
  error: '本地服务未返回可用的工程工作区数据。',
  ready: '',
  readonly: ''
}[props.state]);

const isReadySurface = computed(() =>
  props.state === 'ready' || props.state === 'empty' || props.state === 'readonly');
const isReadonly = computed(() => props.state === 'forbidden' || props.state === 'readonly');
const currentProject = computed(() => props.workspaceOwner?.projection.project ?? props.project);
const persistence = computed(() => props.workspaceOwner?.projection.persistence ?? null);
const run = computed(() => props.workspaceOwner?.projection.run ?? null);
const saveCompatibilityTone = computed(() => {
  const status = currentProject.value?.saveCompatibility.status;
  if (status === 'blocked') return 'ng';
  if (status === 'opaque-passthrough') return 'warning';
  return 'ok';
});
const saveCompatibilityLabel = computed(() => {
  const status = currentProject.value?.saveCompatibility.status;
  if (status === 'blocked') return '当前工程无法安全保存';
  if (status === 'opaque-passthrough') return '含兼容字段，保存时将原样保留';
  return '工程可安全保存';
});
const showSaveCompatibility = computed(() =>
  currentProject.value?.saveCompatibility.status !== 'compatible');
const persistenceTone = computed(() => {
  const phase = persistence.value?.phase;
  if (phase === 'conflict' || phase === 'error' || phase === 'unknown-outcome') return 'ng';
  if (phase === 'dirty' || phase === 'saving' || phase === 'running' || phase === 'readonly') return 'warning';
  if (phase === 'saved' || phase === 'clean') return 'ok';
  return 'idle';
});
const persistenceLabel = computed(() => {
  const projection = persistence.value;
  if (!projection) return '正在准备保存';
  return {
    clean: '已保存',
    dirty: '未保存',
    saving: '保存中',
    saved: '保存成功',
    error: '保存失败',
    conflict: '保存冲突',
    running: '运行中锁定',
    readonly: '只读',
    'unknown-outcome': '保存结果未知',
    disposed: '已释放'
  }[projection.phase];
});
const runTone = computed(() => {
  const phase = run.value?.phase;
  if (phase === 'succeeded') return 'ok';
  if (phase === 'failed' || phase === 'unknown-outcome') return 'ng';
  if (phase === 'admitting' || phase === 'executing' || phase === 'cancel-requested') return 'warning';
  return 'idle';
});
const runLabel = computed(() => {
  const projection = run.value;
  if (!projection) return '正式运行尚未就绪';
  return {
    idle: '正式运行就绪',
    blocked: '当前状态不可正式运行',
    admitting: '正在检查运行条件',
    executing: '正式运行中',
    succeeded: '正式运行完成',
    failed: '正式运行失败',
    cancelled: '正式运行已取消',
    'cancel-requested': '正在停止正式运行',
    'unknown-outcome': '运行结果待确认',
    disposed: '正式运行已结束'
  }[projection.phase];
});
const runResultPresentation = computed(() => run.value?.result
  ? formatInspectionOutcome(run.value.result.outcome)
  : null);
const showRunSummary = computed(() => Boolean(run.value && [
  'succeeded', 'failed', 'cancelled', 'unknown-outcome'
].includes(run.value.phase)));
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
    :data-workspace-in-flight-writes="diagnostics.inFlightWrites"
    :data-workspace-persistence-owner-count="diagnostics.persistenceOwnerCount"
    :data-workspace-run-owner-count="diagnostics.runOwnerCount"
    :data-workspace-run-phase="run?.phase ?? 'unavailable'"
    :data-workspace-run-snapshot-id="run?.clientSnapshotId ?? ''"
    :data-workspace-persistence-phase="persistence?.phase ?? 'unavailable'"
    :data-workspace-dirty="persistence?.dirty ?? false"
    :data-workspace-dirty-generation="persistence?.dirtyGeneration ?? 0"
    :data-workspace-persistence-revision="persistence?.persistenceRevision ?? currentProject?.persistenceRevision ?? -1"
    :data-workspace-save-compatibility="currentProject?.saveCompatibility.status ?? 'unavailable'"
  >
    <header class="workspace-shell__toolbar">
      <div class="workspace-shell__identity">
        <nav
          class="workspace-shell__back-nav"
          aria-label="工程导航"
        >
          <RouterLink
            class="workspace-shell__back"
            to="/projects"
          >
            工程列表
          </RouterLink>
          <RouterLink
            class="workspace-shell__back"
            :to="`/projects/${projectId}`"
          >
            工程详情
          </RouterLink>
        </nav>
        <span
          class="workspace-shell__divider"
          aria-hidden="true"
        />
        <div
          class="workspace-shell__project"
          :title="`工程 ID：${projectId}${currentProject ? `；版本：${currentProject.version}；保存修订：${persistence?.persistenceRevision ?? currentProject.persistenceRevision}` : ''}`"
        >
          <strong>{{ currentProject?.name ?? '工程工作区' }}</strong>
          <small v-if="currentProject">版本 {{ currentProject.version }}</small>
        </div>
      </div>

      <div class="workspace-shell__commands">
        <CvButton
          data-capability="final-decision"
          data-testid="final-decision"
          size="sm"
          variant="secondary"
          :disabled="!persistence || isReadonly"
          title="配置正式运行使用的最终判定"
          @click="openDecision"
        >
          <template #leading>
            <CvIcon
              name="decision"
              size="sm"
            />
          </template>
          最终判定
        </CvButton>
        <CvButton
          v-if="persistence"
          data-testid="workspace-save"
          size="sm"
          variant="secondary"
          :disabled="!persistence.canSave"
          @click="workspaceOwner?.save()"
        >
          <template #leading>
            <CvIcon
              name="save"
              size="sm"
            />
          </template>
          {{ persistence.phase === 'saving' ? '保存中…' : '保存' }}
        </CvButton>
        <CvButton
          v-if="run"
          data-testid="workspace-run"
          size="sm"
          variant="primary"
          :disabled="!run.canRun"
          @click="workspaceOwner?.runFormal()"
        >
          <template #leading>
            <CvIcon
              name="play"
              size="sm"
            />
          </template>
          运行
        </CvButton>
        <CvButton
          v-if="run?.canStop"
          data-testid="workspace-run-stop"
          size="sm"
          variant="danger"
          @click="workspaceOwner?.stopFormal()"
        >
          停止运行
        </CvButton>
        <CvButton
          data-capability="global-variables"
          data-testid="global-variables"
          size="sm"
          variant="quiet"
          :disabled="!persistence || isReadonly"
          title="管理本工程的变量定义与绑定"
          @click="openVariables"
        >
          <template #leading>
            <CvIcon
              name="variables"
              size="sm"
            />
          </template>
          全局变量
        </CvButton>
        <CvButton
          v-if="userRole === 'Admin'"
          data-testid="runtime-package-export"
          size="sm"
          variant="quiet"
          :disabled="!persistence || run?.phase === 'executing'"
          title="从已正式保存的工程导出运行包"
          @click="openRuntimePackage"
        >
          运行包
        </CvButton>
        <RouterLink
          class="workspace-shell__results-link"
          :to="{ path: '/results', query: { source: 'local', projectId } }"
          data-testid="workspace-results"
        >
          当前工程结果
        </RouterLink>
        <CvButton
          v-if="run?.canReconcile"
          data-testid="workspace-run-reconcile"
          size="sm"
          variant="quiet"
          @click="workspaceOwner?.reconcileFormalRun()"
        >
          查询运行结果
        </CvButton>
        <CvButton
          v-if="persistence?.canRetry"
          data-testid="workspace-save-retry"
          size="sm"
          @click="workspaceOwner?.retrySave()"
        >
          重试
        </CvButton>
        <CvButton
          v-if="persistence?.canReconcile"
          data-testid="workspace-save-reconcile"
          size="sm"
          @click="workspaceOwner?.reconcileSave()"
        >
          核对保存结果
        </CvButton>
        <CvButton
          v-if="persistence?.canReapplyConflict"
          data-testid="workspace-conflict-reapply"
          size="sm"
          @click="workspaceOwner?.reapplyConflict()"
        >
          重新应用本地草稿
        </CvButton>
        <CvButton
          v-if="persistence?.canDiscardConflict"
          data-testid="workspace-conflict-discard"
          size="sm"
          @click="workspaceOwner?.discardConflict()"
        >
          放弃本地草稿
        </CvButton>
      </div>
      <section
        v-if="showRunSummary && run"
        class="workspace-shell__run-summary"
        :data-run-phase="run.phase"
        role="status"
      >
        <CvStatusBadge
          :tone="runResultPresentation?.tone ?? (run.phase === 'unknown-outcome' ? 'warning' : 'error')"
          :label="runResultPresentation?.label ?? runLabel"
        />
        <span>{{ runResultPresentation?.executionLabel ?? run.message }}</span>
        <span v-if="runResultPresentation">{{ runResultPresentation.decisionLabel }}</span>
        <span v-if="run.result?.errorMessage">{{ run.result.errorMessage }}</span>
        <span v-else-if="run.phase === 'failed' || run.phase === 'cancelled'">{{ run.message }}</span>
        <RouterLink
          v-if="run.result && run.result.outcome.execution === 'Succeeded'"
          class="workspace-shell__run-result-link"
          :to="{ path: '/results', query: { source: 'local', projectId: run.result.projectId, resultId: run.result.id } }"
          data-testid="workspace-current-result"
        >
          查看本次结果
        </RouterLink>
        <button
          v-if="run.phase === 'unknown-outcome' && run.canReconcile"
          type="button"
          @click="workspaceOwner?.reconcileFormalRun()"
        >
          核对运行结果
        </button>
      </section>
    </header>

    <div
      v-if="isReadySurface && currentProject && workspaceOwner"
      class="workspace-shell__work-area"
    >
      <FlowWorkspace
        :key="projectId"
        :workspace-owner="workspaceOwner"
        :project="currentProject"
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
        <p>工程读取成功后将在此显示可用算子。</p>
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
          <span>工程加载成功后可在此查看节点预览；预览结果不等同于正式运行结果。</span>
        </section>
      </div>

      <aside
        class="workspace-shell__inspector"
        aria-label="属性区占位"
      >
        <div class="workspace-shell__pane-heading">
          <strong>属性区</strong>
          <small>等待工程</small>
        </div>
        <div
          class="workspace-shell__placeholder-field"
          aria-hidden="true"
        />
        <div
          class="workspace-shell__placeholder-field"
          aria-hidden="true"
        />
        <p>工程加载成功后可在此查看并编辑所选节点或连线的属性。</p>
      </aside>
    </div>

    <footer class="workspace-shell__statusbar">
      <span
        class="workspace-shell__project-status"
        :title="currentProject ? `工程：${currentProject.name}；版本：${currentProject.version}` : `工程 ID：${projectId}`"
      >工程：{{ currentProject?.name ?? projectId }}</span>
      <span
        class="workspace-shell__status-divider"
        aria-hidden="true"
      />
      <CvStatusBadge
        :tone="persistenceTone"
        :label="persistenceLabel"
      />
      <CvStatusBadge
        v-if="run"
        :tone="runTone"
        :label="runLabel"
      />
      <CvStatusBadge
        v-if="showSaveCompatibility"
        :tone="saveCompatibilityTone"
        :label="saveCompatibilityLabel"
      />
      <span
        v-if="persistence && ['error', 'conflict', 'unknown-outcome', 'readonly'].includes(persistence.phase)"
        class="workspace-shell__status-message"
      >{{ persistence.message }}</span>
      <span class="workspace-shell__statusbar-spacer" />
      <details class="workspace-shell__diagnostics">
        <summary>技术状态</summary>
        <dl>
          <div><dt>工作区</dt><dd>{{ diagnostics.workspaceOwnerCount }}/1</dd></div>
          <div><dt>属性检查器</dt><dd>{{ diagnostics.inspectorOwnerCount }}/1</dd></div>
          <div><dt>预览</dt><dd>{{ diagnostics.previewOwnerCount }}/1</dd></div>
          <div><dt>图像</dt><dd>{{ diagnostics.imageCanvasOwnerCount }}/1</dd></div>
          <div><dt>ROI</dt><dd>{{ diagnostics.roiOwnerCount }}/1</dd></div>
          <div><dt>保存</dt><dd>{{ diagnostics.persistenceOwnerCount }}/1</dd></div>
          <div><dt>读取中</dt><dd>{{ diagnostics.inFlightReads }}</dd></div>
          <div><dt>写入中</dt><dd>{{ diagnostics.inFlightWrites }}</dd></div>
          <div><dt>活动订阅</dt><dd>{{ diagnostics.activeSubscriptions }}</dd></div>
        </dl>
      </details>
    </footer>
    <GlobalVariablesWorkbench
      v-if="variablesOwner && modalFlowOwner"
      :open="variablesOpen"
      :owner="variablesOwner"
      :flow-owner="modalFlowOwner"
      :readonly="isReadonly || run?.phase === 'executing'"
      @close="variablesOpen = false"
    />
    <FinalDecisionWorkbench
      v-if="decisionOwner"
      :open="decisionOpen"
      :owner="decisionOwner"
      :readonly="isReadonly || run?.phase === 'executing'"
      @close="decisionOpen = false"
    />
    <RuntimePackageExportDialog
      v-if="packageOwner && currentProject"
      :open="packageOpen"
      :project="currentProject"
      :dirty="persistence?.dirty ?? false"
      :owner="packageOwner"
      @close="packageOpen = false"
    />
  </section>
</template>

<style scoped>
.workspace-shell {
  width: 100%;
  height: 100%;
  min-width: 0;
  min-height: 0;
  display: grid;
  grid-template-rows: auto minmax(0, 1fr) var(--cv-workspace-status-height, 24px);
  overflow: hidden;
  background: var(--cv-surface-page);
}

.workspace-shell__toolbar,
.workspace-shell__statusbar {
  display: flex;
  align-items: center;
  border-color: var(--cv-border-subtle);
  background: var(--cv-surface-page);
}

.workspace-shell__toolbar {
  min-width: 0;
  min-height: var(--cv-workspace-toolbar-height, 44px);
  flex-wrap: wrap;
  justify-content: space-between;
  gap: var(--cv-space-2);
  padding: 0 var(--cv-space-2);
  border-bottom: 1px solid var(--cv-border-subtle);
}
.workspace-shell__run-summary { flex: 1 0 100%; min-width: 0; min-height: 34px; margin-inline: calc(-1 * var(--cv-space-2)); padding: 0 var(--cv-space-3); display: flex; align-items: center; gap: var(--cv-space-2); border-top: 1px solid var(--cv-border-subtle); background: var(--cv-surface-raised); color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
.workspace-shell__run-summary > span { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.workspace-shell__run-result-link,.workspace-shell__run-summary > button { margin-left: auto; white-space: nowrap; color: var(--cv-color-link); font-size: var(--cv-font-size-xs); font-weight: 600; }
.workspace-shell__run-summary > button { min-height: 26px; border: 1px solid var(--cv-control-border); border-radius: var(--cv-radius-sm); background: var(--cv-surface-page); cursor: pointer; }

.workspace-shell__identity,
.workspace-shell__commands {
  min-width: 0;
  display: flex;
  align-items: center;
  gap: var(--cv-space-1);
}
.workspace-shell__commands :deep(.cv-button) { flex: 0 0 auto; }
.workspace-shell__commands :deep(.cv-button:not(.cv-button--primary)) { background: var(--cv-surface-raised); }
.workspace-shell__commands :deep(.cv-button--primary) { background: var(--cv-color-industrial-blue); border-color: var(--cv-color-industrial-blue); }
.workspace-shell__commands :deep(.cv-button--primary:hover:not(:disabled)) { background: var(--cv-color-industrial-blue-hover); border-color: var(--cv-color-industrial-blue-hover); }

.workspace-shell__identity > div { min-width: 0; }
.workspace-shell__identity strong,
.workspace-shell__identity small { display: block; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.workspace-shell__identity strong { font-size: var(--cv-font-size-sm); font-weight: var(--cv-font-weight-semibold); letter-spacing: -0.01em; }
.workspace-shell__identity small { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); }
.workspace-shell__project { display: flex; align-items: baseline; gap: var(--cv-space-2); }
.workspace-shell__back-nav { display: flex; align-items: center; gap: var(--cv-space-2); }
.workspace-shell__back { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); text-decoration: none; white-space: nowrap; }
.workspace-shell__back:hover { color: var(--cv-color-link); }
.workspace-shell__commands {
  justify-content: flex-end;
  overflow-x: auto;
  scrollbar-width: none;
}
.workspace-shell__commands::-webkit-scrollbar { display: none; }
.workspace-shell__results-link {
  display: inline-flex;
  align-items: center;
  height: var(--cv-density-control-height-sm);
  padding: 0 var(--cv-space-2);
  border: 1px solid transparent;
  border-radius: var(--cv-radius-sm);
  background: transparent;
  color: var(--cv-text-secondary);
  font-size: var(--cv-font-size-xs);
  font-weight: var(--cv-font-weight-medium);
  text-decoration: none;
  white-space: nowrap;
}
.workspace-shell__results-link:hover { border-color: var(--cv-control-border-hover); background: var(--cv-interactive-hover); color: var(--cv-color-link); }
.workspace-shell__results-link:focus-visible { outline: 2px solid var(--cv-focus-ring-color); outline-offset: 1px; }
.workspace-shell__divider { width: 1px; height: 18px; background: var(--cv-border-subtle); }

.workspace-shell__work-area {
  min-width: 0;
  min-height: 0;
  display: grid;
  grid-template-columns: minmax(180px, 210px) minmax(600px, 1fr) minmax(260px, 296px);
  overflow: hidden;
}
.workspace-shell__work-area > :deep(.flow-workspace) { grid-column: 1 / -1; }
.workspace-shell__work-area--state { grid-template-columns: minmax(180px, 210px) minmax(600px, 1fr) minmax(260px, 296px); }

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
  position: relative;
  gap: var(--cv-space-2);
  padding: 0 var(--cv-space-2);
  border-top: 1px solid var(--cv-border-subtle);
  color: var(--cv-text-secondary);
  font-size: var(--cv-font-size-2xs);
  white-space: nowrap;
}
.workspace-shell__project-status { max-width: 240px; overflow: hidden; color: var(--cv-text-secondary); text-overflow: ellipsis; }
.workspace-shell__status-divider { width: 1px; height: 12px; flex: 0 0 auto; background: var(--cv-border-subtle); }
.workspace-shell__statusbar :deep(.cv-status-badge) {
  min-height: 18px;
  padding: 0 6px;
  border-color: transparent;
  background: transparent;
}
.workspace-shell__status-message { min-width: 0; overflow: hidden; text-overflow: ellipsis; }
.workspace-shell__statusbar-spacer { flex: 1; }

.workspace-shell__diagnostics { position: relative; flex: 0 0 auto; }
.workspace-shell__diagnostics summary {
  padding: 2px var(--cv-space-1);
  color: var(--cv-text-muted);
  cursor: pointer;
  list-style: none;
}
.workspace-shell__diagnostics summary::-webkit-details-marker { display: none; }
.workspace-shell__diagnostics summary:hover { color: var(--cv-text-primary); }
.workspace-shell__diagnostics dl {
  position: absolute;
  z-index: var(--cv-z-dropdown);
  right: 0;
  bottom: calc(100% + var(--cv-space-2));
  width: 224px;
  margin: 0;
  padding: var(--cv-space-3);
  display: grid;
  gap: var(--cv-space-2);
  border: 1px solid var(--cv-border-subtle);
  border-radius: var(--cv-radius-md);
  background: var(--cv-surface-floating);
  box-shadow: var(--cv-elevation-2);
}
.workspace-shell__diagnostics dl div { display: flex; justify-content: space-between; gap: var(--cv-space-3); }
.workspace-shell__diagnostics dt { color: var(--cv-text-secondary); }
.workspace-shell__diagnostics dd { margin: 0; color: var(--cv-text-primary); font-variant-numeric: tabular-nums; }

@media (max-width: 1220px) {
  .workspace-shell__work-area--state { grid-template-columns: 176px minmax(520px, 1fr) 248px; }
  .workspace-shell__project small { display: none; }
}

@media (max-width: 920px) {
  .workspace-shell__work-area--state { grid-template-columns: minmax(0, 1fr); }
  .workspace-shell__work-area--state .workspace-shell__rail,
  .workspace-shell__work-area--state .workspace-shell__inspector { display: none; }
  .workspace-shell__back-nav .workspace-shell__back:first-child { display: none; }
}

@media (max-height: 650px) {
  .workspace-shell__center { grid-template-rows: minmax(280px, 1fr) 36px; }
  .workspace-shell__preview { padding-block: var(--cv-space-2); }
}

</style>
