import type { DeepReadonly } from 'vue';

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

export interface SessionProjectionOwner {
  readonly projection: DeepReadonly<SessionProjection>;
  start(): void;
  refresh(): Promise<void>;
  dispose(): void;
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

export function sessionIdentityOf(user: SessionUserProjection): string {
  return `${user.userId}\u0000${user.username}\u0000${user.role}`;
}
