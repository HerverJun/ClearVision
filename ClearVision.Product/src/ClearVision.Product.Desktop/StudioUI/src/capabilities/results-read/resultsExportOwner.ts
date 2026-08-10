import { reactive, readonly, type DeepReadonly } from 'vue';
import {
  ApiAbortError,
  ApiForbiddenError,
  ApiHttpError,
  ApiNetworkError,
  ApiNotFoundError,
  type ApiTransport
} from '@/platform/api';

export type ResultsExportFormat = 'csv' | 'json';

export interface ResultsExportScopeV1 {
  readonly projectId: string;
  readonly source: 'local';
  readonly startTime: string | null;
  readonly endTime: string | null;
  readonly status: string | null;
  readonly defectType: string | null;
  readonly diagnosticCode: string | null;
}

export type ResultsExportPhase =
  | 'idle'
  | 'creating'
  | 'reconciling'
  | 'queued'
  | 'running'
  | 'cancelling'
  | 'downloading'
  | 'completed'
  | 'failed'
  | 'cancelled'
  | 'expired'
  | 'forbidden'
  | 'error'
  | 'unknown-outcome'
  | 'disposed';

export interface ResultsExportJobSnapshotV1 {
  readonly exportId: string;
  readonly projectId: string;
  readonly source: 'local';
  readonly format: ResultsExportFormat;
  readonly clientOperationId: string;
  readonly state: 'queued' | 'running' | 'completed' | 'failed' | 'cancelled';
  readonly createdAtUtc: string;
  readonly updatedAtUtc: string;
  readonly snapshotUpperBoundUtc: string | null;
  readonly completedAtUtc: string | null;
  readonly artifactExpiresAtUtc: string | null;
  readonly fileName: string;
  readonly errorCode: string | null;
  readonly errorMessage: string | null;
  readonly downloadAvailable: boolean;
}

export interface ResultsExportProjection {
  readonly phase: ResultsExportPhase;
  readonly scope: ResultsExportScopeV1;
  readonly format: ResultsExportFormat | null;
  readonly exportId: string | null;
  readonly clientOperationId: string | null;
  readonly job: ResultsExportJobSnapshotV1 | null;
  readonly message: string;
  readonly canStart: boolean;
  readonly canCancel: boolean;
  readonly canReconcile: boolean;
  readonly canDownload: boolean;
  readonly downloadedSha256: string | null;
}

type MutableProjection = { -readonly [Key in keyof ResultsExportProjection]: ResultsExportProjection[Key] };

export interface ResultsExportOwner {
  readonly projection: DeepReadonly<ResultsExportProjection>;
  start(format: ResultsExportFormat): Promise<void>;
  reconcile(): Promise<void>;
  cancel(): Promise<void>;
  download(): Promise<void>;
  dispose(): void;
}

const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const pollDelayMs = 650;

function record(value: unknown): Readonly<Record<string, unknown>> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? value as Readonly<Record<string, unknown>>
    : Object.freeze({});
}

function field(source: Readonly<Record<string, unknown>>, name: string): unknown {
  if (Object.prototype.hasOwnProperty.call(source, name)) return source[name];
  return source[`${name.slice(0, 1).toUpperCase()}${name.slice(1)}`];
}

function text(value: unknown): string {
  return typeof value === 'string' ? value.trim() : '';
}

function nullableText(value: unknown): string | null {
  const normalized = text(value);
  return normalized || null;
}

function uuid(value: unknown, path: string): string {
  const normalized = text(value);
  if (!uuidPattern.test(normalized)) throw new TypeError(`${path} 必须是有效 UUID。`);
  return normalized;
}

function nullableDate(value: unknown, path: string): string | null {
  if (value === null || value === undefined || value === '') return null;
  const normalized = text(value);
  if (!normalized || Number.isNaN(Date.parse(normalized))) {
    throw new TypeError(`${path} 必须是有效 ISO 时间。`);
  }
  return normalized;
}

function format(value: unknown, path: string): ResultsExportFormat {
  const normalized = text(value).toLowerCase();
  if (normalized !== 'csv' && normalized !== 'json') throw new TypeError(`${path} 必须是 csv 或 json。`);
  return normalized;
}

function state(value: unknown, path: string): ResultsExportJobSnapshotV1['state'] {
  const normalized = typeof value === 'number'
    ? ['queued', 'running', 'completed', 'failed', 'cancelled'][value] ?? ''
    : text(value).toLowerCase();
  if (!['queued', 'running', 'completed', 'failed', 'cancelled'].includes(normalized)) {
    throw new TypeError(`${path} 不是受支持的导出状态。`);
  }
  return normalized as ResultsExportJobSnapshotV1['state'];
}

function decodeJob(payload: unknown): ResultsExportJobSnapshotV1 {
  const source = record(payload);
  const sourceValue = text(field(source, 'source')).toLowerCase();
  if (sourceValue !== 'local') throw new TypeError('服务端返回了不受支持的检测结果来源。');
  const decoded = Object.freeze({
    exportId: uuid(field(source, 'exportId'), '$.exportId'),
    projectId: uuid(field(source, 'projectId'), '$.projectId'),
    source: 'local' as const,
    format: format(field(source, 'format'), '$.format'),
    clientOperationId: uuid(field(source, 'clientOperationId'), '$.clientOperationId'),
    state: state(field(source, 'state'), '$.state'),
    createdAtUtc: text(field(source, 'createdAtUtc')),
    updatedAtUtc: text(field(source, 'updatedAtUtc')),
    snapshotUpperBoundUtc: nullableDate(field(source, 'snapshotUpperBoundUtc'), '$.snapshotUpperBoundUtc'),
    completedAtUtc: nullableDate(field(source, 'completedAtUtc'), '$.completedAtUtc'),
    artifactExpiresAtUtc: nullableDate(field(source, 'artifactExpiresAtUtc'), '$.artifactExpiresAtUtc'),
    fileName: text(field(source, 'fileName')),
    errorCode: nullableText(field(source, 'errorCode')),
    errorMessage: nullableText(field(source, 'errorMessage')),
    downloadAvailable: field(source, 'downloadAvailable') === true
  });
  if (!decoded.createdAtUtc || !decoded.updatedAtUtc || !decoded.fileName) {
    throw new TypeError('Results 导出响应缺少时间或文件身份。');
  }
  return decoded;
}

function errorMessage(error: unknown, fallback: string): string {
  if (error instanceof ApiHttpError) {
    const payload = record(error.payload);
    const serverMessage = text(field(payload, 'message')) || text(field(payload, 'publicMessage'));
    if (serverMessage) return serverMessage;
  }
  return error instanceof Error && error.message ? error.message : fallback;
}

function createOperationId(): string {
  const randomUuid = globalThis.crypto?.randomUUID;
  if (!randomUuid) throw new TypeError('当前宿主不支持稳定的结果导出操作身份。');
  return randomUuid.call(globalThis.crypto);
}

function contentDispositionFileName(headers: Headers, fallback: string): string {
  const value = headers.get('Content-Disposition') ?? '';
  const utf8 = /filename\*=UTF-8''([^;]+)/i.exec(value)?.[1];
  if (utf8) {
    try {
      return decodeURIComponent(utf8);
    } catch {
      return fallback;
    }
  }
  return /filename="?([^";]+)"?/i.exec(value)?.[1] ?? fallback;
}

async function sha256Hex(blob: Blob): Promise<string | null> {
  if (!globalThis.crypto?.subtle) return null;
  const digest = await globalThis.crypto.subtle.digest('SHA-256', await blob.arrayBuffer());
  return Array.from(new Uint8Array(digest), byte => byte.toString(16).padStart(2, '0')).join('');
}

function formatSnapshotMessage(job: ResultsExportJobSnapshotV1): string {
  switch (job.state) {
    case 'queued': return '导出任务已排队，服务端正在准备结果范围。';
    case 'running': return '服务端正在读取当前工程的完整结果范围。';
    case 'completed': return job.downloadAvailable
      ? `服务端已生成 ${job.format.toUpperCase()} 文件，可下载。`
      : '导出任务已完成，但文件已过期，请重新发起导出。';
    case 'cancelled': return job.errorMessage ?? '结果导出已取消。';
    case 'failed': return job.errorMessage ?? '服务端未能完成结果导出。';
  }
}

export function createResultsExportOwner(options: {
  readonly api: ApiTransport;
  readonly scope: ResultsExportScopeV1;
}): ResultsExportOwner {
  if (!options.api.get || !options.api.post || !options.api.getBlob) {
    throw new TypeError('Results 导出需要 shared ApiTransport GET、POST 与 blob GET。');
  }
  if (!uuidPattern.test(options.scope.projectId) || options.scope.source !== 'local') {
    throw new TypeError('Results 导出只接受有效的本机工程 scope。');
  }

  let disposed = false;
  let generation = 0;
  let pollTimer: ReturnType<typeof setTimeout> | null = null;
  let controller: AbortController | null = null;
  const state = reactive<MutableProjection>({
    phase: 'idle',
    scope: Object.freeze({ ...options.scope }),
    format: null,
    exportId: null,
    clientOperationId: null,
    job: null,
    message: '选择格式后由服务端生成当前工程的完整结果文件。',
    canStart: true,
    canCancel: false,
    canReconcile: false,
    canDownload: false,
    downloadedSha256: null
  });

  function clearPoll(): void {
    if (pollTimer === null) return;
    clearTimeout(pollTimer);
    pollTimer = null;
  }

  function syncControls(): void {
    const busy = ['creating', 'reconciling', 'queued', 'running', 'cancelling', 'downloading'].includes(state.phase);
    state.canStart = !disposed && !busy && state.phase !== 'unknown-outcome';
    state.canCancel = !disposed && ['creating', 'reconciling', 'queued', 'running'].includes(state.phase);
    state.canReconcile = !disposed && state.phase === 'unknown-outcome' && Boolean(state.clientOperationId);
    state.canDownload = !disposed && !busy && state.job?.state === 'completed' && state.job.downloadAvailable === true;
  }

  function setUnknown(message: string): void {
    state.phase = 'unknown-outcome';
    state.message = message;
    syncControls();
  }

  function applySnapshot(job: ResultsExportJobSnapshotV1, operation: number): void {
    if (disposed || operation !== generation) return;
    state.job = job;
    state.exportId = job.exportId;
    state.clientOperationId = job.clientOperationId;
    state.format = job.format;
    state.phase = job.state === 'completed' && !job.downloadAvailable
      ? 'expired'
      : job.state;
    state.message = formatSnapshotMessage(job);
    syncControls();
    if (job.state === 'queued' || job.state === 'running') schedulePoll(operation);
  }

  function schedulePoll(operation: number): void {
    clearPoll();
    if (disposed || operation !== generation) return;
    pollTimer = setTimeout(() => {
      pollTimer = null;
      void poll(operation);
    }, pollDelayMs);
  }

  async function poll(operation: number): Promise<void> {
    const exportId = state.exportId;
    if (disposed || operation !== generation || !exportId) return;
    const requestController = new AbortController();
    controller = requestController;
    try {
      const payload = await options.api.get!(`results/exports/${encodeURIComponent(exportId)}`, {
        signal: requestController.signal
      });
      applySnapshot(decodeJob(payload), operation);
    } catch (error) {
      if (disposed || operation !== generation || error instanceof ApiAbortError) return;
      if (error instanceof ApiForbiddenError) {
        state.phase = 'forbidden';
        state.message = '读取结果导出状态需要工程师或管理员权限。';
        syncControls();
      } else {
        setUnknown(`结果导出状态暂时未知：${errorMessage(error, '服务端未返回状态。')} 请按操作身份对账。`);
      }
    } finally {
      if (controller === requestController) controller = null;
    }
  }

  async function reconcileInternal(operation: number, attempts: number): Promise<void> {
    const clientOperationId = state.clientOperationId;
    if (disposed || operation !== generation || !clientOperationId) return;
    state.phase = 'reconciling';
    state.message = '创建响应未知，正在按本次操作标识查询已有导出任务。';
    syncControls();
    for (let attempt = 0; attempt < attempts; attempt++) {
      if (disposed || operation !== generation) return;
      const requestController = new AbortController();
      controller = requestController;
      try {
        const payload = await options.api.get!(
          `results/exports/by-operation/${encodeURIComponent(clientOperationId)}`,
          { signal: requestController.signal }
        );
        applySnapshot(decodeJob(payload), operation);
        return;
      } catch (error) {
        if (disposed || operation !== generation || error instanceof ApiAbortError) return;
        if (!(error instanceof ApiNotFoundError) && !(error instanceof ApiNetworkError)) {
          if (error instanceof ApiForbiddenError) {
            state.phase = 'forbidden';
            state.message = '结果导出对账需要工程师或管理员权限。';
          } else {
            state.phase = 'error';
            state.message = `结果导出对账失败：${errorMessage(error, '服务端响应不可用。')}`;
          }
          syncControls();
          return;
        }
      } finally {
        if (controller === requestController) controller = null;
      }
      if (attempt + 1 < attempts) {
        await new Promise<void>(resolve => setTimeout(resolve, 200 * (attempt + 1)));
      }
    }
    setUnknown('未能按操作标识找到导出任务；禁止自动重发创建请求，请稍后手动核对。');
  }

  const owner: ResultsExportOwner = Object.freeze({
    projection: readonly(state),
    async start(exportFormat: ResultsExportFormat): Promise<void> {
      if (disposed || !state.canStart) return;
      clearPoll();
      generation += 1;
      const operation = generation;
      const clientOperationId = createOperationId();
      const requestController = new AbortController();
      controller = requestController;
      state.phase = 'creating';
      state.format = exportFormat;
      state.exportId = null;
      state.clientOperationId = clientOperationId;
      state.job = null;
      state.downloadedSha256 = null;
      state.message = `正在请求服务端生成 ${exportFormat.toUpperCase()} 完整结果文件。`;
      syncControls();
      try {
        const payload = await options.api.post!(
          'results/exports',
          {
            projectId: options.scope.projectId,
            source: options.scope.source,
            format: exportFormat,
            startTime: options.scope.startTime,
            endTime: options.scope.endTime,
            status: options.scope.status,
            defectType: options.scope.defectType,
            diagnosticCode: options.scope.diagnosticCode,
            clientOperationId
          },
          { signal: requestController.signal }
        );
        if (disposed || operation !== generation) return;
        const envelope = record(payload);
        const job = decodeJob(field(envelope, 'job') ?? payload);
        applySnapshot(job, operation);
      } catch (error) {
        if (disposed || operation !== generation || error instanceof ApiAbortError) return;
        if (error instanceof ApiNetworkError) {
          await reconcileInternal(operation, 3);
        } else if (error instanceof ApiForbiddenError) {
          state.phase = 'forbidden';
          state.message = '结果导出需要工程师或管理员权限。';
          syncControls();
        } else {
          state.phase = 'error';
          state.message = `结果导出未发起：${errorMessage(error, '服务端校验未通过。')}`;
          syncControls();
        }
      } finally {
        if (controller === requestController) controller = null;
      }
    },
    async reconcile(): Promise<void> {
      if (disposed || !state.canReconcile || !state.clientOperationId) return;
      generation += 1;
      clearPoll();
      controller?.abort('results-export-reconcile-superseded');
      controller = null;
      await reconcileInternal(generation, 3);
    },
    async cancel(): Promise<void> {
      if (disposed || (!state.canCancel && state.phase !== 'creating' && state.phase !== 'reconciling')) return;
      if (state.phase === 'creating' || state.phase === 'reconciling') {
        generation += 1;
        clearPoll();
        controller?.abort('results-export-cancelled-before-outcome-known');
        controller = null;
        setUnknown('创建请求已中止，但服务端是否已创建导出任务未知；请按操作身份对账。');
        return;
      }
      if (!state.exportId) return;
      generation += 1;
      const operation = generation;
      clearPoll();
      controller?.abort('results-export-cancel-superseded');
      const requestController = new AbortController();
      controller = requestController;
      state.phase = 'cancelling';
      state.message = '正在请求服务端取消结果导出。';
      syncControls();
      try {
        const payload = await options.api.post!(
          `results/exports/${encodeURIComponent(state.exportId)}/cancel`,
          {},
          { signal: requestController.signal }
        );
        applySnapshot(decodeJob(payload), operation);
      } catch (error) {
        if (disposed || operation !== generation || error instanceof ApiAbortError) return;
        if (error instanceof ApiForbiddenError) {
          state.phase = 'forbidden';
          state.message = '取消结果导出需要工程师或管理员权限。';
          syncControls();
        } else {
          setUnknown(`取消响应未知：${errorMessage(error, '服务端未确认取消结果。')} 请按操作身份对账。`);
        }
      } finally {
        if (controller === requestController) controller = null;
      }
    },
    async download(): Promise<void> {
      if (disposed || !state.canDownload || !state.exportId || !state.job) return;
      const operation = ++generation;
      clearPoll();
      controller?.abort('results-export-download-superseded');
      const requestController = new AbortController();
      controller = requestController;
      state.phase = 'downloading';
      state.message = '正在读取服务端生成的结果文件。';
      syncControls();
      try {
        const response = await options.api.getBlob!(
          `results/exports/${encodeURIComponent(state.exportId)}/download`,
          { signal: requestController.signal }
        );
        if (disposed || operation !== generation) return;
        const expectedSha256 = response.sha256?.trim().toLowerCase() || null;
        const actualSha256 = await sha256Hex(response.blob);
        if (expectedSha256 && actualSha256 && expectedSha256 !== actualSha256) {
          throw new TypeError('下载文件校验和与服务端不一致，已阻止保存。');
        }
        if (typeof document === 'undefined') throw new TypeError('当前宿主不支持文件下载。');
        const url = URL.createObjectURL(response.blob);
        try {
          const anchor = document.createElement('a');
          anchor.href = url;
          anchor.download = contentDispositionFileName(response.headers, state.job.fileName);
          anchor.click();
        } finally {
          URL.revokeObjectURL(url);
        }
        state.phase = 'completed';
        state.downloadedSha256 = actualSha256 ?? expectedSha256;
        state.message = '结果文件已开始下载。';
        syncControls();
      } catch (error) {
        if (disposed || operation !== generation || error instanceof ApiAbortError) return;
        if (error instanceof ApiForbiddenError) {
          state.phase = 'forbidden';
          state.message = '下载结果文件需要工程师或管理员权限。';
        } else if (error instanceof ApiNotFoundError) {
          state.phase = 'expired';
          state.message = '结果文件已过期或不存在，请重新发起导出。';
        } else {
          state.phase = 'error';
          state.message = `结果文件下载失败：${errorMessage(error, '文件响应不可用。')}`;
        }
        syncControls();
      } finally {
        if (controller === requestController) controller = null;
      }
    },
    dispose(): void {
      if (disposed) return;
      disposed = true;
      generation += 1;
      clearPoll();
      controller?.abort('results-export-owner-disposed');
      controller = null;
      state.phase = 'disposed';
      state.message = '结果导出已关闭。';
      state.canStart = false;
      state.canCancel = false;
      state.canReconcile = false;
      state.canDownload = false;
    }
  });
  return owner;
}
