import { describe, expect, it, vi } from 'vitest';
import { ApiAbortError, type ApiGetOptions, type ApiTransport } from '@/platform/api';
import { createResultEvidenceOwner } from '@/capabilities/results-read/resultEvidenceOwner';

describe('resultEvidenceOwner', () => {
  it('loads manifest identity, exports one result, and revokes its blob URL', async () => {
    const revoke = vi.fn();
    const create = vi.fn(() => 'blob:evidence');
    vi.stubGlobal('URL', { ...URL, createObjectURL: create, revokeObjectURL: revoke });
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    const projectId = crypto.randomUUID();
    const resultId = crypto.randomUUID();
    const api = {
      apiBaseUrl: 'http://localhost/api',
      get: vi.fn(async () => ({ status: 'available', message: 'ok', manifest: {
        schemaVersion: 1, manifestId: 'manifest-1', projectId, inspectionResultId: resultId, status: 'available', outcome: 'OK',
        createdAtUtc: '2026-07-22T00:00:00Z', flowVersionHash: 'flow', calibrationBundleId: null, sessionId: null, runId: null,
        retentionClass: 'standard', retentionExpiresAtUtc: null, totalBytes: 4, checksum: 'sha', redaction: { applied: true },
        items: [{ id: 'item-1', role: 'output-image', contentType: 'image/png', relativePath: 'output.png', sizeBytes: 4, sha256: 'sha', available: true, missingReason: null }]
      } })),
      getBlob: vi.fn(async () => ({ blob: new Blob([new Uint8Array([1, 2])]), contentType: 'application/zip', contentLength: 2, etag: null, sha256: null, headers: new Headers({ 'Content-Disposition': 'attachment; filename="evidence.zip"' }) }))
    } as unknown as ApiTransport;
    const owner = createResultEvidenceOwner({
      projectId,
      resultId,
      api,
      context: {
        evidenceStatus: 'available',
        hasEvidenceManifest: true,
        hasImage: false,
        imageReference: null,
        hasOutputData: true,
        hasAnalysisData: false
      }
    });
    await owner.load();
    expect(owner.projection).toMatchObject({
      phase: 'available',
      canExport: true,
      manifest: { manifestId: 'manifest-1', redactionApplied: true },
      image: { phase: 'not-produced', objectUrl: null }
    });
    await owner.exportEvidence();
    expect(click).toHaveBeenCalledOnce();
    expect(revoke).toHaveBeenCalledWith('blob:evidence');
    owner.dispose();
    click.mockRestore();
    vi.unstubAllGlobals();
  });

  it('loads an authenticated local image and distinguishes retained summary from request failure', async () => {
    const revoke = vi.fn();
    const create = vi.fn(() => 'blob:result-image');
    vi.stubGlobal('URL', { ...URL, createObjectURL: create, revokeObjectURL: revoke });
    const projectId = crypto.randomUUID();
    const resultId = crypto.randomUUID();
    const getBlob = vi.fn(async () => ({
      blob: new Blob([new Uint8Array([1])], { type: 'image/png' }),
      contentType: 'image/png',
      contentLength: 1,
      etag: null,
      sha256: null,
      headers: new Headers()
    }));
    const api = {
      apiBaseUrl: 'http://localhost/api',
      get: vi.fn(async () => ({ status: 'missing', errorCode: 'EvidenceManifestMissing', message: 'missing', manifest: null })),
      getBlob
    } as unknown as ApiTransport;
    const owner = createResultEvidenceOwner({
      projectId,
      resultId,
      api,
      context: {
        evidenceStatus: 'missing',
        hasEvidenceManifest: false,
        hasImage: true,
        imageReference: `/api/images/${crypto.randomUUID()}`,
        hasOutputData: false,
        hasAnalysisData: false
      }
    });

    await owner.load();

    expect(getBlob).toHaveBeenCalledWith(expect.stringMatching(/^images\//), expect.any(Object));
    expect(owner.projection).toMatchObject({
      phase: 'retained-summary-only',
      image: { phase: 'available', objectUrl: 'blob:result-image' }
    });
    owner.dispose();
    expect(revoke).toHaveBeenCalledWith('blob:result-image');
    vi.unstubAllGlobals();
  });

  it.each([
    ['disabled', false, false, 'not-produced'],
    ['missing', false, true, 'retained-summary-only'],
    ['expired', true, true, 'expired']
  ] as const)('maps server %s without a manifest to %s', async (
    evidenceStatus,
    hasEvidenceManifest,
    hasOutputData,
    expectedPhase
  ) => {
    const api = {
      apiBaseUrl: 'http://localhost/api',
      get: vi.fn(async () => ({ status: evidenceStatus, message: evidenceStatus, manifest: null })),
      getBlob: vi.fn()
    } as unknown as ApiTransport;
    const owner = createResultEvidenceOwner({
      projectId: crypto.randomUUID(),
      resultId: crypto.randomUUID(),
      api,
      context: {
        evidenceStatus,
        hasEvidenceManifest,
        hasImage: false,
        imageReference: null,
        hasOutputData,
        hasAnalysisData: false
      }
    });

    await owner.load();

    expect(owner.projection.phase).toBe(expectedPhase);
    expect(owner.projection.image.phase).toBe('not-produced');
    expect(api.getBlob).not.toHaveBeenCalled();
    owner.dispose();
  });

  it('aborts an in-flight manifest request and rejects its late completion after dispose', async () => {
    let observedSignal: AbortSignal | undefined;
    const api = {
      apiBaseUrl: 'http://localhost/api',
      get: vi.fn((_path: string, options: ApiGetOptions = {}) => new Promise<unknown>((_resolve, reject) => {
        observedSignal = options.signal;
        options.signal?.addEventListener('abort', () => reject(new ApiAbortError('manifest')), { once: true });
      })),
      getBlob: vi.fn()
    } as unknown as ApiTransport;
    const owner = createResultEvidenceOwner({
      projectId: crypto.randomUUID(),
      resultId: crypto.randomUUID(),
      api,
      context: {
        evidenceStatus: 'missing',
        hasEvidenceManifest: false,
        hasImage: false,
        imageReference: null,
        hasOutputData: false,
        hasAnalysisData: false
      }
    });

    const load = owner.load();
    await Promise.resolve();
    owner.dispose();
    await load;

    expect(observedSignal?.aborted).toBe(true);
    expect(owner.projection).toMatchObject({ phase: 'disposed', image: { phase: 'disposed' } });
  });

  it('rejects a manifest whose result identity differs from the requested evidence', async () => {
    const projectId = crypto.randomUUID();
    const resultId = crypto.randomUUID();
    const api = {
      apiBaseUrl: 'http://localhost/api',
      get: vi.fn(async () => ({ status: 'available', message: 'ok', manifest: {
        manifestId: 'manifest-mismatch',
        projectId,
        inspectionResultId: crypto.randomUUID(),
        status: 'available',
        outcome: 'NG',
        retentionClass: 'standard',
        retentionExpiresAtUtc: null,
        totalBytes: 0,
        checksum: null,
        items: [],
        redaction: { applied: false }
      } })),
      getBlob: vi.fn()
    } as unknown as ApiTransport;
    const owner = createResultEvidenceOwner({
      projectId,
      resultId,
      api,
      context: {
        evidenceStatus: 'available',
        hasEvidenceManifest: true,
        hasImage: false,
        imageReference: null,
        hasOutputData: true,
        hasAnalysisData: false
      }
    });

    await owner.load();

    expect(owner.projection).toMatchObject({
      phase: 'load-failed',
      manifest: null,
      canExport: false
    });
    owner.dispose();
  });

  it('does not abort an in-flight image when evidence export starts', async () => {
    const create = vi.fn()
      .mockReturnValueOnce('blob:evidence-export')
      .mockReturnValueOnce('blob:result-image');
    const revoke = vi.fn();
    vi.stubGlobal('URL', { ...URL, createObjectURL: create, revokeObjectURL: revoke });
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    const projectId = crypto.randomUUID();
    const resultId = crypto.randomUUID();
    const imageId = crypto.randomUUID();
    let imageSignal: AbortSignal | undefined;
    let resolveImage: ((value: {
      blob: Blob;
      contentType: string;
      contentLength: number;
      etag: null;
      sha256: null;
      headers: Headers;
    }) => void) | undefined;
    const api = {
      apiBaseUrl: 'http://localhost/api',
      get: vi.fn(async () => ({ status: 'available', message: 'ok', manifest: {
        manifestId: 'manifest-concurrent', projectId, inspectionResultId: resultId,
        status: 'available', outcome: 'NG', retentionClass: 'standard',
        retentionExpiresAtUtc: null, totalBytes: 1, checksum: null, items: [],
        redaction: { applied: false }
      } })),
      getBlob: vi.fn((path: string, options: ApiGetOptions = {}) => {
        if (path.startsWith('images/')) {
          imageSignal = options.signal;
          return new Promise(resolve => { resolveImage = resolve; });
        }
        return Promise.resolve({
          blob: new Blob([new Uint8Array([1])], { type: 'application/zip' }),
          contentType: 'application/zip',
          contentLength: 1,
          etag: null,
          sha256: null,
          headers: new Headers({ 'Content-Disposition': 'attachment; filename="evidence.zip"' })
        });
      })
    } as unknown as ApiTransport;
    const owner = createResultEvidenceOwner({
      projectId,
      resultId,
      api,
      context: {
        evidenceStatus: 'available',
        hasEvidenceManifest: true,
        hasImage: true,
        imageReference: `/api/images/${imageId}`,
        hasOutputData: true,
        hasAnalysisData: false
      }
    });

    const loading = owner.load();
    await vi.waitFor(() => expect(owner.projection.canExport).toBe(true));
    await owner.exportEvidence();

    expect(imageSignal?.aborted).toBe(false);
    resolveImage?.({
      blob: new Blob([new Uint8Array([2])], { type: 'image/png' }),
      contentType: 'image/png',
      contentLength: 1,
      etag: null,
      sha256: null,
      headers: new Headers()
    });
    await loading;
    expect(owner.projection.image).toMatchObject({ phase: 'available', objectUrl: 'blob:result-image' });
    owner.dispose();
    click.mockRestore();
    vi.unstubAllGlobals();
  });
});
