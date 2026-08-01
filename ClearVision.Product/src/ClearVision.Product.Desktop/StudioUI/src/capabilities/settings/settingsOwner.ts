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
  SettingsContractDecodeError,
  type SettingsErrorCode,
  type SettingsEndpointAccessReason,
  type SettingsRole,
  type SettingsSection,
  type SettingsWriteCoordinatorDiagnostics,
  type SettingsEndpointTask,
  type SettingsWriteResult,
} from './contracts';
import {
  createSettingsApiAdapter,
  type SettingsApiAdapter,
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
  type SettingsDatabaseBackupProjectionV1,
  type SettingsDatabaseStatusProjectionV1,
  type SettingsDiskUsageProjectionV1,
  type SettingsErrorProjectionV1,
  type SettingsProjectionV1,
  type SettingsUserProjectionV1,
  type SettingsUsersProjectionV1,
  type SettingsWriteResponseV1
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
}

export interface SettingsOwnerDiagnostics {
  readonly activeSettingsOwnerCount: number;
  readonly activeAbortControllerCount: number;
  readonly inFlightReadCount: number;
  readonly write: SettingsWriteCoordinatorDiagnostics;
  readonly disposed: boolean;
}

export interface SettingsOwner {
  readonly projection: DeepReadonly<SettingsOwnerProjection>;
  readonly writes: SettingsWriteCoordinator;
  start(): Promise<void>;
  refresh(): Promise<boolean>;
  invalidate(reason?: string): void;
  enqueueEndpointOperation<T>(
    section: SettingsSection,
    endpointId: string,
    task: SettingsEndpointTask<T>
  ): Promise<SettingsWriteResult<T>>;
  saveGenericSection(
    section: GenericSettingsSection,
    value: Readonly<Record<string, unknown>>
  ): Promise<SettingsWriteResult<SettingsWriteResponseV1>>;
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

export interface CreateSettingsOwnerOptions {
  readonly runtime: Pick<ProductRuntime, 'api'>;
  readonly role: SettingsRole;
}

export class SettingsOwnerConflictError extends Error {
  constructor() {
    super('Only one mounted Settings owner is allowed at a time.');
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
    case 'unauthorized': return '登录状态已失效，Settings owner 已停止请求。';
    case 'forbidden': return '当前账户没有 Settings 访问权限。';
    case 'not-found': return 'Settings 服务端对象不存在或当前用户不可见。';
    case 'conflict': return '服务端 Settings 状态已变化，请重新读取后继续。';
    case 'validation': return 'Settings 请求未通过服务端校验。';
    case 'abort': return 'Settings 请求已取消。';
    case 'decode': return 'Settings 服务端响应不符合已冻结合同。';
    case 'network': return '本地 Settings 服务暂时不可用。';
    case 'server': return '本地 Settings 服务发生服务端错误。';
    case 'sensitive-field': return 'Settings 响应包含禁止暴露的敏感字段。';
    case 'unsupported': return '当前 Settings 操作尚未获得合同授权。';
    case 'unknown-outcome': return 'Settings 操作结果未知，请重新读取后再决定下一步。';
    case 'unexpected-http-status': return 'Settings 请求返回了未分类的 HTTP 状态。';
  }
}

function errorProjection(error: unknown): SettingsErrorProjectionV1 {
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

export function projectSettingsOperationFailure(error: unknown): SettingsErrorProjectionV1 {
  const projection = errorProjection(error);
  if (projection.code !== 'network' && projection.code !== 'abort') return projection;
  return Object.freeze({
    code: 'unknown-outcome',
    publicMessage: '操作结果未知；请先重新读取服务端状态，再决定是否重试。',
    policy: null,
    issues: Object.freeze([])
  });
}

function isAbort(error: unknown): boolean {
  return error instanceof ApiAbortError ||
    (typeof DOMException !== 'undefined' && error instanceof DOMException && error.name === 'AbortError');
}

function endpointAccessMessage(endpointId: string, reason: SettingsEndpointAccessReason): string {
  switch (reason) {
    case 'unknown-endpoint': return `Unknown Settings endpoint is forbidden: ${endpointId}.`;
    case 'excluded-endpoint': return `Excluded Settings endpoint is forbidden: ${endpointId}.`;
    case 'section-mismatch': return `Settings endpoint ${endpointId} does not belong to the requested section.`;
    case 'route-only': return `Settings endpoint ${endpointId} is route-only and cannot be executed.`;
    case 'engineer-or-admin-required': return `Settings endpoint ${endpointId} requires Engineer or Admin permission.`;
    case 'admin-required': return `Settings endpoint ${endpointId} requires Admin permission.`;
    case 'allowed': return '';
  }
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
    message: '尚未开始相机预览。'
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
    message: 'Settings owner 尚未启动。',
    generation: 0,
    started: false,
    device: emptyDeviceProjection()
  });
  let disposed = false;
  let generation = 0;
  let readController: AbortController | undefined;
  let previewController: AbortController | undefined;
  let previewSessionId: string | null = null;
  let previewGeneration = 0;
  let previewObjectUrl: string | null = null;

  function updateDevice(update: (current: SettingsDeviceProjectionV1) => SettingsDeviceProjectionV1): void {
    if (disposed) return;
    state.device = Object.freeze(update(state.device));
  }

  function updatePreview(preview: CameraPreviewProjectionV1): void {
    updateDevice(current => Object.freeze({ ...current, preview }));
  }

  function clearPreviewObjectUrl(): void {
    revokeBlobObjectUrl(previewObjectUrl);
    previewObjectUrl = null;
  }

  async function stopPreviewInternal(message: string, stopRemote = true): Promise<string | null> {
    previewGeneration += 1;
    previewController?.abort('settings-preview-stopped');
    previewController = undefined;
    const sessionId = previewSessionId;
    previewSessionId = null;
    clearPreviewObjectUrl();
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
        await stopPreviewInternal('连续预览已停止。');
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

  const owner: SettingsOwner = Object.freeze({
    projection: readonly(state),
    writes,
    async start(): Promise<void> {
      if (disposed || state.started) return;
      state.started = true;
      await owner.refresh();
    },
    async refresh(): Promise<boolean> {
      if (disposed) return false;
      const access = evaluateSettingsRouteAccess(role);
      if (!access.allowed) {
        generation += 1;
        void stopPreviewInternal('Settings 权限已变化，预览已停止。');
        readController?.abort('settings-route-forbidden');
        readController = undefined;
        writes.cancel(undefined, 'settings-route-forbidden');
        state.phase = 'forbidden';
        state.error = Object.freeze({
          code: 'forbidden', publicMessage: '当前账户禁止进入 Settings。', policy: 'settings-route', issues: Object.freeze([])
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
      state.message = '正在读取 Settings 权威投影。';
      state.generation = operationGeneration;
      state.started = true;
      try {
        const projection = await adapter.readGenericProjection(controller.signal);
        if (!isCurrent(operationGeneration, controller)) return false;
        state.settings = projection;
        state.phase = 'ready';
        state.message = projection.safeSubset
          ? '已读取服务端 safe subset；受限 section 由后端权限决定。'
          : '已读取服务端 Settings 投影；未授权的 authority section 已隔离。';
        state.error = null;
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
      void stopPreviewInternal('Settings 投影已失效，预览已停止。');
      readController?.abort(reason);
      readController = undefined;
      writes.invalidate(reason);
      state.phase = 'stale';
      state.generation = generation;
      state.message = 'Settings 投影已过期，请重新读取服务端状态。';
      state.error = Object.freeze({
        code: 'unknown-outcome', publicMessage: `${reason}；请重新读取服务端状态。`, policy: null, issues: Object.freeze([])
      });
    },
    enqueueEndpointOperation<T>(
      section: SettingsSection,
      endpointId: string,
      task: SettingsEndpointTask<T>
    ): Promise<SettingsWriteResult<T>> {
      if (disposed) {
        return Promise.resolve(Object.freeze({
          status: 'disposed', section, generation: writes.diagnostics().generation,
          message: 'Settings owner has been disposed.'
        }));
      }
      const access = evaluateSettingsEndpointAccess(endpointId, section, role);
      if (!access.allowed || !access.endpoint) {
        return Promise.resolve(Object.freeze({
          status: 'forbidden', section, generation: writes.diagnostics().generation,
          message: endpointAccessMessage(endpointId, access.reason)
        }));
      }
      return writes.enqueue(section, context => task(Object.freeze({
        ...context,
        endpoint: access.endpoint!
      })));
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
        state.message = 'Settings section 已保存；服务端投影已更新，运行时重载要求由后端决定。';
      }
      return result;
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
      );
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
        context => adapter.connectTcp(profileId, context.signal)
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
        context => adapter.disconnectTcp(profileId, context.signal)
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
        context => adapter.startTcpServer(profileId, context.signal)
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
        context => adapter.stopTcpServer(profileId, context.signal)
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
        context => adapter.sendTcp(profileId, request, context.signal)
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
        context => adapter.readTcpStatus(profileId, context.signal)
      );
      if (result.status === 'completed') {
        updateDevice(current => Object.freeze({
          ...current,
          tcpStatuses: Object.freeze({ ...current.tcpStatuses, [profileId]: result.value.status })
        }));
      }
      return result;
    },
    async readTcpFrames(profileId: string): Promise<SettingsWriteResult<TcpFramesResponseV1>> {
      const result = await owner.enqueueEndpointOperation(
        'tcp',
        'tcp.runtime',
        context => adapter.readTcpFrames(profileId, context.signal)
      );
      if (result.status === 'completed') {
        updateDevice(current => Object.freeze({
          ...current,
          tcpFrames: Object.freeze({ ...current.tcpFrames, [profileId]: result.value.frames })
        }));
      }
      return result;
    },
    async clearTcpFrames(profileId: string): Promise<SettingsWriteResult<TcpClearFramesResponseV1>> {
      const result = await owner.enqueueEndpointOperation(
        'tcp',
        'tcp.runtime',
        context => adapter.clearTcpFrames(profileId, context.signal)
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
      if (result.value.success) {
        updateDevice(current => Object.freeze({ ...current, cameraBindings: bindings, activeCameraId }));
      }
      return Object.freeze({
        ...result,
        value: operationResult(result.value.success, result.value.message)
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
          void runPreviewLoop(session.sessionId, session.cameraBindingId, operationGeneration, controller, session.triggerMode);
          return operationResult(true, '连续预览已启动。');
        }
      );
      return result;
    },
    async stopCameraPreview(reason = '相机预览已停止。'): Promise<SettingsWriteResult<void>> {
      writes.cancel('camera', reason);
      const sessionId = await stopPreviewInternal(reason, false);
      return owner.enqueueEndpointOperation(
        'camera',
        'camera.preview.stop',
        async context => {
          if (sessionId) await adapter.stopContinuousPreview(sessionId, context.signal);
        }
      );
    },
    diagnostics(): SettingsOwnerDiagnostics {
      const write = writes.diagnostics();
      return Object.freeze({
        activeSettingsOwnerCount,
        activeAbortControllerCount: (readController ? 1 : 0) + write.activeAbortControllerCount,
        inFlightReadCount: readController ? 1 : 0,
        write,
        disposed
      });
    },
    dispose(reason = 'settings-owner-disposed'): void {
      if (disposed) return;
      disposed = true;
      generation += 1;
      void stopPreviewInternal('Settings owner 已释放，预览已停止。');
      readController?.abort(reason);
      readController = undefined;
      writes.dispose(reason);
      clearPreviewObjectUrl();
      state.device = emptyDeviceProjection();
      state.phase = 'disposed';
      state.generation = generation;
      state.message = 'Settings owner 已释放。';
      state.error = null;
      if (activeSettingsOwnerToken === ownerToken) {
        activeSettingsOwnerToken = undefined;
        activeSettingsOwnerCount = Math.max(0, activeSettingsOwnerCount - 1);
      }
    }
  });

  return owner;
}
