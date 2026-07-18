import { reactive, readonly, type DeepReadonly } from 'vue';
import {
  ApiAbortError,
  ApiConflictError,
  ApiHttpError,
  ApiNetworkError,
  ApiUnauthorizedError,
  type ApiTransport,
  type ApiUnauthorizedContext
} from '@/platform/api';
import type { AuthTokenPort } from '@/platform/auth';
import {
  decodeSessionProjection,
  sessionIdentityOf,
  type SessionProjection,
  type SessionProjectionOwner,
  type SessionUserProjection
} from '@/app/session';

export type AuthLifecyclePhase =
  | 'checking-setup'
  | 'setup-required'
  | 'unauthenticated'
  | 'authenticating'
  | 'authenticated'
  | 'stale'
  | 'expired'
  | 'changing-password'
  | 'logging-out'
  | 'protected-transition'
  | 'error'
  | 'disposed';

export interface AuthSetupPolicy {
  readonly usernameMinLength: number;
  readonly passwordMinLength: number;
  readonly requiresUppercase: boolean;
  readonly requiresLowercase: boolean;
  readonly requiresDigit: boolean;
}

export interface AuthLifecycleProjection {
  readonly phase: AuthLifecyclePhase;
  readonly user: SessionUserProjection | null;
  readonly setupPolicy: AuthSetupPolicy | null;
  readonly sessionGeneration: number;
  readonly message: string;
  readonly errorCode: string | null;
  readonly updatedAt: number | null;
}

type MutableAuthLifecycleProjection = {
  -readonly [Key in keyof AuthLifecycleProjection]: AuthLifecycleProjection[Key]
};

type MutableSessionProjection = {
  -readonly [Key in keyof SessionProjection]: SessionProjection[Key]
};

export interface AuthRuntimeTransitions {
  prepareForProtectedTransition(reason: 'logout' | 'change-password'): Promise<boolean>;
  activateAuthenticatedSession(user: SessionUserProjection, generation: number): Promise<boolean>;
  endAuthenticatedSession(reason: 'logout' | 'change-password'): void;
  expireAuthenticatedSession(context: ApiUnauthorizedContext): Promise<void>;
  navigateToLogin(reason: 'logout' | 'change-password' | 'expired'): Promise<void> | void;
}

export interface AuthLifecycleOwner {
  readonly projection: DeepReadonly<AuthLifecycleProjection>;
  readonly session: SessionProjectionOwner;
  start(): Promise<void>;
  refreshSession(): Promise<void>;
  setupAdmin(input: Readonly<{ username: string; password: string; confirmPassword: string }>): Promise<boolean>;
  login(input: Readonly<{ username: string; password: string }>): Promise<boolean>;
  prepareChangePasswordRoute(): Promise<boolean>;
  changePassword(input: Readonly<{ oldPassword: string; newPassword: string }>): Promise<boolean>;
  logout(): Promise<boolean>;
  handleUnauthorized(context: ApiUnauthorizedContext): Promise<void>;
  dispose(): void;
}

export interface CreateAuthLifecycleOwnerOptions {
  readonly api: ApiTransport;
  readonly tokenPort: AuthTokenPort;
  readonly runtime: AuthRuntimeTransitions;
  readonly now?: () => number;
}

interface SetupStatusPayload {
  readonly requiresInitialAdminSetup: boolean;
  readonly usernameMinLength: number;
  readonly passwordMinLength: number;
  readonly requiresUppercase: boolean;
  readonly requiresLowercase: boolean;
  readonly requiresDigit: boolean;
}

interface TokenResponsePayload {
  readonly token: string;
}

function record(value: unknown, label: string): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    throw new TypeError(`${label} must be an object.`);
  }
  return value as Record<string, unknown>;
}

function positiveInteger(value: unknown, fallback: number): number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value > 0 ? value : fallback;
}

export function decodeSetupStatus(payload: unknown): SetupStatusPayload {
  const source = record(payload, 'GET auth/setup-status response');
  if (typeof source.requiresInitialAdminSetup !== 'boolean') {
    throw new TypeError('GET auth/setup-status requiresInitialAdminSetup must be a boolean.');
  }
  return Object.freeze({
    requiresInitialAdminSetup: source.requiresInitialAdminSetup,
    usernameMinLength: positiveInteger(source.usernameMinLength, 3),
    passwordMinLength: positiveInteger(source.passwordMinLength, 6),
    requiresUppercase: source.requiresUppercase === true,
    requiresLowercase: source.requiresLowercase === true,
    requiresDigit: source.requiresDigit === true
  });
}

function decodeTokenResponse(payload: unknown, label: string): TokenResponsePayload {
  const source = record(payload, label);
  if (typeof source.token !== 'string' || !source.token.trim()) {
    throw new TypeError(`${label} token must be a non-empty string.`);
  }
  return Object.freeze({ token: source.token.trim() });
}

function errorDetails(error: unknown): Readonly<{ code: string; message: string }> {
  if (error instanceof ApiHttpError && typeof error.payload === 'object' && error.payload !== null) {
    const payload = error.payload as Record<string, unknown>;
    const code = payload.errorCode ?? payload.ErrorCode ?? payload.code ?? payload.Code;
    const message = payload.error ?? payload.Error ?? payload.message ?? payload.Message;
    return Object.freeze({
      code: typeof code === 'string' && code.trim() ? code.trim().toUpperCase() : `HTTP_${error.status}`,
      message: typeof message === 'string' && message.trim() ? message.trim() : error.message
    });
  }
  return Object.freeze({
    code: error instanceof ApiNetworkError ? 'NETWORK_FAILURE' :
      error instanceof ApiAbortError ? 'REQUEST_ABORTED' : 'AUTH_FAILURE',
    message: error instanceof Error ? error.message : '认证请求失败。'
  });
}

export function createAuthLifecycleOwner(options: CreateAuthLifecycleOwnerOptions): AuthLifecycleOwner {
  const now = options.now ?? Date.now;
  const state = reactive<MutableAuthLifecycleProjection>({
    phase: 'checking-setup',
    user: null,
    setupPolicy: null,
    sessionGeneration: 0,
    message: '正在检查首次管理员初始化状态…',
    errorCode: null,
    updatedAt: null
  });
  const sessionState = reactive<MutableSessionProjection>({
    phase: 'loading',
    user: null,
    sessionGeneration: 0,
    message: '正在确认当前会话…',
    updatedAt: null
  });
  let disposed = false;
  let operationGeneration = 0;
  let activeController: AbortController | undefined;
  let startPromise: Promise<void> | undefined;
  let recoveryPromise: Promise<void> | undefined;
  let setupPromise: Promise<boolean> | undefined;
  let loginPromise: Promise<boolean> | undefined;
  let passwordPromise: Promise<boolean> | undefined;
  let logoutPromise: Promise<boolean> | undefined;
  const unauthorizedFlights = new Map<number, Promise<void>>();

  function syncSession(): void {
    sessionState.user = state.user;
    sessionState.sessionGeneration = state.sessionGeneration;
    sessionState.message = state.message;
    sessionState.updatedAt = state.updatedAt;
    if (state.phase === 'authenticated' || state.phase === 'changing-password' || state.phase === 'logging-out') {
      sessionState.phase = 'authenticated';
    } else if (state.phase === 'stale' || state.phase === 'protected-transition') {
      sessionState.phase = 'stale';
    } else if (state.phase === 'checking-setup' || state.phase === 'authenticating') {
      sessionState.phase = 'loading';
    } else if (state.phase === 'error') {
      sessionState.phase = 'error';
    } else {
      sessionState.phase = 'unauthorized';
    }
  }

  function project(
    phase: AuthLifecyclePhase,
    message: string,
    errorCode: string | null = null
  ): void {
    if (disposed) return;
    state.phase = phase;
    state.message = message;
    state.errorCode = errorCode;
    state.updatedAt = now();
    syncSession();
  }

  function begin(phase: AuthLifecyclePhase, message: string): Readonly<{ generation: number; controller: AbortController }> {
    activeController?.abort('superseded-auth-operation');
    const controller = new AbortController();
    activeController = controller;
    operationGeneration += 1;
    project(phase, message);
    return Object.freeze({ generation: operationGeneration, controller });
  }

  function current(operation: Readonly<{ generation: number; controller: AbortController }>): boolean {
    return !disposed && operation.generation === operationGeneration &&
      activeController === operation.controller && !operation.controller.signal.aborted;
  }

  function finish(operation: Readonly<{ generation: number; controller: AbortController }>): void {
    if (activeController === operation.controller) activeController = undefined;
  }

  async function rejectVerifiedSession(
    reason: string,
    previousUser: SessionUserProjection | null
  ): Promise<void> {
    const rejectedGeneration = state.sessionGeneration;
    options.tokenPort.removeToken();
    state.user = null;
    state.sessionGeneration += 1;
    if (previousUser) {
      project('expired', reason, 'SESSION_EXPIRED');
      await options.runtime.expireAuthenticatedSession(Object.freeze({
        method: 'GET',
        path: 'auth/me',
        url: `${options.api.apiBaseUrl}/auth/me`,
        sessionGeneration: rejectedGeneration
      }));
      await options.runtime.navigateToLogin('expired');
    } else {
      project('unauthenticated', reason);
    }
  }

  async function activateToken(
    token: string,
    operation: Readonly<{ generation: number; controller: AbortController }>
  ): Promise<boolean> {
    const previousUser = state.user;
    options.tokenPort.setToken(token);
    try {
      const payload = await options.api.get<unknown>('auth/me', {
        signal: operation.controller.signal,
        suppressUnauthorizedHandler: true
      });
      if (!current(operation)) return false;
      const user = decodeSessionProjection(payload);
      const sameSessionIdentity = previousUser !== null &&
        sessionIdentityOf(previousUser) === sessionIdentityOf(user);
      state.user = user;
      if (!sameSessionIdentity) state.sessionGeneration += 1;
      project('protected-transition', '认证已确认，正在创建受保护产品运行时…');
      const activated = await options.runtime.activateAuthenticatedSession(user, state.sessionGeneration);
      if (!current(operation)) return false;
      if (!activated) {
        project('protected-transition', '已重新认证，正在按原运行身份完成权威 reconcile。');
        return false;
      }
      project('authenticated', '会话有效。');
      return true;
    } catch (error) {
      if (!current(operation) || error instanceof ApiAbortError) return false;
      if (error instanceof ApiUnauthorizedError) {
        await rejectVerifiedSession('登录凭据未通过 /api/auth/me 权威复核。', previousUser);
        return false;
      }
      const details = errorDetails(error);
      state.user = previousUser;
      project('stale', '已收到 token，但 /api/auth/me 暂时无法完成权威复核。可重试会话恢复。', details.code);
      return false;
    }
  }

  async function recoverSessionInternal(): Promise<void> {
    const token = options.tokenPort.readToken();
    if (!token) {
      state.user = null;
      project('unauthenticated', '请输入账号和密码登录。');
      return;
    }
    const operation = state.user
      ? begin('authenticated', '正在刷新当前会话…')
      : begin('authenticating', '正在恢复已有会话…');
    try {
      await activateToken(token, operation);
    } finally {
      finish(operation);
    }
  }

  const session: SessionProjectionOwner = Object.freeze({
    projection: readonly(sessionState),
    start(): void {},
    refresh(): Promise<void> {
      return owner.refreshSession();
    },
    dispose(): void {}
  });

  const owner: AuthLifecycleOwner = Object.freeze({
    projection: readonly(state),
    session,
    start(): Promise<void> {
      if (disposed) return Promise.resolve();
      if (startPromise) return startPromise;
      const operation = begin('checking-setup', '正在检查首次管理员初始化状态…');
      startPromise = (async () => {
        try {
          const payload = await options.api.get<unknown>('auth/setup-status', {
            signal: operation.controller.signal,
            suppressUnauthorizedHandler: true
          });
          if (!current(operation)) return;
          const setup = decodeSetupStatus(payload);
          state.setupPolicy = Object.freeze({
            usernameMinLength: setup.usernameMinLength,
            passwordMinLength: setup.passwordMinLength,
            requiresUppercase: setup.requiresUppercase,
            requiresLowercase: setup.requiresLowercase,
            requiresDigit: setup.requiresDigit
          });
          if (setup.requiresInitialAdminSetup) {
            options.tokenPort.removeToken();
            state.user = null;
            project('setup-required', '首次使用需要创建管理员账号。');
            return;
          }
          finish(operation);
          await owner.refreshSession();
        } catch (error) {
          if (!current(operation) || error instanceof ApiAbortError) return;
          const details = errorDetails(error);
          project('error', '无法读取首次管理员初始化状态。', details.code);
        } finally {
          finish(operation);
        }
      })().finally(() => { startPromise = undefined; });
      return startPromise;
    },
    refreshSession(): Promise<void> {
      if (disposed) return Promise.resolve();
      if (recoveryPromise) return recoveryPromise;
      recoveryPromise = recoverSessionInternal().finally(() => { recoveryPromise = undefined; });
      return recoveryPromise;
    },
    setupAdmin(input: Readonly<{ username: string; password: string; confirmPassword: string }>): Promise<boolean> {
      if (disposed) return Promise.resolve(false);
      if (setupPromise) return setupPromise;
      const operation = begin('authenticating', '正在创建管理员并验证会话…');
      setupPromise = (async () => {
        try {
          if (input.password !== input.confirmPassword) {
            project('setup-required', '两次输入的密码不一致。', 'PASSWORD_MISMATCH');
            return false;
          }
          const payload = await options.api.post?.<unknown>('auth/setup-admin', {
            username: input.username,
            password: input.password,
            confirmPassword: input.confirmPassword
          }, { signal: operation.controller.signal, suppressUnauthorizedHandler: true });
          if (!current(operation)) return false;
          const response = decodeTokenResponse(payload, 'POST auth/setup-admin response');
          return await activateToken(response.token, operation);
        } catch (error) {
          if (!current(operation) || error instanceof ApiAbortError) return false;
          const details = errorDetails(error);
          if (error instanceof ApiNetworkError) {
            try {
              const statusPayload = await options.api.get<unknown>('auth/setup-status', {
                signal: operation.controller.signal,
                suppressUnauthorizedHandler: true
              });
              if (!current(operation)) return false;
              const status = decodeSetupStatus(statusPayload);
              project(
                status.requiresInitialAdminSetup ? 'setup-required' : 'unauthenticated',
                status.requiresInitialAdminSetup
                  ? '管理员初始化响应丢失，权威状态仍要求初始化；可以安全重新提交。'
                  : '管理员初始化响应丢失，但权威状态表明初始化已完成。请使用刚设置的账号登录。',
                details.code
              );
            } catch (statusError) {
              if (!current(operation) || statusError instanceof ApiAbortError) return false;
              project('error', '管理员初始化结果未知，且无法重新读取初始化状态；不要重复猜测提交结果。', details.code);
            }
          } else if (error instanceof ApiConflictError) {
            project('unauthenticated', '系统已完成初始化，请直接登录。', details.code);
          } else {
            project('setup-required', details.message, details.code);
          }
          return false;
        } finally {
          finish(operation);
        }
      })().finally(() => { setupPromise = undefined; });
      return setupPromise;
    },
    login(input: Readonly<{ username: string; password: string }>): Promise<boolean> {
      if (disposed) return Promise.resolve(false);
      if (loginPromise) return loginPromise;
      const operation = begin('authenticating', '正在验证账号与会话…');
      loginPromise = (async () => {
        try {
          const payload = await options.api.post?.<unknown>('auth/login', {
            username: input.username,
            password: input.password
          }, { signal: operation.controller.signal, suppressUnauthorizedHandler: true });
          if (!current(operation)) return false;
          const response = decodeTokenResponse(payload, 'POST auth/login response');
          return await activateToken(response.token, operation);
        } catch (error) {
          if (!current(operation) || error instanceof ApiAbortError) return false;
          const details = errorDetails(error);
          state.user = null;
          project('unauthenticated', details.message, details.code);
          return false;
        } finally {
          finish(operation);
        }
      })().finally(() => { loginPromise = undefined; });
      return loginPromise;
    },
    async prepareChangePasswordRoute(): Promise<boolean> {
      if (disposed || !state.user || state.phase !== 'authenticated') return false;
      project('protected-transition', '正在确认保存与运行状态允许进入修改密码流程…');
      if (!(await options.runtime.prepareForProtectedTransition('change-password'))) {
        project('authenticated', '修改密码已阻止：存在未安全收口的保存、运行或未知结果。', 'CHANGE_PASSWORD_BLOCKED');
        return false;
      }
      project('authenticated', '可以安全进入修改密码流程。');
      return true;
    },
    changePassword(input: Readonly<{ oldPassword: string; newPassword: string }>): Promise<boolean> {
      if (disposed || !state.user || state.phase !== 'authenticated') return Promise.resolve(false);
      if (passwordPromise) return passwordPromise;
      passwordPromise = (async () => {
        project('protected-transition', '正在确认保存与运行状态允许修改密码…');
        if (!(await options.runtime.prepareForProtectedTransition('change-password'))) {
          project('authenticated', '修改密码已阻止：存在未安全收口的保存、运行或未知结果。', 'CHANGE_PASSWORD_BLOCKED');
          return false;
        }
        const operation = begin('changing-password', '正在修改密码…');
        try {
          await options.api.post?.('auth/change-password', {
            oldPassword: input.oldPassword,
            newPassword: input.newPassword
          }, { signal: operation.controller.signal, suppressUnauthorizedHandler: true });
          if (!current(operation)) return false;
          options.tokenPort.removeToken();
          state.user = null;
          state.sessionGeneration += 1;
          options.runtime.endAuthenticatedSession('change-password');
          project('unauthenticated', '密码修改成功。请使用新密码重新登录。');
          await options.runtime.navigateToLogin('change-password');
          return true;
        } catch (error) {
          if (!current(operation) || error instanceof ApiAbortError) return false;
          const details = errorDetails(error);
          project('authenticated', details.message, details.code);
          return false;
        } finally {
          finish(operation);
        }
      })().finally(() => { passwordPromise = undefined; });
      return passwordPromise;
    },
    logout(): Promise<boolean> {
      if (disposed || !state.user || state.phase !== 'authenticated') return Promise.resolve(false);
      if (logoutPromise) return logoutPromise;
      logoutPromise = (async () => {
        project('protected-transition', '正在确认保存与运行状态允许退出…');
        if (!(await options.runtime.prepareForProtectedTransition('logout'))) {
          project('authenticated', '退出已阻止：存在未安全收口的保存、运行或未知结果。', 'LOGOUT_BLOCKED');
          return false;
        }
        const operation = begin('logging-out', '正在注销服务端会话…');
        try {
          await options.api.post?.('auth/logout', {}, {
            signal: operation.controller.signal,
            suppressUnauthorizedHandler: true
          });
          if (!current(operation)) return false;
          options.tokenPort.removeToken();
          state.user = null;
          state.sessionGeneration += 1;
          options.runtime.endAuthenticatedSession('logout');
          project('unauthenticated', '已安全退出。');
          await options.runtime.navigateToLogin('logout');
          return true;
        } catch (error) {
          if (!current(operation) || error instanceof ApiAbortError) return false;
          const details = errorDetails(error);
          project('authenticated', '服务端注销结果未确认；本地会话保持不变。', details.code);
          return false;
        } finally {
          finish(operation);
        }
      })().finally(() => { logoutPromise = undefined; });
      return logoutPromise;
    },
    handleUnauthorized(context: ApiUnauthorizedContext): Promise<void> {
      const acceptsProtectedUnauthorized = state.user !== null && (
        state.phase === 'authenticated' || state.phase === 'stale' || state.phase === 'protected-transition'
      );
      if (disposed || context.sessionGeneration !== state.sessionGeneration || !acceptsProtectedUnauthorized) {
        return Promise.resolve();
      }
      const existing = unauthorizedFlights.get(context.sessionGeneration);
      if (existing) return existing;
      const generation = context.sessionGeneration;
      const flight = (async () => {
        project('expired', '当前会话已失效。正在隔离产品运行时并保留必要 reconcile 身份。', 'SESSION_EXPIRED');
        options.tokenPort.removeToken();
        state.sessionGeneration += 1;
        await options.runtime.expireAuthenticatedSession(context);
        if (disposed) return;
        state.user = null;
        project('expired', '会话已失效，请重新登录。');
        await options.runtime.navigateToLogin('expired');
      })().finally(() => { unauthorizedFlights.delete(generation); });
      unauthorizedFlights.set(generation, flight);
      return flight;
    },
    dispose(): void {
      if (disposed) return;
      disposed = true;
      operationGeneration += 1;
      activeController?.abort('auth-owner-disposed');
      activeController = undefined;
      options.api.setUnauthorizedHandler?.(null);
      state.phase = 'disposed';
      state.user = null;
      state.message = '认证生命周期已释放。';
      syncSession();
    }
  });

  options.api.setUnauthorizedHandler?.(owner.handleUnauthorized, () => state.sessionGeneration);
  return owner;
}
