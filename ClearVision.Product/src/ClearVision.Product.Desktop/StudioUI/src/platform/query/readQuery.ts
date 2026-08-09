import { readonly, shallowRef, type DeepReadonly, type ShallowRef } from 'vue';
import {
  ApiAbortError,
  ApiDecodeError,
  ApiForbiddenError,
  ApiHttpError,
  ApiNetworkError,
  ApiNotFoundError,
  ApiServerError,
  ApiUnauthorizedError,
  type ApiTransport
} from '@/platform/api';

export type ReadQueryPhase =
  | 'idle'
  | 'loading'
  | 'success'
  | 'empty'
  | 'unauthorized'
  | 'forbidden'
  | 'not-found'
  | 'error'
  | 'stale'
  | 'partial-failure'
  | 'aborted';

export type ReadQueryFailureKind =
  | 'unauthorized'
  | 'forbidden'
  | 'not-found'
  | 'network'
  | 'server'
  | 'decode'
  | 'http'
  | 'unexpected';

export interface ReadQueryFailure {
  readonly kind: ReadQueryFailureKind;
  readonly message: string;
  readonly status?: number;
}

export class ReadQueryDecoderError extends Error {
  constructor(cause: unknown) {
    super('The response did not match the frozen read query contract.', { cause });
    this.name = 'ReadQueryDecoderError';
  }
}

export interface ReadQueryState<T> {
  readonly phase: ReadQueryPhase;
  readonly data?: T;
  readonly failure?: ReadQueryFailure;
  readonly isRefreshing: boolean;
  readonly requestId: number;
  readonly updatedAt?: number;
  readonly sessionGeneration: number;
}

export interface ReadQueryDefinition<T> {
  readonly key: string | (() => string);
  readonly path: string | (() => string);
  readonly decode: (payload: unknown) => T;
  readonly isEmpty?: (data: T) => boolean;
  readonly protected: boolean;
  readonly cacheTimeMs?: number;
}

export interface ReadQueryRefreshOptions {
  readonly force?: boolean;
}

export interface ReadQueryOwner<T> {
  readonly state: DeepReadonly<ShallowRef<ReadQueryState<T>>>;
  refresh(options?: ReadQueryRefreshOptions): Promise<ReadQueryState<T>>;
  abort(reason?: string): void;
  dispose(): void;
}

export interface ReadQueryDiagnostics {
  readonly activeOwnerCount: number;
  readonly activeRequestCount: number;
  readonly cacheEntryCount: number;
  readonly protectedCacheEntryCount: number;
  readonly sessionGeneration: number;
  readonly disposed: boolean;
}

export interface ReadQueryClient {
  readonly sessionGeneration: number;
  createQuery<T>(definition: ReadQueryDefinition<T>): ReadQueryOwner<T>;
  setSessionIdentity(identity: string | null): void;
  clearProtectedCache(reason?: string): void;
  getDiagnostics(): ReadQueryDiagnostics;
  dispose(): void;
}

interface CacheEntry {
  readonly value: unknown;
  readonly protected: boolean;
  readonly expiresAt: number;
}

interface ManagedOwner {
  readonly protected: boolean;
  abort(reason: string): void;
  isActive(): boolean;
}

function resolveValue(value: string | (() => string)): string {
  return typeof value === 'function' ? value() : value;
}

function createInitialState<T>(generation: number): ReadQueryState<T> {
  return Object.freeze({
    phase: 'idle',
    isRefreshing: false,
    requestId: 0,
    sessionGeneration: generation
  });
}

function toFailure(error: unknown): ReadQueryFailure {
  if (error instanceof ReadQueryDecoderError) {
    return Object.freeze({ kind: 'decode', message: '服务响应不符合冻结合同。' });
  }
  if (error instanceof ApiUnauthorizedError) {
    return Object.freeze({ kind: 'unauthorized', status: 401, message: '当前会话已失效。' });
  }
  if (error instanceof ApiForbiddenError) {
    return Object.freeze({ kind: 'forbidden', status: 403, message: '当前账号无权读取此内容。' });
  }
  if (error instanceof ApiNotFoundError) {
    return Object.freeze({ kind: 'not-found', status: 404, message: '请求的内容不存在。' });
  }
  if (error instanceof ApiNetworkError) {
    return Object.freeze({ kind: 'network', message: '无法连接本地服务。' });
  }
  if (error instanceof ApiServerError) {
    return Object.freeze({ kind: 'server', status: error.status, message: '本地服务暂时无法完成请求。' });
  }
  if (error instanceof ApiDecodeError) {
    return Object.freeze({ kind: 'decode', status: error.status, message: '服务响应不符合冻结合同。' });
  }
  if (error instanceof TypeError) {
    return Object.freeze({ kind: 'decode', message: '服务响应不符合冻结合同。' });
  }
  if (error instanceof ApiHttpError) {
    return Object.freeze({ kind: 'http', status: error.status, message: `请求失败（HTTP ${error.status}）。` });
  }
  return Object.freeze({ kind: 'unexpected', message: '读取数据时发生未知错误。' });
}

function errorPhase(failure: ReadQueryFailure, hasPreviousData: boolean): ReadQueryPhase {
  if (failure.kind === 'unauthorized') return 'unauthorized';
  if (failure.kind === 'forbidden') return hasPreviousData ? 'partial-failure' : 'forbidden';
  if (failure.kind === 'not-found') return 'not-found';
  if (hasPreviousData) {
    return failure.kind === 'network' || failure.kind === 'server' || failure.kind === 'http'
      ? 'stale'
      : 'partial-failure';
  }
  return 'error';
}

export function createReadQueryClient(api: ApiTransport): ReadQueryClient {
  const cache = new Map<string, CacheEntry>();
  const owners = new Set<ManagedOwner>();
  let sessionGeneration = 0;
  let sessionIdentity: string | null | undefined;
  let disposed = false;

  function assertActive(): void {
    if (disposed) throw new Error('The read query client has been disposed.');
  }

  function currentCacheKey<T>(definition: ReadQueryDefinition<T>): string {
    const key = resolveValue(definition.key);
    return definition.protected
      ? `protected:${sessionGeneration}:${key}`
      : `public:${key}`;
  }

  function deleteProtectedCache(): void {
    for (const [key, entry] of cache) {
      if (entry.protected) cache.delete(key);
    }
  }

  function clearProtectedCache(reason = 'session-changed', source?: ManagedOwner): void {
    deleteProtectedCache();
    for (const owner of owners) {
      if (owner !== source && owner.protected) owner.abort(reason);
    }
  }

  const client: ReadQueryClient = {
    get sessionGeneration() {
      return sessionGeneration;
    },
    createQuery<T>(definition: ReadQueryDefinition<T>): ReadQueryOwner<T> {
      assertActive();
      const state = shallowRef<ReadQueryState<T>>(createInitialState(sessionGeneration));
      let activeController: AbortController | undefined;
      let requestId = 0;
      let ownerDisposed = false;

      function abortActiveRequest(reason: string): void {
        const controller = activeController;
        if (!controller) return;
        activeController = undefined;
        controller.abort(reason);
        const current = state.value;
        const previousData = current.data;
        state.value = previousData === undefined
          ? Object.freeze({
              phase: 'aborted',
              isRefreshing: false,
              requestId: current.requestId,
              sessionGeneration
            })
          : Object.freeze({
              phase: current.phase,
              data: previousData,
              isRefreshing: false,
              requestId: current.requestId,
              ...(current.updatedAt === undefined ? {} : { updatedAt: current.updatedAt }),
              sessionGeneration
            });
      }

      const managedOwner: ManagedOwner = {
        protected: definition.protected,
        abort(reason: string): void {
          abortActiveRequest(reason);
        },
        isActive(): boolean {
          return activeController !== undefined;
        }
      };
      owners.add(managedOwner);

      const owner: ReadQueryOwner<T> = Object.freeze({
        state: readonly(state),
        async refresh(options: ReadQueryRefreshOptions = {}): Promise<ReadQueryState<T>> {
          if (ownerDisposed || disposed) {
            throw new Error('The read query owner has been disposed.');
          }

          const nextRequestId = ++requestId;
          activeController?.abort('superseded');
          const controller = new AbortController();
          activeController = controller;
          const cacheKey = currentCacheKey(definition);
          const cached = cache.get(cacheKey);
          const now = Date.now();
          if (!options.force && cached && cached.expiresAt > now) {
            const data = cached.value as T;
            state.value = Object.freeze({
              phase: definition.isEmpty?.(data) ? 'empty' : 'success',
              data,
              isRefreshing: false,
              requestId: nextRequestId,
              updatedAt: cached.expiresAt - (definition.cacheTimeMs ?? 0),
              sessionGeneration
            });
            activeController = undefined;
            return state.value;
          }

          const previousData = state.value.data;
          state.value = Object.freeze({
            phase: previousData === undefined ? 'loading' : state.value.phase,
            ...(previousData === undefined ? {} : { data: previousData }),
            isRefreshing: previousData !== undefined,
            requestId: nextRequestId,
            ...(state.value.updatedAt === undefined ? {} : { updatedAt: state.value.updatedAt }),
            sessionGeneration
          });

          try {
            const payload = await api.get(resolveValue(definition.path), { signal: controller.signal });
            let data: T;
            try {
              data = definition.decode(payload);
            } catch (error) {
              throw new ReadQueryDecoderError(error);
            }
            if (ownerDisposed || disposed || nextRequestId !== requestId || controller.signal.aborted) {
              return state.value;
            }

            const updatedAt = Date.now();
            const cacheTimeMs = Math.max(0, definition.cacheTimeMs ?? 0);
            if (cacheTimeMs > 0) {
              cache.set(cacheKey, {
                value: data,
                protected: definition.protected,
                expiresAt: updatedAt + cacheTimeMs
              });
            }
            state.value = Object.freeze({
              phase: definition.isEmpty?.(data) ? 'empty' : 'success',
              data,
              isRefreshing: false,
              requestId: nextRequestId,
              updatedAt,
              sessionGeneration
            });
          } catch (error) {
            if (ownerDisposed || disposed || nextRequestId !== requestId) return state.value;
            if (controller.signal.aborted || error instanceof ApiAbortError) {
              state.value = previousData === undefined
                ? Object.freeze({
                    phase: 'aborted',
                    isRefreshing: false,
                    requestId: nextRequestId,
                    sessionGeneration
                  })
                : Object.freeze({
                    phase: state.value.phase,
                    data: previousData,
                    isRefreshing: false,
                    requestId: nextRequestId,
                    ...(state.value.updatedAt === undefined ? {} : { updatedAt: state.value.updatedAt }),
                    sessionGeneration
                  });
              return state.value;
            }

            const failure = toFailure(error);
            if (failure.kind === 'unauthorized' && definition.protected) {
              const initialIdentity = sessionIdentity === undefined;
              if (sessionIdentity !== null) {
                sessionIdentity = null;
                sessionGeneration += 1;
              }
              if (initialIdentity) {
                deleteProtectedCache();
              } else {
                clearProtectedCache('unauthorized', managedOwner);
              }
            }
            const keepPrevious = previousData !== undefined && failure.kind !== 'unauthorized';
            state.value = Object.freeze({
              phase: errorPhase(failure, keepPrevious),
              ...(keepPrevious ? { data: previousData } : {}),
              failure,
              isRefreshing: false,
              requestId: nextRequestId,
              ...(keepPrevious && state.value.updatedAt !== undefined
                ? { updatedAt: state.value.updatedAt }
                : {}),
              sessionGeneration
            });
          } finally {
            if (activeController === controller) activeController = undefined;
          }
          return state.value;
        },
        abort(reason = 'aborted'): void {
          if (ownerDisposed || disposed) return;
          abortActiveRequest(reason);
        },
        dispose(): void {
          if (ownerDisposed) return;
          owner.abort('disposed');
          ownerDisposed = true;
          owners.delete(managedOwner);
        }
      });
      return owner;
    },
    setSessionIdentity(identity: string | null): void {
      assertActive();
      if (sessionIdentity === identity) return;
      const initialIdentity = sessionIdentity === undefined;
      sessionIdentity = identity;
      sessionGeneration += 1;
      if (initialIdentity) {
        deleteProtectedCache();
      } else {
        clearProtectedCache('session-changed');
      }
    },
    clearProtectedCache(reason?: string): void {
      assertActive();
      clearProtectedCache(reason);
    },
    getDiagnostics(): ReadQueryDiagnostics {
      let activeRequestCount = 0;
      for (const owner of owners) {
        if (owner.isActive()) activeRequestCount += 1;
      }
      return Object.freeze({
        activeOwnerCount: owners.size,
        activeRequestCount,
        cacheEntryCount: cache.size,
        protectedCacheEntryCount: [...cache.values()].filter(entry => entry.protected).length,
        sessionGeneration,
        disposed
      });
    },
    dispose(): void {
      if (disposed) return;
      disposed = true;
      for (const owner of [...owners]) owner.abort('client-disposed');
      owners.clear();
      cache.clear();
    }
  };

  return Object.freeze(client);
}
