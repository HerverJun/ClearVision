import { describe, expect, it, vi } from 'vitest';
import {
  createComparisonPath,
  createLocalResultsPath,
  createLocalResultsQuery,
  createLocalStatisticsPath,
  createPreviousSuccessPath,
  createStationResultsPath,
  createStationStatisticsPath,
  createStationResultsQuery,
  type ResultsListFilters
} from '@/capabilities/results-read';
import {
  ApiAbortError,
  ApiForbiddenError,
  ApiNotFoundError,
  ApiServerError,
  ApiUnauthorizedError,
  type ApiGetOptions,
  type ApiTransport
} from '@/platform/api';
import { createReadQueryClient } from '@/platform/query';

const projectId = '11111111-1111-4111-8111-111111111111';
const resultId = '22222222-2222-4222-8222-222222222222';

const baseFilters: ResultsListFilters = Object.freeze({
  stationId: '',
  outcome: '',
  diagnosticCode: '',
  from: '',
  to: '',
  page: 1,
  pageSize: 20
});

function localPage() {
  return {
    items: [{
      id: resultId,
      resultId,
      projectId,
      status: 'OK',
      executionOutcome: 'Succeeded',
      decisionOutcome: 'Ok',
      decisionSource: 'FinalDecision',
      reasonCode: 'OK',
      hasJudgmentSignal: true,
      defectCount: 0,
      processingTimeMs: 12,
      inspectionTime: '2026-07-15T01:00:02Z',
      startedAt: '2026-07-15T01:00:01Z',
      completedAt: '2026-07-15T01:00:02Z',
      confidenceScore: null,
      flowVersionHash: null,
      calibrationBundleId: null,
      runId: null,
      diagnosticCode: 'OK',
      diagnosticMessage: null,
      errorMessage: null
    }],
    totalCount: 1,
    pageIndex: 0,
    pageSize: 20
  };
}

function stationPage() {
  return {
    items: [{
      schemaVersion: 2,
      stationId: 'station-a',
      lineName: null,
      sequenceId: 1,
      messageId: 'message-1',
      runId: 'run-1',
      packageId: 'package-a',
      packageName: '检测包',
      packageVersion: '1.0.0',
      projectRevision: 1,
      outcome: 'Ok',
      inspectionStatus: 'OK',
      executionOutcome: 'Succeeded',
      decisionOutcome: 'Ok',
      hasJudgmentSignal: true,
      decisionSource: 'FinalDecision',
      reasonCode: 'OK',
      executionTimeMs: 8,
      diagnosticCode: 'OK',
      diagnosticMessage: null,
      startedAtUtc: '2026-07-15T01:00:00Z',
      completedAtUtc: '2026-07-15T01:00:01Z'
    }],
    totalCount: 1,
    pageIndex: 0,
    pageSize: 20
  };
}

function httpDetails(status: number) {
  return {
    url: 'http://localhost:5000/api/results',
    status,
    statusText: 'test',
    payload: undefined,
    responseBody: ''
  };
}

type GetImplementation = (path: string, options?: ApiGetOptions) => Promise<unknown>;

function apiWith(implementation: GetImplementation): ApiTransport {
  return {
    apiBaseUrl: 'http://localhost:5000/api',
    async get<T = unknown>(path: string, options?: ApiGetOptions): Promise<T | undefined> {
      return await implementation(path, options) as T | undefined;
    }
  };
}

describe('Results queries', () => {
  it('builds frozen GET-relative paths and passes all nine outcomes as status', () => {
    expect(createLocalResultsPath(projectId, {
      ...baseFilters,
      outcome: 'Invalid',
      diagnosticCode: 'LOCAL_CURRENT_PAGE_ONLY',
      from: '2026-07-15T00:00:00Z',
      to: '2026-07-15T23:59:59Z',
      page: 3,
      pageSize: 50
    })).toBe(
      `inspection/history/${projectId}?startTime=2026-07-15T00%3A00%3A00Z&endTime=2026-07-15T23%3A59%3A59Z&status=Invalid&pageIndex=2&pageSize=50`
    );
    expect(createStationResultsPath({
      ...baseFilters,
      stationId: 'station-a',
      outcome: 'TimedOut',
      diagnosticCode: 'CAMERA_TIMEOUT',
      page: 2,
      pageSize: 100
    })).toBe('stations/results?stationId=station-a&status=TimedOut&diagnosticCode=CAMERA_TIMEOUT&pageIndex=1&pageSize=100');
    expect(createLocalStatisticsPath(projectId, {
      ...baseFilters,
      outcome: 'Ng',
      from: '2026-07-15T00:00:00Z'
    })).toBe(`inspection/statistics/${projectId}?startTime=2026-07-15T00%3A00%3A00Z&status=Ng`);
    expect(createStationStatisticsPath({
      ...baseFilters,
      stationId: 'station-a',
      outcome: 'Failed',
      diagnosticCode: 'CAMERA_TIMEOUT'
    })).toBe('stations/statistics?stationId=station-a&status=Failed&diagnosticCode=CAMERA_TIMEOUT');
    expect(createPreviousSuccessPath(projectId, resultId)).toBe(
      `inspection/history/${projectId}/${resultId}/previous-success?limit=50`
    );
    expect(createComparisonPath(projectId, resultId, projectId)).toBe(
      `inspection/history/${projectId}/compare?leftId=${resultId}&rightId=${projectId}`
    );
    expect(() => createLocalResultsPath('not-a-guid', baseFilters)).toThrow(TypeError);
  });

  it.each([
    [new ApiUnauthorizedError(httpDetails(401)), 'unauthorized'],
    [new ApiForbiddenError(httpDetails(403)), 'forbidden'],
    [new ApiNotFoundError(httpDetails(404)), 'not-found'],
    [new ApiServerError(httpDetails(503)), 'error']
  ] as const)('maps transport failure %s to shared phase %s', async (failure, phase) => {
    const client = createReadQueryClient(apiWith(async () => { throw failure; }));
    const owner = createLocalResultsQuery(client, () => projectId, () => baseFilters);

    await expect(owner.refresh({ force: true })).resolves.toMatchObject({ phase });
    owner.dispose();
    client.dispose();
  });

  it('keeps previous Station data as stale after a 5xx refresh', async () => {
    let attempt = 0;
    const client = createReadQueryClient(apiWith(async () => {
      attempt += 1;
      if (attempt === 1) return stationPage();
      throw new ApiServerError(httpDetails(503));
    }));
    const owner = createStationResultsQuery(client, () => baseFilters);

    await owner.refresh({ force: true });
    const stale = await owner.refresh({ force: true });

    expect(stale).toMatchObject({
      phase: 'stale',
      data: { items: [{ stationId: 'station-a' }] }
    });
    owner.dispose();
    client.dispose();
  });

  it('aborts superseded local result requests and lets the latest page win', async () => {
    const pending: Array<{
      readonly path: string;
      readonly signal?: AbortSignal;
      resolve(value: unknown): void;
      reject(error: unknown): void;
    }> = [];
    const get = vi.fn((path: string, options: ApiGetOptions = {}) => new Promise<unknown>((resolve, reject) => {
      const entry = {
        path,
        ...(options.signal ? { signal: options.signal } : {}),
        resolve,
        reject
      };
      options.signal?.addEventListener('abort', () => reject(new ApiAbortError(path)), { once: true });
      pending.push(entry);
    }));
    let filters = baseFilters;
    const client = createReadQueryClient(apiWith(get));
    const owner = createLocalResultsQuery(client, () => projectId, () => filters);

    const first = owner.refresh({ force: true });
    await Promise.resolve();
    filters = { ...baseFilters, outcome: 'Ng', page: 2 };
    const second = owner.refresh({ force: true });
    await Promise.resolve();

    expect(pending[0]?.signal?.aborted).toBe(true);
    expect(pending[1]?.path).toContain('status=Ng&pageIndex=1');
    pending[1]?.resolve(localPage());
    await Promise.all([first, second]);
    expect(owner.state.value).toMatchObject({ phase: 'success', data: { totalCount: 1 } });

    owner.dispose();
    client.dispose();
  });

  it('maps malformed and unknown payloads to decode failure', async () => {
    const client = createReadQueryClient(apiWith(async () => ({
      ...stationPage(),
      items: [{ ...stationPage().items[0], executionOutcome: 'Mystery' }]
    })));
    const owner = createStationResultsQuery(client, () => baseFilters);

    await expect(owner.refresh({ force: true })).resolves.toMatchObject({
      phase: 'error',
      failure: { kind: 'decode' }
    });
    owner.dispose();
    client.dispose();
  });

  it('rejects local list data whose project identity differs from the request', async () => {
    const client = createReadQueryClient(apiWith(async () => ({
      ...localPage(),
      items: [{ ...localPage().items[0], projectId: crypto.randomUUID() }]
    })));
    const owner = createLocalResultsQuery(client, () => projectId, () => baseFilters);

    await expect(owner.refresh({ force: true })).resolves.toMatchObject({
      phase: 'error',
      failure: { kind: 'decode' }
    });
    owner.dispose();
    client.dispose();
  });
});
