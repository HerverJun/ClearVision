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
  prepareForLeave(reason?: string): Promise<boolean>;
  setReadonly(reason: string): void;
  dispose(reason?: string): void;
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
    persistence: null
  });
  let disposed = false;
  let flowOwner: FlowCanvasOwner | undefined;
  let persistenceOwner: WorkspacePersistenceOwner | undefined;

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
    prepareForLeave(reason = 'route-leave'): Promise<boolean> {
      void reason;
      return persistenceOwner?.prepareForLeave(reason) ?? Promise.resolve(true);
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
        persistenceOwner?.dispose(reason);
      } finally {
        try {
          flowOwner?.dispose(reason);
        } finally {
          try {
            lease.dispose(reason);
          } finally {
            persistenceOwner = undefined;
            flowOwner = undefined;
            state.persistence = null;
            state.readonlyReason = null;
          }
        }
      }
    }
  });
}
