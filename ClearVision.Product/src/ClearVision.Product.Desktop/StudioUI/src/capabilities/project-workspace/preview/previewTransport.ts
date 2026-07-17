import {
  ApiNotFoundError,
  type ApiBlobResponse,
  type ApiTransport
} from '@/platform/api';
import {
  buildPreviewIdentityV1,
  decodePreviewNodeResponseV1,
  type PreviewArtifactReferenceV1,
  type PreviewIdentityV1,
  type PreviewNodeResponseV1
} from './previewContracts';

export interface PreviewNodeCommand {
  readonly projectId: string;
  readonly targetNodeId: string;
  readonly debugSessionId: string;
  readonly clientRequestSequence: number;
  readonly flowRevision: number;
  readonly flowData: Readonly<Record<string, unknown>>;
  readonly clientSnapshotHash: string;
  readonly inputImageBase64?: string | null;
  readonly parameters?: Readonly<Record<string, unknown>> | null;
  readonly imageFormat?: string;
  readonly timeoutMs?: number;
  readonly signal?: AbortSignal;
}

export interface PreviewTransportDiagnostics {
  readonly inFlightPreview: number;
  readonly inFlightArtifactReads: number;
  readonly inFlightArtifactDeletes: number;
  readonly activeAbortControllers: number;
  readonly trackedArtifactIds: number;
  readonly disposed: boolean;
}

export interface PreviewTransportPort {
  previewNode(command: PreviewNodeCommand): Promise<PreviewNodeResponseV1>;
  getPreviewArtifactBlob(
    artifactId: string,
    options?: Readonly<{ signal?: AbortSignal }>
  ): Promise<Readonly<{ blob: Blob; headers: Headers }>>;
  deletePreviewArtifact(artifactId: string): Promise<void>;
  getResourceDiagnostics(): PreviewTransportDiagnostics;
  subscribeDiagnostics(listener: (value: PreviewTransportDiagnostics) => void): () => void;
  settle(): Promise<void>;
  dispose(): void;
}

export class PreviewArtifactIntegrityError extends Error {
  readonly artifactId: string;

  constructor(artifactId: string, message: string) {
    super(message);
    this.name = 'PreviewArtifactIntegrityError';
    this.artifactId = artifactId;
  }
}

function normalizedContentType(value: string): string {
  return value.split(';', 1)[0]?.trim().toLowerCase() ?? '';
}

function normalizedChecksum(value: string | null): string | null {
  if (!value) return null;
  const normalized = value.trim().replace(/^W\//i, '').replace(/^"|"$/g, '').toLowerCase();
  return /^[0-9a-f]{64}$/.test(normalized) ? normalized : null;
}

const cleanupArtifactIdPattern = /^[A-Za-z0-9_-]{43}$/;

function extractCleanupArtifactIds(payload: unknown): readonly string[] {
  if (typeof payload !== 'object' || payload === null || Array.isArray(payload)) return Object.freeze([]);
  const artifacts = (payload as Readonly<Record<string, unknown>>).artifacts;
  if (!Array.isArray(artifacts)) return Object.freeze([]);
  const ids = new Set<string>();
  for (const artifact of artifacts) {
    if (typeof artifact !== 'object' || artifact === null || Array.isArray(artifact)) continue;
    const artifactId = (artifact as Readonly<Record<string, unknown>>).artifactId;
    if (typeof artifactId === 'string' && cleanupArtifactIdPattern.test(artifactId)) ids.add(artifactId);
  }
  return Object.freeze([...ids]);
}

async function blobSha256(blob: Blob): Promise<string> {
  const subtle = globalThis.crypto?.subtle;
  if (!subtle) throw new Error('Web Crypto SHA-256 is unavailable.');
  const digest = await subtle.digest('SHA-256', await blob.arrayBuffer());
  return [...new Uint8Array(digest)].map(value => value.toString(16).padStart(2, '0')).join('');
}

async function assertArtifactResponse(
  expected: PreviewArtifactReferenceV1,
  response: ApiBlobResponse
): Promise<void> {
  if (normalizedContentType(response.contentType) !== normalizedContentType(expected.contentType)) {
    throw new PreviewArtifactIntegrityError(
      expected.artifactId,
      `Artifact ${expected.artifactId} content type did not match its Preview reference.`
    );
  }
  if (response.contentLength !== expected.length) {
    throw new PreviewArtifactIntegrityError(
      expected.artifactId,
      `Artifact ${expected.artifactId} length did not match its Preview reference.`
    );
  }
  const shaHeader = normalizedChecksum(response.sha256);
  const etag = normalizedChecksum(response.etag);
  if ((shaHeader && shaHeader !== expected.sha256) || (etag && etag !== expected.sha256)) {
    throw new PreviewArtifactIntegrityError(
      expected.artifactId,
      `Artifact ${expected.artifactId} checksum did not match its Preview reference.`
    );
  }
  const actualSha256 = await blobSha256(response.blob);
  if (actualSha256 !== expected.sha256) {
    throw new PreviewArtifactIntegrityError(
      expected.artifactId,
      `Artifact ${expected.artifactId} bytes did not match its Preview SHA-256 reference.`
    );
  }
}

function snapshot(state: {
  inFlightPreview: number;
  inFlightArtifactReads: number;
  inFlightArtifactDeletes: number;
  deleteControllers: Set<AbortController>;
  artifacts: Map<string, PreviewArtifactReferenceV1>;
  disposed: boolean;
}): PreviewTransportDiagnostics {
  return Object.freeze({
    inFlightPreview: state.inFlightPreview,
    inFlightArtifactReads: state.inFlightArtifactReads,
    inFlightArtifactDeletes: state.inFlightArtifactDeletes,
    activeAbortControllers: state.deleteControllers.size,
    trackedArtifactIds: state.artifacts.size,
    disposed: state.disposed
  });
}

export function createPreviewTransportPort(api: ApiTransport): PreviewTransportPort {
  if (typeof api.post !== 'function' || typeof api.getBlob !== 'function' || typeof api.delete !== 'function') {
    throw new TypeError('Preview transport requires POST, blob GET, and DELETE on the shared ApiTransport.');
  }
  const post = api.post.bind(api);
  const getBlob = api.getBlob.bind(api);
  const deleteRequest = api.delete.bind(api);
  const state = {
    inFlightPreview: 0,
    inFlightArtifactReads: 0,
    inFlightArtifactDeletes: 0,
    deleteControllers: new Set<AbortController>(),
    artifacts: new Map<string, PreviewArtifactReferenceV1>(),
    disposed: false
  };
  const listeners = new Set<(value: PreviewTransportDiagnostics) => void>();
  const pending = new Set<Promise<unknown>>();
  const artifactDeletes = new Map<string, Promise<void>>();

  function assertActive(): void {
    if (state.disposed) throw new Error('Preview transport port has been disposed.');
  }

  function publish(): void {
    const value = snapshot(state);
    for (const listener of listeners) listener(value);
  }

  function track<T>(promise: Promise<T>): Promise<T> {
    pending.add(promise);
    promise.finally(() => pending.delete(promise)).catch(() => {});
    return promise;
  }

  function beginArtifactDelete(artifactId: string): Promise<void> {
    const existing = artifactDeletes.get(artifactId);
    if (existing) return existing;
    const controller = new AbortController();
    state.deleteControllers.add(controller);
    state.inFlightArtifactDeletes += 1;
    publish();
    const operation = Promise.resolve(deleteRequest(`preview-artifacts/${encodeURIComponent(artifactId)}`, {
      signal: controller.signal
    })).catch(error => {
      if (error instanceof ApiNotFoundError) return;
      throw error;
    }).finally(() => {
      state.deleteControllers.delete(controller);
      state.artifacts.delete(artifactId);
      state.inFlightArtifactDeletes = Math.max(0, state.inFlightArtifactDeletes - 1);
      artifactDeletes.delete(artifactId);
      publish();
    });
    const tracked = track(operation);
    artifactDeletes.set(artifactId, tracked);
    return tracked;
  }

  function releasePayloadArtifacts(payload: unknown): void {
    void Promise.allSettled(extractCleanupArtifactIds(payload).map(beginArtifactDelete));
  }

  return Object.freeze({
    async previewNode(command: PreviewNodeCommand): Promise<PreviewNodeResponseV1> {
      assertActive();
      const identity: PreviewIdentityV1 = buildPreviewIdentityV1(command);
      state.inFlightPreview += 1;
      publish();
      try {
        const payload = await post<unknown>('flows/preview-node', {
          projectId: command.projectId,
          targetNodeId: command.targetNodeId,
          debugSessionId: command.debugSessionId,
          clientRequestSequence: command.clientRequestSequence,
          flowRevision: command.flowRevision,
          flowData: command.flowData,
          inputImageBase64: command.inputImageBase64 ?? null,
          parameters: command.parameters ?? null,
          imageFormat: command.imageFormat ?? '.png',
          timeoutMs: command.timeoutMs,
          artifactMode: 'references'
        }, command.signal ? { signal: command.signal } : {});
        let response: PreviewNodeResponseV1;
        try {
          response = decodePreviewNodeResponseV1(payload, identity);
        } catch (error) {
          releasePayloadArtifacts(payload);
          throw error;
        }
        if (state.disposed) {
          releasePayloadArtifacts(payload);
          throw new DOMException('Preview transport was disposed before the response could be observed.', 'AbortError');
        }
        for (const artifact of response.artifacts) state.artifacts.set(artifact.artifactId, artifact);
        publish();
        return response;
      } finally {
        state.inFlightPreview = Math.max(0, state.inFlightPreview - 1);
        publish();
      }
    },
    async getPreviewArtifactBlob(
      artifactId: string,
      options: Readonly<{ signal?: AbortSignal }> = {}
    ): Promise<Readonly<{ blob: Blob; headers: Headers }>> {
      assertActive();
      const expected = state.artifacts.get(artifactId);
      if (!expected) {
        throw new PreviewArtifactIntegrityError(artifactId, 'Artifact is not owned by the active Preview transport.');
      }
      state.inFlightArtifactReads += 1;
      publish();
      try {
        const response = await getBlob(`preview-artifacts/${encodeURIComponent(artifactId)}`, options);
        await assertArtifactResponse(expected, response);
        return Object.freeze({ blob: response.blob, headers: response.headers });
      } finally {
        state.inFlightArtifactReads = Math.max(0, state.inFlightArtifactReads - 1);
        publish();
      }
    },
    deletePreviewArtifact(artifactId: string): Promise<void> {
      if (!artifactId || state.disposed) return Promise.resolve();
      return beginArtifactDelete(artifactId);
    },
    getResourceDiagnostics(): PreviewTransportDiagnostics {
      return snapshot(state);
    },
    subscribeDiagnostics(listener: (value: PreviewTransportDiagnostics) => void): () => void {
      assertActive();
      listeners.add(listener);
      listener(snapshot(state));
      return () => listeners.delete(listener);
    },
    async settle(): Promise<void> {
      await Promise.allSettled([...pending]);
    },
    dispose(): void {
      if (state.disposed) return;
      state.disposed = true;
      for (const artifactId of state.artifacts.keys()) void beginArtifactDelete(artifactId).catch(() => {});
      state.inFlightPreview = 0;
      state.inFlightArtifactReads = 0;
      publish();
      listeners.clear();
    }
  });
}
