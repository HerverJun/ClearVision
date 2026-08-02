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

export type ResultEvidencePhase =
  | 'idle'
  | 'loading'
  | 'available'
  | 'partial'
  | 'retained-summary-only'
  | 'expired'
  | 'not-produced'
  | 'load-failed'
  | 'exporting'
  | 'export-error'
  | 'disposed';

export type ResultImagePhase =
  | 'idle'
  | 'loading'
  | 'available'
  | 'retained-summary-only'
  | 'not-produced'
  | 'load-failed'
  | 'disposed';

export interface ResultImageProjection {
  readonly phase: ResultImagePhase;
  readonly objectUrl: string | null;
  readonly message: string;
}

export interface ResultEvidenceProjection {
  readonly phase: ResultEvidencePhase;
  readonly manifest: ResultEvidenceManifestV1 | null;
  readonly message: string;
  readonly canExport: boolean;
  readonly image: ResultImageProjection;
}

type MutableProjection = { -readonly [Key in keyof ResultEvidenceProjection]: ResultEvidenceProjection[Key] };

export interface ResultEvidenceOwner {
  readonly projection: DeepReadonly<ResultEvidenceProjection>;
  load(): Promise<void>;
  exportEvidence(): Promise<void>;
  dispose(): void;
}

export interface ResultEvidenceContext {
  readonly evidenceStatus: string;
  readonly hasEvidenceManifest: boolean;
  readonly hasImage: boolean;
  readonly imageReference: string | null;
  readonly hasOutputData: boolean;
  readonly hasAnalysisData: boolean;
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

function text(value: unknown): string {
  return typeof value === 'string' ? value.trim() : '';
}

function nullableText(value: unknown): string | null {
  return text(value) || null;
}

function nonNegativeNumber(value: unknown, fieldName: string): number {
  const decoded = Number(value);
  if (!Number.isFinite(decoded) || decoded < 0) {
    throw new TypeError(`${fieldName} 必须是非负有限数字。`);
  }
  return decoded;
}

function decodeManifest(payload: unknown): Readonly<{
  status: string;
  errorCode: string | null;
  message: string;
  manifest: ResultEvidenceManifestV1 | null;
}> {
  const envelope = record(payload);
  const status = text(field(envelope, 'status')).toLowerCase() || 'missing';
  const errorCode = nullableText(field(envelope, 'errorCode'));
  const message = text(field(envelope, 'message')) || '证据清单不可用。';
  const raw = field(envelope, 'manifest');
  if (raw === null || raw === undefined) {
    return Object.freeze({ status, errorCode, message, manifest: null });
  }

  const source = record(raw);
  const rawItems = field(source, 'items');
  if (!Array.isArray(rawItems)) throw new TypeError('Evidence manifest items 必须是数组。');
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
    totalBytes: nonNegativeNumber(field(source, 'totalBytes') ?? 0, 'Evidence manifest totalBytes'),
    checksum: nullableText(field(source, 'checksum')),
    items: Object.freeze(rawItems.map((entry, index) => {
      const item = record(entry);
      return Object.freeze({
        id: text(field(item, 'id')) || `evidence-item-${index}`,
        role: text(field(item, 'role')) || 'unknown',
        contentType: text(field(item, 'contentType')) || 'application/octet-stream',
        relativePath: nullableText(field(item, 'relativePath')),
        sizeBytes: nonNegativeNumber(field(item, 'sizeBytes') ?? 0, `Evidence item ${index} sizeBytes`),
        sha256: nullableText(field(item, 'sha256')),
        available: field(item, 'available') === true,
        missingReason: nullableText(field(item, 'missingReason'))
      });
    })),
    redactionApplied: field(record(field(source, 'redaction')), 'applied') === true
  } satisfies ResultEvidenceManifestV1);
  if (!manifest.manifestId || !manifest.projectId || !manifest.resultId) {
    throw new TypeError('Evidence manifest identity 不完整。');
  }
  return Object.freeze({ status, errorCode, message, manifest });
}

function contentDispositionFileName(headers: Headers): string {
  const value = headers.get('Content-Disposition') ?? '';
  const utf8 = /filename\*=UTF-8''([^;]+)/i.exec(value)?.[1];
  if (utf8) return decodeURIComponent(utf8);
  return /filename="?([^";]+)"?/i.exec(value)?.[1] ?? 'clearvision-evidence.json';
}

function classifyEvidence(
  status: string,
  manifest: ResultEvidenceManifestV1 | null,
  context: ResultEvidenceContext
): Exclude<ResultEvidencePhase, 'idle' | 'loading' | 'load-failed' | 'exporting' | 'export-error' | 'disposed'> {
  if (status === 'available') return 'available';
  if (status === 'partial') return 'partial';
  if (status === 'expired' || context.evidenceStatus.toLowerCase() === 'expired') return 'expired';
  if (status === 'disabled' || context.evidenceStatus.toLowerCase() === 'disabled') return 'not-produced';
  if (manifest || context.hasEvidenceManifest || context.hasImage || context.hasOutputData || context.hasAnalysisData) {
    return 'retained-summary-only';
  }
  return 'not-produced';
}

function evidenceMessage(
  phase: ResultEvidencePhase,
  serverMessage: string,
  errorCode: string | null
): string {
  if (phase === 'retained-summary-only') return '证据文件不可用，仅保留权威结果摘要。';
  if (phase === 'not-produced') return '本次结果未产生可保留证据。';
  if (phase === 'expired') return '证据已过保留期，结果摘要仍可调查。';
  if (phase === 'partial') return '部分证据可用；缺失项按清单原因显示。';
  if (phase === 'available') return '证据清单可用。';
  return errorCode ? `${serverMessage}（${errorCode}）` : serverMessage;
}

function imageApiPath(reference: string | null): string | null {
  if (!reference || !/^\/api\/images\/[0-9a-f-]{36}$/i.test(reference)) return null;
  return reference.slice('/api/'.length);
}

export function createResultEvidenceOwner(options: {
  readonly projectId: string;
  readonly resultId: string;
  readonly api: ApiTransport;
  readonly context: ResultEvidenceContext;
}): ResultEvidenceOwner {
  if (!options.api.get || !options.api.getBlob) {
    throw new TypeError('Evidence 需要 shared ApiTransport GET 与 blob GET。');
  }

  let disposed = false;
  let loadGeneration = 0;
  let exportGeneration = 0;
  let loadController: AbortController | null = null;
  let exportController: AbortController | null = null;
  let activeExportUrl: string | null = null;
  let activeImageUrl: string | null = null;
  let settledEvidencePhase: ResultEvidencePhase = 'idle';
  const state = reactive<MutableProjection>({
    phase: 'idle',
    manifest: null,
    message: '等待读取证据清单。',
    canExport: false,
    image: Object.freeze({ phase: 'idle', objectUrl: null, message: '等待检查图像状态。' })
  });
  const base = `inspection/history/${encodeURIComponent(options.projectId)}/${encodeURIComponent(options.resultId)}/evidence`;

  function releaseExportUrl(): void {
    if (!activeExportUrl) return;
    URL.revokeObjectURL(activeExportUrl);
    activeExportUrl = null;
  }

  function releaseImageUrl(): void {
    if (!activeImageUrl) return;
    URL.revokeObjectURL(activeImageUrl);
    activeImageUrl = null;
  }

  function setImage(phase: ResultImagePhase, objectUrl: string | null, message: string): void {
    state.image = Object.freeze({ phase, objectUrl, message });
  }

  async function loadManifest(operation: number, signal: AbortSignal): Promise<void> {
    try {
      const decoded = decodeManifest(await options.api.get!(`${base}/manifest`, { signal }));
      if (disposed || operation !== loadGeneration) return;
      if (
        decoded.manifest &&
        (
          decoded.manifest.projectId.toLowerCase() !== options.projectId.toLowerCase() ||
          decoded.manifest.resultId.toLowerCase() !== options.resultId.toLowerCase()
        )
      ) {
        throw new TypeError('Evidence manifest identity 与请求不一致。');
      }
      const phase = classifyEvidence(decoded.status, decoded.manifest, options.context);
      settledEvidencePhase = phase;
      state.manifest = decoded.manifest;
      state.phase = phase;
      state.message = evidenceMessage(phase, decoded.message, decoded.errorCode);
      state.canExport = Boolean(decoded.manifest) && (phase === 'available' || phase === 'partial');
    } catch (error) {
      if (disposed || operation !== loadGeneration || error instanceof ApiAbortError) return;
      settledEvidencePhase = 'load-failed';
      state.phase = 'load-failed';
      state.message = `证据清单请求失败：${error instanceof Error ? error.message : '响应不可用。'}`;
      state.canExport = false;
    }
  }

  async function loadImage(operation: number, signal: AbortSignal): Promise<void> {
    const imagePath = imageApiPath(options.context.imageReference);
    if (!imagePath) {
      setImage(
        options.context.hasImage ? 'retained-summary-only' : 'not-produced',
        null,
        options.context.hasImage
          ? '图像引用已不可用，仅保留结果摘要。'
          : '本次检测未产生可访问图像。'
      );
      return;
    }

    setImage('loading', null, '正在读取本机结果图像。');
    try {
      const response = await options.api.getBlob!(imagePath, { signal });
      if (disposed || operation !== loadGeneration) return;
      const contentType = response.contentType || response.blob.type;
      if (!contentType.toLowerCase().startsWith('image/')) {
        throw new TypeError('图像端点返回了非图像内容。');
      }
      releaseImageUrl();
      activeImageUrl = URL.createObjectURL(response.blob);
      setImage('available', activeImageUrl, '本机保留图像可用。');
    } catch (error) {
      if (disposed || operation !== loadGeneration || error instanceof ApiAbortError) return;
      releaseImageUrl();
      setImage(
        'load-failed',
        null,
        `图像请求失败：${error instanceof Error ? error.message : '响应不可用。'}`
      );
    }
  }

  const owner: ResultEvidenceOwner = Object.freeze({
    projection: readonly(state),
    async load(): Promise<void> {
      if (disposed) return;
      const operation = ++loadGeneration;
      loadController?.abort('evidence-load-superseded');
      exportGeneration += 1;
      exportController?.abort('evidence-load-superseded');
      exportController = null;
      const controller = new AbortController();
      loadController = controller;
      state.phase = 'loading';
      state.message = '正在读取证据清单。';
      state.canExport = false;
      try {
        await Promise.all([
          loadManifest(operation, controller.signal),
          loadImage(operation, controller.signal)
        ]);
      } finally {
        if (loadController === controller) loadController = null;
      }
    },
    async exportEvidence(): Promise<void> {
      if (disposed || !state.canExport) return;
      const operation = ++exportGeneration;
      exportController?.abort('evidence-export-superseded');
      const controller = new AbortController();
      exportController = controller;
      state.phase = 'exporting';
      state.message = '正在导出本条证据。';
      try {
        const response = await options.api.getBlob!(`${base}/export`, { signal: controller.signal });
        if (disposed || operation !== exportGeneration) return;
        releaseExportUrl();
        activeExportUrl = URL.createObjectURL(response.blob);
        const anchor = document.createElement('a');
        anchor.href = activeExportUrl;
        anchor.download = contentDispositionFileName(response.headers);
        anchor.click();
        releaseExportUrl();
        state.phase = settledEvidencePhase;
        state.message = '本条证据导出已开始。';
        state.canExport = true;
      } catch (error) {
        if (disposed || operation !== exportGeneration || error instanceof ApiAbortError) return;
        state.phase = 'export-error';
        state.message = `证据导出失败：${error instanceof Error ? error.message : '服务端拒绝导出。'}`;
        state.canExport = Boolean(state.manifest) && (
          settledEvidencePhase === 'available' || settledEvidencePhase === 'partial'
        );
      } finally {
        if (exportController === controller) exportController = null;
      }
    },
    dispose(): void {
      if (disposed) return;
      disposed = true;
      loadGeneration += 1;
      exportGeneration += 1;
      loadController?.abort('result-evidence-owner-disposed');
      exportController?.abort('result-evidence-owner-disposed');
      loadController = null;
      exportController = null;
      releaseExportUrl();
      releaseImageUrl();
      state.phase = 'disposed';
      state.manifest = null;
      state.canExport = false;
      setImage('disposed', null, '图像 owner 已关闭。');
    }
  });
  return owner;
}
