import { describe, expect, it, vi } from 'vitest';
import { type ApiGetOptions, type ApiTransport, ApiServerError } from '@/platform/api';
import { createReadQueryClient } from '@/platform/query';
import { createSystemStatusOwner, decodeSystemHealth } from '@/platform/status';

type ApiImpl = (path: string, options?: ApiGetOptions) => Promise<unknown>;

function apiWith(impl: ApiImpl): ApiTransport {
  return Object.freeze({
    apiBaseUrl: 'http://localhost:5000/api',
    async get<T>(path: string, options?: ApiGetOptions): Promise<T | undefined> {
      return await impl(path, options) as T | undefined;
    }
  });
}

describe('systemStatusOwner', () => {
  it('decodes the public health contract without treating unknown status as healthy', () => {
    expect(decodeSystemHealth({ status: 'Healthy', port: 5000 })).toEqual({
      status: 'Healthy', port: 5000, healthy: true
    });
    expect(decodeSystemHealth({ status: 'Degraded', port: 5000 }).healthy).toBe(false);
    expect(() => decodeSystemHealth({ status: 'Healthy', port: 70_000 })).toThrow();
  });

  it('projects online health and keeps previous data stale after refresh failure', async () => {
    const get = vi.fn()
      .mockResolvedValueOnce({ status: 'Healthy', port: 5000 })
      .mockRejectedValueOnce(new ApiServerError({
        url: 'http://localhost:5000/health',
        status: 503,
        statusText: '',
        payload: undefined,
        responseBody: ''
      }));
    const queries = createReadQueryClient(apiWith(get));
    const owner = createSystemStatusOwner({ queries });

    await owner.refresh();
    expect(owner.projection.phase).toBe('online');
    await owner.refresh();

    expect(owner.projection.phase).toBe('stale');
    expect(owner.projection.health?.port).toBe(5000);
    owner.dispose();
    queries.dispose();
  });

  it('clears its timer and active request on dispose', () => {
    let requestSignal: AbortSignal | undefined;
    const clear = vi.fn();
    const queries = createReadQueryClient(apiWith((_, options) => {
      requestSignal = options?.signal;
      return new Promise(() => undefined);
    }));
    const owner = createSystemStatusOwner({
      queries,
      setInterval: () => 22 as unknown as ReturnType<typeof setInterval>,
      clearInterval: clear
    });

    owner.start();
    owner.dispose();

    expect(clear).toHaveBeenCalledOnce();
    expect(requestSignal?.aborted).toBe(true);
    expect(queries.getDiagnostics().activeOwnerCount).toBe(0);
    queries.dispose();
  });
});
