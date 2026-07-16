import { readonly, reactive, type DeepReadonly } from 'vue';
import type { WorkspaceProjectV1 } from './workspaceContracts';
import type { ReadQueryClient } from '@/platform/query';
import {
  createFlowCanvasOwner,
  type FlowCanvasOwner
} from './flow';
import type {
  WorkspaceLifecycleDiagnosticsOwner,
  WorkspaceOwnerDiagnosticsLease
} from './workspaceLifecycleDiagnostics';

export type WorkspaceOwnerPhase = 'ready' | 'empty' | 'readonly' | 'disposed';

export interface WorkspaceOwnerProjection {
  readonly phase: WorkspaceOwnerPhase;
  readonly project: WorkspaceProjectV1;
  readonly readonlyReason: string | null;
}

type MutableWorkspaceOwnerProjection = {
  -readonly [Key in keyof WorkspaceOwnerProjection]: WorkspaceOwnerProjection[Key]
};

export interface WorkspaceOwner {
  readonly projectId: string;
  readonly projection: DeepReadonly<WorkspaceOwnerProjection>;
  openFlowCanvas(): FlowCanvasOwner;
  setReadonly(reason: string): void;
  dispose(reason?: string): void;
}

export function createWorkspaceOwner(
  project: WorkspaceProjectV1,
  diagnostics: WorkspaceLifecycleDiagnosticsOwner,
  queries: ReadQueryClient
): WorkspaceOwner {
  const lease: WorkspaceOwnerDiagnosticsLease = diagnostics.reserveWorkspaceOwner(project.id);
  const isEmpty = project.flow === null || project.flow.operators.length === 0;
  const state = reactive<MutableWorkspaceOwnerProjection>({
    phase: isEmpty ? 'empty' : 'ready',
    project,
    readonlyReason: null
  });
  let disposed = false;
  let flowOwner: FlowCanvasOwner | undefined;

  return Object.freeze({
    projectId: project.id,
    projection: readonly(state),
    openFlowCanvas(): FlowCanvasOwner {
      if (disposed) throw new Error('Workspace owner has been disposed.');
      if (flowOwner) throw new Error(`FlowCanvas owner already exists for project ${project.id}.`);
      flowOwner = createFlowCanvasOwner({
        project,
        queries,
        diagnostics,
        initialMutationGate: state.phase === 'readonly' ? 'readonly' : 'editable'
      });
      return flowOwner;
    },
    setReadonly(reason: string): void {
      if (disposed) return;
      state.phase = 'readonly';
      state.readonlyReason = reason.trim() || '后端拒绝当前读取刷新；保留已解码的只读投影。';
      flowOwner?.setMutationGate('readonly');
    },
    dispose(reason = 'workspace-owner-disposed'): void {
      if (disposed) return;
      disposed = true;
      state.phase = 'disposed';
      try {
        flowOwner?.dispose(reason);
      } finally {
        try {
          lease.dispose(reason);
        } finally {
          flowOwner = undefined;
          state.readonlyReason = null;
        }
      }
    }
  });
}
