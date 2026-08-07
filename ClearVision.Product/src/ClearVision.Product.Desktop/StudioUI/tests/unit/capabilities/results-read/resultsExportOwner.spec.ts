import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  createResultsExportOwner,
  type ResultsExportJobSnapshotV1,
  type ResultsExportScopeV1
} from '@/capabilities/results-read';
import {
  ApiAbortError,
  ApiNetworkError,
  type ApiBlobResponse,
  type ApiGetOptions,
  type ApiTransport,
  type ApiWriteOptions
} from '@/platform/api';

const projectId = '11111111-1111-4111-8111-111111111111';
const exportId = '22222222-2222-4222-8222-222222222222';

const scope: ResultsExportScopeV1 = Object.freeze({
  projectId,
  source: 'local',
  startTime: '2026-08-01T00:00:00Z',
  endTime: '2026-08-02T00:00:00Z',
  status: 'Ng',
  defectType: 'Scratch',
  diagnosticCode: 'CAMERA_TIMEOUT'
});

function snapshot(overrides: Partial<ResultsExportJobSnapshotV1> = {}): ResultsExportJobSnapshotV1 {
  return {
    exportId,
    projectId,
    source: 'local',
    format: 'csv',
    clientOperationId: '33333333-3333-4333-8333-333333333333',
    state: 'completed',
    createdAtUtc: '2026-08-02T00:00:00Z',
    updatedAtUtc: '2026-08-02T00:00:01Z',
    snapshotUpperBoundUtc: '2026-08-02T00:00:00Z',
    completedAtUtc: '2026-08-02T00:00:01Z',
    artifactExpiresAtUtc: '2026-08-03T00:00:01Z',
    fileName: 'results.csv',
    errorCode: null,
    errorMessage: null,
    downloadAvailable: true,
    ...overrides
  };
}

function transport(
  get: (path: string, options?: ApiGetOptions) => Promise<unknown>,
  post: (path: string, body: unknown, options?: ApiWriteOptions) => Promise<unknown>,
  getBlob: (path: string, options?: ApiGetOptions) => Promise<ApiBlobResponse>
): ApiTransport {
  return {
    apiBaseUrl: 'http://localhost:5000/api',
    get: get as ApiTransport['get'],
    post: post as NonNullable<ApiTransport['post']>,
    getBlob: getBlob as NonNullable<ApiTransport['getBlob']>
  };
}

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe('resultsExportOwner', () => {
  it('sends the full scope, polls the same job and exposes download after completion', async () => {
    vi.useFakeTimers();
    let clientOperationId = '';
    const post = vi.fn(async (...args: [string, unknown, ApiWriteOptions?]) => {
      const body = args[1];
      clientOperationId = (body as { clientOperationId: string }).clientOperationId;
      return { job: snapshot({
        clientOperationId,
        state: 'queued',
        completedAtUtc: null,
        artifactExpiresAtUtc: null,
        downloadAvailable: false
      }) };
    });
    const get = vi.fn(async () => snapshot({
      clientOperationId,
      state: 'completed'
    }));
    const getBlob = vi.fn(async (): Promise<ApiBlobResponse> => ({
      blob: new Blob(['result']),
      contentType: 'text/csv',
      contentLength: 6,
      etag: null,
      sha256: null,
      headers: new Headers()
    }));
    const owner = createResultsExportOwner({ api: transport(get, post, getBlob), scope });

    await owner.start('csv');

    expect(post).toHaveBeenCalledWith(
      'results/exports',
      expect.objectContaining({
        projectId,
        source: 'local',
        format: 'csv',
        startTime: scope.startTime,
        endTime: scope.endTime,
        status: scope.status,
        defectType: scope.defectType,
        diagnosticCode: scope.diagnosticCode,
        clientOperationId: expect.stringMatching(/^[0-9a-f-]{36}$/i)
      }),
      expect.objectContaining({ signal: expect.any(AbortSignal) })
    );
    expect(owner.projection.phase).toBe('queued');
    expect(owner.projection.canCancel).toBe(true);

    await vi.advanceTimersByTimeAsync(650);

    expect(get).toHaveBeenCalledWith(
      `results/exports/${exportId}`,
      expect.objectContaining({ signal: expect.any(AbortSignal) })
    );
    expect(owner.projection.phase).toBe('completed');
    expect(owner.projection.canDownload).toBe(true);
    owner.dispose();
  });

  it('reconciles a network-ambiguous create without retrying the create request', async () => {
    let clientOperationId = '';
    const post = vi.fn(async (...args: [string, unknown, ApiWriteOptions?]) => {
      const body = args[1];
      clientOperationId = (body as { clientOperationId: string }).clientOperationId;
      throw new ApiNetworkError('http://localhost:5000/api/results/exports', new Error('offline'));
    });
    const get = vi.fn(async (path: string) => {
      expect(path).toBe(`results/exports/by-operation/${clientOperationId}`);
      return snapshot({
        clientOperationId,
        format: 'json',
        fileName: 'results.json'
      });
    });
    const getBlob = vi.fn(async (): Promise<ApiBlobResponse> => ({
      blob: new Blob(['result']),
      contentType: 'application/json',
      contentLength: 6,
      etag: null,
      sha256: null,
      headers: new Headers()
    }));
    const owner = createResultsExportOwner({ api: transport(get, post, getBlob), scope });

    await owner.start('json');

    expect(post).toHaveBeenCalledOnce();
    expect(get).toHaveBeenCalledOnce();
    expect(owner.projection.phase).toBe('completed');
    expect(owner.projection.clientOperationId).toBe(clientOperationId);
    expect(owner.projection.canDownload).toBe(true);
    owner.dispose();
  });

  it('cancels a queued job through the job endpoint and keeps the artifact unavailable', async () => {
    let clientOperationId = '';
    const post = vi.fn(async (path: string, body: unknown) => {
      if (path === 'results/exports') {
        clientOperationId = (body as { clientOperationId: string }).clientOperationId;
        return { job: snapshot({
          clientOperationId,
          state: 'queued',
          completedAtUtc: null,
          artifactExpiresAtUtc: null,
          downloadAvailable: false
        }) };
      }
      return snapshot({
        clientOperationId,
        state: 'cancelled',
        completedAtUtc: null,
        artifactExpiresAtUtc: null,
        errorCode: 'RESULTS_EXPORT_CANCELLED',
        errorMessage: 'cancelled',
        downloadAvailable: false
      });
    });
    const get = vi.fn(async () => undefined);
    const getBlob = vi.fn(async (): Promise<ApiBlobResponse> => ({
      blob: new Blob(['result']),
      contentType: 'text/csv',
      contentLength: 6,
      etag: null,
      sha256: null,
      headers: new Headers()
    }));
    const owner = createResultsExportOwner({ api: transport(get, post, getBlob), scope });

    await owner.start('csv');
    await owner.cancel();

    expect(post).toHaveBeenNthCalledWith(
      2,
      `results/exports/${exportId}/cancel`,
      {},
      expect.objectContaining({ signal: expect.any(AbortSignal) })
    );
    expect(owner.projection.phase).toBe('cancelled');
    expect(owner.projection.canDownload).toBe(false);
    owner.dispose();
  });

  it('aborts an in-flight create and freezes the projection at disposed', async () => {
    const signals: AbortSignal[] = [];
    const post = vi.fn((...args: [string, unknown, ApiWriteOptions?]) => new Promise<unknown>((_resolve, reject) => {
      const options = args[2];
      const signal = options?.signal;
      if (!signal) throw new Error('missing signal');
      signals.push(signal);
      signal.addEventListener('abort', () => reject(new ApiAbortError('http://localhost:5000/api/results/exports')), { once: true });
    }));
    const get = vi.fn(async () => undefined);
    const getBlob = vi.fn(async (): Promise<ApiBlobResponse> => ({
      blob: new Blob(['result']),
      contentType: 'text/csv',
      contentLength: 6,
      etag: null,
      sha256: null,
      headers: new Headers()
    }));
    const owner = createResultsExportOwner({ api: transport(get, post, getBlob), scope });

    const start = owner.start('csv');
    await Promise.resolve();
    owner.dispose();
    await start;

    expect(signals).toHaveLength(1);
    expect(signals[0]?.aborted).toBe(true);
    expect(owner.projection.phase).toBe('disposed');
    expect(owner.projection.canStart).toBe(false);
  });

  it('downloads a completed artifact using the server filename and records its checksum', async () => {
    let clientOperationId = '';
    const post = vi.fn(async (...args: [string, unknown, ApiWriteOptions?]) => {
      const body = args[1];
      clientOperationId = (body as { clientOperationId: string }).clientOperationId;
      return { job: snapshot({ clientOperationId }) };
    });
    const get = vi.fn(async () => undefined);
    const blob = new Blob(['result']);
    const getBlob = vi.fn(async (): Promise<ApiBlobResponse> => ({
      blob,
      contentType: 'text/csv',
      contentLength: blob.size,
      etag: 'etag-1',
      sha256: null,
      headers: new Headers({ 'Content-Disposition': "attachment; filename*=UTF-8''results%20export.csv" })
    }));
    const click = vi.fn();
    const anchor = { href: '', download: '', click } as unknown as HTMLAnchorElement;
    vi.spyOn(document, 'createElement').mockReturnValue(anchor);
    const createObjectURL = vi.fn(() => 'blob:results');
    const revokeObjectURL = vi.fn();
    vi.stubGlobal('URL', { createObjectURL, revokeObjectURL });
    const owner = createResultsExportOwner({ api: transport(get, post, getBlob), scope });

    await owner.start('csv');
    await owner.download();

    expect(getBlob).toHaveBeenCalledWith(
      `results/exports/${exportId}/download`,
      expect.objectContaining({ signal: expect.any(AbortSignal) })
    );
    expect(anchor.download).toBe('results export.csv');
    expect(click).toHaveBeenCalledOnce();
    expect(createObjectURL).toHaveBeenCalledWith(blob);
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:results');
    expect(owner.projection.phase).toBe('completed');
    expect(owner.projection.downloadedSha256).toMatch(/^[0-9a-f]{64}$/i);
    owner.dispose();
  });
});
