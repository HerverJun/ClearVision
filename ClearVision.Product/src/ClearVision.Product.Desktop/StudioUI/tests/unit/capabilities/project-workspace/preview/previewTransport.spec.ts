import { runInNewContext } from 'node:vm';
import { describe, expect, it, vi } from 'vitest';
import {
  ApiNotFoundError,
  type ApiBlobResponse,
  type ApiTransport
} from '@/platform/api';
import {
  PreviewArtifactIntegrityError,
  createPreviewTransportPort
} from '@/capabilities/project-workspace/preview/previewTransport';

const projectId = '11111111-1111-4111-8111-111111111111';
const nodeId = '22222222-2222-4222-8222-222222222222';
const debugSessionId = '33333333-3333-4333-8333-333333333333';
const artifactId = 'A'.repeat(43);
const sha256 = '9f64a747e1b97f131fabb6b447296c9b6f0201e79fb3c5356e6c77e89b6a806a';

function apiPost(value: unknown): NonNullable<ApiTransport['post']> {
  return value as NonNullable<ApiTransport['post']>;
}

function response(overrides: Record<string, unknown> = {}) {
  return {
    success: true,
    projectId,
    targetNodeId: nodeId,
    debugSessionId,
    executionTimeMs: 12,
    inputImageBase64: null,
    outputImageBase64: null,
    outputData: { score: 0.9 },
    errorMessage: null,
    failedOperatorId: null,
    failedOperatorName: null,
    failedOperatorType: null,
    diagnostics: [],
    missingResources: [],
    artifacts: [{
      artifactId,
      kind: 'image',
      role: 'outputImage',
      pathHint: '$.output',
      contentType: 'image/png',
      length: 4,
      sha256,
      createdAtUtc: '2026-07-17T00:00:00Z',
      expiresAtUtc: '2026-07-17T00:10:00Z',
      width: 1,
      height: 1,
      channels: 4
    }],
    observation: {
      schemaVersion: 'execution-observation.v1',
      identity: {
        projectId,
        targetNodeId: nodeId,
        debugSessionId,
        clientRequestSequence: 7,
        flowRevision: 3
      },
      outcome: {
        success: true,
        executionTimeMs: 12,
        errorMessage: null,
        failedOperatorId: null,
        failedOperatorName: null,
        failedOperatorType: null,
        executedOperatorCount: 1
      },
      diagnostics: []
    },
    ...overrides
  };
}

function command() {
  return {
    projectId,
    targetNodeId: nodeId,
    debugSessionId,
    clientRequestSequence: 7,
    flowRevision: 3,
    flowData: Object.freeze({ operators: Object.freeze([]), connections: Object.freeze([]) }),
    clientSnapshotHash: 'snapshot-hash'
  };
}

function blobResponse(overrides: Partial<ApiBlobResponse> = {}): ApiBlobResponse {
  const headers = new Headers({
    'Content-Type': 'image/png',
    ETag: `"${sha256}"`,
    'X-Artifact-Sha256': sha256
  });
  return Object.freeze({
    blob: new Blob([new Uint8Array([1, 2, 3, 4])], { type: 'image/png' }),
    contentType: 'image/png',
    contentLength: 4,
    etag: `"${sha256}"`,
    sha256,
    headers,
    ...overrides
  });
}

function crossRealmBlob(bytes: ArrayBuffer | Uint8Array): Blob {
  const arrayBuffer = bytes instanceof Uint8Array ? bytes.buffer : bytes;
  return {
    size: arrayBuffer.byteLength,
    type: 'image/png',
    arrayBuffer: async () => arrayBuffer
  } as unknown as Blob;
}

describe('G4 Preview transport', () => {
  it('uses strict identity, registers artifacts, validates binary metadata and cleans up', async () => {
    const post = vi.fn(async () => response());
    const getBlob = vi.fn(async () => blobResponse());
    const deleteRequest = vi.fn(async () => undefined);
    const api: ApiTransport = {
      apiBaseUrl: 'http://localhost:5000/api',
      get: vi.fn(),
      post: apiPost(post),
      getBlob,
      delete: deleteRequest
    };
    const port = createPreviewTransportPort(api);

    await expect(port.previewNode(command())).resolves.toMatchObject({ success: true, projectId });
    expect(post).toHaveBeenCalledWith(
      'flows/preview-node',
      expect.objectContaining({ artifactMode: 'references', flowRevision: 3 }),
      {}
    );
    expect(port.getResourceDiagnostics().trackedArtifactIds).toBe(1);
    await expect(port.getPreviewArtifactBlob(artifactId)).resolves.toMatchObject({ blob: expect.any(Blob) });
    await expect(port.deletePreviewArtifact(artifactId)).resolves.toBeUndefined();
    expect(port.getResourceDiagnostics()).toMatchObject({
      trackedArtifactIds: 0,
      inFlightPreview: 0,
      inFlightArtifactReads: 0,
      inFlightArtifactDeletes: 0
    });
    port.dispose();
  });

  it('fails closed on observation identity mismatch', async () => {
    const deleteRequest = vi.fn(async () => undefined);
    const api: ApiTransport = {
      apiBaseUrl: 'http://localhost:5000/api',
      get: vi.fn(),
      post: apiPost(vi.fn(async () => response({
        observation: {
          ...response().observation,
          identity: { ...response().observation.identity, flowRevision: 2 }
        }
      }))),
      getBlob: vi.fn(),
      delete: deleteRequest
    };
    const port = createPreviewTransportPort(api);
    await expect(port.previewNode(command())).rejects.toMatchObject({
      path: '$.observation.identity.flowRevision',
      expectation: 'to match the active preview request identity'
    });
    await port.settle();
    expect(deleteRequest).toHaveBeenCalledWith(
      `preview-artifacts/${artifactId}`,
      expect.objectContaining({ signal: expect.any(AbortSignal) })
    );
    expect(port.getResourceDiagnostics().inFlightPreview).toBe(0);
    expect(port.getResourceDiagnostics().trackedArtifactIds).toBe(0);
    port.dispose();
  });

  it('finishes artifact cleanup after disposal instead of aborting DELETE', async () => {
    let releaseDelete: (() => void) | undefined;
    const deleteRequest = vi.fn(() => new Promise<void>(resolve => { releaseDelete = resolve; }));
    const api: ApiTransport = {
      apiBaseUrl: 'http://localhost:5000/api',
      get: vi.fn(),
      post: apiPost(vi.fn(async () => response())),
      getBlob: vi.fn(),
      delete: deleteRequest
    };
    const port = createPreviewTransportPort(api);
    await port.previewNode(command());

    port.dispose();
    expect(deleteRequest).toHaveBeenCalledTimes(1);
    expect(port.getResourceDiagnostics()).toMatchObject({
      disposed: true,
      activeAbortControllers: 1,
      inFlightArtifactDeletes: 1,
      trackedArtifactIds: 1
    });

    releaseDelete?.();
    await port.settle();
    expect(port.getResourceDiagnostics()).toMatchObject({
      activeAbortControllers: 0,
      inFlightArtifactDeletes: 0,
      trackedArtifactIds: 0
    });
  });

  it('rejects artifact metadata mismatch before ImageCanvas can consume the blob', async () => {
    const api: ApiTransport = {
      apiBaseUrl: 'http://localhost:5000/api',
      get: vi.fn(),
      post: apiPost(vi.fn(async () => response())),
      getBlob: vi.fn(async () => blobResponse({ contentLength: 5 })),
      delete: vi.fn()
    };
    const port = createPreviewTransportPort(api);
    await port.previewNode(command());
    await expect(port.getPreviewArtifactBlob(artifactId)).rejects.toBeInstanceOf(PreviewArtifactIntegrityError);
    port.dispose();
  });

  it('hashes the actual Blob bytes and rejects a same-length body with matching forged headers', async () => {
    const forged = blobResponse({
      blob: new Blob([new Uint8Array([4, 3, 2, 1])], { type: 'image/png' })
    });
    const api: ApiTransport = {
      apiBaseUrl: 'http://localhost:5000/api',
      get: vi.fn(),
      post: apiPost(vi.fn(async () => response())),
      getBlob: vi.fn(async () => forged),
      delete: vi.fn()
    };
    const port = createPreviewTransportPort(api);
    await port.previewNode(command());

    await expect(port.getPreviewArtifactBlob(artifactId))
      .rejects.toThrow(/actual Blob bytes|bytes did not match/i);
    port.dispose();
  });

  it.each([
    ['ArrayBuffer', () => runInNewContext('Uint8Array.from([1, 2, 3, 4]).buffer') as ArrayBuffer],
    ['Uint8Array', () => runInNewContext('Uint8Array.from([1, 2, 3, 4])') as Uint8Array]
  ] as const)('normalizes cross-realm %s bytes before Web Crypto SHA-256', async (_kind, createBytes) => {
    const api: ApiTransport = {
      apiBaseUrl: 'http://localhost:5000/api',
      get: vi.fn(),
      post: apiPost(vi.fn(async () => response())),
      getBlob: vi.fn(async () => blobResponse({ blob: crossRealmBlob(createBytes()) })),
      delete: vi.fn()
    };
    const port = createPreviewTransportPort(api);
    await port.previewNode(command());

    await expect(port.getPreviewArtifactBlob(artifactId))
      .resolves.toMatchObject({ blob: expect.anything() });
    port.dispose();
  });

  it('treats artifact DELETE 404 as an already released terminal state', async () => {
    const api: ApiTransport = {
      apiBaseUrl: 'http://localhost:5000/api',
      get: vi.fn(),
      post: apiPost(vi.fn(async () => response())),
      getBlob: vi.fn(),
      delete: vi.fn(async () => {
        throw new ApiNotFoundError({
          url: `http://localhost:5000/api/preview-artifacts/${artifactId}`,
          status: 404,
          statusText: 'Not Found',
          payload: null,
          responseBody: ''
        });
      })
    };
    const port = createPreviewTransportPort(api);
    await port.previewNode(command());
    await expect(port.deletePreviewArtifact(artifactId)).resolves.toBeUndefined();
    expect(port.getResourceDiagnostics().trackedArtifactIds).toBe(0);
    port.dispose();
  });
});
