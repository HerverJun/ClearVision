import { describe, expect, it } from 'vitest';
import { ApiAbortError, type ApiGetOptions, type ApiTransport } from '@/platform/api';
import { createReadQueryClient } from '@/platform/query';
import { createResultAnalysisOwner } from '@/capabilities/results-read/resultAnalysisOwner';

const projectId = '11111111-1111-4111-8111-111111111111';
const nextProjectId = '33333333-3333-4333-8333-333333333333';

function analysisPayload(path: string, requestedProjectId: string): unknown {
  const startTime = '2026-08-06T00:00:00.000Z';
  const endTime = '2026-08-07T00:00:00.000Z';
  if (path.includes('defect-distribution')) {
    return { projectId: requestedProjectId, startTime, endTime, totalDefects: 0, items: [] };
  }
  if (path.includes('analysis/trend')) {
    return {
      projectId: requestedProjectId, interval: 'Hour', startTime, endTime, dataPoints: []
    };
  }
  return {
    projectId: requestedProjectId,
    generatedAt: endTime,
    period: { startTime, endTime },
    summary: {
      projectId: requestedProjectId, totalCount: 0, okCount: 0, ngCount: 0, errorCount: 0,
      okRate: 0, yieldRate: 0, totalDefects: 0, averageProcessingTimeMs: 0
    },
    defectDistribution: { projectId: requestedProjectId, startTime, endTime, totalDefects: 0, items: [] },
    confidenceDistribution: {
      projectId: requestedProjectId, startTime, endTime, totalDefects: 0, buckets: [], averageConfidence: 0
    },
    hourlyTrend: { projectId: requestedProjectId, interval: 'Hour', startTime, endTime, dataPoints: [] },
    recommendations: []
  };
}

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

  it('keeps the new project authoritative when a disposed project analysis completes late', async () => {
    const lateResolvers: Array<(value: unknown) => void> = [];
    const api: ApiTransport = {
      apiBaseUrl: 'http://localhost:5000/api',
      async get<T = unknown>(path: string): Promise<T | undefined> {
        if (path.includes(projectId)) {
          return await new Promise<T>(resolve => {
            lateResolvers.push(value => resolve(value as T));
          });
        }
        return analysisPayload(path, nextProjectId) as T;
      }
    };
    const queries = createReadQueryClient(api);
    const filters = () => ({ from: '', to: '', outcome: '', defectType: '' });
    const createOwner = (id: string) => createResultAnalysisOwner({
      projectId: id,
      queries,
      filters,
      trendStart: () => '2026-08-06T00:00:00.000Z',
      trendEnd: () => '2026-08-07T00:00:00.000Z'
    });

    const previousOwner = createOwner(projectId);
    const previousRefresh = previousOwner.refresh({ force: true });
    await Promise.resolve();
    expect(lateResolvers).toHaveLength(3);

    previousOwner.dispose('project-switch');
    const currentOwner = createOwner(nextProjectId);
    await currentOwner.refresh({ force: true });
    expect(currentOwner.projection).toMatchObject({ phase: 'ready', projectId: nextProjectId });
    expect(currentOwner.projection.distribution.data?.projectId).toBe(nextProjectId);
    expect(currentOwner.projection.trend.data?.projectId).toBe(nextProjectId);
    expect(currentOwner.projection.report.data?.projectId).toBe(nextProjectId);

    for (const resolve of lateResolvers) resolve(analysisPayload('analysis/report', projectId));
    await previousRefresh;
    expect(previousOwner.projection.phase).toBe('disposed');
    expect(currentOwner.projection).toMatchObject({ phase: 'ready', projectId: nextProjectId });
    expect(currentOwner.projection.report.data?.projectId).toBe(nextProjectId);

    currentOwner.dispose('unit-test-dispose');
    queries.dispose();
  });
});
