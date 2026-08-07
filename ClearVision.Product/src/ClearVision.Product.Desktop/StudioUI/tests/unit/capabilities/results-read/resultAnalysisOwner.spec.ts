import { describe, expect, it } from 'vitest';
import { ApiAbortError, type ApiGetOptions, type ApiTransport } from '@/platform/api';
import { createReadQueryClient } from '@/platform/query';
import { createResultAnalysisOwner } from '@/capabilities/results-read/resultAnalysisOwner';

const projectId = '11111111-1111-4111-8111-111111111111';

describe('ResultAnalysisOwner', () => {
  it('owns three analysis queries and aborts all of them on dispose', async () => {
    const signals: AbortSignal[] = [];
    const api: ApiTransport = {
      apiBaseUrl: 'http://localhost:5000/api',
      async get<T = unknown>(path: string, options: ApiGetOptions = {}): Promise<T | undefined> {
        const signal = options.signal;
        if (!signal) throw new Error(`Missing abort signal for ${path}`);
        signals.push(signal);
        return await new Promise<T | undefined>((_resolve, reject) => {
          if (signal.aborted) {
            reject(new ApiAbortError(path, signal.reason));
            return;
          }
          signal.addEventListener('abort', () => reject(new ApiAbortError(path, signal.reason)), { once: true });
        });
      }
    };
    const queries = createReadQueryClient(api);
    const owner = createResultAnalysisOwner({
      projectId,
      queries,
      filters: () => ({ from: '', to: '', outcome: '', defectType: '' }),
      trendStart: () => '2026-08-06T00:00:00.000Z',
      trendEnd: () => '2026-08-07T00:00:00.000Z'
    });

    const refresh = owner.refresh({ force: true });
    expect(queries.getDiagnostics().activeOwnerCount).toBe(3);
    expect(signals).toHaveLength(3);

    owner.dispose('unit-test-dispose');
    await refresh;

    expect(signals.every(signal => signal.aborted)).toBe(true);
    expect(queries.getDiagnostics().activeRequestCount).toBe(0);
    expect(owner.projection.phase).toBe('disposed');

    await owner.refresh({ force: true });
    expect(signals).toHaveLength(3);
    queries.dispose();
  });
});
