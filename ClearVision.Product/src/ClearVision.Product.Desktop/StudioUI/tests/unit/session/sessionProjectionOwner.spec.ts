import { describe, expect, it, vi } from 'vitest';
import { createSessionProjectionOwner, decodeSessionProjection } from '@/app/session';
import { ApiUnauthorizedError, type ApiGetOptions, type ApiTransport } from '@/platform/api';
import { createReadQueryClient } from '@/platform/query';

type ApiImpl = (path: string, options?: ApiGetOptions) => Promise<unknown>;

function apiWith(impl: ApiImpl): ApiTransport {
  return Object.freeze({
    apiBaseUrl: 'http://localhost:5000/api',
    async get<T>(path: string, options?: ApiGetOptions): Promise<T | undefined> {
      return await impl(path, options) as T | undefined;
    }
  });
}

describe('sessionProjectionOwner', () => {
  it('decodes required fields and preserves unknown role strings as UI projection only', () => {
    expect(decodeSessionProjection({
      userId: 'u-1',
      username: 'operator',
      role: 'FutureRole',
      extra: true
    })).toEqual({ userId: 'u-1', username: 'operator', role: 'FutureRole' });
    expect(() => decodeSessionProjection({ username: 'missing-id', role: 'Admin' })).toThrow();
  });

  it('projects authenticated identity and advances session generation only when identity changes', async () => {
    const get = vi.fn(async () => ({ userId: 'u-1', username: 'engineer', role: 'Engineer' }));
    const queries = createReadQueryClient(apiWith(get));
    const owner = createSessionProjectionOwner({ queries, hasToken: () => true });

    await owner.refresh();
    const generation = queries.sessionGeneration;
    await owner.refresh();

    expect(owner.projection.phase).toBe('authenticated');
    expect(owner.projection.user?.username).toBe('engineer');
    expect(queries.sessionGeneration).toBe(generation);
    owner.dispose();
    queries.dispose();
  });

  it('clears projection and protected cache when the token disappears', async () => {
    let tokenPresent = true;
    const queries = createReadQueryClient(apiWith(async () => ({
      userId: 'u-1', username: 'engineer', role: 'Engineer'
    })));
    const owner = createSessionProjectionOwner({
      queries,
      hasToken: () => tokenPresent
    });
    await owner.refresh();
    const generation = queries.sessionGeneration;
    tokenPresent = false;
    await owner.refresh();

    expect(owner.projection.phase).toBe('unauthorized');
    expect(owner.projection.user).toBeNull();
    expect(queries.sessionGeneration).toBe(generation + 1);
    owner.dispose();
    queries.dispose();
  });

  it('maps 401 to unauthorized without exposing a login handoff', async () => {
    const queries = createReadQueryClient(apiWith(async () => {
      throw new ApiUnauthorizedError({
        url: 'http://localhost:5000/api/auth/me',
        status: 401,
        statusText: '',
        payload: undefined,
        responseBody: ''
      });
    }));
    const owner = createSessionProjectionOwner({ queries, hasToken: () => true });

    await owner.refresh();

    expect(owner.projection.phase).toBe('unauthorized');
    expect(owner.projection.message).toContain('预置');
    owner.dispose();
    queries.dispose();
  });

  it('owns exactly one timer and aborts its request on dispose', () => {
    let requestSignal: AbortSignal | undefined;
    const clear = vi.fn();
    const queries = createReadQueryClient(apiWith((_, options) => {
      requestSignal = options?.signal;
      return new Promise(() => undefined);
    }));
    const owner = createSessionProjectionOwner({
      queries,
      hasToken: () => true,
      setInterval: () => 11 as unknown as ReturnType<typeof setInterval>,
      clearInterval: clear
    });

    owner.start();
    owner.start();
    owner.dispose();

    expect(clear).toHaveBeenCalledTimes(1);
    expect(requestSignal?.aborted).toBe(true);
    expect(queries.getDiagnostics().activeOwnerCount).toBe(0);
    queries.dispose();
  });
});
