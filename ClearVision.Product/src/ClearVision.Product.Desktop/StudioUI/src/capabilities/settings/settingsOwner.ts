import { reactive, readonly, type DeepReadonly } from 'vue';
import type { ProductRuntime } from '@/app/productRuntime';
import {
  ApiAbortError,
  ApiConfigurationError,
  ApiDecodeError,
  ApiHttpError,
  ApiNetworkError,
  ApiRequestPathError
} from '@/platform/api';
import {
  evaluateSettingsEndpointAccess,
  evaluateSettingsRouteAccess,
  findSettingsEndpoint,
  SettingsContractDecodeError,
  SettingsUnknownOutcomeError,
  type SettingsErrorCode,
  type SettingsEndpointAccessReason,
  type SettingsOperationKind,
  type SettingsRole,
  type SettingsSection,
  type SettingsWriteCoordinatorDiagnostics,
  type SettingsEndpointTask,
  type SettingsWriteResult,
} from './contracts';
import {
  createSettingsApiAdapter,
  type SettingsApiAdapter,
  type AiModelMutationRequestV1,
  type AiReasoningSupportRequestV1,
  type StationCommunicationSettingsUpdateRequestV1,
  type StationTokenOperationNameV1,
  type SettingsChangePasswordRequest,
  type SettingsCreateUserRequest,
  type SettingsUpdateUserRequest
} from './apiAdapter';
import type {
  CameraDiscoveryProviderV1,
  PlcTestConnectionRequestV1,
  SerialPhotoelectricTestRequestV1,
  TcpSendRequestV1
} from './deviceApiAdapter';
import type {
  CameraBindingV1,
  CameraBindingsResponseV1,
  CameraDiscoveryResponseV1,
  CameraPreviewDiagnosticsV1,
  CameraPreviewProjectionV1,
  PlcMappingV1,
  PlcMappingsResponseV1,
  PlcSettingsResponseV1,
  PlcSettingsV1,
  PlcTestConnectionResponseV1,
  SettingsDeviceProjectionV1,
  TcpClearFramesResponseV1,
  TcpFramesResponseV1,
  TcpProfilesResponseV1,
  TcpProfileV1,
  TcpRuntimeResponseV1,
  TcpStatusResponseV1,
  TriggerDiagnosticsV1,
  SerialPhotoelectricPortV1
} from './deviceContracts';
import {
  decodeSettingsErrorPayloadV1,
  settingsErrorCodeFromHttpStatus,
  type SettingsAccountOperationResponseV1,
  type AiModelMutationResponseV1,
  type SettingsDatabaseBackupProjectionV1,
  type SettingsDatabaseStatusProjectionV1,
  type SettingsDiskUsageProjectionV1,
  type SettingsErrorProjectionV1,
  type AiModelConnectionTestProjectionV1,
  type AiModelsProjectionV1,
  type AiReasoningSupportProjectionV1,
  type SettingsProjectionV1,
  type SettingsUserProjectionV1,
  type SettingsUsersProjectionV1,
  type SettingsWriteResponseV1,
  type StationCommunicationProjectionV1,
  type StationTokenOperationV1
} from './decoder';
import type { GenericSettingsSection } from './contracts';
import { createSettingsWriteCoordinator, type SettingsWriteCoordinator } from './settingsWriteCoordinator';

export type SettingsOwnerPhase = 'idle' | 'loading' | 'ready' | 'forbidden' | 'stale' | 'error' | 'disposed';

export interface SettingsOwnerProjection {
  readonly phase: SettingsOwnerPhase;
  readonly role: string | null;
  readonly settings: SettingsProjectionV1 | null;
  readonly error: SettingsErrorProjectionV1 | null;
  readonly message: string;
  readonly generation: number;
  readonly started: boolean;
  readonly device: SettingsDeviceProjectionV1;
  readonly station: StationCommunicationProjectionV1 | null;
  readonly aiModels: AiModelsProjectionV1 | null;
  readonly dirtySectionCount: number;
  readonly pendingSectionCount: number;
  readonly unknownOutcomeKeys: readonly SettingsAuthorityReconcileKey[];
}

export interface SettingsOwnerDiagnostics {
  readonly activeSettingsOwnerCount: number;
  readonly activeAbortControllerCount: number;
  readonly inFlightReadCount: number;
  readonly dirtySectionCount: number;
  readonly pendingSectionCount: number;
  readonly write: SettingsWriteCoordinatorDiagnostics;
  readonly preview: CameraPreviewDiagnosticsV1;
  readonly disposed: boolean;
}

export type SettingsLeaveProtectionKind = 'settings-draft' | 'settings-pending' | 'settings-unknown';

export interface SettingsPanelState {
  readonly dirty: boolean;
  readonly pending: boolean;
}

export type SettingsAuthorityReconcileKey =
  | 'generic-settings'
  | 'plc-settings'
  | 'plc-mappings'
  | 'tcp-profiles'
  | `tcp-runtime:${string}`
  | 'camera-bindings'
  | 'camera-preview'
  | 'station-communication'
  | 'ai-models'
  | `ai-model-test:${string}`
  | 'users'
  | 'change-password'
  | 'database-backup';

export interface SettingsOwner {
  readonly projection: DeepReadonly<SettingsOwnerProjection>;
  readonly writes: SettingsWriteCoordinator;
  start(): Promise<void>;
  refresh(): Promise<boolean>;
  reconcileAuthority(key: SettingsAuthorityReconcileKey): Promise<SettingsWriteResult<unknown>>;
  recordChangePasswordSessionResult(sessionInvalidated: boolean, outcomeUnknown?: boolean): void;
  invalidate(reason?: string): void;
  enqueueEndpointOperation<T>(
    section: SettingsSection,
    endpointId: string,
    task: SettingsEndpointTask<T>,
    operationKind?: SettingsOperationKind,
    reconcileKey?: SettingsAuthorityReconcileKey
  ): Promise<SettingsWriteResult<T>>;
  registerPanelState(section: SettingsSection, readState: () => SettingsPanelState): () => void;
  refreshPanelState(): void;
  leaveProtection(): SettingsLeaveProtectionKind | null;
  saveGenericSection(
    section: GenericSettingsSection,
    value: Readonly<Record<string, unknown>>
  ): Promise<SettingsWriteResult<SettingsWriteResponseV1>>;
  readStationCommunication(): Promise<SettingsWriteResult<StationCommunicationProjectionV1>>;
  saveStationCommunication(
    request: StationCommunicationSettingsUpdateRequestV1
  ): Promise<SettingsWriteResult<StationCommunicationProjectionV1>>;
  runStationTokenOperation(
    operation: StationTokenOperationNameV1
  ): Promise<SettingsWriteResult<StationTokenOperationV1>>;
  readAiModels(): Promise<SettingsWriteResult<AiModelsProjectionV1>>;
  createAiModel(
    request: AiModelMutationRequestV1
  ): Promise<SettingsWriteResult<SettingsAiModelsMutationResultV1>>;
  updateAiModel(
    id: string,
    request: AiModelMutationRequestV1
  ): Promise<SettingsWriteResult<SettingsAiModelsMutationResultV1>>;
  deleteAiModel(id: string): Promise<SettingsWriteResult<SettingsAiModelsMutationResultV1>>;
  activateAiModel(id: string): Promise<SettingsWriteResult<SettingsAiModelsMutationResultV1>>;
  setAiModelDefault(
    id: string,
    role: 'planner' | 'shadow-eval'
  ): Promise<SettingsWriteResult<SettingsAiModelsMutationResultV1>>;
  testAiModel(id: string): Promise<SettingsWriteResult<AiModelConnectionTestProjectionV1>>;
  readAiReasoningSupport(
    request: AiReasoningSupportRequestV1
  ): Promise<SettingsWriteResult<AiReasoningSupportProjectionV1>>;
  readDiskUsage(path?: string): Promise<SettingsWriteResult<SettingsDiskUsageProjectionV1>>;
  readDatabaseStatus(): Promise<SettingsWriteResult<SettingsDatabaseStatusProjectionV1>>;
  backupDatabase(): Promise<SettingsWriteResult<SettingsDatabaseBackupProjectionV1>>;
  changePassword(
    request: SettingsChangePasswordRequest
  ): Promise<SettingsWriteResult<SettingsAccountOperationResponseV1>>;
  readUsers(): Promise<SettingsWriteResult<SettingsUsersProjectionV1>>;
  createUser(request: SettingsCreateUserRequest): Promise<SettingsWriteResult<SettingsUserProjectionV1>>;
  updateUser(
    id: string,
    request: SettingsUpdateUserRequest
  ): Promise<SettingsWriteResult<SettingsUserProjectionV1>>;
  deleteUser(id: string): Promise<SettingsWriteResult<void>>;
  resetUserPassword(
    id: string,
    newPassword: string
  ): Promise<SettingsWriteResult<SettingsAccountOperationResponseV1>>;
  readPlcSettings(): Promise<SettingsWriteResult<PlcSettingsResponseV1>>;
  readPlcMappings(): Promise<SettingsWriteResult<PlcMappingsResponseV1>>;
  savePlcSettings(settings: PlcSettingsV1): Promise<SettingsWriteResult<PlcSettingsResponseV1>>;
  savePlcMappings(mappings: readonly PlcMappingV1[]): Promise<SettingsWriteResult<PlcMappingsResponseV1>>;
  testPlcConnection(request: PlcTestConnectionRequestV1): Promise<SettingsWriteResult<PlcTestConnectionResponseV1>>;
  readTcpProfiles(): Promise<SettingsWriteResult<TcpProfilesResponseV1>>;
  saveTcpProfiles(profiles: readonly TcpProfileV1[]): Promise<SettingsWriteResult<TcpProfilesResponseV1>>;
  connectTcp(profileId: string): Promise<SettingsWriteResult<TcpRuntimeResponseV1>>;
  disconnectTcp(profileId: string): Promise<SettingsWriteResult<TcpRuntimeResponseV1>>;
  startTcpServer(profileId: string): Promise<SettingsWriteResult<TcpRuntimeResponseV1>>;
  stopTcpServer(profileId: string): Promise<SettingsWriteResult<TcpRuntimeResponseV1>>;
  sendTcp(profileId: string, request: TcpSendRequestV1): Promise<SettingsWriteResult<TcpRuntimeResponseV1>>;
  readTcpStatus(profileId: string): Promise<SettingsWriteResult<TcpStatusResponseV1>>;
  readTcpFrames(profileId: string): Promise<SettingsWriteResult<TcpFramesResponseV1>>;
  clearTcpFrames(profileId: string): Promise<SettingsWriteResult<TcpClearFramesResponseV1>>;
  discoverCameras(provider: CameraDiscoveryProviderV1): Promise<SettingsWriteResult<CameraDiscoveryResponseV1>>;
  readCameraBindings(): Promise<SettingsWriteResult<CameraBindingsResponseV1>>;
  saveCameraBindings(
    bindings: readonly CameraBindingV1[],
    activeCameraId: string
  ): Promise<SettingsWriteResult<SettingsDeviceOperationResult>>;
  readTriggerDiagnostics(): Promise<SettingsWriteResult<TriggerDiagnosticsV1>>;
  readSerialPhotoelectricPorts(): Promise<SettingsWriteResult<readonly SerialPhotoelectricPortV1[]>>;
  testSerialPhotoelectric(request: SerialPhotoelectricTestRequestV1): Promise<SettingsWriteResult<SettingsDeviceOperationResult>>;
  learnEnterPhotoelectricDevice(timeoutMs: number): Promise<SettingsWriteResult<SettingsDeviceOperationResult>>;
  captureSoftTrigger(cameraBindingId: string): Promise<SettingsWriteResult<SettingsDeviceOperationResult>>;
  startCameraPreview(cameraBindingId: string): Promise<SettingsWriteResult<SettingsDeviceOperationResult>>;
  stopCameraPreview(reason?: string): Promise<SettingsWriteResult<void>>;
  diagnostics(): SettingsOwnerDiagnostics;
  dispose(reason?: string): void;
}

export interface SettingsDeviceOperationResult {
  readonly success: boolean;
  readonly message: string;
  readonly response?: string;
  readonly errors?: readonly unknown[];
}

export interface SettingsAiModelsMutationResultV1 {
  readonly message: string;
  readonly modelId: string | null;
  readonly projection: AiModelsProjectionV1;
}

export interface CreateSettingsOwnerOptions {
  readonly runtime: Pick<ProductRuntime, 'api'>;
  readonly role: SettingsRole;
}

export class SettingsOwnerConflictError extends Error {
  constructor() {
    super('同一时刻只能挂载一个设置生命周期管理器。');
    this.name = 'SettingsOwnerConflictError';
  }
}

let activeSettingsOwnerToken: object | undefined;
let activeSettingsOwnerCount = 0;

export function getSettingsOwnerActiveCount(): number {
  return activeSettingsOwnerCount;
}

type MutableProjection = {
  -readonly [Key in keyof SettingsOwnerProjection]: SettingsOwnerProjection[Key]
};

function safeRole(role: SettingsRole): string | null {
  const normalized = typeof role === 'string' ? role.trim() : '';
  return normalized || null;
}

function fallbackMessage(code: SettingsErrorCode): string {
  switch (code) {
    case 'unauthorized': return '登录状态已失效，设置页已停止请求。';
    case 'forbidden': return '当前账户没有设置访问权限。';
    case 'not-found': return '服务端设置不存在或当前用户不可见。';
    case 'conflict': return '服务端设置已变化，请重新读取后继续。';
    case 'validation': return '设置请求未通过服务端校验。';
    case 'abort': return '设置请求已取消。';
    case 'decode': return '设置服务响应不符合已冻结合同。';
    case 'network': return '本地设置服务暂时不可用。';
    case 'server': return '本地设置服务发生错误。';
    case 'sensitive-field': return '设置响应包含禁止暴露的敏感字段。';
    case 'unsupported': return '当前设置操作尚未获得合同授权。';
    case 'unknown-outcome': return '设置操作结果未知，请重新读取后再决定下一步。';
    case 'unexpected-http-status': return '设置请求返回了未分类的 HTTP 状态。';
  }
}

function errorProjection(error: unknown): SettingsErrorProjectionV1 {
  if (error instanceof SettingsUnknownOutcomeError) {
    return Object.freeze({
      code: 'unknown-outcome',
      publicMessage: '操作结果未知；请重新读取服务端状态后再决定是否重试。',
      policy: null,
      issues: Object.freeze([])
    });
  }
  if (error instanceof ApiHttpError) {
    const fallbackCode = settingsErrorCodeFromHttpStatus(error.status);
    if (typeof error.payload === 'object' && error.payload !== null) {
      try {
        return decodeSettingsErrorPayloadV1(error.payload, '$.error', fallbackCode);
      } catch {
        // The transport already classified the HTTP error; never surface raw payload fields.
      }
    }
    return Object.freeze({
      code: fallbackCode,
      publicMessage: fallbackMessage(fallbackCode),
      policy: null,
      issues: Object.freeze([])
    });
  }

  const code: SettingsErrorCode = error instanceof ApiAbortError
    ? 'abort'
    : error instanceof ApiNetworkError
      ? 'network'
        : error instanceof ApiDecodeError
          ? 'decode'
          : error instanceof SettingsContractDecodeError
            ? 'decode'
          : error instanceof ApiConfigurationError || error instanceof ApiRequestPathError
            ? 'unsupported'
          : 'unexpected-http-status';
  return Object.freeze({
    code,
    publicMessage: fallbackMessage(code),
    policy: null,
    issues: Object.freeze([])
  });
}

export function projectSettingsError(error: unknown): SettingsErrorProjectionV1 {
  return errorProjection(error);
}

export function projectSettingsOperationFailure(
  error: unknown,
  operationKind?: SettingsOperationKind
): SettingsErrorProjectionV1 {
  const projection = errorProjection(error);
  const unknownMutation = operationKind !== undefined && operationKind !== 'read' &&
    (projection.code === 'network' || projection.code === 'abort' || projection.code === 'decode');
  if (!unknownMutation) return projection;
  return Object.freeze({
    code: 'unknown-outcome',
    publicMessage: '操作结果未知；请先重新读取服务端状态，再决定是否重试。',
    policy: null,
    issues: Object.freeze([])
  });
}

export function settingsOperationResultMessage(result: {
  readonly status: string;
  readonly message?: string;
  readonly error?: unknown;
  readonly operationKind?: SettingsOperationKind;
}): string {
  if (result.status === 'failed') {
    const projection = projectSettingsOperationFailure(result.error, result.operationKind);
    return projection.code === 'unknown-outcome'
      ? '操作结果未知；请先重新读取服务端状态，再决定是否重试。'
      : projection.publicMessage;
  }
  if (result.operationKind && result.operationKind !== 'read' &&
      (result.status === 'cancelled' || result.status === 'stale' || result.status === 'disposed')) {
    return '操作结果未知；请先重新读取服务端状态，再决定是否重试。';
  }
  return result.message ?? '操作未完成。';
}

function isAbort(error: unknown): boolean {
  return error instanceof ApiAbortError ||
    (typeof DOMException !== 'undefined' && error instanceof DOMException && error.name === 'AbortError');
}

function endpointAccessMessage(reason: SettingsEndpointAccessReason): string {
  switch (reason) {
    case 'unknown-endpoint': return '请求的设置操作未在允许列表中。';
    case 'excluded-endpoint': return '请求的设置操作已被明确排除。';
    case 'section-mismatch': return '请求的设置操作不属于当前配置分组。';
    case 'route-only': return '该设置入口仅用于导航，不能直接执行。';
    case 'engineer-or-admin-required': return '当前操作需要工程师或管理员权限。';
    case 'admin-required': return '当前操作需要管理员权限。';
    case 'allowed': return '';
  }
}

function operationKindForEndpoint(endpointId: string): SettingsOperationKind {
  const endpoint = findSettingsEndpoint(endpointId);
  if (endpoint?.kind === 'read') return 'read';
  if (endpointId.startsWith('auth.') || endpointId.startsWith('users.')) return 'account-operation';
  if (endpointId.startsWith('settings.database.')) return 'database-operation';
  if (endpoint?.kind === 'runtime-operation' || endpointId.endsWith('.runtime')) return 'runtime-operation';
  return 'write';
}

function reconcileKeyForEndpoint(
  endpointId: string,
  operationKind: SettingsOperationKind,
  override?: SettingsAuthorityReconcileKey
): SettingsAuthorityReconcileKey | null {
  if (override || operationKind === 'read') return override ?? null;

  const endpoint = findSettingsEndpoint(endpointId);
  if (endpointId === 'ai.models.test') return null;
  if (endpoint?.kind === 'test') return null;
  if (endpointId === 'settings.write' || endpointId === 'settings.theme.write') return 'generic-settings';
  if (endpointId === 'plc.settings.write') return 'plc-settings';
  if (endpointId === 'plc.mappings.write') return 'plc-mappings';
  if (endpointId === 'tcp.profiles.write') return 'tcp-profiles';
  if (endpointId === 'auth.change-password') return 'change-password';
  if (endpointId.startsWith('users.')) return 'users';
  if (endpointId === 'settings.database.backup') return 'database-backup';
  if (endpointId === 'station.settings.write' || endpointId === 'station.token') return 'station-communication';
  if (endpointId.startsWith('ai.models.')) return 'ai-models';
  if (endpointId === 'camera.bindings.write') return 'camera-bindings';
  if (endpointId.startsWith('camera.preview.') || endpointId === 'camera.soft-trigger-capture') {
    return 'camera-preview';
  }
  if (endpointId === 'camera.trigger-and-preview') return 'camera-preview';
  return null;
}

function emptyCameraPreviewDiagnostics(): CameraPreviewDiagnosticsV1 {
  return Object.freeze({
    controller: 'idle',
    session: 'none',
    frameLoop: 'idle',
    blobUrl: 'none',
    controllerCount: 0,
    sessionCount: 0,
    frameLoopCount: 0,
    blobUrlCount: 0
  });
}

function emptyCameraPreview(): CameraPreviewProjectionV1 {
  return Object.freeze({
    phase: 'idle',
    sessionId: null,
    cameraBindingId: null,
    imageUrl: null,
    width: null,
    height: null,
    frameSequence: null,
    triggerMode: null,
    triggerSource: null,
    contentType: null,
    message: '尚未开始相机预览。',
    diagnostics: emptyCameraPreviewDiagnostics()
  });
}

function emptyDeviceProjection(): SettingsDeviceProjectionV1 {
  return Object.freeze({
    plcSettings: null,
    plcMappings: Object.freeze([]),
    tcpProfiles: Object.freeze([]),
    tcpStatuses: Object.freeze({}),
    tcpFrames: Object.freeze({}),
    cameraBindings: Object.freeze([]),
    activeCameraId: '',
    cameraDiscovery: null,
    triggerDiagnostics: null,
    serialPorts: Object.freeze([]),
    preview: emptyCameraPreview()
  });
}

function operationResult(
  success: boolean,
  message: string,
  response?: string,
  errors?: readonly unknown[]
): SettingsDeviceOperationResult {
  const result: { success: boolean; message: string; response?: string; errors?: readonly unknown[] } = {
    success,
    message
  };
  if (response !== undefined) result.response = response;
  if (errors !== undefined) result.errors = errors;
  return Object.freeze(result);
}

async function blobObjectUrl(blob: Blob, contentType: string): Promise<string> {
  if (typeof URL !== 'undefined' && typeof URL.createObjectURL === 'function') {
    return URL.createObjectURL(blob);
  }
  const bytes = new Uint8Array(await blob.arrayBuffer());
  let binary = '';
  for (let offset = 0; offset < bytes.length; offset += 0x8000) {
    binary += String.fromCharCode(...bytes.subarray(offset, offset + 0x8000));
  }
  return `data:${contentType || blob.type || 'image/png'};base64,${globalThis.btoa?.(binary) ?? ''}`;
}

function revokeBlobObjectUrl(value: string | null): void {
  if (!value?.startsWith('blob:')) return;
  if (typeof URL !== 'undefined' && typeof URL.revokeObjectURL === 'function') URL.revokeObjectURL(value);
}

function delayWithAbort(milliseconds: number, signal: AbortSignal): Promise<void> {
  return new Promise(resolve => {
    const timer = setTimeout(() => {
      signal.removeEventListener('abort', onAbort);
      resolve();
    }, milliseconds);
    function onAbort(): void {
      clearTimeout(timer);
      signal.removeEventListener('abort', onAbort);
      resolve();
    }
    signal.addEventListener('abort', onAbort, { once: true });
  });
}

function positiveHeader(headers: Headers, name: string): number | null {
  const value = Number(headers.get(name));
  return Number.isFinite(value) && value > 0 ? value : null;
}

export function createSettingsOwner(options: CreateSettingsOwnerOptions): SettingsOwner {
  if (activeSettingsOwnerToken) throw new SettingsOwnerConflictError();
  const ownerToken = {};
  activeSettingsOwnerToken = ownerToken;
  activeSettingsOwnerCount += 1;

  const role = safeRole(options.role);
  const adapter: SettingsApiAdapter = createSettingsApiAdapter(options.runtime.api);
  const writes = createSettingsWriteCoordinator();
  const state = reactive<MutableProjection>({
    phase: 'idle',
    role,
    settings: null,
    error: null,
    message: '设置页尚未启动。',
    generation: 0,
    started: false,
    device: emptyDeviceProjection(),
    station: null,
    aiModels: null,
    dirtySectionCount: 0,
    pendingSectionCount: 0,
    unknownOutcomeKeys: Object.freeze([])
  });
  let disposed = false;
  let generation = 0;
  let readController: AbortController | undefined;
  let previewController: AbortController | undefined;
  let previewSessionId: string | null = null;
  let previewGeneration = 0;
  let previewObjectUrl: string | null = null;
  let previewFrameLoopCount = 0;
  let previewFrameLoopPromise: Promise<void> | undefined;
  const panelStates = new Map<SettingsSection, Set<() => SettingsPanelState>>();
  const unknownOutcomeKeys = new Set<SettingsAuthorityReconcileKey>();

  function syncPanelState(): void {
    let dirtySectionCount = 0;
    let pendingSectionCount = 0;
    for (const readers of panelStates.values()) {
      let sectionDirty = false;
      let sectionPending = false;
      for (const readState of readers) {
        try {
          const panelState = readState();
          sectionDirty ||= panelState.dirty;
          sectionPending ||= panelState.pending;
        } catch {
          // A panel can be in the middle of unmounting; its disposer removes it next.
        }
      }
      if (sectionDirty) dirtySectionCount += 1;
      if (sectionPending) pendingSectionCount += 1;
    }
    state.dirtySectionCount = dirtySectionCount;
    state.pendingSectionCount = pendingSectionCount;
  }

  function updateDevice(update: (current: SettingsDeviceProjectionV1) => SettingsDeviceProjectionV1): void {
    if (disposed) return;
    state.device = Object.freeze(update(state.device));
  }

  function updatePreview(preview: CameraPreviewProjectionV1): void {
    updateDevice(current => Object.freeze({
      ...current,
      preview: Object.freeze({ ...preview, diagnostics: previewDiagnostics() })
    }));
  }

  function previewDiagnostics(): CameraPreviewDiagnosticsV1 {
    const controllerCount = previewController ? 1 : 0;
    const sessionCount = previewSessionId ? 1 : 0;
    const blobUrlCount = previewObjectUrl ? 1 : 0;
    const frameLoopCount = disposed ? 0 : previewFrameLoopCount;
    return Object.freeze({
      controller: controllerCount > 0 ? 'active' : 'idle',
      session: sessionCount > 0 ? 'active' : 'none',
      frameLoop: frameLoopCount > 0 ? 'active' : 'idle',
      blobUrl: blobUrlCount > 0 ? 'active' : 'none',
      controllerCount,
      sessionCount,
      frameLoopCount,
      blobUrlCount
    });
  }

  function refreshPreviewDiagnostics(): void {
    if (disposed) return;
    updateDevice(current => Object.freeze({
      ...current,
      preview: Object.freeze({ ...current.preview, diagnostics: previewDiagnostics() })
    }));
  }

  function clearPreviewObjectUrl(): void {
    revokeBlobObjectUrl(previewObjectUrl);
    previewObjectUrl = null;
  }

  async function stopPreviewInternal(
    message: string,
    stopRemote = true,
    waitForFrameLoop = true
  ): Promise<string | null> {
    previewGeneration += 1;
    const frameLoopPromise = previewFrameLoopPromise;
    previewController?.abort('settings-preview-stopped');
    previewController = undefined;
    const sessionId = previewSessionId;
    previewSessionId = null;
    clearPreviewObjectUrl();
    if (waitForFrameLoop && frameLoopPromise) await frameLoopPromise.catch(() => undefined);
    if (sessionId && stopRemote) await adapter.stopContinuousPreview(sessionId).catch(() => undefined);
    if (!disposed) updatePreview({ ...emptyCameraPreview(), message });
    return sessionId;
  }

  async function setPreviewBlob(
    blob: Blob,
    contentType: string,
    operationGeneration: number,
    metadata: Readonly<Partial<CameraPreviewProjectionV1>>
  ): Promise<boolean> {
    const imageUrl = await blobObjectUrl(blob, contentType);
    if (disposed || operationGeneration !== previewGeneration) {
      revokeBlobObjectUrl(imageUrl);
      return false;
    }
    clearPreviewObjectUrl();
    previewObjectUrl = imageUrl;
    updatePreview({
      ...emptyCameraPreview(),
      ...metadata,
      imageUrl,
      contentType: contentType || blob.type || 'image/png'
    });
    return true;
  }

  async function runPreviewLoop(
    sessionId: string,
    cameraBindingId: string,
    operationGeneration: number,
    controller: AbortController,
    triggerMode: string
  ): Promise<void> {
    previewFrameLoopCount += 1;
    refreshPreviewDiagnostics();
    try {
      while (!disposed && operationGeneration === previewGeneration && !controller.signal.aborted) {
        try {
          const response = await adapter.getContinuousPreviewFrame(sessionId, controller.signal);
          const accepted = await setPreviewBlob(response.blob, response.contentType, operationGeneration, {
            phase: 'running',
            sessionId,
            cameraBindingId,
            triggerMode,
            triggerSource: null,
            width: positiveHeader(response.headers, 'X-Image-Width'),
            height: positiveHeader(response.headers, 'X-Image-Height'),
            frameSequence: positiveHeader(response.headers, 'X-Frame-Sequence'),
            message: '连续预览正在接收帧。'
          });
          if (!accepted) return;
          await delayWithAbort(80, controller.signal);
        } catch (error) {
          if (disposed || controller.signal.aborted || isAbort(error)) return;
          await stopPreviewInternal('连续预览已停止。', true, false);
          if (!disposed) {
            updatePreview({
              ...emptyCameraPreview(),
              phase: 'error',
              cameraBindingId,
              message: `连续预览失败：${error instanceof Error ? error.message : '帧读取失败。'}`
            });
          }
          return;
        }
      }
    } finally {
      previewFrameLoopCount = Math.max(0, previewFrameLoopCount - 1);
      refreshPreviewDiagnostics();
    }
  }

  function isCurrent(operationGeneration: number, controller: AbortController): boolean {
    return !disposed && generation === operationGeneration && readController === controller;
  }

  function setError(error: unknown, operationGeneration: number, controller: AbortController): void {
    if (!isCurrent(operationGeneration, controller) || isAbort(error)) return;
    const projection = errorProjection(error);
    state.error = projection;
    state.phase = projection.code === 'forbidden' ? 'forbidden' : 'error';
    state.message = projection.publicMessage;
    state.generation = operationGeneration;
    state.started = true;
  }

  function syncUnknownOutcomeProjection(): void {
    state.unknownOutcomeKeys = Object.freeze([...unknownOutcomeKeys]);
  }

  function markUnknownOutcome(key: SettingsAuthorityReconcileKey): void {
    if (unknownOutcomeKeys.has(key)) return;
    unknownOutcomeKeys.add(key);
    syncUnknownOutcomeProjection();
  }

  function reconcileUnknownOutcome(key: SettingsAuthorityReconcileKey): void {
    if (!unknownOutcomeKeys.delete(key)) return;
    syncUnknownOutcomeProjection();
  }

  function reconcileAiModelAuthority(): void {
    reconcileUnknownOutcome('ai-models');
    for (const key of [...unknownOutcomeKeys]) {
      if (key.startsWith('ai-model-test:')) reconcileUnknownOutcome(key);
    }
  }

  async function finishAiModelTest(
    result: SettingsWriteResult<AiModelConnectionTestProjectionV1>,
    modelId: string
  ): Promise<SettingsWriteResult<AiModelConnectionTestProjectionV1>> {
    if (result.status !== 'completed') return result;
    const reread = await owner.readAiModels();
    if (reread.status !== 'completed') {
      return unknownAfterAuthorityRead<AiModelConnectionTestProjectionV1>(
        'ai-model',
        result.operationKind ?? 'write',
        result.generation,
        reread as SettingsWriteResult<unknown>,
        'AI 模型连接测试已完成，但模型状态重新读取失败，操作结果未知。',
        `ai-model-test:${modelId}`
      );
    }
    return result;
  }

  function unsupportedReconcile(
    section: SettingsSection,
    message: string
  ): SettingsWriteResult<unknown> {
    return Object.freeze({
      status: 'failed',
      section,
      generation: writes.diagnostics().generation,
      operationKind: 'read',
      error: new SettingsUnknownOutcomeError(new Error(message), 'read'),
      message
    });
  }

  function reconcileResult<T>(result: SettingsWriteResult<T>): SettingsWriteResult<unknown> {
    return result as SettingsWriteResult<unknown>;
  }

  function unknownAfterAuthorityRead<T>(
    section: SettingsSection,
    operationKind: SettingsOperationKind,
    generation: number,
    result: SettingsWriteResult<unknown>,
    message: string,
    reconcileKey: SettingsAuthorityReconcileKey = section === 'station'
      ? 'station-communication'
      : 'ai-models'
  ): SettingsWriteResult<T> {
    markUnknownOutcome(reconcileKey);
    const originalError = result.status === 'failed'
      ? result.error
      : new Error('服务端状态重新读取未完成。');
    const error = new SettingsUnknownOutcomeError(originalError, operationKind);
    return Object.freeze({
      status: 'failed',
      section,
      generation,
      operationKind,
      error,
      message
    });
  }

  async function finishAiMutation(
    result: SettingsWriteResult<AiModelMutationResponseV1>,
    fallbackModelId: string | null
  ): Promise<SettingsWriteResult<SettingsAiModelsMutationResultV1>> {
    if (result.status !== 'completed') return result as unknown as SettingsWriteResult<SettingsAiModelsMutationResultV1>;
    const reread = await owner.readAiModels();
    if (reread.status !== 'completed') {
      return unknownAfterAuthorityRead<SettingsAiModelsMutationResultV1>(
        'ai-model',
        result.operationKind ?? 'write',
        result.generation,
        reread as SettingsWriteResult<unknown>,
        'AI 模型操作已提交，但重新读取服务端模型状态失败；结果未知。'
      );
    }
    return Object.freeze({
      ...result,
      value: Object.freeze({
        message: result.value.message,
        modelId: result.value.id ?? fallbackModelId,
        projection: reread.value
      })
    });
  }

  function previewResourcesAreIdle(): boolean {
    return previewController === undefined &&
      previewSessionId === null &&
      previewObjectUrl === null &&
      previewFrameLoopCount === 0;
  }

  const owner: SettingsOwner = Object.freeze({
    projection: readonly(state),
    writes,
    async start(): Promise<void> {
      if (disposed || state.started) return;
      state.started = true;
      await owner.refresh();
    },
    async reconcileAuthority(key: SettingsAuthorityReconcileKey): Promise<SettingsWriteResult<unknown>> {
      if (disposed) {
        return Object.freeze({
          status: 'disposed',
          section: 'general',
          generation: writes.diagnostics().generation,
          operationKind: 'read',
          message: '设置页已卸载，无法继续核对。'
        });
      }

      if (key === 'generic-settings') {
        // `/api/settings` is the page-level authority read. It is deliberately
        // not exposed as a normal section operation because the endpoint is
        // route-scoped in the access matrix. Reconcile it through the shared
        // coordinator so an unknown write can only clear after a decoded read.
        const result = await writes.enqueue(
          'general',
          context => adapter.readGenericProjection(context.signal),
          'read'
        );
        if (result.status === 'completed' && !disposed) {
          state.settings = result.value;
          state.phase = 'ready';
          state.error = null;
          state.message = '已重新读取服务端设置状态；通用设置未知结果已完成核对。';
          reconcileUnknownOutcome(key);
        }
        return reconcileResult(result);
      }

      if (key === 'plc-settings') return reconcileResult(await owner.readPlcSettings());
      if (key === 'plc-mappings') return reconcileResult(await owner.readPlcMappings());
      if (key === 'tcp-profiles') return reconcileResult(await owner.readTcpProfiles());
      if (key === 'camera-bindings') return reconcileResult(await owner.readCameraBindings());
      if (key === 'station-communication') return reconcileResult(await owner.readStationCommunication());
      if (key === 'ai-models') return reconcileResult(await owner.readAiModels());
      if (key.startsWith('ai-model-test:')) return reconcileResult(await owner.readAiModels());
      if (key === 'users') return reconcileResult(await owner.readUsers());
      if (key === 'camera-preview') {
        return reconcileResult(await owner.stopCameraPreview('重新核对相机预览会话'));
      }
      if (key === 'change-password') {
        return unsupportedReconcile(
          'security',
          '修改密码只能由现有身份验证流程的会话失效结果确认。'
        );
      }
      if (key === 'database-backup') {
        return unsupportedReconcile(
          'database',
          '数据库备份没有可确认结果的读取合同；普通数据库状态读取不会清除结果未知状态。'
        );
      }
      if (key.startsWith('tcp-runtime:')) {
        const profileId = key.slice('tcp-runtime:'.length).trim();
        if (!profileId) return unsupportedReconcile('tcp', 'TCP 配置缺少可核对的连接身份。');
        const statusResult = await owner.readTcpStatus(profileId);
        if (statusResult.status === 'completed') return reconcileResult(statusResult);
        const framesResult = await owner.readTcpFrames(profileId);
        return reconcileResult(framesResult);
      }

      return unsupportedReconcile('general', '当前设置操作没有可用的服务端核对方式。');
    },
    recordChangePasswordSessionResult(sessionInvalidated: boolean, outcomeUnknown = false): void {
      if (sessionInvalidated) {
        reconcileUnknownOutcome('change-password');
      } else if (outcomeUnknown) {
        markUnknownOutcome('change-password');
      }
    },
    async refresh(): Promise<boolean> {
      if (disposed) return false;
      syncPanelState();
      if (unknownOutcomeKeys.size > 0 || state.pendingSectionCount > 0 || state.dirtySectionCount > 0) {
        state.message = unknownOutcomeKeys.size > 0
          ? '设置操作结果未知；请先读取对应服务端状态。'
          : state.pendingSectionCount > 0
            ? '设置操作仍在执行；完成前不能覆盖当前状态。'
            : '设置存在未保存草稿；请先保存或放弃草稿。';
        return false;
      }
      const access = evaluateSettingsRouteAccess(role);
      if (!access.allowed) {
        generation += 1;
        void stopPreviewInternal('设置访问权限已变化，预览已停止。');
        readController?.abort('settings-route-forbidden');
        readController = undefined;
        writes.cancel(undefined, 'settings-route-forbidden');
        state.phase = 'forbidden';
        state.error = Object.freeze({
          code: 'forbidden', publicMessage: '当前账户无权进入设置。', policy: 'settings-route', issues: Object.freeze([])
        });
        state.message = state.error.publicMessage;
        state.generation = generation;
        state.started = true;
        return false;
      }

      const operationGeneration = ++generation;
      writes.invalidate('settings-refresh');
      const controller = new AbortController();
      readController?.abort('settings-read-superseded');
      readController = controller;
      state.phase = 'loading';
      state.error = null;
      state.message = '正在读取服务端设置状态。';
      state.generation = operationGeneration;
      state.started = true;
      try {
        const projection = await adapter.readGenericProjection(controller.signal);
        if (!isCurrent(operationGeneration, controller)) return false;
        state.settings = projection;
        state.phase = 'ready';
        state.message = projection.safeSubset
          ? '已读取服务端安全子集；受限配置分组由后端权限决定。'
          : '已读取服务端设置状态；无权访问的配置区域已隔离。';
        state.error = null;
        reconcileUnknownOutcome('generic-settings');
        return true;
      } catch (error) {
        setError(error, operationGeneration, controller);
        return false;
      } finally {
        if (readController === controller) readController = undefined;
      }
    },
    invalidate(reason = 'settings-owner-invalidated'): void {
      if (disposed) return;
      generation += 1;
      void stopPreviewInternal('设置状态已失效，预览已停止。');
      readController?.abort(reason);
      readController = undefined;
      writes.invalidate(reason);
      state.phase = 'stale';
      state.generation = generation;
      state.message = '设置状态已过期，请重新读取服务端状态。';
      state.error = Object.freeze({
        code: 'unknown-outcome', publicMessage: `${reason}；请重新读取服务端状态。`, policy: null, issues: Object.freeze([])
      });
    },
    enqueueEndpointOperation<T>(
      section: SettingsSection,
      endpointId: string,
      task: SettingsEndpointTask<T>,
      operationKindOverride?: SettingsOperationKind,
      reconcileKeyOverride?: SettingsAuthorityReconcileKey
    ): Promise<SettingsWriteResult<T>> {
      const operationKind = operationKindOverride ?? operationKindForEndpoint(endpointId);
      if (disposed) {
        return Promise.resolve(Object.freeze({
          status: 'disposed', section, generation: writes.diagnostics().generation,
          operationKind,
          message: '设置页已卸载。'
        }));
      }
      const access = evaluateSettingsEndpointAccess(endpointId, section, role);
      if (!access.allowed || !access.endpoint) {
        return Promise.resolve(Object.freeze({
          status: 'forbidden', section, generation: writes.diagnostics().generation,
          operationKind,
          message: endpointAccessMessage(access.reason)
        }));
      }
      return writes.enqueue(
        section,
        context => task(Object.freeze({ ...context, endpoint: access.endpoint! })),
        operationKind
      ).then(result => {
        const mutation = result.operationKind !== undefined && result.operationKind !== 'read';
        const reconcileKey = reconcileKeyForEndpoint(endpointId, operationKind, reconcileKeyOverride);
        const unknown = mutation && (
          (result.status === 'failed' && projectSettingsOperationFailure(result.error, result.operationKind).code === 'unknown-outcome') ||
          result.status === 'cancelled' || result.status === 'stale' || result.status === 'disposed'
        );
        if (unknown && reconcileKey) markUnknownOutcome(reconcileKey);
        return result;
      });
    },
    registerPanelState(section: SettingsSection, readState: () => SettingsPanelState): () => void {
      if (disposed) return () => undefined;
      const readers = panelStates.get(section) ?? new Set<() => SettingsPanelState>();
      readers.add(readState);
      panelStates.set(section, readers);
      syncPanelState();
      return () => {
        const current = panelStates.get(section);
        if (current?.delete(readState)) {
          if (current.size === 0) panelStates.delete(section);
          syncPanelState();
        }
      };
    },
    refreshPanelState(): void {
      if (!disposed) syncPanelState();
    },
    leaveProtection(): SettingsLeaveProtectionKind | null {
      syncPanelState();
      if (unknownOutcomeKeys.size > 0) return 'settings-unknown';
      if (state.pendingSectionCount > 0) return 'settings-pending';
      if (state.dirtySectionCount > 0) return 'settings-draft';
      return null;
    },
    async saveGenericSection(
      section: GenericSettingsSection,
      value: Readonly<Record<string, unknown>>
    ): Promise<SettingsWriteResult<SettingsWriteResponseV1>> {
      const result = await owner.enqueueEndpointOperation(
        section,
        'settings.write',
        context => adapter.writeGenericSection(section, value, context.signal)
      );
      if (result.status === 'completed' && !disposed) {
        state.settings = result.value.config;
        state.phase = 'ready';
        state.error = null;
        state.message = '设置分组已保存；服务端状态已更新，运行时重载要求由后端决定。';
      }
      return result;
    },
    async readStationCommunication(): Promise<SettingsWriteResult<StationCommunicationProjectionV1>> {
      const result = await owner.enqueueEndpointOperation(
        'station',
        'station.settings.read',
        context => adapter.readStationCommunication(context.signal),
        'read'
      );
      if (result.status === 'completed') {
        state.station = result.value;
        reconcileUnknownOutcome('station-communication');
      }
      return result;
    },
    async saveStationCommunication(
      request: StationCommunicationSettingsUpdateRequestV1
    ): Promise<SettingsWriteResult<StationCommunicationProjectionV1>> {
      const result = await owner.enqueueEndpointOperation(
        'station',
        'station.settings.write',
        context => adapter.writeStationCommunication(request, context.signal),
        'write',
        'station-communication'
      );
      if (result.status !== 'completed') return result;
      const reread = await owner.readStationCommunication();
      if (reread.status !== 'completed') {
        return unknownAfterAuthorityRead<StationCommunicationProjectionV1>(
          'station',
          result.operationKind ?? 'write',
          result.generation,
          reread as SettingsWriteResult<unknown>,
          '工作站配置已提交，但服务端工作站状态重新读取失败；结果未知。'
        );
      }
      return Object.freeze({ ...result, value: reread.value });
    },
    async runStationTokenOperation(
      operation: StationTokenOperationNameV1
    ): Promise<SettingsWriteResult<StationTokenOperationV1>> {
      const result = await owner.enqueueEndpointOperation(
        'station',
        'station.token',
        context => adapter.stationToken(operation, context.signal),
        'write',
        'station-communication'
      );
      if (result.status !== 'completed') return result;
      const reread = await owner.readStationCommunication();
      if (reread.status !== 'completed') {
        return unknownAfterAuthorityRead<StationTokenOperationV1>(
          'station',
          result.operationKind ?? 'write',
          result.generation,
          reread as SettingsWriteResult<unknown>,
          '工作站访问令牌操作已提交，但服务端工作站状态重新读取失败；结果未知。'
        );
      }
      return Object.freeze({
        ...result,
        value: Object.freeze({
          ...result.value,
          settings: reread.value
        })
      });
    },
    async readAiModels(): Promise<SettingsWriteResult<AiModelsProjectionV1>> {
      const result = await owner.enqueueEndpointOperation(
        'ai-model',
        'ai.models.read',
        context => adapter.readAiModels(context.signal),
        'read'
      );
      if (result.status === 'completed') {
        state.aiModels = result.value;
        reconcileAiModelAuthority();
      }
      return result;
    },
    createAiModel(
      request: AiModelMutationRequestV1
    ): Promise<SettingsWriteResult<SettingsAiModelsMutationResultV1>> {
      return owner.enqueueEndpointOperation(
        'ai-model',
        'ai.models.create',
        context => adapter.createAiModel(request, context.signal),
        'write',
        'ai-models'
      ).then(result => finishAiMutation(result, null));
    },
    updateAiModel(
      id: string,
      request: AiModelMutationRequestV1
    ): Promise<SettingsWriteResult<SettingsAiModelsMutationResultV1>> {
      return owner.enqueueEndpointOperation(
        'ai-model',
        'ai.models.update',
        context => adapter.updateAiModel(id, request, context.signal),
        'write',
        'ai-models'
      ).then(result => finishAiMutation(result, id));
    },
    deleteAiModel(id: string): Promise<SettingsWriteResult<SettingsAiModelsMutationResultV1>> {
      return owner.enqueueEndpointOperation(
        'ai-model',
        'ai.models.delete',
        context => adapter.deleteAiModel(id, context.signal),
        'write',
        'ai-models'
      ).then(result => finishAiMutation(result, id));
    },
    activateAiModel(id: string): Promise<SettingsWriteResult<SettingsAiModelsMutationResultV1>> {
      return owner.enqueueEndpointOperation(
        'ai-model',
        'ai.models.activate',
        context => adapter.activateAiModel(id, context.signal),
        'write',
        'ai-models'
      ).then(result => finishAiMutation(result, id));
    },
    setAiModelDefault(
      id: string,
      role: 'planner' | 'shadow-eval'
    ): Promise<SettingsWriteResult<SettingsAiModelsMutationResultV1>> {
      return owner.enqueueEndpointOperation(
        'ai-model',
        role === 'planner' ? 'ai.models.default-planner' : 'ai.models.default-shadow-eval',
        context => adapter.setAiModelDefault(id, role, context.signal),
        'write',
        'ai-models'
      ).then(result => finishAiMutation(result, id));
    },
    testAiModel(id: string): Promise<SettingsWriteResult<AiModelConnectionTestProjectionV1>> {
      return owner.enqueueEndpointOperation(
        'ai-model',
        'ai.models.test',
        context => adapter.testAiModel(id, context.signal),
        'write',
        `ai-model-test:${id}`
      ).then(result => finishAiModelTest(result, id));
    },
    readAiReasoningSupport(
      request: AiReasoningSupportRequestV1
    ): Promise<SettingsWriteResult<AiReasoningSupportProjectionV1>> {
      return owner.enqueueEndpointOperation(
        'ai-model',
        'ai.reasoning-support',
        context => adapter.readAiReasoningSupport(request, context.signal),
        'read'
      );
    },
    readDiskUsage(path?: string): Promise<SettingsWriteResult<SettingsDiskUsageProjectionV1>> {
      return owner.enqueueEndpointOperation(
        'storage',
        'settings.disk-usage.read',
        context => adapter.readDiskUsage(path, context.signal)
      );
    },
    readDatabaseStatus(): Promise<SettingsWriteResult<SettingsDatabaseStatusProjectionV1>> {
      return owner.enqueueEndpointOperation(
        'database',
        'settings.database.status.read',
        context => adapter.readDatabaseStatus(context.signal)
      );
    },
    backupDatabase(): Promise<SettingsWriteResult<SettingsDatabaseBackupProjectionV1>> {
      return owner.enqueueEndpointOperation(
        'database',
        'settings.database.backup',
        context => adapter.backupDatabase(context.signal)
      );
    },
    changePassword(
      request: SettingsChangePasswordRequest
    ): Promise<SettingsWriteResult<SettingsAccountOperationResponseV1>> {
      return owner.enqueueEndpointOperation(
        'security',
        'auth.change-password',
        context => adapter.changePassword(request, context.signal)
      );
    },
    readUsers(): Promise<SettingsWriteResult<SettingsUsersProjectionV1>> {
      return owner.enqueueEndpointOperation(
        'security',
        'users.read',
        context => adapter.readUsers(context.signal)
      ).then(result => {
        if (result.status === 'completed') reconcileUnknownOutcome('users');
        return result;
      });
    },
    createUser(
      request: SettingsCreateUserRequest
    ): Promise<SettingsWriteResult<SettingsUserProjectionV1>> {
      return owner.enqueueEndpointOperation(
        'security',
        'users.create',
        context => adapter.createUser(request, context.signal)
      );
    },
    updateUser(
      id: string,
      request: SettingsUpdateUserRequest
    ): Promise<SettingsWriteResult<SettingsUserProjectionV1>> {
      return owner.enqueueEndpointOperation(
        'security',
        'users.update',
        context => adapter.updateUser(id, request, context.signal)
      );
    },
    deleteUser(id: string): Promise<SettingsWriteResult<void>> {
      return owner.enqueueEndpointOperation(
        'security',
        'users.delete',
        context => adapter.deleteUser(id, context.signal)
      );
    },
    resetUserPassword(
      id: string,
      newPassword: string
    ): Promise<SettingsWriteResult<SettingsAccountOperationResponseV1>> {
      return owner.enqueueEndpointOperation(
        'security',
        'users.reset-password',
        context => adapter.resetUserPassword(id, newPassword, context.signal)
      );
    },
    async readPlcSettings(): Promise<SettingsWriteResult<PlcSettingsResponseV1>> {
      const result = await owner.enqueueEndpointOperation(
        'plc',
        'plc.settings.read',
        context => adapter.readPlcSettings(context.signal)
      );
      const settings = result.status === 'completed' ? result.value.settings : null;
      if (settings) {
        updateDevice(current => Object.freeze({
          ...current,
          plcSettings: settings,
          plcMappings: settings[settings.activeProtocol.toLowerCase() as 's7' | 'mc' | 'fins'].mappings
        }));
        reconcileUnknownOutcome('plc-settings');
        reconcileUnknownOutcome('plc-mappings');
      }
      return result;
    },
    async readPlcMappings(): Promise<SettingsWriteResult<PlcMappingsResponseV1>> {
      const result = await owner.enqueueEndpointOperation(
        'plc',
        'plc.mappings.read',
        context => adapter.readPlcMappings(context.signal)
      );
      if (result.status === 'completed') {
        updateDevice(current => Object.freeze({ ...current, plcMappings: result.value.mappings }));
        reconcileUnknownOutcome('plc-mappings');
      }
      return result;
    },
    async savePlcSettings(settings: PlcSettingsV1): Promise<SettingsWriteResult<PlcSettingsResponseV1>> {
      const result = await owner.enqueueEndpointOperation(
        'plc',
        'plc.settings.write',
        context => adapter.writePlcSettings(settings, context.signal)
      );
      const savedSettings = result.status === 'completed' ? result.value.settings : null;
      if (savedSettings) {
        updateDevice(current => Object.freeze({
          ...current,
          plcSettings: savedSettings,
          plcMappings: savedSettings[savedSettings.activeProtocol.toLowerCase() as 's7' | 'mc' | 'fins'].mappings
        }));
      }
      return result;
    },
    async savePlcMappings(mappings: readonly PlcMappingV1[]): Promise<SettingsWriteResult<PlcMappingsResponseV1>> {
      const result = await owner.enqueueEndpointOperation(
        'plc',
        'plc.mappings.write',
        context => adapter.writePlcMappings(mappings, context.signal)
      );
      if (result.status === 'completed') {
        updateDevice(current => Object.freeze({ ...current, plcMappings: result.value.mappings }));
      }
      return result;
    },
    testPlcConnection(request: PlcTestConnectionRequestV1): Promise<SettingsWriteResult<PlcTestConnectionResponseV1>> {
      return owner.enqueueEndpointOperation(
        'plc',
        'plc.test-connection',
        context => adapter.testPlcConnection(request, context.signal)
      );
    },
    async readTcpProfiles(): Promise<SettingsWriteResult<TcpProfilesResponseV1>> {
      const result = await owner.enqueueEndpointOperation(
        'tcp',
        'tcp.profiles.read',
        context => adapter.readTcpProfiles(context.signal)
      );
      if (result.status === 'completed') {
        updateDevice(current => Object.freeze({ ...current, tcpProfiles: result.value.profiles }));
        reconcileUnknownOutcome('tcp-profiles');
      }
      return result;
    },
    async saveTcpProfiles(profiles: readonly TcpProfileV1[]): Promise<SettingsWriteResult<TcpProfilesResponseV1>> {
      const result = await owner.enqueueEndpointOperation(
        'tcp',
        'tcp.profiles.write',
        context => adapter.writeTcpProfiles(profiles, context.signal)
      );
      if (result.status === 'completed') {
        updateDevice(current => Object.freeze({ ...current, tcpProfiles: result.value.profiles }));
      }
      return result;
    },
    async connectTcp(profileId: string): Promise<SettingsWriteResult<TcpRuntimeResponseV1>> {
      const result = await owner.enqueueEndpointOperation(
        'tcp',
        'tcp.runtime',
        context => adapter.connectTcp(profileId, context.signal),
        undefined,
        `tcp-runtime:${profileId}`
      );
      if (result.status === 'completed' && result.value.status) {
        updateDevice(current => Object.freeze({
          ...current,
          tcpStatuses: Object.freeze({ ...current.tcpStatuses, [profileId]: result.value.status })
        }));
      }
      return result;
    },
    async disconnectTcp(profileId: string): Promise<SettingsWriteResult<TcpRuntimeResponseV1>> {
      const result = await owner.enqueueEndpointOperation(
        'tcp',
        'tcp.runtime',
        context => adapter.disconnectTcp(profileId, context.signal),
        undefined,
        `tcp-runtime:${profileId}`
      );
      if (result.status === 'completed' && result.value.status) {
        updateDevice(current => Object.freeze({
          ...current,
          tcpStatuses: Object.freeze({ ...current.tcpStatuses, [profileId]: result.value.status })
        }));
      }
      return result;
    },
    async startTcpServer(profileId: string): Promise<SettingsWriteResult<TcpRuntimeResponseV1>> {
      const result = await owner.enqueueEndpointOperation(
        'tcp',
        'tcp.runtime',
        context => adapter.startTcpServer(profileId, context.signal),
        undefined,
        `tcp-runtime:${profileId}`
      );
      if (result.status === 'completed' && result.value.status) {
        updateDevice(current => Object.freeze({
          ...current,
          tcpStatuses: Object.freeze({ ...current.tcpStatuses, [profileId]: result.value.status })
        }));
      }
      return result;
    },
    async stopTcpServer(profileId: string): Promise<SettingsWriteResult<TcpRuntimeResponseV1>> {
      const result = await owner.enqueueEndpointOperation(
        'tcp',
        'tcp.runtime',
        context => adapter.stopTcpServer(profileId, context.signal),
        undefined,
        `tcp-runtime:${profileId}`
      );
      if (result.status === 'completed' && result.value.status) {
        updateDevice(current => Object.freeze({
          ...current,
          tcpStatuses: Object.freeze({ ...current.tcpStatuses, [profileId]: result.value.status })
        }));
      }
      return result;
    },
    async sendTcp(profileId: string, request: TcpSendRequestV1): Promise<SettingsWriteResult<TcpRuntimeResponseV1>> {
      const result = await owner.enqueueEndpointOperation(
        'tcp',
        'tcp.runtime',
        context => adapter.sendTcp(profileId, request, context.signal),
        undefined,
        `tcp-runtime:${profileId}`
      );
      if (result.status === 'completed' && result.value.status) {
        updateDevice(current => Object.freeze({
          ...current,
          tcpStatuses: Object.freeze({ ...current.tcpStatuses, [profileId]: result.value.status })
        }));
      }
      return result;
    },
    async readTcpStatus(profileId: string): Promise<SettingsWriteResult<TcpStatusResponseV1>> {
      const result = await owner.enqueueEndpointOperation(
        'tcp',
        'tcp.runtime',
        context => adapter.readTcpStatus(profileId, context.signal),
        'read'
      );
      if (result.status === 'completed') {
        updateDevice(current => Object.freeze({
          ...current,
          tcpStatuses: Object.freeze({ ...current.tcpStatuses, [profileId]: result.value.status })
        }));
        reconcileUnknownOutcome(`tcp-runtime:${profileId}`);
      }
      return result;
    },
    async readTcpFrames(profileId: string): Promise<SettingsWriteResult<TcpFramesResponseV1>> {
      const result = await owner.enqueueEndpointOperation(
        'tcp',
        'tcp.runtime',
        context => adapter.readTcpFrames(profileId, context.signal),
        'read'
      );
      if (result.status === 'completed') {
        updateDevice(current => Object.freeze({
          ...current,
          tcpFrames: Object.freeze({ ...current.tcpFrames, [profileId]: result.value.frames })
        }));
        reconcileUnknownOutcome(`tcp-runtime:${profileId}`);
      }
      return result;
    },
    async clearTcpFrames(profileId: string): Promise<SettingsWriteResult<TcpClearFramesResponseV1>> {
      const result = await owner.enqueueEndpointOperation(
        'tcp',
        'tcp.runtime',
        context => adapter.clearTcpFrames(profileId, context.signal),
        undefined,
        `tcp-runtime:${profileId}`
      );
      if (result.status === 'completed' && result.value.success) {
        updateDevice(current => Object.freeze({
          ...current,
          tcpFrames: Object.freeze({ ...current.tcpFrames, [profileId]: Object.freeze([]) })
        }));
      }
      return result;
    },
    discoverCameras(provider: CameraDiscoveryProviderV1): Promise<SettingsWriteResult<CameraDiscoveryResponseV1>> {
      const endpointId = provider === 'all'
        ? 'camera.discovery.all'
        : provider === 'huaray' ? 'camera.discovery.huaray' : 'camera.discovery.hikvision';
      return owner.enqueueEndpointOperation(
        'camera',
        endpointId,
        async context => {
          const result = await adapter.discoverCameras(provider, context.signal);
          if (result.devices.length > 0) updateDevice(current => Object.freeze({ ...current, cameraDiscovery: result }));
          else updateDevice(current => Object.freeze({ ...current, cameraDiscovery: result }));
          return result;
        }
      );
    },
    async readCameraBindings(): Promise<SettingsWriteResult<CameraBindingsResponseV1>> {
      const result = await owner.enqueueEndpointOperation(
        'camera',
        'camera.bindings.read',
        context => adapter.readCameraBindings(context.signal)
      );
      if (result.status === 'completed') {
        updateDevice(current => Object.freeze({
          ...current,
          cameraBindings: result.value.bindings,
          activeCameraId: result.value.activeCameraId
        }));
        reconcileUnknownOutcome('camera-bindings');
      }
      return result;
    },
    async saveCameraBindings(
      bindings: readonly CameraBindingV1[],
      activeCameraId: string
    ): Promise<SettingsWriteResult<SettingsDeviceOperationResult>> {
      const result = await owner.enqueueEndpointOperation(
        'camera',
        'camera.bindings.write',
        context => adapter.writeCameraBindings(bindings, activeCameraId, context.signal)
      );
      if (result.status !== 'completed') return result;
      if (!result.value.success) return Object.freeze({ ...result, value: operationResult(false, result.value.message) });
      const reread = await owner.readCameraBindings();
      if (reread.status !== 'completed') {
        markUnknownOutcome('camera-bindings');
        const rereadError = reread.status === 'failed' ? reread.error : new Error(reread.message);
        const unknown = new SettingsUnknownOutcomeError(rereadError, 'write');
        return Object.freeze({
          status: 'failed',
          section: 'camera',
          generation: result.generation,
          operationKind: 'write',
          error: unknown,
          message: '相机绑定已提交，但服务端重新读取失败；请重新读取后确认结果。'
        });
      }
      updateDevice(current => Object.freeze({
        ...current,
        cameraBindings: reread.value.bindings,
        activeCameraId: reread.value.activeCameraId
      }));
      return Object.freeze({
        ...result,
        value: operationResult(true, '相机绑定已保存，并已重新读取服务端绑定状态。')
      });
    },
    readTriggerDiagnostics(): Promise<SettingsWriteResult<TriggerDiagnosticsV1>> {
      return owner.enqueueEndpointOperation(
        'camera',
        'trigger-input.diagnostics.read',
        context => adapter.readTriggerDiagnostics(context.signal)
      ).then(result => {
        if (result.status === 'completed') updateDevice(current => Object.freeze({ ...current, triggerDiagnostics: result.value }));
        return result;
      });
    },
    readSerialPhotoelectricPorts(): Promise<SettingsWriteResult<readonly SerialPhotoelectricPortV1[]>> {
      return owner.enqueueEndpointOperation(
        'camera',
        'trigger-input.serial-ports.read',
        context => adapter.readSerialPhotoelectricPorts(context.signal)
      ).then(result => {
        if (result.status === 'completed') updateDevice(current => Object.freeze({ ...current, serialPorts: result.value }));
        return result;
      });
    },
    async testSerialPhotoelectric(request: SerialPhotoelectricTestRequestV1): Promise<SettingsWriteResult<SettingsDeviceOperationResult>> {
      const result = await owner.enqueueEndpointOperation(
        'camera',
        'trigger-input.serial-test',
        context => adapter.testSerialPhotoelectric(request, context.signal)
      );
      if (result.status !== 'completed') return result;
      return Object.freeze({ ...result, value: operationResult(true, result.value.message) });
    },
    async learnEnterPhotoelectricDevice(timeoutMs: number): Promise<SettingsWriteResult<SettingsDeviceOperationResult>> {
      const result = await owner.enqueueEndpointOperation(
        'camera',
        'trigger-input.enter-learn',
        context => adapter.learnEnterPhotoelectricDevice(timeoutMs, context.signal)
      );
      if (result.status !== 'completed') return result;
      return Object.freeze({
        ...result,
        value: operationResult(true, `已识别 Enter 光电设备：${result.value.deviceId}。`)
      });
    },
    async captureSoftTrigger(cameraBindingId: string): Promise<SettingsWriteResult<SettingsDeviceOperationResult>> {
      const result = await owner.enqueueEndpointOperation(
        'camera',
        'camera.soft-trigger-capture',
        async context => {
          await stopPreviewInternal('已切换到单帧抓图。');
          const captured = await adapter.softTriggerCapture(cameraBindingId, context.signal);
          const accepted = await setPreviewBlob(captured.blob.blob, captured.blob.contentType, previewGeneration, {
            phase: 'captured',
            cameraBindingId: captured.cameraBindingId,
            triggerMode: captured.triggerMode,
            triggerSource: captured.triggerSource,
            width: captured.width,
            height: captured.height,
            message: '单帧已捕获，仅用于调试预览。'
          });
          return operationResult(accepted, accepted ? '单帧已捕获，仅用于调试预览。' : '单帧已过期，未更新预览。');
        }
      );
      return result;
    },
    async startCameraPreview(cameraBindingId: string): Promise<SettingsWriteResult<SettingsDeviceOperationResult>> {
      const result = await owner.enqueueEndpointOperation(
        'camera',
        'camera.preview.start',
        async context => {
          await stopPreviewInternal('已停止上一相机预览。');
          const session = await adapter.startContinuousPreview(cameraBindingId, context.signal);
          if (context.signal.aborted || disposed) {
            await adapter.stopContinuousPreview(session.sessionId).catch(() => undefined);
            throw new DOMException('连续预览启动结果已过期。', 'AbortError');
          }
          previewGeneration += 1;
          const operationGeneration = previewGeneration;
          const controller = new AbortController();
          previewController = controller;
          previewSessionId = session.sessionId;
          updatePreview({
            ...emptyCameraPreview(),
            phase: 'running',
            sessionId: session.sessionId,
            cameraBindingId: session.cameraBindingId,
            triggerMode: session.triggerMode,
            message: '连续预览已启动，正在等待帧。'
          });
          const loop = runPreviewLoop(
            session.sessionId,
            session.cameraBindingId,
            operationGeneration,
            controller,
            session.triggerMode
          );
          const trackedLoop = loop.finally(() => {
            if (previewFrameLoopPromise === trackedLoop) previewFrameLoopPromise = undefined;
          });
          previewFrameLoopPromise = trackedLoop;
          return operationResult(true, '连续预览已启动。');
        }
      );
      return result;
    },
    async stopCameraPreview(reason = '相机预览已停止。'): Promise<SettingsWriteResult<void>> {
      writes.cancel('camera', reason);
      const sessionId = await stopPreviewInternal(reason, false);
      const result = await owner.enqueueEndpointOperation(
        'camera',
        'camera.preview.stop',
        async context => {
          if (sessionId) await adapter.stopContinuousPreview(sessionId, context.signal);
        }
      );
      if (result.status === 'completed' && previewResourcesAreIdle()) {
        if (sessionId || !unknownOutcomeKeys.has('camera-preview')) {
          reconcileUnknownOutcome('camera-preview');
        }
      }
      return result;
    },
    diagnostics(): SettingsOwnerDiagnostics {
      const write = writes.diagnostics();
      return Object.freeze({
        activeSettingsOwnerCount,
        activeAbortControllerCount: (readController ? 1 : 0) + write.activeAbortControllerCount,
        inFlightReadCount: readController ? 1 : 0,
        dirtySectionCount: state.dirtySectionCount,
        pendingSectionCount: state.pendingSectionCount,
        write,
        preview: previewDiagnostics(),
        disposed
      });
    },
    dispose(reason = 'settings-owner-disposed'): void {
      if (disposed) return;
      disposed = true;
      generation += 1;
      void stopPreviewInternal('设置页已释放，预览已停止。');
      readController?.abort(reason);
      readController = undefined;
      writes.dispose(reason);
      clearPreviewObjectUrl();
      panelStates.clear();
      unknownOutcomeKeys.clear();
      state.unknownOutcomeKeys = Object.freeze([]);
      state.device = emptyDeviceProjection();
      state.dirtySectionCount = 0;
      state.pendingSectionCount = 0;
      state.phase = 'disposed';
      state.generation = generation;
      state.message = '设置页已释放。';
      state.error = null;
      if (activeSettingsOwnerToken === ownerToken) {
        activeSettingsOwnerToken = undefined;
        activeSettingsOwnerCount = Math.max(0, activeSettingsOwnerCount - 1);
      }
    }
  });

  return owner;
}
