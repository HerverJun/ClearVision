import { reactive, readonly, watch, type DeepReadonly } from 'vue';
import {
  ApiAbortError,
  ApiForbiddenError,
  ApiNotFoundError,
  type ApiBlobResponse,
  type ApiTransport
} from '@/platform/api';
import type { FlowCanvasOwner } from '../flow';

export interface CameraBindingV1 {
  readonly id: string;
  readonly displayName: string;
  readonly deviceId: string | null;
  readonly manufacturer: string | null;
  readonly modelName: string | null;
  readonly triggerMode: string;
  readonly isEnabled: boolean;
  readonly connectionStatus: string;
}

export interface CapturedCameraFrameV1 {
  readonly projectId: string;
  readonly sourceNodeId: string;
  readonly frameId: string;
  readonly sourceSignature: string;
  readonly imageBase64: string;
  readonly cameraBindingId: string;
  readonly triggerMode: string;
  readonly width: number | null;
  readonly height: number | null;
  readonly contentType: string;
  readonly capturedAtUtc: string;
}

export interface CameraBindingEditorProjection {
  readonly phase: 'idle' | 'loading' | 'ready' | 'error' | 'disposed';
  readonly capturePhase: 'idle' | 'capturing' | 'captured' | 'cancelled' | 'error';
  readonly bindings: readonly CameraBindingV1[];
  readonly selectedNodeId: string | null;
  readonly currentBindingId: string | null;
  readonly frame: CapturedCameraFrameV1 | null;
  readonly message: string;
  readonly canCapture: boolean;
}

type MutableProjection = { -readonly [Key in keyof CameraBindingEditorProjection]: CameraBindingEditorProjection[Key] };

export interface CameraBindingEditorOwner {
  readonly projection: DeepReadonly<CameraBindingEditorProjection>;
  refreshBindings(): Promise<void>;
  selectBinding(parameterName: string, bindingId: string): boolean;
  capture(): Promise<CapturedCameraFrameV1 | null>;
  cancelCapture(): void;
  getPreviewInputContext(targetNode: Readonly<Record<string, unknown>>): Readonly<Record<string, unknown>> | null;
  dispose(reason?: string): void;
}

function record(value: unknown): Readonly<Record<string, unknown>> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? value as Readonly<Record<string, unknown>>
    : Object.freeze({});
}

function field(source: Readonly<Record<string, unknown>>, camel: string): unknown {
  if (Object.prototype.hasOwnProperty.call(source, camel)) return source[camel];
  const pascal = `${camel.slice(0, 1).toUpperCase()}${camel.slice(1)}`;
  return source[pascal];
}

function text(value: unknown): string {
  return typeof value === 'string' ? value.trim() : value === null || value === undefined ? '' : String(value).trim();
}

function boolean(value: unknown, fallback = false): boolean {
  return typeof value === 'boolean' ? value : fallback;
}

function enumIdentity(value: unknown): string {
  const projected = record(value);
  return text(field(projected, 'value') ?? field(projected, 'persistenceValue') ?? value);
}

function isImageAcquisition(node: Readonly<Record<string, unknown>>): boolean {
  const identity = enumIdentity(field(node, 'type')).toLowerCase();
  return identity === 'imageacquisition' || identity === '0';
}

function parameterValue(node: Readonly<Record<string, unknown>>, name: string): unknown {
  const parameters = field(node, 'parameters');
  if (!Array.isArray(parameters)) return undefined;
  const target = name.toLowerCase();
  const parameter = parameters.map(record).find(item =>
    text(field(item, 'name')).toLowerCase() === target);
  return parameter ? field(parameter, 'value') ?? field(parameter, 'defaultValue') : undefined;
}

function nodeId(node: Readonly<Record<string, unknown>>): string {
  return text(field(node, 'id'));
}

function cameraBindingId(node: Readonly<Record<string, unknown>>): string {
  return text(parameterValue(node, 'CameraBindingId')) || text(parameterValue(node, 'CameraId'));
}

function sourceSignature(node: Readonly<Record<string, unknown>>): string {
  const payload = JSON.stringify({
    nodeType: enumIdentity(field(node, 'type')),
    sourceType: text(parameterValue(node, 'SourceType')).toLowerCase(),
    cameraBindingId: cameraBindingId(node),
    triggerMode: text(parameterValue(node, 'TriggerMode')),
    exposureTime: parameterValue(node, 'ExposureTime'),
    gain: parameterValue(node, 'Gain')
  });
  let hash = 5381;
  for (let index = 0; index < payload.length; index += 1) {
    hash = (((hash << 5) + hash) + payload.charCodeAt(index)) >>> 0;
  }
  return hash.toString(16);
}

function isReachable(
  sourceNodeId: string,
  targetNodeId: string,
  connections: readonly Readonly<Record<string, unknown>>[]
): boolean {
  if (sourceNodeId === targetNodeId) return true;
  const visited = new Set([sourceNodeId]);
  const pending = [sourceNodeId];
  while (pending.length > 0) {
    const current = pending.shift()!;
    for (const connection of connections) {
      const source = text(field(connection, 'sourceOperatorId') ?? field(connection, 'sourceNodeId') ?? connection.source);
      const target = text(field(connection, 'targetOperatorId') ?? field(connection, 'targetNodeId') ?? connection.target);
      if (source !== current || !target || visited.has(target)) continue;
      if (target === targetNodeId) return true;
      visited.add(target);
      pending.push(target);
    }
  }
  return false;
}

function decodeBindings(payload: unknown): readonly CameraBindingV1[] {
  if (!Array.isArray(payload)) throw new TypeError('相机绑定响应必须是数组。');
  return Object.freeze(payload.map((entry, index) => {
    const source = record(entry);
    const id = text(field(source, 'id'));
    if (!id) throw new TypeError(`相机绑定[${index}]缺少 ID。`);
    return Object.freeze({
      id,
      displayName: text(field(source, 'displayName')) || id,
      deviceId: text(field(source, 'deviceId')) || null,
      manufacturer: text(field(source, 'manufacturer')) || null,
      modelName: text(field(source, 'modelName')) || null,
      triggerMode: text(field(source, 'triggerMode')) || 'Software',
      isEnabled: boolean(field(source, 'isEnabled'), true),
      connectionStatus: text(field(source, 'connectionStatus')) || 'Unknown'
    });
  }));
}

async function blobToBase64(blob: Blob): Promise<string> {
  const bytes = new Uint8Array(await blob.arrayBuffer());
  const chunkSize = 0x8000;
  let binary = '';
  for (let offset = 0; offset < bytes.length; offset += chunkSize) {
    binary += String.fromCharCode(...bytes.subarray(offset, offset + chunkSize));
  }
  return globalThis.btoa(binary);
}

function positiveHeader(headers: Headers, name: string): number | null {
  const value = Number(headers.get(name));
  return Number.isFinite(value) && value > 0 ? value : null;
}

export function createCameraBindingEditorOwner(options: {
  readonly projectId: string;
  readonly flowOwner: FlowCanvasOwner;
  readonly api: ApiTransport;
}): CameraBindingEditorOwner {
  if (!options.api.get || !options.api.post || !options.api.getBlob || !options.api.postBlob) {
    throw new TypeError('相机编辑器需要 shared ApiTransport 的 GET、POST、GET blob 与 POST blob。');
  }
  const get = options.api.get.bind(options.api);
  const post = options.api.post.bind(options.api);
  const getBlob = options.api.getBlob.bind(options.api);
  const postBlob = options.api.postBlob.bind(options.api);
  const state = reactive<MutableProjection>({
    phase: 'idle',
    capturePhase: 'idle',
    bindings: Object.freeze([]),
    selectedNodeId: null,
    currentBindingId: null,
    frame: null,
    message: '正在读取相机绑定。',
    canCapture: false
  });
  let disposed = false;
  let generation = 0;
  let captureController: AbortController | null = null;
  let activePreviewSessionId: string | null = null;

  function selectedNode(): Readonly<Record<string, unknown>> | null {
    const selectedId = options.flowOwner.projection.runtime?.selectedNodeId ?? null;
    if (!selectedId) return null;
    return options.flowOwner.projection.draft.operators.find(item => nodeId(item) === selectedId) ?? null;
  }

  function syncSelection(): void {
    if (disposed) return;
    const node = selectedNode();
    const isAcquisition = isImageAcquisition(node ?? Object.freeze({}));
    state.selectedNodeId = isAcquisition && node ? nodeId(node) : null;
    state.currentBindingId = isAcquisition && node ? cameraBindingId(node) || null : null;
    const sourceType = isAcquisition && node ? text(parameterValue(node, 'SourceType')).toLowerCase() : '';
    const binding = state.bindings.find(item => item.id === state.currentBindingId);
    state.canCapture = options.flowOwner.projection.mutationGate === 'editable' && sourceType === 'camera' &&
      Boolean(binding?.isEnabled) && state.capturePhase !== 'capturing';
    if (state.frame && (!node || state.frame.sourceNodeId !== nodeId(node) || state.frame.sourceSignature !== sourceSignature(node))) {
      state.frame = null;
      state.capturePhase = 'idle';
      state.message = '采集配置已变化，旧单帧已失效，请重新捕获。';
    }
  }

  const stopWatch = watch(
    () => [
      options.flowOwner.projection.draft,
      options.flowOwner.projection.runtime?.selectedNodeId ?? null,
      options.flowOwner.projection.runtime?.flowRevision ?? 0,
      options.flowOwner.projection.mutationGate
    ] as const,
    syncSelection,
    { immediate: true, flush: 'sync' }
  );

  async function stopPreviewSession(): Promise<void> {
    const sessionId = activePreviewSessionId;
    activePreviewSessionId = null;
    if (!sessionId) return;
    await post('cameras/continuous-preview/stop', { sessionId }).catch(() => undefined);
  }

  async function captureBlob(binding: CameraBindingV1, signal: AbortSignal): Promise<ApiBlobResponse> {
    if (binding.triggerMode.trim().toLowerCase() === 'software') {
      return postBlob('cameras/soft-trigger-capture', { cameraBindingId: binding.id }, { signal });
    }
    const startPayload = record(await post('cameras/continuous-preview/start', { cameraBindingId: binding.id }, { signal }));
    const sessionId = text(field(startPayload, 'sessionId'));
    if (!sessionId) throw new TypeError('连续预览启动响应缺少 sessionId。');
    activePreviewSessionId = sessionId;
    try {
      return await getBlob(`cameras/continuous-preview/frame/${encodeURIComponent(sessionId)}?_=${Date.now()}`, { signal });
    } finally {
      await stopPreviewSession();
    }
  }

  const owner: CameraBindingEditorOwner = Object.freeze({
    projection: readonly(state),
    async refreshBindings(): Promise<void> {
      if (disposed) return;
      const operation = ++generation;
      state.phase = 'loading';
      state.message = '正在读取相机绑定。';
      try {
        const bindings = decodeBindings(await get('cameras/bindings'));
        if (disposed || operation !== generation) return;
        state.bindings = bindings;
        state.phase = 'ready';
        state.message = bindings.length > 0 ? `已读取 ${bindings.length} 个相机绑定。` : '尚未配置相机绑定。';
      } catch (error) {
        if (disposed || operation !== generation || error instanceof ApiAbortError) return;
        state.phase = 'error';
        state.message = error instanceof ApiForbiddenError
          ? '当前账户没有相机操作权限。'
          : '相机绑定读取失败，请检查设备服务后重试。';
      } finally {
        syncSelection();
      }
    },
    selectBinding(parameterName: string, bindingId: string): boolean {
      if (disposed || options.flowOwner.projection.mutationGate !== 'editable') return false;
      const node = selectedNode();
      if (!node || !isImageAcquisition(node)) return false;
      const binding = state.bindings.find(item => item.id === bindingId && item.isEnabled);
      if (!binding) {
        state.message = '所选相机绑定已失效，请刷新后重新选择。';
        return false;
      }
      captureController?.abort('camera-binding-changed');
      state.frame = null;
      const result = options.flowOwner.commands.patchNodeParameter({
        nodeId: nodeId(node),
        parameterName,
        value: binding.id
      });
      state.message = result.ok ? `已绑定相机：${binding.displayName}。` : result.message;
      syncSelection();
      return result.ok;
    },
    async capture(): Promise<CapturedCameraFrameV1 | null> {
      if (disposed || state.capturePhase === 'capturing') return null;
      const source = selectedNode();
      const binding = state.bindings.find(item => item.id === state.currentBindingId);
      if (!source || !state.canCapture || !binding) {
        state.capturePhase = 'error';
        state.message = '请先选择启用的相机绑定，并将采集源设为相机。';
        return null;
      }
      const operation = ++generation;
      const controller = new AbortController();
      captureController = controller;
      state.capturePhase = 'capturing';
      state.canCapture = false;
      state.message = `正在从 ${binding.displayName} 获取单帧。`;
      try {
        const response = await captureBlob(binding, controller.signal);
        if (response.blob.size === 0) throw new Error('相机未返回图像数据。');
        const imageBase64 = await blobToBase64(response.blob);
        if (disposed || operation !== generation || controller.signal.aborted) return null;
        const currentSource = options.flowOwner.projection.draft.operators.find(item => nodeId(item) === nodeId(source));
        if (!currentSource || sourceSignature(currentSource) !== sourceSignature(source)) {
          state.capturePhase = 'cancelled';
          state.message = '捕获期间采集配置已变化，返回帧已丢弃。';
          return null;
        }
        const frame = Object.freeze({
          projectId: options.projectId,
          sourceNodeId: nodeId(source),
          frameId: globalThis.crypto.randomUUID(),
          sourceSignature: sourceSignature(source),
          imageBase64,
          cameraBindingId: response.headers.get('X-Camera-Id') || binding.id,
          triggerMode: response.headers.get('X-Trigger-Mode') || binding.triggerMode,
          width: positiveHeader(response.headers, 'X-Image-Width'),
          height: positiveHeader(response.headers, 'X-Image-Height'),
          contentType: response.contentType,
          capturedAtUtc: new Date().toISOString()
        } satisfies CapturedCameraFrameV1);
        state.frame = frame;
        state.capturePhase = 'captured';
        state.message = frame.width && frame.height
          ? `单帧已捕获：${frame.width} x ${frame.height}。`
          : '单帧已捕获，可用于下游预览与 ROI。';
        return frame;
      } catch (error) {
        if (disposed || operation !== generation || error instanceof ApiAbortError || controller.signal.aborted) {
          if (!disposed) {
            state.capturePhase = 'cancelled';
            state.message = '单帧捕获已取消。';
          }
          return null;
        }
        state.frame = null;
        state.capturePhase = 'error';
        state.message = error instanceof ApiForbiddenError
          ? '当前账户没有相机操作权限。'
          : error instanceof ApiNotFoundError
            ? '相机绑定已不存在，请刷新绑定后重试。'
            : `单帧捕获失败：${error instanceof Error ? error.message : '设备未返回可用图像。'}`;
        return null;
      } finally {
        if (captureController === controller) captureController = null;
        syncSelection();
      }
    },
    cancelCapture(): void {
      if (disposed || state.capturePhase !== 'capturing') return;
      generation += 1;
      captureController?.abort('camera-capture-cancelled');
      captureController = null;
      void stopPreviewSession();
      state.capturePhase = 'cancelled';
      state.message = '单帧捕获已取消。';
      syncSelection();
    },
    getPreviewInputContext(
      targetNode: Readonly<Record<string, unknown>>
    ): Readonly<Record<string, unknown>> | null {
      const frame = state.frame;
      if (disposed || !frame || frame.projectId !== options.projectId) return null;
      const source = options.flowOwner.projection.draft.operators.find(item => nodeId(item) === frame.sourceNodeId);
      if (!source || !isImageAcquisition(source) ||
        field(source, 'isEnabled') === false || sourceSignature(source) !== frame.sourceSignature) {
        state.frame = null;
        state.capturePhase = 'idle';
        state.message = '采集节点或相机配置已变化，旧单帧已失效。';
        return null;
      }
      const connections = options.flowOwner.projection.draft.connections.map(record);
      if (!isReachable(frame.sourceNodeId, nodeId(targetNode), connections)) return null;
      return Object.freeze({
        imageBase64: frame.imageBase64,
        sourceNodeId: frame.sourceNodeId,
        frameId: frame.frameId
      });
    },
    dispose(): void {
      if (disposed) return;
      disposed = true;
      generation += 1;
      stopWatch();
      captureController?.abort('camera-owner-disposed');
      captureController = null;
      void stopPreviewSession();
      state.frame = null;
      state.bindings = Object.freeze([]);
      state.phase = 'disposed';
      state.capturePhase = 'idle';
      state.canCapture = false;
      state.message = '相机编辑器已释放。';
    }
  });

  void owner.refreshBindings();
  return owner;
}
