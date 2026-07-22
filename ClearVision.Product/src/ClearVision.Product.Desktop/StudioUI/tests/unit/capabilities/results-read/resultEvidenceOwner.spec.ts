import { describe, expect, it, vi } from 'vitest';
import type { ApiTransport } from '@/platform/api';
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
    const owner = createResultEvidenceOwner({ projectId, resultId, api });
    await owner.load();
    expect(owner.projection).toMatchObject({ phase: 'available', canExport: true, manifest: { manifestId: 'manifest-1', redactionApplied: true } });
    await owner.exportEvidence();
    expect(click).toHaveBeenCalledOnce();
    expect(revoke).toHaveBeenCalledWith('blob:evidence');
    owner.dispose();
    click.mockRestore();
    vi.unstubAllGlobals();
  });
});
