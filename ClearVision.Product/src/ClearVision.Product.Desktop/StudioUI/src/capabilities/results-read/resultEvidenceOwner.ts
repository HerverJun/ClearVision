import { reactive, readonly, type DeepReadonly } from 'vue';
import { ApiAbortError, type ApiTransport } from '@/platform/api';

export interface ResultEvidenceItemV1 {
  readonly id: string;
  readonly role: string;
  readonly contentType: string;
  readonly relativePath: string | null;
  readonly sizeBytes: number;
  readonly sha256: string | null;
  readonly available: boolean;
  readonly missingReason: string | null;
}

export interface ResultEvidenceManifestV1 {
  readonly manifestId: string;
  readonly projectId: string;
  readonly resultId: string;
  readonly status: string;
  readonly outcome: string;
  readonly flowVersionHash: string | null;
  readonly calibrationBundleId: string | null;
  readonly runId: string | null;
  readonly sessionId: string | null;
  readonly retentionClass: string;
  readonly retentionExpiresAtUtc: string | null;
  readonly totalBytes: number;
  readonly checksum: string | null;
  readonly items: readonly ResultEvidenceItemV1[];
  readonly redactionApplied: boolean;
}

export interface ResultEvidenceProjection {
  readonly phase: 'idle' | 'loading' | 'available' | 'partial' | 'missing' | 'expired' | 'disabled' | 'error' | 'exporting' | 'disposed';
  readonly manifest: ResultEvidenceManifestV1 | null;
  readonly message: string;
  readonly canExport: boolean;
}

type MutableProjection = { -readonly [Key in keyof ResultEvidenceProjection]: ResultEvidenceProjection[Key] };

export interface ResultEvidenceOwner {
  readonly projection: DeepReadonly<ResultEvidenceProjection>;
  load(): Promise<void>;
  exportEvidence(): Promise<void>;
  dispose(): void;
}

function record(value: unknown): Readonly<Record<string, unknown>> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? value as Readonly<Record<string, unknown>>
    : Object.freeze({});
}
function field(source: Readonly<Record<string, unknown>>, camel: string): unknown {
  if (Object.prototype.hasOwnProperty.call(source, camel)) return source[camel];
  return source[`${camel.slice(0, 1).toUpperCase()}${camel.slice(1)}`];
}
function text(value: unknown): string { return typeof value === 'string' ? value.trim() : ''; }
function nullableText(value: unknown): string | null { return text(value) || null; }

function decodeManifest(payload: unknown): Readonly<{ status: string; message: string; manifest: ResultEvidenceManifestV1 | null }> {
  const envelope = record(payload);
  const status = text(field(envelope, 'status')).toLowerCase() || 'missing';
  const message = text(field(envelope, 'message')) || '证据清单不可用。';
  const raw = field(envelope, 'manifest');
  if (raw === null || raw === undefined) return Object.freeze({ status, message, manifest: null });
  const source = record(raw);
  const items = field(source, 'items');
  if (!Array.isArray(items)) throw new TypeError('Evidence manifest items 必须是数组。');
  const manifest = Object.freeze({
    manifestId: text(field(source, 'manifestId')),
    projectId: text(field(source, 'projectId')),
    resultId: text(field(source, 'inspectionResultId')),
    status: text(field(source, 'status')),
    outcome: text(field(source, 'outcome')),
    flowVersionHash: nullableText(field(source, 'flowVersionHash')),
    calibrationBundleId: nullableText(field(source, 'calibrationBundleId')),
    runId: nullableText(field(source, 'runId')),
    sessionId: nullableText(field(source, 'sessionId')),
    retentionClass: text(field(source, 'retentionClass')) || 'standard',
    retentionExpiresAtUtc: nullableText(field(source, 'retentionExpiresAtUtc')),
    totalBytes: Number(field(source, 'totalBytes')) || 0,
    checksum: nullableText(field(source, 'checksum')),
    items: Object.freeze(items.map(entry => {
      const item = record(entry);
      return Object.freeze({
        id: text(field(item, 'id')),
        role: text(field(item, 'role')),
        contentType: text(field(item, 'contentType')) || 'application/octet-stream',
        relativePath: nullableText(field(item, 'relativePath')),
        sizeBytes: Number(field(item, 'sizeBytes')) || 0,
        sha256: nullableText(field(item, 'sha256')),
        available: field(item, 'available') === true,
        missingReason: nullableText(field(item, 'missingReason'))
      });
    })),
    redactionApplied: field(record(field(source, 'redaction')), 'applied') === true
  } satisfies ResultEvidenceManifestV1);
  if (!manifest.manifestId || !manifest.projectId || !manifest.resultId) throw new TypeError('Evidence manifest identity 不完整。');
  return Object.freeze({ status, message, manifest });
}

function contentDispositionFileName(headers: Headers): string {
  const value = headers.get('Content-Disposition') ?? '';
  const utf8 = /filename\*=UTF-8''([^;]+)/i.exec(value)?.[1];
  if (utf8) return decodeURIComponent(utf8);
  return /filename="?([^";]+)"?/i.exec(value)?.[1] ?? 'clearvision-evidence.zip';
}

export function createResultEvidenceOwner(options: {
  readonly projectId: string;
  readonly resultId: string;
  readonly api: ApiTransport;
}): ResultEvidenceOwner {
  if (!options.api.get || !options.api.getBlob) throw new TypeError('Evidence 需要 shared ApiTransport GET 与 blob GET。');
  let disposed = false;
  let generation = 0;
  let controller: AbortController | null = null;
  let activeUrl: string | null = null;
  const state = reactive<MutableProjection>({ phase: 'idle', manifest: null, message: '等待读取证据清单。', canExport: false });
  const base = `inspection/history/${encodeURIComponent(options.projectId)}/${encodeURIComponent(options.resultId)}/evidence`;

  function releaseUrl(): void {
    if (!activeUrl) return;
    URL.revokeObjectURL(activeUrl);
    activeUrl = null;
  }

  const owner: ResultEvidenceOwner = Object.freeze({
    projection: readonly(state),
    async load(): Promise<void> {
      if (disposed) return;
      const operation = ++generation;
      controller?.abort('evidence-load-superseded');
      controller = new AbortController();
      state.phase = 'loading';
      state.message = '正在读取证据清单。';
      try {
        const decoded = decodeManifest(await options.api.get!(`${base}/manifest`, { signal: controller.signal }));
        if (disposed || operation !== generation) return;
        state.manifest = decoded.manifest;
        state.phase = ['available', 'partial', 'expired', 'disabled'].includes(decoded.status)
          ? decoded.status as ResultEvidenceProjection['phase']
          : decoded.manifest ? 'available' : 'missing';
        state.message = decoded.message;
        state.canExport = state.phase === 'available' || state.phase === 'partial';
      } catch (error) {
        if (disposed || operation !== generation || error instanceof ApiAbortError) return;
        state.phase = 'error';
        state.message = `证据清单读取失败：${error instanceof Error ? error.message : '响应不可用。'}`;
        state.canExport = false;
      }
    },
    async exportEvidence(): Promise<void> {
      if (disposed || !state.canExport) return;
      const operation = ++generation;
      controller?.abort('evidence-export-superseded');
      controller = new AbortController();
      state.phase = 'exporting';
      state.message = '正在导出本条证据。';
      try {
        const response = await options.api.getBlob!(`${base}/export`, { signal: controller.signal });
        if (disposed || operation !== generation) return;
        releaseUrl();
        activeUrl = URL.createObjectURL(response.blob);
        const anchor = document.createElement('a');
        anchor.href = activeUrl;
        anchor.download = contentDispositionFileName(response.headers);
        anchor.click();
        releaseUrl();
        state.phase = state.manifest?.items.some(item => !item.available) ? 'partial' : 'available';
        state.message = '本条证据导出已开始。';
        state.canExport = true;
      } catch (error) {
        if (disposed || operation !== generation || error instanceof ApiAbortError) return;
        state.phase = 'error';
        state.message = `证据导出失败：${error instanceof Error ? error.message : '服务端拒绝导出。'}`;
        state.canExport = Boolean(state.manifest);
      }
    },
    dispose(): void {
      if (disposed) return;
      disposed = true;
      generation += 1;
      controller?.abort('result-evidence-owner-disposed');
      controller = null;
      releaseUrl();
      state.phase = 'disposed';
      state.manifest = null;
      state.canExport = false;
    }
  });
  return owner;
}
