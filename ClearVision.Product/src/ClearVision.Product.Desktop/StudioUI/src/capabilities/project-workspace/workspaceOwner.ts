import { nextTick, readonly, reactive, type DeepReadonly } from 'vue';
import {
  encodeWorkspaceFlowUpdateV1,
  encodeWorkspaceHandoffFlowV1,
  type WorkspaceJsonObject,
  type WorkspaceProjectV1
} from './workspaceContracts';
import type {
  WorkspaceHandoffArtifactV1,
  WorkspaceHandoffBuildSummaryV1,
  WorkspaceHandoffSourceV1
} from './handoff/handoffContracts';
import type { ReadQueryClient } from '@/platform/query';
import type { ApiTransport } from '@/platform/api';
import {
  createFlowCanvasOwner,
  type FlowCanvasOwner
} from './flow';
import {
  createWorkspacePersistenceOwner,
  createWorkspaceProjectPersistencePort,
  type WorkspacePersistenceOwner,
  type WorkspacePersistenceProjection,
  type WorkspaceSaveAttemptResult
} from './persistence';
import {
  createWorkspaceRunCommandOwner,
  createWorkspaceRunPort,
  type WorkspaceRunCommandOwner,
  type WorkspaceRunReconciliationV1,
  type WorkspaceRunResultV1
} from './run';
import type { WorkspaceRunIdentityV1 } from './run/runContracts';
import {
  createWorkspaceGlobalVariablesOwner,
  type WorkspaceGlobalVariablesOwner
} from './global-variables';
import {
  createFinalDecisionOwner,
  type FinalDecisionOwner
} from './final-decision';
import {
  createRuntimePackageExportOwner,
  type RuntimePackageExportOwner
} from './runtime-package';
import type {
  WorkspaceLifecycleDiagnosticsOwner,
  WorkspaceOwnerDiagnosticsLease
} from './workspaceLifecycleDiagnostics';

export type WorkspaceOwnerPhase = 'ready' | 'empty' | 'readonly' | 'disposed';

export interface WorkspaceOwnerProjection {
  readonly phase: WorkspaceOwnerPhase;
  readonly project: WorkspaceProjectV1;
  readonly readonlyReason: string | null;
  readonly persistence: DeepReadonly<WorkspacePersistenceProjection> | null;
  readonly run: DeepReadonly<WorkspaceRunCommandOwner['projection']> | null;
  readonly handoff: WorkspaceHandoffOwnerProjection | null;
}

export type WorkspaceHandoffOwnerPhase =
  | 'workspace-staging'
  | 'workspace-staged-unsaved'
  | 'workspace-save-conflict'
  | 'workspace-save-unknown-outcome'
  | 'workspace-saved';

export interface WorkspaceHandoffOwnerProjection {
  readonly phase: WorkspaceHandoffOwnerPhase;
  readonly source: WorkspaceHandoffSourceV1 | null;
  readonly build: WorkspaceHandoffBuildSummaryV1;
  readonly message: string;
}

type MutableWorkspaceOwnerProjection = {
  -readonly [Key in keyof WorkspaceOwnerProjection]: WorkspaceOwnerProjection[Key]
};

export interface WorkspaceOwner {
  readonly projectId: string;
  readonly projection: DeepReadonly<WorkspaceOwnerProjection>;
  openFlowCanvas(): FlowCanvasOwner;
  getFlowCanvasOwner(): FlowCanvasOwner | null;
  getGlobalVariablesOwner(): WorkspaceGlobalVariablesOwner | null;
  getFinalDecisionOwner(): FinalDecisionOwner | null;
  getRuntimePackageExportOwner(): RuntimePackageExportOwner | null;
  save(): Promise<WorkspaceSaveAttemptResult>;
  retrySave(): Promise<WorkspaceSaveAttemptResult>;
  reconcileSave(): Promise<WorkspaceSaveAttemptResult>;
  reapplyConflict(): void;
  discardConflict(): void;
  runFormal(): Promise<WorkspaceRunResultV1 | null>;
  stopFormal(): Promise<boolean>;
  reconcileFormalRun(): Promise<WorkspaceRunReconciliationV1 | null>;
  prepareForLeave(reason?: string): Promise<boolean>;
  quarantineForSessionExpiration(): WorkspaceSessionReconcileIdentity | null;
  reconcileAfterReauthentication(): Promise<boolean>;
  setReadonly(reason: string): void;
  stageHandoffDraft(artifact: WorkspaceHandoffArtifactV1): Promise<void>;
  adoptNewHandoffDraft(input: Readonly<{
    flow: WorkspaceJsonObject;
    source: WorkspaceHandoffSourceV1;
    build: WorkspaceHandoffBuildSummaryV1;
  }>): Promise<void>;
  confirmHandoff(source: WorkspaceHandoffSourceV1): void;
  discardHandoffDraft(): Promise<void>;
  dispose(reason?: string): void;
}

export interface WorkspaceSessionReconcileIdentity extends WorkspaceRunIdentityV1 {
  readonly operationId: string;
  readonly runId: string | null;
  readonly executionSnapshotId: string;
}

export function createWorkspaceOwner(
  project: WorkspaceProjectV1,
  diagnostics: WorkspaceLifecycleDiagnosticsOwner,
  queries: ReadQueryClient,
  api: ApiTransport | undefined,
  featureFlags: Readonly<Record<string, boolean>>
): WorkspaceOwner {
  const lease: WorkspaceOwnerDiagnosticsLease = diagnostics.reserveWorkspaceOwner(project.id);
  const isEmpty = project.flow === null || project.flow.operators.length === 0;
  const state = reactive<MutableWorkspaceOwnerProjection>({
    phase: isEmpty ? 'empty' : 'ready',
    project,
    readonlyReason: null,
    persistence: null,
    run: null,
    handoff: null
  });
  let disposed = false;
  let flowOwner: FlowCanvasOwner | undefined;
  let persistenceOwner: WorkspacePersistenceOwner | undefined;
  let runOwner: WorkspaceRunCommandOwner | undefined;
  let globalVariablesOwner: WorkspaceGlobalVariablesOwner | undefined;
  let finalDecisionOwner: FinalDecisionOwner | undefined;
  let runtimePackageExportOwner: RuntimePackageExportOwner | undefined;

  function projectHandoffSave(result: WorkspaceSaveAttemptResult): WorkspaceSaveAttemptResult {
    if (!state.handoff) return result;
    if (result.status === 'saved' || result.status === 'no-op') {
      state.handoff = Object.freeze({
        ...state.handoff,
        phase: 'workspace-saved',
        message: 'AI 候选已通过现有工程保存链正式保存。'
      });
    } else if (result.status === 'conflict') {
      state.handoff = Object.freeze({
        ...state.handoff,
        phase: 'workspace-save-conflict',
        message: '工程版本已变化；候选仍保留在本地草稿，禁止盲目覆盖。'
      });
    } else if (result.status === 'unknown-outcome') {
      state.handoff = Object.freeze({
        ...state.handoff,
        phase: 'workspace-save-unknown-outcome',
        message: '保存响应结果未知；请使用现有保存协调操作，禁止重复提交。'
      });
    }
    return result;
  }

  return Object.freeze({
    projectId: project.id,
    projection: readonly(state),
    openFlowCanvas(): FlowCanvasOwner {
      if (disposed) throw new Error('Workspace owner has been disposed.');
      if (flowOwner) throw new Error(`FlowCanvas owner already exists for project ${project.id}.`);
      if (!api) throw new Error('Workspace Flow/Preview composition requires the shared ApiTransport.');
      flowOwner = createFlowCanvasOwner({
        project,
        queries,
        api,
        featureFlags,
        diagnostics,
        initialMutationGate: state.phase === 'readonly' ? 'readonly' : 'editable'
      });
      globalVariablesOwner = createWorkspaceGlobalVariablesOwner({
        projectId: project.id,
        baseline: state.project.globalVariables,
        api
      });
      finalDecisionOwner = createFinalDecisionOwner({
        flowOwner,
        api,
        initial: state.project.flow?.decisionConfiguration ?? null
      });
      persistenceOwner = createWorkspacePersistenceOwner({
        baseline: state.project,
        flowOwner,
        globalVariablesOwner,
        port: createWorkspaceProjectPersistencePort(api, project.id),
        diagnostics,
        readonlyReason: state.phase === 'readonly' ? state.readonlyReason : null,
        onBaselineChanged(nextProject) {
          if (disposed) return;
          state.project = nextProject;
          state.phase = nextProject.flow === null || nextProject.flow.operators.length === 0
            ? 'empty'
            : 'ready';
        }
      });
      state.persistence = persistenceOwner.projection;
      runtimePackageExportOwner = createRuntimePackageExportOwner({
        projectId: project.id,
        persistenceOwner,
        api
      });
      runOwner = createWorkspaceRunCommandOwner({
        projectId: project.id,
        persistenceOwner,
        port: createWorkspaceRunPort(api, project.id),
        diagnostics
      });
      state.run = runOwner.projection;
      return flowOwner;
    },
    getFlowCanvasOwner(): FlowCanvasOwner | null {
      return flowOwner ?? null;
    },
    getGlobalVariablesOwner(): WorkspaceGlobalVariablesOwner | null {
      return globalVariablesOwner ?? null;
    },
    getFinalDecisionOwner(): FinalDecisionOwner | null {
      return finalDecisionOwner ?? null;
    },
    getRuntimePackageExportOwner(): RuntimePackageExportOwner | null {
      return runtimePackageExportOwner ?? null;
    },
    async save(): Promise<WorkspaceSaveAttemptResult> {
      if (!persistenceOwner) {
        return Object.freeze({ status: 'failed', project: null });
      }
      return projectHandoffSave(await persistenceOwner.save());
    },
    async retrySave(): Promise<WorkspaceSaveAttemptResult> {
      if (!persistenceOwner) {
        return Object.freeze({ status: 'failed', project: null });
      }
      return projectHandoffSave(await persistenceOwner.retry());
    },
    async reconcileSave(): Promise<WorkspaceSaveAttemptResult> {
      if (!persistenceOwner) {
        return Object.freeze({ status: 'failed', project: null });
      }
      return projectHandoffSave(await persistenceOwner.reconcile());
    },
    reapplyConflict(): void {
      persistenceOwner?.reapplyConflict();
    },
    discardConflict(): void {
      persistenceOwner?.discardConflict();
    },
    runFormal(): Promise<WorkspaceRunResultV1 | null> {
      return runOwner?.run() ?? Promise.resolve(null);
    },
    stopFormal(): Promise<boolean> {
      return runOwner?.stop() ?? Promise.resolve(false);
    },
    reconcileFormalRun(): Promise<WorkspaceRunReconciliationV1 | null> {
      return runOwner?.reconcile() ?? Promise.resolve(null);
    },
    async prepareForLeave(reason = 'route-leave'): Promise<boolean> {
      if (runOwner && !(await runOwner.prepareForLeave(reason))) {
        return false;
      }
      if (persistenceOwner?.projection.phase === 'unknown-outcome') {
        const reconciliation = await persistenceOwner.reconcile();
        if (reconciliation.status === 'unknown-outcome' || reconciliation.status === 'failed') {
          return false;
        }
      }
      return await persistenceOwner?.prepareForLeave(reason) ?? true;
    },
    quarantineForSessionExpiration(): WorkspaceSessionReconcileIdentity | null {
      if (disposed) return null;
      state.phase = 'readonly';
      state.readonlyReason = '会话已失效；本地 draft 与运行身份已隔离，重新认证并完成 reconcile 前禁止写入。';
      flowOwner?.setMutationGate('readonly');
      persistenceOwner?.setReadonly(state.readonlyReason);
      const identity = runOwner?.reconciliationIdentity();
      if (!identity) return null;
      return Object.freeze({
        ...identity,
        operationId: identity.clientSnapshotId,
        runId: runOwner?.projection.result?.id ?? null,
        executionSnapshotId: runOwner?.projection.result?.executionSnapshotId ?? identity.clientSnapshotId
      });
    },
    async reconcileAfterReauthentication(): Promise<boolean> {
      if (disposed) return true;
      if (persistenceOwner?.projection.phase === 'unknown-outcome') {
        const save = await persistenceOwner.reconcile();
        if (save.status === 'unknown-outcome' || save.status === 'failed') return false;
      }
      const runPhase = runOwner?.projection.phase;
      if (runPhase === 'executing' || runPhase === 'cancel-requested' || runPhase === 'unknown-outcome') {
        const reconciliation = await runOwner?.reconcile();
        return Boolean(reconciliation && (
          reconciliation.status === 'cancelled' ||
          reconciliation.status === 'succeeded' ||
          reconciliation.status === 'failed'
        ));
      }
      return runPhase !== 'admitting';
    },
    setReadonly(reason: string): void {
      if (disposed) return;
      state.phase = 'readonly';
      state.readonlyReason = reason.trim() || '后端拒绝当前读取刷新；保留已解码的只读投影。';
      flowOwner?.setMutationGate('readonly');
      persistenceOwner?.setReadonly(state.readonlyReason);
    },
    async stageHandoffDraft(artifact: WorkspaceHandoffArtifactV1): Promise<void> {
      if (disposed) throw new Error('Workspace owner has been disposed.');
      if (artifact.targetKind !== 'existing' || artifact.projectBaseline.projectId !== project.id ||
          artifact.projectBaseline.persistenceRevision !== state.project.persistenceRevision) {
        throw new Error('Artifact baseline does not match the mounted Workspace project.');
      }
      if (persistenceOwner?.projection.dirty) {
        throw new Error('Workspace has an unsaved draft and cannot stage an AI candidate.');
      }
      if (!flowOwner || flowOwner.projection.phase !== 'mounted') {
        throw new Error('Canonical FlowCanvas must be mounted before staging a handoff candidate.');
      }
      state.handoff = Object.freeze({
        phase: 'workspace-staging',
        source: null,
        build: artifact.build,
        message: '正在把 AI 候选装载到唯一 Workspace owner。'
      });
      flowOwner.replaceFlow(encodeWorkspaceHandoffFlowV1(artifact.candidateFlow), state.project.name);
      await nextTick();
    },
    async adoptNewHandoffDraft(input: Readonly<{
      flow: WorkspaceJsonObject;
      source: WorkspaceHandoffSourceV1;
      build: WorkspaceHandoffBuildSummaryV1;
    }>): Promise<void> {
      if (disposed) throw new Error('Workspace owner has been disposed.');
      if (input.source.targetKind !== 'new') {
        throw new Error('Only a new-project handoff draft can be adopted after Project creation.');
      }
      if (persistenceOwner?.projection.dirty) {
        throw new Error('The created Project Workspace is no longer at its blank baseline.');
      }
      if (!flowOwner || flowOwner.projection.phase !== 'mounted') {
        throw new Error('Canonical FlowCanvas must be mounted before adopting a new-project draft.');
      }
      state.handoff = Object.freeze({
        phase: 'workspace-staging',
        source: input.source,
        build: input.build,
        message: '正在把未落库候选接管到正式 Project Workspace。'
      });
      flowOwner.replaceFlow(input.flow, state.project.name);
      await nextTick();
      state.handoff = Object.freeze({
        phase: 'workspace-staged-unsaved',
        source: input.source,
        build: input.build,
        message: 'AI 候选已进入正式工程的本地草稿，尚未保存。'
      });
    },
    confirmHandoff(source: WorkspaceHandoffSourceV1): void {
      if (disposed || !state.handoff || state.handoff.phase !== 'workspace-staging') {
        throw new Error('Workspace has no staged handoff awaiting confirmation.');
      }
      state.handoff = Object.freeze({
        ...state.handoff,
        phase: 'workspace-staged-unsaved',
        source,
        message: 'AI 候选，尚未保存。请检查画布、参数和资源后显式保存。'
      });
    },
    async discardHandoffDraft(): Promise<void> {
      if (disposed || !state.handoff) return;
      if (!flowOwner || flowOwner.projection.phase !== 'mounted') {
        throw new Error('Canonical FlowCanvas is not available.');
      }
      if (persistenceOwner?.projection.phase === 'saving' ||
          persistenceOwner?.projection.phase === 'unknown-outcome') {
        throw new Error('保存结果尚未协调，不能放弃本地候选。');
      }
      flowOwner.replaceFlow(encodeWorkspaceFlowUpdateV1(state.project), state.project.name);
      await nextTick();
      state.handoff = null;
    },
    dispose(reason = 'workspace-owner-disposed'): void {
      if (disposed) return;
      disposed = true;
      state.phase = 'disposed';
      try {
        runOwner?.dispose(reason);
      } finally {
        try {
          persistenceOwner?.dispose(reason);
        } finally {
          try {
            runtimePackageExportOwner?.dispose();
          } finally {
            try {
              finalDecisionOwner?.dispose();
            } finally {
              try {
                globalVariablesOwner?.dispose();
              } finally {
                try {
                  flowOwner?.dispose(reason);
                } finally {
                  try {
                    lease.dispose(reason);
                  } finally {
                    runOwner = undefined;
                    persistenceOwner = undefined;
                    runtimePackageExportOwner = undefined;
                    finalDecisionOwner = undefined;
                    globalVariablesOwner = undefined;
                    flowOwner = undefined;
                    state.persistence = null;
                    state.run = null;
                    state.handoff = null;
                    state.readonlyReason = null;
                  }
                }
              }
            }
          }
        }
      }
    }
  });
}
