import { reactive, readonly, type DeepReadonly } from 'vue';
import { ApiHttpError, type ApiTransport } from '@/platform/api';
import { decodeProjectDetails, type ProjectDetails } from '@/capabilities/projects-read/projectContracts';
import { createWorkspaceRunPort } from '@/capabilities/project-workspace/run/runContracts';
import type { InspectionRunIdentity } from './contracts';
import type { InspectionRunOwner } from './inspectionRunOwner';

export interface InspectionCameraOption {
  readonly id: string;
  readonly label: string;
  readonly enabled: boolean;
  readonly connectionStatus: string | null;
}

export interface InspectionRunPageProjection {
  readonly phase: 'loading' | 'ready' | 'starting' | 'stopping' | 'error' | 'disposed';
  readonly project: ProjectDetails | null;
  readonly cameras: readonly InspectionCameraOption[];
  readonly selectedCameraId: string | null;
  readonly message: string;
  readonly errorCode: string | null;
}

type MutableProjection = { -readonly [Key in keyof InspectionRunPageProjection]: InspectionRunPageProjection[Key] };

export interface InspectionRunPageOwner {
  readonly projection: DeepReadonly<InspectionRunPageProjection>;
  readonly run: InspectionRunOwner;
  load(): Promise<void>;
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
    message: '正在读取工程与连续检测状态。',
    errorCode: null
  });
  const admission = createWorkspaceRunPort(options.api, options.projectId);
  let disposed = false;
  let controller: AbortController | null = null;

  async function load(): Promise<void> {
    if (disposed) return;
    controller?.abort();
    controller = new AbortController();
    state.phase = 'loading';
    state.errorCode = null;
    try {
      const [projectPayload, cameraPayload] = await Promise.all([
        options.api.get(`projects/${encodeURIComponent(options.projectId)}`, { signal: controller.signal }),
        options.api.get('cameras/bindings', { signal: controller.signal })
      ]);
      if (disposed) return;
      state.project = decodeProjectDetails(projectPayload);
      state.cameras = cameraOptions(cameraPayload);
      const firstEnabled = state.cameras.find(camera => camera.enabled);
      if (!state.selectedCameraId || !state.cameras.some(camera => camera.id === state.selectedCameraId && camera.enabled)) {
        state.selectedCameraId = firstEnabled?.id ?? null;
      }
      await options.run.hydrate();
      if (disposed) return;
      state.phase = 'ready';
      state.message = options.run.projection.message;
    } catch {
      if (disposed || controller.signal.aborted) return;
      state.phase = 'error';
      state.errorCode = 'INSPECTION_PAGE_LOAD_FAILED';
      state.message = '无法读取工程、相机或运行状态，请确认本地服务可用后重试。';
    } finally {
      controller = null;
    }
  }

  function selectCamera(cameraId: string | null): void {
    if (disposed || state.phase === 'starting' || state.phase === 'stopping') return;
    state.selectedCameraId = cameraId;
  }

  async function start(): Promise<boolean> {
    if (disposed || !state.project || options.run.projection.runtime?.isBusy) return false;
    state.phase = 'starting';
    state.errorCode = null;
    state.message = '正在校验已保存工程快照。';
    try {
      const clientSnapshotId = crypto.randomUUID();
      const admitted = await admission.admit({
        projectId: options.projectId,
        clientSnapshotId,
        expectedPersistenceRevision: state.project.persistenceRevision
      });
      if (!admitted.allowed || admitted.persistenceRevision == null ||
          !admitted.canonicalFlowHash || !admitted.decisionConfigurationHash) {
        state.phase = 'error';
        state.errorCode = admitted.code ?? 'INSPECTION_ADMISSION_REJECTED';
        state.message = admitted.message;
        return false;
      }
      const identity: InspectionRunIdentity = Object.freeze({
        projectId: admitted.projectId,
        clientSnapshotId: admitted.clientSnapshotId,
        expectedPersistenceRevision: admitted.persistenceRevision,
        expectedCanonicalFlowHash: admitted.canonicalFlowHash,
        expectedDecisionConfigurationHash: admitted.decisionConfigurationHash
      });
      const started = await options.run.start(identity, state.selectedCameraId);
      state.phase = started ? 'ready' : 'error';
      state.errorCode = started ? null : options.run.projection.errorCode;
      state.message = options.run.projection.message;
      return started;
    } catch (error) {
      state.phase = 'error';
      const payload = error instanceof ApiHttpError && typeof error.payload === 'object' && error.payload !== null
        ? error.payload as Record<string, unknown>
        : null;
      state.errorCode = typeof payload?.code === 'string' ? payload.code :
        typeof payload?.Code === 'string' ? payload.Code : 'INSPECTION_ADMISSION_FAILED';
      state.message = typeof payload?.error === 'string' ? payload.error :
        typeof payload?.Error === 'string' ? payload.Error : '无法取得已保存工程的运行身份，未启动连续检测。';
      return false;
    }
  }

  async function stop(): Promise<boolean> {
    if (disposed || options.run.projection.runtime?.sessionType !== 'ContinuousInspection') return false;
    state.phase = 'stopping';
    const stopped = await options.run.stop();
    state.phase = stopped ? 'ready' : 'error';
    state.errorCode = stopped ? null : options.run.projection.errorCode;
    state.message = options.run.projection.message;
    return stopped;
  }

  return Object.freeze({
    projection: readonly(state),
    run: options.run,
    load,
    selectCamera,
    start,
    stop,
    dispose(): void {
      if (disposed) return;
      disposed = true;
      controller?.abort();
      controller = null;
      options.run.dispose();
      state.phase = 'disposed';
    }
  });
}
