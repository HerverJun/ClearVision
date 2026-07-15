import { describe, expect, it, vi } from 'vitest';
import type { ApiGetOptions, ApiTransport } from '@/platform/api';
import {
  ApiServerError,
  ApiUnauthorizedError
} from '@/platform/api';
import { createReadQueryClient } from '@/platform/query';

function httpDetails(status: number) {
  return {
    url: 'http://localhost:5000/api/projects',
    status,
    statusText: '',
    payload: undefined,
    responseBody: ''
  };
}

type ApiImpl = (path: string, options?: ApiGetOptions) => Promise<unknown>;

function createApi(impl: ApiImpl): ApiTransport {
  return Object.freeze({
    apiBaseUrl: 'http://localhost:5000/api',
    async get<T>(path: string, options?: ApiGetOptions): Promise<T | undefined> {
      return await impl(path, options) as T | undefined;
    }
  });
}

function decodeValue(payload: unknown): { value: string } {
  if (typeof payload !== 'object' || payload === null ||
      typeof Reflect.get(payload, 'value') !== 'string') {
    throw new TypeError('invalid value');
  }
  return Object.freeze({ value: Reflect.get(payload, 'value') as string });
}

describe('readQuery', () => {
  it('rejects malformed business payloads as decode failures', async () => {
    const client = createReadQueryClient(createApi(async () => ({ unknown: true })));
    const query = client.createQuery({
      key: 'malformed',
      path: 'projects',
      decode: decodeValue,
      protected: true
    });

    const result = await query.refresh();

    expect(result.phase).toBe('error');
    expect(result.failure?.kind).toBe('decode');
    query.dispose();
    client.dispose();
  });

  it('uses latest-request-wins and prevents an older response from overwriting state', async () => {
    const resolvers: Array<(value: unknown) => void> = [];
    const signals: AbortSignal[] = [];
    const api = createApi((_, options) => {
      if (options?.signal) signals.push(options.signal);
      return new Promise(resolve => resolvers.push(resolve));
    });
    const client = createReadQueryClient(api);
    const query = client.createQuery({
      key: 'latest',
      path: 'projects',
      decode: decodeValue,
      protected: true
    });

    const first = query.refresh();
    const second = query.refresh();
    expect(signals[0]?.aborted).toBe(true);
    resolvers[1]?.({ value: 'new' });
    await second;
    resolvers[0]?.({ value: 'old' });
    await first;

    expect(query.state.value.data?.value).toBe('new');
    expect(query.state.value.requestId).toBe(2);
    query.dispose();
    client.dispose();
  });

  it('isolates protected cache by session generation and clears it on identity change', async () => {
    const get = vi.fn(async () => ({ value: `call-${get.mock.calls.length}` }));
    const client = createReadQueryClient(createApi(get));
    client.setSessionIdentity('user-a');
    const query = client.createQuery({
      key: 'cached',
      path: 'projects',
      decode: decodeValue,
      protected: true,
      cacheTimeMs: 60_000
    });

    await query.refresh();
    await query.refresh();
    expect(get).toHaveBeenCalledTimes(1);
    expect(client.getDiagnostics().protectedCacheEntryCount).toBe(1);

    client.setSessionIdentity('user-b');
    expect(client.getDiagnostics().protectedCacheEntryCount).toBe(0);
    await query.refresh();
    expect(get).toHaveBeenCalledTimes(2);
    expect(client.sessionGeneration).toBe(2);
    query.dispose();
    client.dispose();
  });

  it('keeps previous data stale after a server failure', async () => {
    const get = vi.fn()
      .mockResolvedValueOnce({ value: 'stable' })
      .mockRejectedValueOnce(new ApiServerError(httpDetails(503)));
    const client = createReadQueryClient(createApi(get));
    const query = client.createQuery({
      key: 'stale',
      path: 'projects',
      decode: decodeValue,
      protected: true
    });

    await query.refresh({ force: true });
    const result = await query.refresh({ force: true });

    expect(result.phase).toBe('stale');
    expect(result.data?.value).toBe('stable');
    expect(result.failure?.kind).toBe('server');
    query.dispose();
    client.dispose();
  });

  it('clears protected data and advances generation on 401', async () => {
    const get = vi.fn()
      .mockResolvedValueOnce({ value: 'private' })
      .mockRejectedValueOnce(new ApiUnauthorizedError(httpDetails(401)));
    const client = createReadQueryClient(createApi(get));
    client.setSessionIdentity('user-a');
    const query = client.createQuery({
      key: 'unauthorized',
      path: 'projects',
      decode: decodeValue,
      protected: true
    });

    await query.refresh({ force: true });
    const generation = client.sessionGeneration;
    const result = await query.refresh({ force: true });

    expect(result.phase).toBe('unauthorized');
    expect(result.data).toBeUndefined();
    expect(client.sessionGeneration).toBe(generation + 1);
    expect(client.getDiagnostics().protectedCacheEntryCount).toBe(0);
    query.dispose();
    client.dispose();
  });

  it('aborts active work and releases owner diagnostics on dispose', () => {
    let signal: AbortSignal | undefined;
    const client = createReadQueryClient(createApi((_, options) => {
      signal = options?.signal;
      return new Promise(() => undefined);
    }));
    const query = client.createQuery({
      key: 'dispose',
      path: 'projects',
      decode: decodeValue,
      protected: true
    });

    void query.refresh();
    expect(client.getDiagnostics().activeRequestCount).toBe(1);
    query.dispose();

    expect(signal?.aborted).toBe(true);
    expect(client.getDiagnostics().activeOwnerCount).toBe(0);
    client.dispose();
  });
});
