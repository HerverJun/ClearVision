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
  createWorkspaceNewDraftOwner,
  type WorkspaceNewDraftOwner
} from './workspaceNewDraftOwner';
import {
  createWorkspaceProjectReadPort,
  type WorkspaceProjectReadPort
} from './workspaceQueries';
import {
  createWorkspaceHandoffReceivePort,
  type WorkspaceHandoffReceivePort
} from './handoff/handoffReceivePort';

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
  readonly lifecycleDiagnostics?: WorkspaceLifecycleDiagnosticsOwner;
  refreshSession(): Promise<void>;
  openProject(projectId: string): WorkspaceProjectReadPort;
  mountProject(project: WorkspaceProjectV1): WorkspaceOwner;
  mountNewHandoffDraft(artifactId: string): WorkspaceNewDraftOwner;
  openHandoffReceiver(): WorkspaceHandoffReceivePort;
  getLeaveProtectionSnapshot(projectId?: string): WorkspaceLeaveProtectionSnapshot | null;
  prepareForLeave(reason: string, projectId?: string): Promise<boolean>;
  prepareForProjectTransition(projectId: string, reason: 'project-delete'): Promise<boolean>;
  prepareForProtectedTransition(reason: 'logout' | 'change-password'): Promise<boolean>;
  quarantineForSessionExpiration(): WorkspaceRuntimeQuarantine;
  reconcileAfterReauthentication(): Promise<boolean>;
  dispose(): void;
}

export interface WorkspaceLeaveProtectionSnapshot {
  readonly projectId: string | null;
  readonly persistencePhase: string | null;
  readonly dirty: boolean;
  readonly runPhase: string | null;
  readonly childPending?: boolean;
  readonly childUnknown?: boolean;
}

export interface WorkspaceRuntimeQuarantine {
  readonly activeOwnerCount: number;
  readonly activeNewDraftOwnerCount: number;
  readonly activeHandoffReceiverCount: number;
  readonly runIdentities: readonly WorkspaceSessionReconcileIdentity[];
}

export function createWorkspaceRuntime(options: CreateWorkspaceRuntimeOptions): WorkspaceRuntime {
  const diagnosticsOwner = options.diagnostics ?? createWorkspaceLifecycleDiagnosticsOwner(
    options.diagnosticsOptions
  );
  const enabled = options.featureFlags[workspaceCapabilityFlagKey] === true;
  const activeReads = new Set<WorkspaceProjectReadPort>();
  const activeOwners = new Set<WorkspaceOwner>();
  const activeNewDraftOwners = new Set<WorkspaceNewDraftOwner>();
  const activeHandoffReceivers = new Set<WorkspaceHandoffReceivePort>();
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
    for (const owner of [...activeNewDraftOwners]) {
      if (projectId !== undefined && projectId !== 'new') continue;
      if (!(await owner.prepareForLeave())) return false;
    }
    for (const receiver of [...activeHandoffReceivers]) {
      if (!(await receiver.prepareForLeave())) return false;
    }
    return true;
  }

  return Object.freeze({
    enabled,
    session: options.session.projection,
    diagnostics: diagnosticsOwner.diagnostics,
    lifecycleDiagnostics: diagnosticsOwner,
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
        options.featureFlags,
        options.session.projection.user?.role === 'Admin' || options.session.projection.user?.role === 'Engineer'
      );
      let ownerDisposed = false;
      const owner: WorkspaceOwner = Object.freeze({
        projectId: inner.projectId,
        projection: inner.projection,
        openFlowCanvas: inner.openFlowCanvas,
        getFlowCanvasOwner: inner.getFlowCanvasOwner,
        getGlobalVariablesOwner: inner.getGlobalVariablesOwner,
        getFinalDecisionOwner: inner.getFinalDecisionOwner,
        getRuntimePackageExportOwner: inner.getRuntimePackageExportOwner,
        getTemplateOwner: inner.getTemplateOwner,
        save: inner.save,
        retrySave: inner.retrySave,
        reconcileSave: inner.reconcileSave,
        reconcileExternalProject: inner.reconcileExternalProject,
        hasPendingChildOperation: inner.hasPendingChildOperation,
        hasUnknownChildOperation: inner.hasUnknownChildOperation,
        reapplyConflict: inner.reapplyConflict,
        discardConflict: inner.discardConflict,
        hydrateFormalRun: inner.hydrateFormalRun,
        refreshFormalAdmission: inner.refreshFormalAdmission,
        runFormal: inner.runFormal,
        stopFormal: inner.stopFormal,
        reconcileFormalRun: inner.reconcileFormalRun,
        prepareForLeave: inner.prepareForLeave,
        quarantineForSessionExpiration: inner.quarantineForSessionExpiration,
        reconcileAfterReauthentication: inner.reconcileAfterReauthentication,
        setReadonly: inner.setReadonly,
        stageHandoffDraft: inner.stageHandoffDraft,
        adoptNewHandoffDraft: inner.adoptNewHandoffDraft,
        confirmHandoff: inner.confirmHandoff,
        discardHandoffDraft: inner.discardHandoffDraft,
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
    mountNewHandoffDraft(artifactId: string): WorkspaceNewDraftOwner {
      assertActive();
      if (!enabled || !options.api) throw new Error('Workspace handoff requires the shared ApiTransport.');
      const inner = createWorkspaceNewDraftOwner({
        artifactId,
        diagnostics: diagnosticsOwner,
        queries: options.queries,
        api: options.api,
        featureFlags: options.featureFlags
      });
      let ownerDisposed = false;
      const owner: WorkspaceNewDraftOwner = Object.freeze({
        projectId: null,
        projection: inner.projection,
        openFlowCanvas: inner.openFlowCanvas,
        getFlowCanvasOwner: inner.getFlowCanvasOwner,
        isDirty: inner.isDirty,
        setMetadata: inner.setMetadata,
        stageHandoffDraft: inner.stageHandoffDraft,
        confirmHandoff: inner.confirmHandoff,
        createSaveIntent: inner.createSaveIntent,
        markProjectCreating: inner.markProjectCreating,
        markSaveUnknown: inner.markSaveUnknown,
        markSaveFailed: inner.markSaveFailed,
        discardHandoffDraft: inner.discardHandoffDraft,
        setReadonly: inner.setReadonly,
        clearReadonly: inner.clearReadonly,
        prepareForLeave: inner.prepareForLeave,
        dispose(reason = 'workspace-new-draft-disposed'): void {
          if (ownerDisposed) return;
          ownerDisposed = true;
          activeNewDraftOwners.delete(owner);
          inner.dispose(reason);
        }
      });
      activeNewDraftOwners.add(owner);
      return owner;
    },
    openHandoffReceiver(): WorkspaceHandoffReceivePort {
      assertActive();
      if (!enabled || !options.api) throw new Error('Workspace handoff requires the shared ApiTransport.');
      const inner = createWorkspaceHandoffReceivePort({
        api: options.api,
        diagnostics: diagnosticsOwner
      });
      let receiverDisposed = false;
      const receiver: WorkspaceHandoffReceivePort = Object.freeze({
        projection: inner.projection,
        hasPendingOperation: inner.hasPendingOperation,
        hasUnknownOutcome: inner.hasUnknownOutcome,
        quarantineForSessionExpiration: inner.quarantineForSessionExpiration,
        reconcileAfterReauthentication: inner.reconcileAfterReauthentication,
        prepareForLeave: inner.prepareForLeave,
        settle: inner.settle,
        receive: inner.receive,
        reject: inner.reject,
        dispose(reason = 'workspace-handoff-receiver-disposed'): void {
          if (receiverDisposed) return;
          receiverDisposed = true;
          activeHandoffReceivers.delete(receiver);
          inner.dispose(reason);
        }
      });
      activeHandoffReceivers.add(receiver);
      return receiver;
    },
    getLeaveProtectionSnapshot(projectId?: string): WorkspaceLeaveProtectionSnapshot | null {
      assertActive();
      const owner = [...activeOwners].find(candidate => projectId === undefined || candidate.projectId === projectId);
      const receiverPending = [...activeHandoffReceivers].some(receiver => receiver.hasPendingOperation());
      const receiverUnknown = [...activeHandoffReceivers].some(receiver => receiver.hasUnknownOutcome());
      if (owner) {
        return Object.freeze({
          projectId: owner.projectId,
          persistencePhase: owner.projection.persistence?.phase ?? null,
          dirty: owner.projection.persistence?.dirty ?? false,
          runPhase: owner.projection.run?.phase ?? null,
          childPending: (typeof owner.hasPendingChildOperation === 'function' && owner.hasPendingChildOperation()) || receiverPending,
          childUnknown: (typeof owner.hasUnknownChildOperation === 'function' && owner.hasUnknownChildOperation()) || receiverUnknown
        });
      }
      const newDraft = [...activeNewDraftOwners].find(() => projectId === undefined || projectId === 'new');
      if (newDraft) {
        const flowOwner = newDraft.getFlowCanvasOwner();
        return Object.freeze({
          projectId: null,
          persistencePhase: newDraft.projection.savePhase,
          dirty: newDraft.isDirty(),
          runPhase: null,
          childPending: newDraft.projection.savePhase === 'workspace-project-creating' ||
            flowOwner?.hasPendingLifecycleOperation() === true || receiverPending,
          childUnknown: newDraft.projection.savePhase === 'workspace-save-unknown-outcome' ||
            flowOwner?.hasUnknownLifecycleOutcome() === true || receiverUnknown
        });
      }
      if (activeHandoffReceivers.size === 0) return null;
      return Object.freeze({
        projectId: null,
        persistencePhase: null,
        dirty: false,
        runPhase: null,
        childPending: receiverPending,
        childUnknown: receiverUnknown
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
      for (const read of [...activeReads]) read.dispose('session-expired');
      for (const owner of [...activeNewDraftOwners]) {
        owner.setReadonly('会话已失效；新工程草稿与本地候选已隔离，重新认证前禁止写入。');
      }
      for (const receiver of [...activeHandoffReceivers]) {
        receiver.quarantineForSessionExpiration();
      }
      const runIdentities = [...activeOwners]
        .map(owner => owner.quarantineForSessionExpiration())
        .filter((identity): identity is WorkspaceSessionReconcileIdentity => identity !== null);
      return Object.freeze({
        activeOwnerCount: activeOwners.size,
        activeNewDraftOwnerCount: activeNewDraftOwners.size,
        activeHandoffReceiverCount: activeHandoffReceivers.size,
        runIdentities: Object.freeze(runIdentities)
      });
    },
    async reconcileAfterReauthentication(): Promise<boolean> {
      assertActive();
      for (const owner of [...activeOwners]) {
        if (!(await owner.reconcileAfterReauthentication())) return false;
      }
      for (const owner of [...activeNewDraftOwners]) {
        if (!owner.clearReadonly()) return false;
      }
      for (const receiver of [...activeHandoffReceivers]) {
        if (!receiver.reconcileAfterReauthentication()) return false;
      }
      return true;
    },
    dispose(): void {
      if (disposed) return;
      disposed = true;
      for (const read of [...activeReads]) read.dispose('workspace-runtime-disposed');
      for (const receiver of [...activeHandoffReceivers]) receiver.dispose('workspace-runtime-disposed');
      for (const owner of [...activeOwners]) owner.dispose('workspace-runtime-disposed');
      for (const owner of [...activeNewDraftOwners]) owner.dispose('workspace-runtime-disposed');
      activeReads.clear();
      activeOwners.clear();
      activeNewDraftOwners.clear();
      activeHandoffReceivers.clear();
      diagnosticsOwner.dispose();
    }
  });
}
