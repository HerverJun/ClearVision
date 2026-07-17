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
  type WorkspaceRunPort,
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
}

type MutableWorkspaceRunProjection = {
  -readonly [Key in keyof WorkspaceRunProjection]: WorkspaceRunProjection[Key]
};

export interface WorkspaceRunCommandOwner {
  readonly projectId: string;
  readonly projection: DeepReadonly<WorkspaceRunProjection>;
  run(): Promise<WorkspaceRunResultV1 | null>;
  stop(): boolean;
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
    canStop: false
  });
  let disposed = false;
  let operationGeneration = 0;
  let activeController: AbortController | undefined;
  let runPromise: Promise<WorkspaceRunResultV1 | null> | undefined;
  const pending = new Set<Promise<unknown>>();

  function isCurrent(generation: number, clientSnapshotId: string): boolean {
    return !disposed && generation === operationGeneration && state.projectId === options.projectId &&
      options.persistenceOwner.projectId === options.projectId && state.clientSnapshotId === clientSnapshotId;
  }

  function syncDiagnostics(inFlight = false): void {
    if (disposed) return;
    lease.update(Object.freeze({
      ...zeroResources(),
      activeSubscriptions: 1,
      activeAbortControllers: activeController ? 1 : 0,
      inFlightExecute: inFlight ? 1 : 0
    }));
  }

  function syncAvailability(): void {
    if (disposed) return;
    state.canRun = !runPromise && options.persistenceOwner.projection.canRun &&
      (state.phase === 'idle' || state.phase === 'blocked' || state.phase === 'succeeded' || state.phase === 'failed');
    state.canStop = Boolean(runPromise && activeController &&
      (state.phase === 'admitting' || state.phase === 'executing'));
  }

  function track<T>(promise: Promise<T>): Promise<T> {
    pending.add(promise);
    promise.finally(() => pending.delete(promise)).catch(() => {});
    return promise;
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
      state.errorCode = errorCode(error) ?? (error instanceof ApiAbortError ? 'RUN_CANCELLED' : 'RUN_NETWORK_FAILURE');
      if (error instanceof ApiAbortError) {
        state.phase = 'unknown-outcome';
        state.message = 'Formal Run cancellation was requested. The server outcome is unknown, so Workspace remains locked.';
      } else if (error instanceof ApiNetworkError) {
        state.phase = 'unknown-outcome';
        state.message = 'Formal Run network outcome is unknown, so Workspace remains locked.';
      } else {
        state.phase = 'failed';
        state.message = messageFor(error);
        options.persistenceOwner.clearRunning(state.message);
      }
      return null;
    } finally {
      if (isCurrent(generation, clientSnapshotId)) {
        activeController = undefined;
        syncDiagnostics(false);
        syncAvailability();
      }
    }
  }

  const owner: WorkspaceRunCommandOwner = Object.freeze({
    projectId: options.projectId,
    projection: readonly(state),
    run(): Promise<WorkspaceRunResultV1 | null> {
      if (runPromise) return runPromise;
      const operation = track(performRun());
      runPromise = operation.finally(() => {
        runPromise = undefined;
        syncAvailability();
      });
      syncAvailability();
      return runPromise;
    },
    stop(): boolean {
      if (disposed || !activeController || !state.canStop) return false;
      state.phase = 'cancel-requested';
      state.message = 'Stopping Formal Run. Waiting for the authoritative terminal outcome.';
      activeController.abort();
      syncAvailability();
      return true;
    },
    async settle(): Promise<void> {
      await Promise.allSettled([...pending]);
    },
    dispose(reason = 'workspace-run-disposed'): void {
      if (disposed) return;
      disposed = true;
      operationGeneration += 1;
      activeController?.abort();
      activeController = undefined;
      state.phase = 'disposed';
      state.canRun = false;
      state.canStop = false;
      lease.update(zeroResources());
      lease.dispose(reason);
    }
  });
  syncDiagnostics();
  syncAvailability();
  return owner;
}
