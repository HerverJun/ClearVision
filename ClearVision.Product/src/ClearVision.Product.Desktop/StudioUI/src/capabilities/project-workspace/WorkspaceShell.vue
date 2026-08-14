<script setup lang="ts">
import { computed, ref, shallowRef, watch, type DeepReadonly } from 'vue';
import { RouterLink } from 'vue-router';
import {
  CvButton,
  CvField,
  CvModal,
  CvPageState,
  CvStatusBadge,
  type CvStatusTone
} from '@/design-system';
import { createLocalResultsDeepLink } from '@/shared/productionTraceLinks';
import type { WorkspaceProjectV1 } from './workspaceContracts';
import type { WorkspaceOwner } from './workspaceOwner';
import type { WorkspaceNewDraftOwner } from './workspaceNewDraftOwner';
import FlowWorkspace from './flow/FlowWorkspace.vue';
import type { WorkspaceLifecycleDiagnostics } from './workspaceLifecycleDiagnostics';
import type { WorkspaceLifecycleDiagnosticsOwner } from './workspaceLifecycleDiagnostics';
import { GlobalVariablesWorkbench, type WorkspaceGlobalVariablesOwner } from './global-variables';
import { FinalDecisionWorkbench, type FinalDecisionOwner } from './final-decision';
import type { FlowCanvasOwner } from './flow';
import { RuntimePackageExportDialog, type RuntimePackageExportOwner } from './runtime-package';
import { TemplateWorkbench, type TemplateOwner } from './templates';
import WorkspaceHandoffBanner from './handoff/WorkspaceHandoffBanner.vue';
import type { WorkspaceHandoffReceiveProjection } from './handoff/handoffContracts';
import WorkspaceCommandBar from './WorkspaceCommandBar.vue';
import RunConsole from '@/capabilities/inspection-run/RunConsole.vue';
import RunStatusBar from '@/capabilities/inspection-run/RunStatusBar.vue';
import {
  calculateRunConsoleStatistics,
  type RunConsoleAdmissionCheck,
  type RunConsoleResultItem,
  type RunConsoleViolation
} from '@/capabilities/inspection-run';

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
  newDraftOwner?: WorkspaceNewDraftOwner | null;
  message: string | null;
  diagnostics: WorkspaceLifecycleDiagnostics;
  lifecycleDiagnostics?: WorkspaceLifecycleDiagnosticsOwner | undefined;
  handoffReceive?: DeepReadonly<WorkspaceHandoffReceiveProjection> | null;
  userRole?: string | null | undefined;
}>();

const emit = defineEmits<{
  retry: [];
  refreshSession: [];
  requestSave: [];
}>();
const variablesOpen = ref(false);
const decisionOpen = ref(false);
const variablesOwner = shallowRef<WorkspaceGlobalVariablesOwner | null>(null);
const decisionOwner = shallowRef<FinalDecisionOwner | null>(null);
const modalFlowOwner = shallowRef<FlowCanvasOwner | null>(null);
const packageOwner = shallowRef<RuntimePackageExportOwner | null>(null);
const packageOpen = ref(false);
const templateOwner = shallowRef<TemplateOwner | null>(null);
const templateOpen = ref(false);
const runDetailsOpen = ref(false);

function closeCapabilityDialogs(): void {
  variablesOpen.value = false;
  decisionOpen.value = false;
  packageOpen.value = false;
  templateOpen.value = false;
  variablesOwner.value = null;
  modalFlowOwner.value = null;
  decisionOwner.value = null;
  packageOwner.value = null;
  templateOwner.value = null;
  runDetailsOpen.value = false;
}

watch(
  () => [props.projectId, props.project?.id ?? null, props.workspaceOwner] as const,
  ([projectId, projectIdentity, owner], previous) => {
    if (!previous || projectId === previous[0] && projectIdentity === previous[1] && owner === previous[2]) return;
    closeCapabilityDialogs();
  }
);

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

function openTemplates(): void {
  templateOwner.value = props.workspaceOwner?.getTemplateOwner() ?? null;
  templateOpen.value = templateOwner.value !== null;
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
const newDraft = computed(() => props.newDraftOwner?.projection ?? null);
const canvasProject = computed(() => currentProject.value ?? newDraft.value?.project ?? null);
const canvasOwner = computed(() => props.workspaceOwner ?? props.newDraftOwner ?? null);
const effectiveProjectId = computed(() => props.workspaceOwner?.projectId ?? props.projectId);
const persistence = computed(() => props.workspaceOwner?.projection.persistence ?? null);
const run = computed(() => props.workspaceOwner?.projection.run ?? null);
const handoff = computed(() => props.workspaceOwner?.projection.handoff ?? newDraft.value?.handoff ?? null);
const showHandoffReceive = computed(() => Boolean(props.handoffReceive && ![
  'idle', 'workspace-staged-unsaved', 'disposed'
].includes(props.handoffReceive.phase)));
const handoffReceiveTone = computed<CvStatusTone>(() => {
  const phase = props.handoffReceive?.phase;
  if (phase === 'workspace-loading-artifact' || phase === 'workspace-staging') return 'info';
  if (phase === 'workspace-dirty-conflict' || phase === 'artifact-baseline-conflict') return 'warning';
  return 'ng';
});
const handoffReceiveLabel = computed(() => {
  const labels: Partial<Record<WorkspaceHandoffReceiveProjection['phase'], string>> = {
    'workspace-loading-artifact': '正在读取 AI 候选',
    'workspace-dirty-conflict': '本地草稿冲突',
    'artifact-expired': '候选已过期',
    'artifact-consumed': '候选已接收',
    'artifact-baseline-conflict': '工程保存基线冲突',
    'workspace-staging': '正在装载候选',
    error: '候选接收失败'
  };
  return props.handoffReceive ? labels[props.handoffReceive.phase] ?? '候选交接' : '候选交接';
});

async function discardHandoff(): Promise<void> {
  const owner = props.workspaceOwner ?? props.newDraftOwner;
  if (!owner || !handoff.value) return;
  if (typeof window !== 'undefined' && !window.confirm('放弃 AI 候选并恢复交接前的安全状态？此操作不会修改已保存工程。')) return;
  await owner.discardHandoffDraft();
}
function updateNewDraftName(value: string): void {
  props.newDraftOwner?.setMetadata({ name: value });
}
function updateNewDraftDescription(value: string): void {
  props.newDraftOwner?.setMetadata({ description: value });
}
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
const commandBarProjectName = computed(() => currentProject.value?.name ?? newDraft.value?.project.name ?? '工程工作区');
const commandBarProjectSubtitle = computed(() => {
  if (currentProject.value) return `流程编辑 · 版本 ${currentProject.value.version}`;
  if (newDraft.value) return '新工程草稿 · 尚未创建工程';
  return null;
});
const commandBarProjectTitle = computed(() => {
  if (props.newDraftOwner) return '未落库的新工程草稿';
  const project = currentProject.value;
  return `工程 ID：${effectiveProjectId.value}${project ? `；版本：${project.version}；保存修订：${persistence.value?.persistenceRevision ?? project.persistenceRevision}` : ''}`;
});
const commandBarSaveTone = computed<CvStatusTone>(() => persistence.value?.dirty ? 'warning' : persistence.value ? 'ok' : 'warning');
const commandBarSaveLabel = computed(() => {
  if (persistence.value) return persistenceLabel.value;
  if (newDraft.value?.savePhase === 'workspace-project-creating') return '正在创建工程';
  if (newDraft.value?.savePhase === 'workspace-save-unknown-outcome') return '创建结果未知';
  return '未保存';
});
const commandBarActionLabel = computed(() => {
  if (persistence.value?.phase === 'saving' || newDraft.value?.savePhase === 'workspace-project-creating') return '保存中…';
  if (newDraft.value?.savePhase === 'workspace-save-unknown-outcome') return '核对创建结果';
  return '保存';
});
const runTone = computed(() => {
  const phase = run.value?.phase;
  if (phase === 'succeeded') return 'ok';
  if (phase === 'failed' || phase === 'unknown-outcome') return 'ng';
  if (phase === 'admitting' || phase === 'executing' || phase === 'cancel-requested' ||
    phase === 'occupied' || phase === 'reconnecting' || phase === 'disconnected') return 'warning';
  return 'idle';
});
const runLabel = computed(() => {
  const projection = run.value;
  if (!projection) return '正式运行尚未就绪';
  return {
    idle: '正式运行就绪',
    hydrating: '正在读取运行状态',
    blocked: '当前状态不可正式运行',
    admitting: '正在检查运行条件',
    executing: '正式运行中',
    occupied: '其他运行占用',
    reconnecting: '实时恢复中',
    disconnected: '实时已断开',
    succeeded: '正式运行完成',
    failed: '正式运行失败',
    cancelled: '正式运行已取消',
    'cancel-requested': '正在停止正式运行',
    'unknown-outcome': '运行结果待确认',
    disposed: '正式运行已结束'
  }[projection.phase];
});
const runPending = computed(() => Boolean(run.value && [
  'hydrating', 'admitting', 'executing', 'cancel-requested', 'reconnecting'
].includes(run.value.phase)));
const runIdentity = computed(() => {
  const projection = run.value;
  const admission = projection?.admission;
  const runtime = projection?.runtime;
  return [
    { key: 'revision', label: '保存修订', value: String(admission?.persistenceRevision ??
      runtime?.persistenceRevision ?? persistence.value?.persistenceRevision ?? '--') },
    { key: 'snapshot', label: '执行快照', value: admission?.clientSnapshotId ??
      runtime?.clientSnapshotId ?? '--' },
    { key: 'flow', label: '流程身份', value: admission?.canonicalFlowHash ??
      runtime?.canonicalFlowHash ?? '--' },
    { key: 'decision', label: '判定身份', value: admission?.decisionConfigurationHash ??
      runtime?.decisionConfigurationHash ?? '--' },
    { key: 'session', label: '会话', value: runtime?.sessionId ?? '--' }
  ];
});
const runAdmissionCodes = computed(() => run.value?.admission?.violations
  .map(item => item.code ?? '')
  .filter(Boolean) ?? []);
const runMessageByCode: Readonly<Record<string, string>> = Object.freeze({
  DECISION_FLOW_REQUIRED: '请先创建并保存流程，再配置最终判定。',
  DECISION_BINDING_REQUIRED: '请先配置最终判定，再开始正式运行。',
  DECISION_SOURCE_OPERATOR_NOT_FOUND: '最终判定引用的算子不存在，请重新选择。',
  DECISION_SOURCE_OPERATOR_DISABLED: '最终判定引用的算子已禁用，请启用或重新选择。',
  DECISION_SOURCE_OUTPUT_NOT_FOUND: '最终判定引用的输出不存在，请重新选择。',
  DECISION_SOURCE_OUTPUT_MISMATCH: '最终判定引用的输出信息不一致，请重新配置。',
  DECISION_SOURCE_TYPE_MISMATCH: '最终判定的数据类型与算子输出不匹配。',
  DECISION_SOURCE_OUTPUT_INELIGIBLE: '所选输出不能作为最终判定来源。',
  DECISION_RULE_CONTRACT_MISMATCH: '最终判定规则与算子输出约束不一致。',
  DECISION_RULE_TYPE_MISMATCH: '最终判定规则与所选数据类型不匹配。',
  DECISION_STRING_MAP_VALUES_REQUIRED: '请填写最终判定的 OK 与 NG 映射值。',
  DECISION_STRING_MAP_VALUES_CONFLICT: '最终判定的 OK 与 NG 映射值不能相同。',
  DECISION_STRING_MAP_CONSTRAINT_MISMATCH: '最终判定映射不符合算子输出约束。',
  DECISION_NUMERIC_COMPARISON_REQUIRED: '请为数值判定设置有效的比较条件和阈值。',
  ADMISSION_DECISION_IDENTITY_MISMATCH: '最终判定配置在运行检查后已变更，请重新检查运行条件。'
});
function presentRunMessage(message: string | null | undefined, code: string | null | undefined): string | null {
  const normalizedCode = code?.trim().toUpperCase();
  if (normalizedCode && runMessageByCode[normalizedCode]) return runMessageByCode[normalizedCode];
  if (normalizedCode?.includes('DECISION')) return '最终判定配置未通过，请检查最终判定设置。';
  if (normalizedCode && ['PARAMETER', 'REQUIRED'].some(segment => normalizedCode.includes(segment))) {
    return '必要参数尚未完整配置，请检查标记为必填的参数。';
  }
  if (normalizedCode && ['RESOURCE', 'ASSET', 'MISSING'].some(segment => normalizedCode.includes(segment))) {
    return '工程所需资源不完整，请补齐资源后重新检查。';
  }
  if (normalizedCode && ['DEVICE', 'CAMERA', 'SITE_PROFILE'].some(segment => normalizedCode.includes(segment))) {
    return '设备或现场配置未满足运行条件，请检查设备连接与现场配置。';
  }
  return message?.trim() || null;
}
const runHasCode = (...segments: readonly string[]): boolean => runAdmissionCodes.value.some(
  code => segments.some(segment => code.includes(segment)));
const runCheckState = (blocked: boolean): RunConsoleAdmissionCheck['state'] =>
  blocked ? 'blocked' : run.value?.admission?.allowed ? 'pass' :
    run.value?.phase === 'admitting' ? 'pending' : 'unknown';
const runAdmissionChecks = computed<readonly RunConsoleAdmissionCheck[]>(() => [
  {
    key: 'revision',
    label: '保存修订',
    state: run.value?.admission?.persistenceRevision === persistence.value?.persistenceRevision
      ? 'pass' : persistence.value?.dirty ? 'blocked' : run.value?.phase === 'admitting' ? 'pending' : 'unknown',
    detail: persistence.value?.dirty
      ? '本地参数尚未保存'
      : '保存修订 ' + (run.value?.admission?.persistenceRevision ?? persistence.value?.persistenceRevision ?? '--')
  },
  {
    key: 'flow',
    label: '流程与判定身份',
    state: run.value?.admission?.canonicalFlowHash && run.value.admission.decisionConfigurationHash
      ? 'pass' : runCheckState(runHasCode('FLOW', 'DECISION')),
    detail: run.value?.admission?.canonicalFlowHash ? '已取得正式流程与判定身份' : '等待正式流程身份'
  },
  {
    key: 'parameters',
    label: '必要参数',
    state: persistence.value?.dirty ? 'blocked' : runCheckState(runHasCode('PARAMETER', 'REQUIRED')),
    detail: persistence.value?.dirty ? '保存当前参数后重新检查' : '由运行前检查验证'
  },
  {
    key: 'resources',
    label: '工程资源',
    state: runCheckState(runHasCode('RESOURCE', 'ASSET', 'MISSING')),
    detail: runHasCode('RESOURCE', 'ASSET', 'MISSING') ? '存在缺失资源' : '由运行前检查验证'
  },
  {
    key: 'decision',
    label: '最终判定',
    state: runCheckState(runHasCode('DECISION')),
    detail: runHasCode('DECISION') ? '最终判定配置阻断运行' : '由运行前检查验证'
  },
  {
    key: 'device',
    label: '设备准入',
    state: runCheckState(runHasCode('DEVICE', 'CAMERA', 'SITE_PROFILE')),
    detail: runHasCode('DEVICE', 'CAMERA', 'SITE_PROFILE') ? '设备或现场配置阻断运行' : '由后端有效快照校验'
  },
  {
    key: 'package',
    label: '运行包',
    state: 'not-applicable',
    detail: '工作区正式运行直接使用已保存工程快照'
  }
]);
const runViolations = computed<readonly RunConsoleViolation[]>(() => (run.value?.admission?.violations ?? []).map(
  (item, index) => ({
    key: (item.code ?? 'ADMISSION') + '-' + index,
    code: item.code ?? run.value?.admission?.code ?? 'ADMISSION_REJECTED',
    message: presentRunMessage(item.reason, item.code ?? run.value?.admission?.code) ?? '运行条件未通过。',
    target: item.operatorName || item.parameterName
      ? [item.operatorName, item.parameterName].filter(Boolean).join(' · ')
      : null
  })));
const runResults = computed<readonly RunConsoleResultItem[]>(() => {
  const result = run.value?.result;
  if (!result) return [];
  return [Object.freeze({
    id: result.id,
    timestamp: null,
    outcome: result.outcome,
    defectCount: null,
    processingTimeMs: null,
    errorMessage: result.errorMessage,
    diagnostics: Object.freeze([
      { key: 'snapshot', label: '执行快照', value: result.executionSnapshotId },
      { key: 'revision', label: '保存修订', value: String(result.persistenceRevision) },
      { key: 'flow', label: '流程身份', value: result.flowHash ?? '--' },
      { key: 'decision', label: '判定身份', value: result.decisionConfigurationHash ?? '--' }
    ])
  })];
});
const runStatistics = computed(() => calculateRunConsoleStatistics(runResults.value));
const showTopStateStack = computed(() => Boolean(newDraft.value || showHandoffReceive.value || handoff.value));
const blockedAdmissionChecks = computed(() => runAdmissionChecks.value.filter(item => item.state === 'blocked'));
const runBlockerCount = computed(() => Math.max(blockedAdmissionChecks.value.length, runViolations.value.length));
const runProjectionMessage = computed(() => presentRunMessage(
  run.value?.message,
  run.value?.errorCode ?? run.value?.admission?.code
));
const runAdmissionTone = computed<CvStatusTone>(() => {
  if (runBlockerCount.value > 0) return 'ng';
  if (run.value?.admission?.allowed === true || run.value?.canRun === true) return 'ok';
  if (run.value?.phase === 'hydrating' || run.value?.phase === 'admitting') return 'warning';
  return 'idle';
});
const runAdmissionLabel = computed(() => {
  if (runBlockerCount.value > 0) return `准入阻断 ${runBlockerCount.value} 项`;
  if (run.value?.admission?.allowed === true || run.value?.canRun === true) return '准入通过';
  if (run.value?.phase === 'hydrating' || run.value?.phase === 'admitting') return '准入检查中';
  return '待检查准入';
});
const runBlockerMessage = computed(() => {
  if (persistence.value?.dirty) return '请先保存当前工程';
  const violation = runViolations.value[0]?.message;
  if (violation) return violation;
  if (run.value && ['blocked', 'failed', 'unknown-outcome'].includes(run.value.phase)) {
    return runProjectionMessage.value;
  }
  return null;
});
const runStatusMessage = computed(() => {
  if (runBlockerMessage.value) return runBlockerMessage.value;
  if (!run.value || ![
    'succeeded', 'occupied', 'reconnecting', 'disconnected', 'failed', 'cancelled',
    'cancel-requested', 'unknown-outcome'
  ].includes(run.value.phase)) return null;
  return runProjectionMessage.value;
});
function workspaceResultsLink(resultId?: string): string {
  const projectId = effectiveProjectId.value;
  return createLocalResultsDeepLink({
    projectId,
    ...(resultId ? { resultId } : {}),
    returnTo: `/projects/${encodeURIComponent(projectId)}/workspace`
  });
}
</script>

<template>
  <section
    class="workspace-shell"
    :class="{
      'workspace-shell--has-run-status': Boolean(run && currentProject),
      'workspace-shell--has-top-state': showTopStateStack
    }"
    data-capability="project-workspace"
    data-evidence-surface="f03-workspace-shell"
    :data-workspace-state="state"
    :data-workspace-project-id="effectiveProjectId"
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
    :data-workspace-persistence-phase="persistence?.phase ?? newDraft?.savePhase ?? 'unavailable'"
    :data-workspace-dirty="persistence?.dirty ?? newDraftOwner?.isDirty() ?? false"
    :data-workspace-handoff-phase="handoff?.phase ?? 'none'"
    :data-workspace-dirty-generation="persistence?.dirtyGeneration ?? 0"
    :data-workspace-persistence-revision="persistence?.persistenceRevision ?? currentProject?.persistenceRevision ?? -1"
    :data-workspace-save-compatibility="currentProject?.saveCompatibility.status ?? 'unavailable'"
  >
    <WorkspaceCommandBar
      :project-id="effectiveProjectId"
      :project-name="commandBarProjectName"
      :project-subtitle="commandBarProjectSubtitle"
      :project-title="commandBarProjectTitle"
      :show-project-details="!newDraftOwner"
      :show-save-state="Boolean(persistence || newDraft)"
      :save-state-tone="commandBarSaveTone"
      :save-state-label="commandBarSaveLabel"
      :can-open-decision="Boolean(persistence) && !isReadonly"
      :show-save="Boolean(persistence || newDraft)"
      :can-save="persistence ? persistence.canSave : Boolean(newDraft?.canSave)"
      :save-label="commandBarActionLabel"
      :can-open-variables="Boolean(persistence) && !isReadonly"
      :show-runtime-package="userRole === 'Admin'"
      :can-open-runtime-package="Boolean(persistence) && run?.phase !== 'executing'"
      :can-open-templates="Boolean(persistence && currentProject) && !isReadonly"
      :show-results="!newDraftOwner"
      :results-link="workspaceResultsLink()"
      :can-retry-save="persistence?.canRetry === true"
      :can-reconcile-save="persistence?.canReconcile === true"
      :can-reapply-conflict="persistence?.canReapplyConflict === true"
      :can-discard-conflict="persistence?.canDiscardConflict === true"
      @open-decision="openDecision"
      @request-save="emit('requestSave')"
      @open-variables="openVariables"
      @open-runtime-package="openRuntimePackage"
      @open-templates="openTemplates"
      @retry-save="workspaceOwner?.retrySave()"
      @reconcile-save="workspaceOwner?.reconcileSave()"
      @reapply-conflict="workspaceOwner?.reapplyConflict()"
      @discard-conflict="workspaceOwner?.discardConflict()"
    />

    <RunStatusBar
      v-if="run && currentProject"
      :phase-label="runLabel"
      :tone="runTone"
      :message="runStatusMessage"
      :connected="run.connected === true"
      :reconnect-attempt="run.reconnectAttempt ?? 0"
      :pending="runPending"
      :can-start="run.canRun"
      :can-stop="run.canStop"
      :can-reconcile="run.canReconcile"
      :admission-label="runAdmissionLabel"
      :admission-tone="runAdmissionTone"
      :blocker-count="runBlockerCount"
      :blocker-message="runBlockerMessage"
      start-test-id="workspace-run"
      stop-test-id="workspace-run-stop"
      reconcile-test-id="workspace-run-reconcile"
      @check-admission="workspaceOwner?.refreshFormalAdmission()"
      @start="workspaceOwner?.runFormal()"
      @stop="workspaceOwner?.stopFormal()"
      @reconcile="workspaceOwner?.reconcileFormalRun()"
      @details="runDetailsOpen = true"
    >
      <template #result-action>
        <RouterLink
          v-if="runResults.length > 0"
          :to="workspaceResultsLink(runResults[0]?.id)"
          data-testid="workspace-current-result"
        >
          查看本次结果
        </RouterLink>
      </template>
    </RunStatusBar>

    <CvModal
      v-if="run && currentProject"
      :open="runDetailsOpen"
      title="运行详情"
      description="身份、准入项、违规详情与近期结果"
      close-label="关闭运行详情"
      size="lg"
      @close="runDetailsOpen = false"
    >
      <div
        class="workspace-shell__run-details"
        data-testid="workspace-run-details-panel"
      >
        <RunConsole
          mode="formal"
          :project-name="currentProject.name"
          :phase-label="runLabel"
          :tone="runTone"
          :message="runProjectionMessage ?? ''"
          :error-code="run.errorCode"
          :connected="run.connected === true"
          :reconnect-attempt="run.reconnectAttempt ?? 0"
          :pending="runPending"
          :can-start="run.canRun"
          :can-stop="run.canStop"
          :can-reconcile="run.canReconcile"
          :identity="runIdentity"
          :admission="runAdmissionChecks"
          :violations="runViolations"
          :statistics="runStatistics"
          :results="runResults"
          start-test-id="workspace-run"
          stop-test-id="workspace-run-stop"
          reconcile-test-id="workspace-run-reconcile"
          @start="workspaceOwner?.runFormal()"
          @stop="workspaceOwner?.stopFormal()"
          @reconcile="workspaceOwner?.reconcileFormalRun()"
          @refresh-admission="workspaceOwner?.refreshFormalAdmission()"
        >
          <template #result-action="{ result }">
            <RouterLink
              :to="workspaceResultsLink(result.id)"
              data-testid="workspace-current-result-detail"
            >
              查看本次结果
            </RouterLink>
          </template>
        </RunConsole>
      </div>
    </CvModal>

    <div
      v-show="showTopStateStack"
      class="workspace-shell__top-state-stack"
      data-testid="workspace-top-state-stack"
    >
      <section
        v-if="newDraft"
        class="workspace-shell__new-project"
        data-testid="workspace-new-project-metadata"
      >
        <div>
          <strong>新工程信息</strong>
          <span>{{ newDraft.message }}</span>
        </div>
        <CvField
          name="newProjectName"
          label="工程名称"
          required
          :model-value="newDraft.project.name"
          :disabled="newDraft.metadataLocked"
          @update:model-value="updateNewDraftName"
        />
        <CvField
          name="newProjectDescription"
          label="工程描述"
          :model-value="newDraft.project.description ?? ''"
          :disabled="newDraft.metadataLocked"
          @update:model-value="updateNewDraftDescription"
        />
      </section>

      <section
        v-if="showHandoffReceive && handoffReceive"
        class="workspace-shell__handoff-receive"
        :data-handoff-receive-phase="handoffReceive.phase"
        role="status"
      >
        <CvStatusBadge
          :tone="handoffReceiveTone"
          :label="handoffReceiveLabel"
        />
        <div>
          <strong>{{ handoffReceive.message }}</strong>
          <small v-if="handoffReceive.blocker">
            技术信息：<code>{{ handoffReceive.blocker }}</code>
          </small>
          <span>{{ handoffReceive.nextStep }}</span>
        </div>
      </section>

      <WorkspaceHandoffBanner
        v-if="handoff"
        :handoff="handoff"
        @discard="discardHandoff"
      />
    </div>

    <div
      v-if="isReadySurface && canvasProject && canvasOwner"
      class="workspace-shell__work-area"
    >
      <FlowWorkspace
        :key="effectiveProjectId"
        :workspace-owner="canvasOwner"
        :project="canvasProject"
        :lifecycle-diagnostics="lifecycleDiagnostics"
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
    <TemplateWorkbench
      v-if="templateOwner && currentProject"
      :open="templateOpen"
      :owner="templateOwner"
      :dirty="persistence?.dirty ?? false"
      :readonly="isReadonly"
      @close="templateOpen = false"
    />
  </section>
</template>

<style scoped>
.workspace-shell {
  --cv-font-size-2xs: 12px;
  width: 100%;
  height: 100%;
  min-width: 0;
  min-height: 0;
  display: grid;
  grid-template-rows: auto minmax(0, 1fr) var(--cv-workspace-status-height, 24px);
  overflow: hidden;
  background: var(--cv-surface-page);
}
.workspace-shell--has-run-status,
.workspace-shell--has-top-state {
  grid-template-rows: auto auto minmax(0, 1fr) var(--cv-workspace-status-height, 24px);
}
.workspace-shell--has-run-status.workspace-shell--has-top-state {
  grid-template-rows: auto auto auto minmax(0, 1fr) var(--cv-workspace-status-height, 24px);
}

.workspace-shell__statusbar {
  display: flex;
  align-items: center;
  border-color: var(--cv-border-subtle);
  background: var(--cv-surface-page);
}

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
.workspace-shell__top-state-stack { min-width: 0; min-height: 0; display: grid; }
.workspace-shell__run-details {
  min-width: 0;
  min-height: 0;
  display: block;
}
.workspace-shell__run-details :deep(.run-console) { min-width: 0; }
.workspace-shell__handoff-receive { display: flex; min-width: 0; align-items: center; gap: var(--cv-space-4); padding: var(--cv-space-3) var(--cv-density-page-padding); border-block-end: 1px solid var(--cv-border-subtle); background: var(--cv-surface-page); }
.workspace-shell__new-project { display: grid; grid-template-columns: minmax(280px, 1fr) minmax(220px, 320px) minmax(240px, 380px); gap: var(--cv-space-4); align-items: end; padding: var(--cv-space-3) var(--cv-density-page-padding); border-block-end: 1px solid var(--cv-border-subtle); background: var(--cv-surface-raised); }
.workspace-shell__new-project > div { display: grid; gap: 2px; min-width: 0; }
.workspace-shell__new-project span { color: var(--cv-text-secondary); font-size: var(--cv-font-size-xs); }
.workspace-shell__handoff-receive > div { display: grid; min-width: 0; gap: 2px; }
.workspace-shell__handoff-receive strong { color: var(--cv-text-primary); font-size: var(--cv-font-size-xs); }
.workspace-shell__handoff-receive span { color: var(--cv-text-secondary); font-size: var(--cv-font-size-2xs); line-height: var(--cv-line-height-normal); }
.workspace-shell__handoff-receive small { color: var(--cv-text-muted); font-size: var(--cv-font-size-2xs); line-height: var(--cv-line-height-normal); }
.workspace-shell__handoff-receive code { font-family: var(--cv-font-family-mono); font-size: inherit; }
@media (max-width: 980px) { .workspace-shell__new-project { grid-template-columns: 1fr 1fr; } .workspace-shell__new-project > div { grid-column: 1 / -1; } }
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
  min-height: 20px;
  padding: 2px var(--cv-space-1);
  display: inline-flex;
  align-items: center;
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

.workspace-shell[data-workspace-persistence-phase="conflict"] .workspace-shell__statusbar,
.workspace-shell[data-workspace-persistence-phase="error"] .workspace-shell__statusbar,
.workspace-shell[data-workspace-persistence-phase="unknown-outcome"] .workspace-shell__statusbar {
  border-top-color: var(--cv-color-status-ng-border);
  background: color-mix(in srgb, var(--cv-color-status-ng-soft) 32%, var(--cv-surface-page));
}

.workspace-shell[data-workspace-run-phase="executing"] .workspace-shell__statusbar,
.workspace-shell[data-workspace-run-phase="reconnecting"] .workspace-shell__statusbar,
.workspace-shell[data-workspace-run-phase="disconnected"] .workspace-shell__statusbar {
  border-top-color: var(--cv-color-status-warning-border);
}

@media (max-width: 1220px) {
  .workspace-shell__work-area--state { grid-template-columns: 176px minmax(520px, 1fr) 248px; }
}

@media (max-width: 1040px) {
  .workspace-shell__work-area--state { grid-template-columns: minmax(0, 1fr); }
  .workspace-shell__work-area--state .workspace-shell__rail,
  .workspace-shell__work-area--state .workspace-shell__inspector { display: none; }
}

@media (max-height: 650px) {
  .workspace-shell__center { grid-template-rows: minmax(280px, 1fr) 36px; }
  .workspace-shell__preview { padding-block: var(--cv-space-2); }
}

</style>
