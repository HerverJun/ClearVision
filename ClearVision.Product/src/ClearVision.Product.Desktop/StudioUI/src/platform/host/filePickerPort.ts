import type { StudioHostAdapter } from './types';

export type FilePickerFilterKind = 'image' | 'model' | 'template' | 'data' | 'all';

export interface FilePickerRequest {
  readonly parameterName: string;
  readonly filter: string;
  readonly timeoutMs?: number;
}

export interface FilePickerSelectedResult {
  readonly status: 'selected';
  readonly parameterName: string;
  readonly filePath: string;
}

export interface FilePickerCancelledResult {
  readonly status: 'cancelled';
  readonly parameterName: string;
}

export type FilePickerResult = FilePickerSelectedResult | FilePickerCancelledResult;

export interface FilePickerPortDiagnostics {
  readonly disposed: boolean;
  readonly activeRequest: boolean;
  readonly queuedRequestCount: number;
  readonly activeSubscriptionCount: number;
  readonly lateResponseCount: number;
  readonly ignoredResponseCount: number;
}

export interface FilePickerPort {
  pick(request: FilePickerRequest): Promise<FilePickerResult>;
  getDiagnostics(): FilePickerPortDiagnostics;
  dispose(reason?: string): void;
}

export class FilePickerPortDisposedError extends Error {
  constructor() {
    super('文件选择服务已关闭。');
    this.name = 'FilePickerPortDisposedError';
  }
}

export class FilePickerRequestError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'FilePickerRequestError';
  }
}

export class FilePickerBusyError extends Error {
  constructor() {
    super('文件选择服务正在处理上一个请求，请先完成或关闭当前文件窗口。');
    this.name = 'FilePickerBusyError';
  }
}

export class FilePickerTimeoutError extends Error {
  constructor() {
    super('文件窗口响应超时；请关闭当前文件窗口后重试。');
    this.name = 'FilePickerTimeoutError';
  }
}

export class FilePickerProtocolError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'FilePickerProtocolError';
  }
}

export class FilePickerHostError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'FilePickerHostError';
  }
}

const FILE_PICKER_FILTERS: Readonly<Record<FilePickerFilterKind, string>> = Object.freeze({
  image: 'Image Files|*.bmp;*.jpg;*.png;*.jpeg;*.tif;*.tiff|All Files|*.*',
  model: 'Model Files|*.onnx;*.pt;*.pth;*.engine;*.xml;*.bin|All Files|*.*',
  template: 'Template Files|*.png;*.jpg;*.jpeg;*.bmp;*.json|All Files|*.*',
  data: 'Data Files|*.json;*.txt;*.yaml;*.yml;*.csv;*.bin|All Files|*.*',
  all: 'All Files|*.*'
});

const DEFAULT_TIMEOUT_MS = 30_000;

function normalized(value: unknown): string {
  return String(value ?? '').trim().toLowerCase();
}

function normalizedParameterName(value: unknown): string {
  return normalized(value).replace(/[^a-z0-9]/g, '');
}

function field(source: Readonly<Record<string, unknown>>, name: string): unknown {
  const pascal = `${name.slice(0, 1).toUpperCase()}${name.slice(1)}`;
  if (Object.prototype.hasOwnProperty.call(source, name)) return source[name];
  if (Object.prototype.hasOwnProperty.call(source, pascal)) return source[pascal];
  return undefined;
}

function record(value: unknown): Readonly<Record<string, unknown>> | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? value as Readonly<Record<string, unknown>>
    : null;
}

export function filePickerFilter(kind: FilePickerFilterKind): string {
  return FILE_PICKER_FILTERS[kind];
}

export function resolveFilePickerFilter(
  parameterName: string,
  kind?: FilePickerFilterKind
): string {
  if (kind) return filePickerFilter(kind);

  const name = normalizedParameterName(parameterName);
  if (name.includes('model') || name.includes('embedding') || name.includes('onnx')) {
    return filePickerFilter('model');
  }
  if (name.includes('template')) {
    return filePickerFilter('template');
  }
  if (name.includes('label') || name.includes('catalog') || name.includes('bank') || name.includes('json')) {
    return filePickerFilter('data');
  }
  if (
    name === 'filepath' ||
    name.includes('image') ||
    name.includes('template') ||
    name.endsWith('filepath') ||
    name.endsWith('templatepath') ||
    name.endsWith('modelpath') ||
    name.endsWith('catalogpath') ||
    name.endsWith('labelspath') ||
    name.endsWith('bankpath') ||
    name.endsWith('path')
  ) {
    return filePickerFilter('image');
  }
  return filePickerFilter('all');
}

interface DecodedFilePickedEvent {
  readonly parameterName: string;
  readonly filePath: string | null;
  readonly isCancelled: boolean;
}

function decodeFilePickedEvent(message: unknown): DecodedFilePickedEvent | null {
  const envelope = record(message);
  if (!envelope) return null;

  const messageType = normalized(field(envelope, 'type') ?? field(envelope, 'messageType'));
  if (messageType !== 'filepickedevent') return null;

  const payload = record(field(envelope, 'payload') ?? field(envelope, 'data')) ?? envelope;
  const parameterName = field(payload, 'parameterName');
  const filePath = field(payload, 'filePath');
  const isCancelled = field(payload, 'isCancelled');
  if (typeof parameterName !== 'string' || parameterName.trim().length === 0) {
    throw new FilePickerProtocolError('文件选择事件缺少参数名称。');
  }
  if (typeof isCancelled !== 'boolean') {
    throw new FilePickerProtocolError('文件选择事件的取消状态无效。');
  }
  if (filePath !== null && filePath !== undefined && typeof filePath !== 'string') {
    throw new FilePickerProtocolError('文件选择事件的路径无效。');
  }
  if (!isCancelled && typeof filePath !== 'string') {
    throw new FilePickerProtocolError('文件选择事件未提供路径。');
  }

  return Object.freeze({
    parameterName: parameterName.trim(),
    filePath: typeof filePath === 'string' ? filePath.trim() : null,
    isCancelled
  });
}

interface PendingRequest {
  readonly request: FilePickerRequest;
  readonly resolve: (result: FilePickerResult) => void;
  readonly reject: (error: unknown) => void;
  timeoutId: ReturnType<typeof setTimeout> | undefined;
  timedOut: boolean;
}

export function createFilePickerPort(host: StudioHostAdapter): FilePickerPort {
  const queue: PendingRequest[] = [];
  let active: PendingRequest | undefined;
  let disposed = false;
  let lateResponseCount = 0;
  let ignoredResponseCount = 0;

  const clearTimeoutFor = (pending: PendingRequest): void => {
    if (pending.timeoutId === undefined) return;
    clearTimeout(pending.timeoutId);
    pending.timeoutId = undefined;
  };

  const failQueued = (error: unknown): void => {
    while (queue.length > 0) {
      queue.shift()?.reject(error);
    }
  };

  const pump = (): void => {
    if (disposed || active || queue.length === 0) return;
    const next = queue.shift();
    if (!next) return;
    active = next;
    const timeoutMs = typeof next.request.timeoutMs === 'number' && Number.isFinite(next.request.timeoutMs)
      ? Math.max(1, next.request.timeoutMs)
      : DEFAULT_TIMEOUT_MS;
    next.timeoutId = setTimeout(() => {
      if (active !== next || next.timedOut) return;
      next.timedOut = true;
      next.timeoutId = undefined;
      next.reject(new FilePickerTimeoutError());
    }, timeoutMs);

    try {
      host.postMessage({
        messageType: 'PickFileCommand',
        parameterName: next.request.parameterName,
        filter: next.request.filter
      });
    } catch (error) {
      clearTimeoutFor(next);
      active = undefined;
      next.reject(new FilePickerHostError(error instanceof Error ? error.message : '无法打开文件选择服务。'));
      failQueued(new FilePickerHostError('无法打开文件选择服务。'));
    }
  };

  const handleMessage = (message: unknown): void => {
    if (disposed || !active) return;

    let event: DecodedFilePickedEvent | null;
    try {
      event = decodeFilePickedEvent(message);
    } catch {
      ignoredResponseCount += 1;
      return;
    }
    if (!event) return;

    if (normalizedParameterName(event.parameterName) !== normalizedParameterName(active.request.parameterName)) {
      ignoredResponseCount += 1;
      return;
    }

    const settled = active;
    clearTimeoutFor(settled);
    if (settled.timedOut) {
      lateResponseCount += 1;
      active = undefined;
      pump();
      return;
    }

    active = undefined;
    if (event.isCancelled) {
      settled.resolve(Object.freeze({ status: 'cancelled', parameterName: settled.request.parameterName }));
    } else {
      settled.resolve(Object.freeze({
        status: 'selected',
        parameterName: settled.request.parameterName,
        filePath: event.filePath!
      }));
    }
    pump();
  };

  const unsubscribe = host.subscribe(handleMessage);

  const port: FilePickerPort = {
    pick(request: FilePickerRequest): Promise<FilePickerResult> {
      if (disposed) return Promise.reject(new FilePickerPortDisposedError());
      const parameterName = request.parameterName.trim();
      if (!parameterName) return Promise.reject(new FilePickerRequestError('文件选择请求缺少参数名称。'));
      if (!request.filter.trim()) return Promise.reject(new FilePickerRequestError('文件选择请求缺少文件过滤器。'));

      return new Promise<FilePickerResult>((resolve, reject) => {
        queue.push({
          request: Object.freeze({ ...request, parameterName }),
          resolve,
          reject,
          timeoutId: undefined,
          timedOut: false
        });
        pump();
      });
    },
    getDiagnostics(): FilePickerPortDiagnostics {
      return Object.freeze({
        disposed,
        activeRequest: active !== undefined,
        queuedRequestCount: queue.length,
        activeSubscriptionCount: disposed ? 0 : 1,
        lateResponseCount,
        ignoredResponseCount
      });
    },
    dispose(): void {
      if (disposed) return;
      disposed = true;
      const error = new FilePickerPortDisposedError();
      if (active) {
        clearTimeoutFor(active);
        if (!active.timedOut) active.reject(error);
        active = undefined;
      }
      failQueued(error);
      unsubscribe();
    }
  };

  return Object.freeze(port);
}
