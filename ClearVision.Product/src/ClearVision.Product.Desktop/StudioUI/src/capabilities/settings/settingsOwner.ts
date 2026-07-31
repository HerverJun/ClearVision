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
  diagnostics(): SettingsOwnerDiagnostics;
  dispose(reason?: string): void;
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
    started: false
  });
  let disposed = false;
  let generation = 0;
  let readController: AbortController | undefined;

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
      readController?.abort(reason);
      readController = undefined;
      writes.dispose(reason);
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
