import { readonly, reactive, type DeepReadonly } from 'vue';
import type { ReadQueryClient, ReadQueryOwner, ReadQueryState } from '@/platform/query';

export interface SessionUserProjection {
  readonly userId: string;
  readonly username: string;
  readonly role: string;
}

export type SessionProjectionPhase = 'loading' | 'authenticated' | 'unauthorized' | 'error' | 'stale';

export interface SessionProjection {
  readonly phase: SessionProjectionPhase;
  readonly user: SessionUserProjection | null;
  readonly sessionGeneration: number;
  readonly message: string;
  readonly updatedAt: number | null;
}

type MutableSessionProjection = {
  -readonly [Key in keyof SessionProjection]: SessionProjection[Key]
};

export interface SessionProjectionOwner {
  readonly projection: DeepReadonly<SessionProjection>;
  start(): void;
  refresh(): Promise<void>;
  dispose(): void;
}

export interface SessionProjectionOwnerOptions {
  readonly queries: ReadQueryClient;
  readonly hasToken: () => boolean;
  readonly refreshIntervalMs?: number;
  readonly setInterval?: (handler: () => void, timeoutMs: number) => ReturnType<typeof setInterval>;
  readonly clearInterval?: (handle: ReturnType<typeof setInterval>) => void;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

export function decodeSessionProjection(payload: unknown): SessionUserProjection {
  if (!isRecord(payload) ||
      typeof payload.userId !== 'string' || !payload.userId.trim() ||
      typeof payload.username !== 'string' || !payload.username.trim() ||
      typeof payload.role !== 'string' || !payload.role.trim()) {
    throw new TypeError('GET auth/me did not match the frozen session projection contract.');
  }
  return Object.freeze({
    userId: payload.userId,
    username: payload.username,
    role: payload.role
  });
}

function identityOf(user: SessionUserProjection): string {
  return `${user.userId}\u0000${user.username}\u0000${user.role}`;
}

export function createSessionProjectionOwner(
  options: SessionProjectionOwnerOptions
): SessionProjectionOwner {
  const state = reactive<MutableSessionProjection>({
    phase: 'loading',
    user: null,
    sessionGeneration: options.queries.sessionGeneration,
    message: '正在确认当前会话…',
    updatedAt: null
  });
  const query: ReadQueryOwner<SessionUserProjection> = options.queries.createQuery({
    key: 'session-projection',
    path: 'auth/me',
    decode: decodeSessionProjection,
    protected: true
  });
  const intervalMs = Math.max(5_000, options.refreshIntervalMs ?? 60_000);
  const schedule = options.setInterval ?? globalThis.setInterval.bind(globalThis);
  const cancelSchedule = options.clearInterval ?? globalThis.clearInterval.bind(globalThis);
  let timer: ReturnType<typeof setInterval> | undefined;
  let started = false;
  let disposed = false;

  function project(result: ReadQueryState<SessionUserProjection>): void {
    if (disposed) return;
    if ((result.phase === 'success' || result.phase === 'empty') && result.data) {
      options.queries.setSessionIdentity(identityOf(result.data));
      state.phase = 'authenticated';
      state.user = result.data;
      state.message = '会话有效';
      state.updatedAt = result.updatedAt ?? Date.now();
    } else if (result.phase === 'unauthorized') {
      options.queries.setSessionIdentity(null);
      state.phase = 'unauthorized';
      state.user = null;
      state.message = '当前预览需要由测试或宿主预置有效会话。';
      state.updatedAt = Date.now();
    } else if ((result.phase === 'stale' || result.phase === 'partial-failure') && result.data) {
      state.phase = 'stale';
      state.user = result.data;
      state.message = '会话刷新失败，正在显示上次确认的用户投影。';
    } else if (result.phase !== 'aborted') {
      state.phase = 'error';
      state.user = null;
      state.message = result.failure?.message ?? '无法读取当前会话。';
      state.updatedAt = Date.now();
    }
    state.sessionGeneration = options.queries.sessionGeneration;
  }

  const owner: SessionProjectionOwner = Object.freeze({
    projection: readonly(state),
    start(): void {
      if (disposed || started) return;
      started = true;
      void owner.refresh();
      timer = schedule(() => { void owner.refresh(); }, intervalMs);
    },
    async refresh(): Promise<void> {
      if (disposed) return;
      if (!options.hasToken()) {
        options.queries.setSessionIdentity(null);
        state.phase = 'unauthorized';
        state.user = null;
        state.sessionGeneration = options.queries.sessionGeneration;
        state.message = '当前预览没有预置会话。';
        state.updatedAt = Date.now();
        return;
      }
      if (!state.user) {
        state.phase = 'loading';
        state.message = '正在确认当前会话…';
      }
      project(await query.refresh({ force: true }));
    },
    dispose(): void {
      if (disposed) return;
      disposed = true;
      if (timer !== undefined) cancelSchedule(timer);
      timer = undefined;
      query.dispose();
    }
  });
  return owner;
}
