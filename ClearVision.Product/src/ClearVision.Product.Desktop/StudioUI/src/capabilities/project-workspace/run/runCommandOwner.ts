import { reactive, readonly, type DeepReadonly } from 'vue';
import { ApiAbortError, ApiHttpError, ApiNetworkError } from '@/platform/api';
import type { WorkspacePersistenceOwner } from '../persistence';
import type {
  WorkspaceCapabilityDiagnosticsLease,
  WorkspaceLifecycleDiagnosticsOwner,
  WorkspaceResourceSnapshot
} from '../workspaceLifecycleDiagnostics';
import {
  type WorkspaceRunAdmissionV1,
  type WorkspaceRunIdentityV1,
  type WorkspaceRunPort,
  type WorkspaceRunReconciliationV1,
  type WorkspaceRunResultV1
} from './runContracts';

export type WorkspaceRunPhase =
  | 'idle'
  | 'blocked'
  | 'admitting'
  | 'executing'
  | 'succeeded'
  | 'failed'
  | 'cancelled'
  | 'cancel-requested'
  | 'unknown-outcome'
  | 'disposed';

export interface WorkspaceRunProjection {
  readonly phase: WorkspaceRunPhase;
  readonly projectId: string;
  readonly clientSnapshotId: string | null;
  readonly admission: WorkspaceRunAdmissionV1 | null;
  readonly result: WorkspaceRunResultV1 | null;
  readonly message: string;
  readonly errorCode: string | null;
  readonly canRun: boolean;
  readonly canStop: boolean;
  readonly canReconcile: boolean;
}

type MutableWorkspaceRunProjection = {
  -readonly [Key in keyof WorkspaceRunProjection]: WorkspaceRunProjection[Key]
};

export interface WorkspaceRunCommandOwner {
  readonly projectId: string;
  readonly projection: DeepReadonly<WorkspaceRunProjection>;
  run(): Promise<WorkspaceRunResultV1 | null>;
  stop(): Promise<boolean>;
  reconcile(): Promise<WorkspaceRunReconciliationV1 | null>;
  reconciliationIdentity(): WorkspaceRunIdentityV1 | null;
  prepareForLeave(reason?: string): Promise<boolean>;
  settle(): Promise<void>;
  dispose(reason?: string): void;
}

function zeroResources(): WorkspaceResourceSnapshot {
  return Object.freeze({
    activeSubscriptions: 0,
    activeTimers: 0,
    activeAnimationFrames: 0,
    activeObservers: 0,
    activeAbortControllers: 0,
    activeBlobUrls: 0,
    activePreviewArtifactIds: 0,
    activeHostSubscriptions: 0,
    inFlightReads: 0,
    inFlightWrites: 0,
    inFlightPreview: 0,
    inFlightExecute: 0
  });
}

function errorCode(error: unknown): string | null {
  if (!(error instanceof ApiHttpError) || typeof error.payload !== 'object' || error.payload === null) return null;
  const payload = error.payload as Record<string, unknown>;
  const code = payload.code ?? payload.Code;
  return typeof code === 'string' && code.trim() ? code.trim().toUpperCase() : null;
}

function messageFor(error: unknown): string {
  return error instanceof Error && error.message.trim()
    ? error.message
    : 'Formal Run request failed.';
}

function terminalMessage(result: WorkspaceRunResultV1): string {
  if (result.outcome.execution === 'Cancelled') return 'Formal Run was cancelled by the runtime.';
  if (result.outcome.execution !== 'Succeeded') return result.errorMessage || 'Formal Run failed.';
  switch (result.outcome.decision) {
    case 'Ok': return 'Formal Run completed: OK.';
    case 'Ng': return 'Formal Run completed: NG.';
    case 'Undetermined': return 'Formal Run completed: decision undetermined.';
    case 'Invalid': return 'Formal Run completed: decision invalid.';
    default: return 'Formal Run completed.';
  }
}

export function createWorkspaceRunCommandOwner(options: {
  readonly projectId: string;
  readonly persistenceOwner: WorkspacePersistenceOwner;
  readonly port: WorkspaceRunPort;
  readonly diagnostics: WorkspaceLifecycleDiagnosticsOwner;
}): WorkspaceRunCommandOwner {
  if (options.port.projectId !== options.projectId || options.persistenceOwner.projectId !== options.projectId) {
    throw new TypeError('Workspace Run owner requires one Project identity.');
  }
  const lease: WorkspaceCapabilityDiagnosticsLease = options.diagnostics.reserveRun(options.projectId);
  const state = reactive<MutableWorkspaceRunProjection>({
    phase: 'idle',
    projectId: options.projectId,
    clientSnapshotId: null,
    admission: null,
    result: null,
    message: 'Formal Run is ready when the persisted Project is clean.',
    errorCode: null,
    canRun: false,
    canStop: false,
    canReconcile: false
  });
  let disposed = false;
  let operationGeneration = 0;
  let activeController: AbortController | undefined;
  let authoritySettledLocalAbort: AbortController | undefined;
  let stopController: AbortController | undefined;
  let reconcileController: AbortController | undefined;
  let runPromise: Promise<WorkspaceRunResultV1 | null> | undefined;
  let stopPromise: Promise<boolean> | undefined;
  let reconcilePromise: Promise<WorkspaceRunReconciliationV1 | null> | undefined;
  const pending = new Set<Promise<unknown>>();

  function isCurrent(generation: number, clientSnapshotId: string): boolean {
    return !disposed && generation === operationGeneration && state.projectId === options.projectId &&
      options.persistenceOwner.projectId === options.projectId && state.clientSnapshotId === clientSnapshotId;
  }

  function syncDiagnostics(inFlight = false): void {
    if (disposed) return;
    const activeAbortControllers = [activeController, stopController, reconcileController]
      .filter(controller => controller !== undefined).length;
    lease.update(Object.freeze({
      ...zeroResources(),
      activeSubscriptions: 1,
      activeAbortControllers,
      inFlightExecute: inFlight ? 1 : 0
    }));
  }

  function syncAvailability(): void {
    if (disposed) return;
    state.canRun = !runPromise && options.persistenceOwner.projection.canRun &&
      (state.phase === 'idle' || state.phase === 'blocked' || state.phase === 'succeeded' ||
        state.phase === 'failed' || state.phase === 'cancelled');
    state.canStop = Boolean(runPromise && activeController && state.phase === 'executing');
    state.canReconcile = Boolean(!stopPromise && !reconcilePromise && state.clientSnapshotId && state.admission &&
      (state.phase === 'executing' || state.phase === 'cancel-requested' || state.phase === 'unknown-outcome'));
  }

  function track<T>(promise: Promise<T>): Promise<T> {
    pending.add(promise);
    promise.finally(() => pending.delete(promise)).catch(() => {});
    return promise;
  }

  function currentIdentity(): WorkspaceRunIdentityV1 | null {
    const admission = state.admission;
    if (!state.clientSnapshotId || !admission || admission.persistenceRevision == null ||
      !admission.canonicalFlowHash || !admission.decisionConfigurationHash) {
      return null;
    }
    return {
      projectId: options.projectId,
      clientSnapshotId: state.clientSnapshotId,
      expectedPersistenceRevision: admission.persistenceRevision,
      expectedCanonicalFlowHash: admission.canonicalFlowHash,
      expectedDecisionConfigurationHash: admission.decisionConfigurationHash
    };
  }

  function reconciliationMatchesIdentity(reconciliation: WorkspaceRunReconciliationV1, identity: WorkspaceRunIdentityV1): boolean {
    return reconciliation.projectId === identity.projectId &&
      reconciliation.clientSnapshotId === identity.clientSnapshotId &&
      reconciliation.persistenceRevision === identity.expectedPersistenceRevision &&
      reconciliation.canonicalFlowHash === identity.expectedCanonicalFlowHash &&
      reconciliation.decisionConfigurationHash === identity.expectedDecisionConfigurationHash;
  }

  function resultMatchesIdentity(result: WorkspaceRunResultV1, identity: WorkspaceRunIdentityV1): boolean {
    return result.projectId === identity.projectId &&
      result.executionSnapshotId === identity.clientSnapshotId &&
      result.persistenceRevision === identity.expectedPersistenceRevision &&
      result.flowHash === identity.expectedCanonicalFlowHash &&
      result.decisionConfigurationHash === identity.expectedDecisionConfigurationHash;
  }

  function cancelAdmission(reason: string): boolean {
    if (disposed || state.phase !== 'admitting' || !activeController) return false;
    const controller = activeController;
    operationGeneration += 1;
    activeController = undefined;
    authoritySettledLocalAbort = undefined;
    runPromise = undefined;
    state.clientSnapshotId = null;
    state.admission = null;
    state.result = null;
    state.errorCode = 'RUN_ADMISSION_CANCELLED';
    state.message = `Formal Run admission was cancelled locally (${reason}); no runtime session was created.`;
    controller.abort();
    options.persistenceOwner.clearRunning(state.message);
    state.phase = options.persistenceOwner.projection.canRun ? 'idle' : 'blocked';
    syncDiagnostics(false);
    syncAvailability();
    return true;
  }

  function applyReconciliation(
    reconciliation: WorkspaceRunReconciliationV1,
    identity: WorkspaceRunIdentityV1,
    generation: number
  ): void {
    if (!isCurrent(generation, identity.clientSnapshotId)) return;
    if (!reconciliationMatchesIdentity(reconciliation, identity)) {
      state.phase = 'unknown-outcome';
      state.errorCode = 'RUN_RECONCILE_IDENTITY_MISMATCH';
      state.message = 'The reconcile response did not match the formal run identity. Workspace remains locked.';
      syncAvailability();
      return;
    }
    if (reconciliation.result && !resultMatchesIdentity(reconciliation.result, identity)) {
      state.phase = 'unknown-outcome';
      state.errorCode = 'RUN_RECONCILE_RESULT_IDENTITY_MISMATCH';
      state.message = 'The reconciled result did not match the formal run identity. Workspace remains locked.';
      syncAvailability();
      return;
    }

    switch (reconciliation.status) {
      case 'still-running':
        state.phase = state.phase === 'cancel-requested' ? 'cancel-requested' : 'executing';
        state.errorCode = reconciliation.code;
        state.message = reconciliation.message;
        break;
      case 'cancel-requested':
        state.phase = 'cancel-requested';
        state.errorCode = reconciliation.code;
        state.message = reconciliation.message;
        break;
      case 'cancelled':
        state.result = reconciliation.result;
        state.phase = 'cancelled';
        state.errorCode = reconciliation.code;
        state.message = reconciliation.message;
        options.persistenceOwner.clearRunning(state.message);
        break;
      case 'succeeded':
        if (!reconciliation.result) {
          state.phase = 'unknown-outcome';
          state.errorCode = 'RUN_RECONCILE_RESULT_MISSING';
          state.message = 'The authoritative run succeeded but no formal result was returned. Workspace remains locked.';
          break;
        }
        state.result = reconciliation.result;
        state.phase = 'succeeded';
        state.errorCode = reconciliation.code;
        state.message = terminalMessage(reconciliation.result);
        options.persistenceOwner.clearRunning(state.message);
        break;
      case 'failed':
        state.result = reconciliation.result;
        state.phase = 'failed';
        state.errorCode = reconciliation.code;
        state.message = reconciliation.message;
        options.persistenceOwner.clearRunning(state.message);
        break;
      case 'result-not-found':
      case 'identity-mismatch':
        state.phase = 'unknown-outcome';
        state.errorCode = reconciliation.code ?? `RUN_${reconciliation.status.toUpperCase().replace('-', '_')}`;
        state.message = `${reconciliation.message} Workspace remains locked.`;
        break;
    }
    syncAvailability();
  }

  async function performRun(): Promise<WorkspaceRunResultV1 | null> {
    if (disposed) return null;
    if (!options.persistenceOwner.projection.canRun) {
      state.phase = 'blocked';
      state.errorCode = 'RUN_PERSISTENCE_GATE';
      state.message = 'Save, reconcile, or resolve the current Project state before Formal Run.';
      syncAvailability();
      return null;
    }
    if (!options.persistenceOwner.setRunning('Formal Run admission is in progress.')) {
      state.phase = 'blocked';
      state.errorCode = 'RUN_MUTATION_GATE';
      state.message = 'Workspace mutations are not eligible for Formal Run.';
      syncAvailability();
      return null;
    }

    const generation = ++operationGeneration;
    const clientSnapshotId = globalThis.crypto.randomUUID();
    const controller = new AbortController();
    activeController = controller;
    state.clientSnapshotId = clientSnapshotId;
    state.admission = null;
    state.result = null;
    state.phase = 'admitting';
    state.errorCode = null;
    state.message = 'Validating the persisted Project snapshot for Formal Run.';
    syncDiagnostics(true);
    syncAvailability();

    try {
      const admission = await options.port.admit({
        projectId: options.projectId,
        clientSnapshotId,
        expectedPersistenceRevision: options.persistenceOwner.projection.persistenceRevision
      }, { signal: controller.signal });
      if (!isCurrent(generation, clientSnapshotId)) return null;
      state.admission = admission;
      if (!admission.allowed) {
        state.phase = 'blocked';
        state.errorCode = admission.code;
        state.message = admission.message;
        options.persistenceOwner.clearRunning('Formal Run admission was rejected.');
        return null;
      }
      if (admission.projectId !== options.projectId || admission.clientSnapshotId !== clientSnapshotId ||
        admission.persistenceRevision !== options.persistenceOwner.projection.persistenceRevision ||
        !admission.canonicalFlowHash || !admission.decisionConfigurationHash) {
        state.phase = 'blocked';
        state.errorCode = 'ADMISSION_IDENTITY_INVALID';
        state.message = 'Admission did not echo the current persisted Project identity.';
        options.persistenceOwner.clearRunning('Formal Run admission identity was rejected.');
        return null;
      }

      state.phase = 'executing';
      state.message = 'Formal Run is executing the admitted persisted Project snapshot.';
      syncAvailability();
      const result = await options.port.execute({
        projectId: options.projectId,
        clientSnapshotId,
        expectedPersistenceRevision: admission.persistenceRevision,
        expectedCanonicalFlowHash: admission.canonicalFlowHash,
        expectedDecisionConfigurationHash: admission.decisionConfigurationHash
      }, { signal: controller.signal });
      if (!isCurrent(generation, clientSnapshotId)) return null;
      if (result.projectId !== options.projectId || result.executionSnapshotId !== clientSnapshotId ||
        result.persistenceRevision !== admission.persistenceRevision || result.flowHash !== admission.canonicalFlowHash ||
        result.decisionConfigurationHash !== admission.decisionConfigurationHash) {
        state.phase = 'unknown-outcome';
        state.errorCode = 'RUN_RESULT_IDENTITY_MISMATCH';
        state.message = 'The execute response did not match the admitted persisted Project snapshot.';
        return null;
      }
      state.result = result;
      state.phase = result.outcome.execution === 'Cancelled'
        ? 'cancelled'
        : result.outcome.execution === 'Succeeded' ? 'succeeded' : 'failed';
      state.message = terminalMessage(result);
      options.persistenceOwner.clearRunning(state.message);
      return result;
    } catch (error) {
      if (!isCurrent(generation, clientSnapshotId)) return null;
      if (state.phase === 'cancelled' || state.phase === 'succeeded' || state.phase === 'failed') {
        return state.result;
      }
      if (authoritySettledLocalAbort === controller) {
        authoritySettledLocalAbort = undefined;
        return null;
      }
      state.errorCode = errorCode(error) ?? (error instanceof ApiAbortError ? 'RUN_CANCELLED' : 'RUN_NETWORK_FAILURE');
      if (error instanceof ApiAbortError) {
        state.phase = 'unknown-outcome';
        state.message = 'Formal Run cancellation was requested. The server outcome is unknown, so Workspace remains locked.';
      } else if (error instanceof ApiNetworkError) {
        state.phase = 'unknown-outcome';
        state.message = 'Formal Run network outcome is unknown, so Workspace remains locked.';
      } else if (error instanceof ApiHttpError) {
        state.phase = 'unknown-outcome';
        state.message = 'Formal Run returned an indeterminate server response. Reconcile the authoritative outcome before leaving.';
      } else {
        state.phase = 'failed';
        state.message = messageFor(error);
        options.persistenceOwner.clearRunning(state.message);
      }
      return null;
    } finally {
      if (isCurrent(generation, clientSnapshotId)) {
        activeController = undefined;
        if (authoritySettledLocalAbort === controller) authoritySettledLocalAbort = undefined;
        syncDiagnostics(false);
        syncAvailability();
      }
    }
  }

  async function stopCurrent(): Promise<boolean> {
    if (stopPromise) return stopPromise;
    if (disposed || !activeController || !state.canStop) return false;

    const generation = operationGeneration;
    const clientSnapshotId = state.clientSnapshotId;
    if (!clientSnapshotId) return false;
    const identity = currentIdentity();
    if (!identity) {
      state.phase = 'unknown-outcome';
      state.errorCode = 'RUN_IDENTITY_UNAVAILABLE';
      state.message = 'Formal Run identity is incomplete. Workspace remains locked.';
      activeController.abort();
      syncAvailability();
      return true;
    }

    state.phase = 'cancel-requested';
    state.message = 'Stopping Formal Run through the authoritative runtime coordinator.';
    syncAvailability();
    const controller = new AbortController();
    stopController = controller;
    syncDiagnostics(Boolean(activeController));
    const operation = track((async () => {
      try {
        const reconciliation = await options.port.stop(identity, { signal: controller.signal });
        applyReconciliation(reconciliation, identity, generation);
        if (isCurrent(generation, clientSnapshotId) && activeController) {
          // The authoritative stop has settled the server-side run. Abort only
          // the local execute response wait so the owner can release its flight.
          authoritySettledLocalAbort = activeController;
          activeController.abort();
        }
        return true;
      } catch (error) {
        if (isCurrent(generation, clientSnapshotId)) {
          state.phase = 'unknown-outcome';
          state.errorCode = errorCode(error) ?? 'RUN_STOP_NETWORK_FAILURE';
          state.message = 'The stop request outcome is unknown. Reconcile the authoritative run before leaving.';
          // The server-side run is no longer tied to this HTTP request. Abort
          // only the local response wait so dispose cannot be mistaken for stop.
          activeController?.abort();
          syncDiagnostics(false);
          syncAvailability();
        }
        return true;
      }
    })());
    stopPromise = operation.finally(() => {
      stopPromise = undefined;
      if (stopController === controller) stopController = undefined;
      syncDiagnostics(Boolean(activeController));
      syncAvailability();
    });
    syncAvailability();
    return stopPromise;
  }

  async function reconcileCurrent(): Promise<WorkspaceRunReconciliationV1 | null> {
    if (reconcilePromise) return reconcilePromise;
    if (disposed) return null;
    const identity = currentIdentity();
    const clientSnapshotId = state.clientSnapshotId;
    if (!identity || !clientSnapshotId) return null;
    const generation = operationGeneration;
    state.message = 'Reconciling Formal Run against the authoritative runtime and result repository.';
    syncAvailability();
    const controller = new AbortController();
    reconcileController = controller;
    syncDiagnostics(Boolean(activeController));
    const operation = track((async () => {
      try {
        const reconciliation = await options.port.reconcile(identity, { signal: controller.signal });
        if (!isCurrent(generation, clientSnapshotId)) return null;
        applyReconciliation(reconciliation, identity, generation);
        return reconciliation;
      } catch (error) {
        if (isCurrent(generation, clientSnapshotId)) {
          state.phase = 'unknown-outcome';
          state.errorCode = errorCode(error) ?? 'RUN_RECONCILE_NETWORK_FAILURE';
          state.message = 'Formal Run reconcile failed before an authoritative answer was received. Workspace remains locked.';
          syncAvailability();
        }
        return null;
      }
    })());
    reconcilePromise = operation.finally(() => {
      reconcilePromise = undefined;
      if (reconcileController === controller) reconcileController = undefined;
      syncDiagnostics(Boolean(activeController));
      syncAvailability();
    });
    syncAvailability();
    return reconcilePromise;
  }

  async function prepareRunForLeave(reason = 'route-leave'): Promise<boolean> {
    void reason;
    if (disposed) return true;
    if (state.phase === 'admitting') {
      cancelAdmission(reason);
      return true;
    }
    if (state.phase === 'executing') {
      await stopCurrent();
    }
    if (state.phase === 'cancel-requested' || state.phase === 'unknown-outcome') {
      const reconciliation = await reconcileCurrent();
      return Boolean(reconciliation && (
        reconciliation.status === 'cancelled' ||
        reconciliation.status === 'succeeded' ||
        reconciliation.status === 'failed'
      ));
    }
    return state.phase !== 'disposed';
  }

  const owner: WorkspaceRunCommandOwner = Object.freeze({
    projectId: options.projectId,
    projection: readonly(state),
    run(): Promise<WorkspaceRunResultV1 | null> {
      if (runPromise) return runPromise;
      const operation = track(performRun());
      const flight = operation.finally(() => {
        if (runPromise === flight) {
          runPromise = undefined;
          syncAvailability();
        }
      });
      runPromise = flight;
      syncAvailability();
      return runPromise;
    },
    stop(): Promise<boolean> {
      return stopCurrent();
    },
    reconcile(): Promise<WorkspaceRunReconciliationV1 | null> {
      return reconcileCurrent();
    },
    reconciliationIdentity(): WorkspaceRunIdentityV1 | null {
      return currentIdentity();
    },
    prepareForLeave(reason = 'route-leave'): Promise<boolean> {
      return prepareRunForLeave(reason);
    },
    async settle(): Promise<void> {
      await Promise.allSettled([...pending]);
    },
    dispose(reason = 'workspace-run-disposed'): void {
      if (disposed) return;
      if (state.phase === 'admitting') cancelAdmission(reason);
      disposed = true;
      operationGeneration += 1;
      activeController?.abort();
      stopController?.abort();
      reconcileController?.abort();
      activeController = undefined;
      stopController = undefined;
      reconcileController = undefined;
      authoritySettledLocalAbort = undefined;
      state.phase = 'disposed';
      state.canRun = false;
      state.canStop = false;
      state.canReconcile = false;
      lease.update(zeroResources());
      lease.dispose(reason);
    }
  });
  syncDiagnostics();
  syncAvailability();
  return owner;
}
