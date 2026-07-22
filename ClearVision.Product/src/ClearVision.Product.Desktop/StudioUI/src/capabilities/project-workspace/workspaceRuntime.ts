import type { DeepReadonly } from 'vue';
import type {
  SessionProjection,
  SessionProjectionOwner
} from '@/app/session/sessionProjectionOwner';
import type { ReadQueryClient } from '@/platform/query';
import type { ApiTransport } from '@/platform/api';
import type { WorkspaceProjectV1 } from './workspaceContracts';
import {
  createWorkspaceLifecycleDiagnosticsOwner,
  type CreateWorkspaceLifecycleDiagnosticsOptions,
  type WorkspaceLifecycleDiagnostics,
  type WorkspaceLifecycleDiagnosticsOwner
} from './workspaceLifecycleDiagnostics';
import { createWorkspaceOwner, type WorkspaceOwner } from './workspaceOwner';
import type { WorkspaceSessionReconcileIdentity } from './workspaceOwner';
import {
  createWorkspaceProjectReadPort,
  type WorkspaceProjectReadPort
} from './workspaceQueries';

export const workspaceCapabilityFlagKey = 'Studio2.Workspace';

export interface CreateWorkspaceRuntimeOptions {
  readonly queries: ReadQueryClient;
  readonly api?: ApiTransport;
  readonly session: SessionProjectionOwner;
  readonly featureFlags: Readonly<Record<string, boolean>>;
  readonly diagnostics?: WorkspaceLifecycleDiagnosticsOwner;
  readonly diagnosticsOptions?: CreateWorkspaceLifecycleDiagnosticsOptions;
}

export interface WorkspaceRuntime {
  readonly enabled: boolean;
  readonly session: DeepReadonly<SessionProjection>;
  readonly diagnostics: WorkspaceLifecycleDiagnostics;
  refreshSession(): Promise<void>;
  openProject(projectId: string): WorkspaceProjectReadPort;
  mountProject(project: WorkspaceProjectV1): WorkspaceOwner;
  getLeaveProtectionSnapshot(projectId?: string): WorkspaceLeaveProtectionSnapshot | null;
  prepareForLeave(reason: string, projectId?: string): Promise<boolean>;
  prepareForProjectTransition(projectId: string, reason: 'project-delete'): Promise<boolean>;
  prepareForProtectedTransition(reason: 'logout' | 'change-password'): Promise<boolean>;
  quarantineForSessionExpiration(): WorkspaceRuntimeQuarantine;
  reconcileAfterReauthentication(): Promise<boolean>;
  dispose(): void;
}

export interface WorkspaceLeaveProtectionSnapshot {
  readonly projectId: string;
  readonly persistencePhase: string | null;
  readonly dirty: boolean;
  readonly runPhase: string | null;
}

export interface WorkspaceRuntimeQuarantine {
  readonly activeOwnerCount: number;
  readonly runIdentities: readonly WorkspaceSessionReconcileIdentity[];
}

export function createWorkspaceRuntime(options: CreateWorkspaceRuntimeOptions): WorkspaceRuntime {
  const diagnosticsOwner = options.diagnostics ?? createWorkspaceLifecycleDiagnosticsOwner(
    options.diagnosticsOptions
  );
  const enabled = options.featureFlags[workspaceCapabilityFlagKey] === true;
  const activeReads = new Set<WorkspaceProjectReadPort>();
  const activeOwners = new Set<WorkspaceOwner>();
  let disposed = false;

  function assertActive(): void {
    if (disposed) throw new Error('WorkspaceRuntime has been disposed.');
  }

  async function prepareOwnersForLeave(reason: string, projectId?: string): Promise<boolean> {
    assertActive();
    for (const owner of [...activeOwners]) {
      if (projectId !== undefined && owner.projectId !== projectId) continue;
      if (!(await owner.prepareForLeave(reason))) return false;
    }
    return true;
  }

  return Object.freeze({
    enabled,
    session: options.session.projection,
    diagnostics: diagnosticsOwner.diagnostics,
    refreshSession(): Promise<void> {
      if (disposed) return Promise.resolve();
      return options.session.refresh();
    },
    openProject(projectId: string): WorkspaceProjectReadPort {
      assertActive();
      if (!enabled) throw new Error('Workspace capability is disabled by startup configuration.');
      const inner = createWorkspaceProjectReadPort(options.queries, diagnosticsOwner, projectId);
      let portDisposed = false;
      const port: WorkspaceProjectReadPort = Object.freeze({
        projectId: inner.projectId,
        state: inner.state,
        refresh: inner.refresh,
        dispose(reason = 'workspace-read-disposed'): void {
          if (portDisposed) return;
          portDisposed = true;
          activeReads.delete(port);
          inner.dispose(reason);
        }
      });
      activeReads.add(port);
      return port;
    },
    mountProject(project: WorkspaceProjectV1): WorkspaceOwner {
      assertActive();
      if (!enabled) throw new Error('Workspace capability is disabled by startup configuration.');
      const inner = createWorkspaceOwner(
        project,
        diagnosticsOwner,
        options.queries,
        options.api,
        options.featureFlags
      );
      let ownerDisposed = false;
      const owner: WorkspaceOwner = Object.freeze({
        projectId: inner.projectId,
        projection: inner.projection,
        openFlowCanvas: inner.openFlowCanvas,
        getFlowCanvasOwner: inner.getFlowCanvasOwner,
        getGlobalVariablesOwner: inner.getGlobalVariablesOwner,
        getFinalDecisionOwner: inner.getFinalDecisionOwner,
        save: inner.save,
        retrySave: inner.retrySave,
        reconcileSave: inner.reconcileSave,
        reapplyConflict: inner.reapplyConflict,
        discardConflict: inner.discardConflict,
        runFormal: inner.runFormal,
        stopFormal: inner.stopFormal,
        reconcileFormalRun: inner.reconcileFormalRun,
        prepareForLeave: inner.prepareForLeave,
        quarantineForSessionExpiration: inner.quarantineForSessionExpiration,
        reconcileAfterReauthentication: inner.reconcileAfterReauthentication,
        setReadonly: inner.setReadonly,
        dispose(reason = 'workspace-owner-disposed'): void {
          if (ownerDisposed) return;
          ownerDisposed = true;
          activeOwners.delete(owner);
          inner.dispose(reason);
        }
      });
      activeOwners.add(owner);
      return owner;
    },
    getLeaveProtectionSnapshot(projectId?: string): WorkspaceLeaveProtectionSnapshot | null {
      assertActive();
      const owner = [...activeOwners].find(candidate => projectId === undefined || candidate.projectId === projectId);
      if (!owner) return null;
      return Object.freeze({
        projectId: owner.projectId,
        persistencePhase: owner.projection.persistence?.phase ?? null,
        dirty: owner.projection.persistence?.dirty ?? false,
        runPhase: owner.projection.run?.phase ?? null
      });
    },
    prepareForLeave(reason: string, projectId?: string): Promise<boolean> {
      return prepareOwnersForLeave(reason, projectId);
    },
    prepareForProjectTransition(projectId: string, reason: 'project-delete'): Promise<boolean> {
      return prepareOwnersForLeave(reason, projectId);
    },
    prepareForProtectedTransition(reason: 'logout' | 'change-password'): Promise<boolean> {
      return prepareOwnersForLeave(`auth-${reason}`);
    },
    quarantineForSessionExpiration(): WorkspaceRuntimeQuarantine {
      assertActive();
      const runIdentities = [...activeOwners]
        .map(owner => owner.quarantineForSessionExpiration())
        .filter((identity): identity is WorkspaceSessionReconcileIdentity => identity !== null);
      return Object.freeze({
        activeOwnerCount: activeOwners.size,
        runIdentities: Object.freeze(runIdentities)
      });
    },
    async reconcileAfterReauthentication(): Promise<boolean> {
      assertActive();
      for (const owner of [...activeOwners]) {
        if (!(await owner.reconcileAfterReauthentication())) return false;
      }
      return true;
    },
    dispose(): void {
      if (disposed) return;
      disposed = true;
      for (const read of [...activeReads]) read.dispose('workspace-runtime-disposed');
      for (const owner of [...activeOwners]) owner.dispose('workspace-runtime-disposed');
      activeReads.clear();
      activeOwners.clear();
      diagnosticsOwner.dispose();
    }
  });
}
