import { reactive, readonly, type DeepReadonly } from 'vue';
import { ApiAbortError, ApiHttpError, type ApiTransport } from '@/platform/api';
import { decodeProjectDetails, type ProjectDetails } from '@/capabilities/projects-read/projectContracts';
import {
  createWorkspaceRunPort,
  type WorkspaceRunAdmissionV1
} from '@/capabilities/project-workspace/run/runContracts';
import type { InspectionRunIdentity } from './contracts';
import type { InspectionRunOwner } from './inspectionRunOwner';

export interface InspectionCameraOption {
  readonly id: string;
  readonly label: string;
  readonly enabled: boolean;
  readonly connectionStatus: string | null;
}

export interface InspectionRunPageProjection {
  readonly phase: 'loading' | 'ready' | 'admitting' | 'starting' | 'stopping' | 'error' | 'disposed';
  readonly project: ProjectDetails | null;
  readonly cameras: readonly InspectionCameraOption[];
  readonly selectedCameraId: string | null;
  readonly admission: WorkspaceRunAdmissionV1 | null;
  readonly admissionCheckedAt: string | null;
  readonly message: string;
  readonly errorCode: string | null;
}

type MutableProjection = {
  -readonly [Key in keyof InspectionRunPageProjection]: InspectionRunPageProjection[Key]
};

export interface InspectionRunPageOwner {
  readonly projection: DeepReadonly<InspectionRunPageProjection>;
  readonly run: InspectionRunOwner;
  load(): Promise<void>;
  refreshAdmission(): Promise<WorkspaceRunAdmissionV1 | null>;
  selectCamera(cameraId: string | null): void;
  start(): Promise<boolean>;
  stop(): Promise<boolean>;
  dispose(): void;
}

function cameraOptions(value: unknown): readonly InspectionCameraOption[] {
  if (!Array.isArray(value)) return Object.freeze([]);
  return Object.freeze(value.flatMap(item => {
    if (typeof item !== 'object' || item === null || Array.isArray(item)) return [];
    const record = item as Record<string, unknown>;
    if (typeof record.id !== 'string' || !record.id.trim()) return [];
    const label = typeof record.displayName === 'string' && record.displayName.trim()
      ? record.displayName
      : record.id;
    return [Object.freeze({
      id: record.id,
      label,
      enabled: record.isEnabled !== false,
      connectionStatus: typeof record.connectionStatus === 'string' ? record.connectionStatus : null
    })];
  }));
}

function responseCode(error: unknown, fallback: string): string {
  if (!(error instanceof ApiHttpError) || typeof error.payload !== 'object' || error.payload === null) return fallback;
  const payload = error.payload as Record<string, unknown>;
  const code = payload.code ?? payload.Code;
  return typeof code === 'string' && code.trim() ? code.trim().toUpperCase() : fallback;
}

function responseMessage(error: unknown, fallback: string): string {
  if (!(error instanceof ApiHttpError) || typeof error.payload !== 'object' || error.payload === null) return fallback;
  const payload = error.payload as Record<string, unknown>;
  const message = payload.error ?? payload.Error;
  return typeof message === 'string' && message.trim() ? message.trim() : fallback;
}

export function createInspectionRunPageOwner(options: {
  readonly projectId: string;
  readonly api: ApiTransport;
  readonly run: InspectionRunOwner;
}): InspectionRunPageOwner {
  const state = reactive<MutableProjection>({
    phase: 'loading',
    project: null,
    cameras: Object.freeze([]),
    selectedCameraId: null,
    admission: null,
    admissionCheckedAt: null,
    message: '正在读取工程与连续检测状态。',
    errorCode: null
  });
  const admissionPort = createWorkspaceRunPort(options.api, options.projectId);
  let disposed = false;
  let generation = 0;
  let loadController: AbortController | null = null;
  let admissionController: AbortController | null = null;
  let admissionPromise: Promise<WorkspaceRunAdmissionV1 | null> | null = null;
  let startPromise: Promise<boolean> | null = null;
  let stopPromise: Promise<boolean> | null = null;

  async function load(): Promise<void> {
    if (disposed) return;
    const current = ++generation;
    loadController?.abort();
    const controller = new AbortController();
    loadController = controller;
    state.phase = 'loading';
    state.errorCode = null;
    try {
      const [projectPayload, cameraPayload] = await Promise.all([
        options.api.get('projects/' + encodeURIComponent(options.projectId), { signal: controller.signal }),
        options.api.get('cameras/bindings', { signal: controller.signal }),
        options.run.hydrate()
      ]);
      if (disposed || current !== generation) return;
      state.project = decodeProjectDetails(projectPayload);
      state.cameras = cameraOptions(cameraPayload);
      const firstEnabled = state.cameras.find(camera => camera.enabled);
      if (!state.selectedCameraId ||
        !state.cameras.some(camera => camera.id === state.selectedCameraId && camera.enabled)) {
        state.selectedCameraId = firstEnabled?.id ?? null;
      }
      state.phase = 'ready';
      state.message = options.run.projection.message;
      if (!options.run.projection.runtime?.isBusy) await refreshAdmission();
    } catch (error) {
      if (disposed || current !== generation || controller.signal.aborted || error instanceof ApiAbortError) return;
      state.phase = 'error';
      state.errorCode = responseCode(error, 'INSPECTION_PAGE_LOAD_FAILED');
      state.message = error instanceof ApiHttpError && (error.status === 401 || error.status === 403)
        ? '当前会话无权读取连续检测控制台。'
        : '无法读取工程、相机或运行状态。';
    } finally {
      if (loadController === controller) loadController = null;
    }
  }

  async function performAdmission(): Promise<WorkspaceRunAdmissionV1 | null> {
    if (disposed || !state.project || options.run.projection.runtime?.isBusy) return null;
    admissionController?.abort();
    const controller = new AbortController();
    admissionController = controller;
    state.phase = 'admitting';
    state.errorCode = null;
    state.message = '正在读取已保存工程的服务端运行准入。';
    try {
      const admitted = await admissionPort.admit({
        projectId: options.projectId,
        clientSnapshotId: crypto.randomUUID(),
        expectedPersistenceRevision: state.project.persistenceRevision
      }, { signal: controller.signal });
      if (disposed || admissionController !== controller) return null;
      state.admission = admitted;
      state.admissionCheckedAt = new Date().toISOString();
      state.phase = admitted.allowed ? 'ready' : 'error';
      state.errorCode = admitted.allowed ? null : admitted.code ?? 'INSPECTION_ADMISSION_REJECTED';
      state.message = admitted.message;
      return admitted;
    } catch (error) {
      if (disposed || controller.signal.aborted || error instanceof ApiAbortError) return null;
      state.admission = null;
      state.phase = 'error';
      state.errorCode = responseCode(error, 'INSPECTION_ADMISSION_FAILED');
      state.message = error instanceof ApiHttpError && (error.status === 401 || error.status === 403)
        ? '当前会话无权读取运行准入。'
        : responseMessage(error, '无法读取已保存工程的运行准入。');
      if (error instanceof ApiHttpError && error.status === 409) await options.run.reconcile();
      return null;
    } finally {
      if (admissionController === controller) admissionController = null;
    }
  }

  function refreshAdmission(): Promise<WorkspaceRunAdmissionV1 | null> {
    if (admissionPromise) return admissionPromise;
    const operation = performAdmission();
    const flight = operation.finally(() => {
      if (admissionPromise === flight) admissionPromise = null;
    });
    admissionPromise = flight;
    return flight;
  }

  function selectCamera(cameraId: string | null): void {
    if (disposed || state.phase === 'admitting' || state.phase === 'starting' || state.phase === 'stopping') return;
    state.selectedCameraId = cameraId;
  }

  async function performStart(): Promise<boolean> {
    if (disposed || !state.project || options.run.projection.runtime?.isBusy) return false;
    const admitted = await refreshAdmission();
    if (!admitted || !admitted.allowed || admitted.persistenceRevision == null ||
      !admitted.canonicalFlowHash || !admitted.decisionConfigurationHash) {
      return false;
    }
    state.phase = 'starting';
    state.errorCode = null;
    state.message = '正在启动已准入的连续检测会话。';
    const identity: InspectionRunIdentity = Object.freeze({
      projectId: admitted.projectId,
      clientSnapshotId: admitted.clientSnapshotId,
      expectedPersistenceRevision: admitted.persistenceRevision,
      expectedCanonicalFlowHash: admitted.canonicalFlowHash,
      expectedDecisionConfigurationHash: admitted.decisionConfigurationHash
    });
    const started = await options.run.start(identity, state.selectedCameraId);
    if (disposed) return false;
    state.phase = started ? 'ready' : 'error';
    state.errorCode = started ? null : options.run.projection.errorCode;
    state.message = options.run.projection.message;
    return started;
  }

  function start(): Promise<boolean> {
    if (startPromise) return startPromise;
    const operation = performStart();
    const flight = operation.finally(() => {
      if (startPromise === flight) startPromise = null;
    });
    startPromise = flight;
    return flight;
  }

  async function performStop(): Promise<boolean> {
    if (disposed || options.run.projection.runtime?.sessionType !== 'ContinuousInspection') return false;
    state.phase = 'stopping';
    const stopped = await options.run.stop();
    if (disposed) return false;
    state.phase = stopped ? 'ready' : 'error';
    state.errorCode = stopped ? null : options.run.projection.errorCode;
    state.message = options.run.projection.message;
    if (stopped) await refreshAdmission();
    return stopped;
  }

  function stop(): Promise<boolean> {
    if (stopPromise) return stopPromise;
    const operation = performStop();
    const flight = operation.finally(() => {
      if (stopPromise === flight) stopPromise = null;
    });
    stopPromise = flight;
    return flight;
  }

  return Object.freeze({
    projection: readonly(state),
    run: options.run,
    load,
    refreshAdmission,
    selectCamera,
    start,
    stop,
    dispose(): void {
      if (disposed) return;
      disposed = true;
      generation += 1;
      loadController?.abort();
      admissionController?.abort();
      loadController = null;
      admissionController = null;
      admissionPromise = null;
      startPromise = null;
      stopPromise = null;
      options.run.dispose();
      state.phase = 'disposed';
    }
  });
}
