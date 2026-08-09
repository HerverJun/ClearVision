import { readonly, reactive, type DeepReadonly } from 'vue';
import type { ReadQueryClient, ReadQueryOwner, ReadQueryState } from '@/platform/query';

export interface SystemHealthProjection {
  readonly status: string;
  readonly port: number;
  readonly healthy: boolean;
  readonly service?: string;
  readonly version?: string;
}

export type SystemStatusPhase = 'loading' | 'online' | 'offline' | 'stale';

export interface SystemStatusProjection {
  readonly phase: SystemStatusPhase;
  readonly health: SystemHealthProjection | null;
  readonly message: string;
  readonly updatedAt: number | null;
}

type MutableSystemStatusProjection = {
  -readonly [Key in keyof SystemStatusProjection]: SystemStatusProjection[Key]
};

export interface SystemStatusOwner {
  readonly projection: DeepReadonly<SystemStatusProjection>;
  start(): void;
  refresh(): Promise<void>;
  dispose(): void;
}

export interface SystemStatusOwnerOptions {
  readonly queries: ReadQueryClient;
  readonly refreshIntervalMs?: number;
  readonly setInterval?: (handler: () => void, timeoutMs: number) => ReturnType<typeof setInterval>;
  readonly clearInterval?: (handle: ReturnType<typeof setInterval>) => void;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

export function decodeSystemHealth(payload: unknown): SystemHealthProjection {
  if (!isRecord(payload) || typeof payload.status !== 'string' || !payload.status.trim() ||
      typeof payload.port !== 'number' || !Number.isInteger(payload.port) ||
      payload.port < 1 || payload.port > 65_535) {
    throw new TypeError('GET /health did not match the frozen system status contract.');
  }
  return Object.freeze({
    status: payload.status,
    port: payload.port,
    healthy: payload.status.toLowerCase() === 'healthy',
    ...(typeof payload.service === 'string' && payload.service.trim()
      ? { service: payload.service.trim() }
      : {}),
    ...(typeof payload.version === 'string' && payload.version.trim()
      ? { version: payload.version.trim() }
      : {})
  });
}

export function createSystemStatusOwner(options: SystemStatusOwnerOptions): SystemStatusOwner {
  const state = reactive<MutableSystemStatusProjection>({
    phase: 'loading',
    health: null,
    message: '正在连接本地服务…',
    updatedAt: null
  });
  const query: ReadQueryOwner<SystemHealthProjection> = options.queries.createQuery({
    key: 'system-health',
    path: '/health',
    decode: decodeSystemHealth,
    protected: false,
    cacheTimeMs: 5_000
  });
  const intervalMs = Math.max(5_000, options.refreshIntervalMs ?? 30_000);
  const schedule = options.setInterval ?? globalThis.setInterval.bind(globalThis);
  const cancelSchedule = options.clearInterval ?? globalThis.clearInterval.bind(globalThis);
  let timer: ReturnType<typeof setInterval> | undefined;
  let started = false;
  let disposed = false;

  function project(result: ReadQueryState<SystemHealthProjection>): void {
    if (disposed) return;
    if ((result.phase === 'success' || result.phase === 'empty') && result.data) {
      state.phase = result.data.healthy ? 'online' : 'offline';
      state.health = result.data;
      state.message = result.data.healthy ? '本地服务在线' : `本地服务状态：${result.data.status}`;
      state.updatedAt = result.updatedAt ?? Date.now();
    } else if ((result.phase === 'stale' || result.phase === 'partial-failure') && result.data) {
      state.phase = 'stale';
      state.health = result.data;
      state.message = '状态刷新失败，显示上次确认结果。';
    } else if (result.phase !== 'aborted') {
      state.phase = 'offline';
      state.health = null;
      state.message = result.failure?.message ?? '本地服务不可用。';
      state.updatedAt = Date.now();
    }
  }

  const owner: SystemStatusOwner = Object.freeze({
    projection: readonly(state),
    start(): void {
      if (disposed || started) return;
      started = true;
      void owner.refresh();
      timer = schedule(() => { void owner.refresh(); }, intervalMs);
    },
    async refresh(): Promise<void> {
      if (disposed) return;
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
