import { readonly, reactive, type DeepReadonly } from 'vue';
import type { WorkspaceProjectV1 } from './workspaceContracts';
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
}

type MutableWorkspaceOwnerProjection = {
  -readonly [Key in keyof WorkspaceOwnerProjection]: WorkspaceOwnerProjection[Key]
};

export interface WorkspaceOwner {
  readonly projectId: string;
  readonly projection: DeepReadonly<WorkspaceOwnerProjection>;
  openFlowCanvas(): FlowCanvasOwner;
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
    run: null
  });
  let disposed = false;
  let flowOwner: FlowCanvasOwner | undefined;
  let persistenceOwner: WorkspacePersistenceOwner | undefined;
  let runOwner: WorkspaceRunCommandOwner | undefined;

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
      persistenceOwner = createWorkspacePersistenceOwner({
        baseline: state.project,
        flowOwner,
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
      runOwner = createWorkspaceRunCommandOwner({
        projectId: project.id,
        persistenceOwner,
        port: createWorkspaceRunPort(api, project.id),
        diagnostics
      });
      state.run = runOwner.projection;
      return flowOwner;
    },
    save(): Promise<WorkspaceSaveAttemptResult> {
      if (!persistenceOwner) {
        return Promise.resolve(Object.freeze({ status: 'failed', project: null }));
      }
      return persistenceOwner.save();
    },
    retrySave(): Promise<WorkspaceSaveAttemptResult> {
      if (!persistenceOwner) {
        return Promise.resolve(Object.freeze({ status: 'failed', project: null }));
      }
      return persistenceOwner.retry();
    },
    reconcileSave(): Promise<WorkspaceSaveAttemptResult> {
      if (!persistenceOwner) {
        return Promise.resolve(Object.freeze({ status: 'failed', project: null }));
      }
      return persistenceOwner.reconcile();
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
            flowOwner?.dispose(reason);
          } finally {
            try {
              lease.dispose(reason);
            } finally {
              runOwner = undefined;
              persistenceOwner = undefined;
              flowOwner = undefined;
              state.persistence = null;
              state.run = null;
              state.readonlyReason = null;
            }
          }
        }
      }
    }
  });
}
