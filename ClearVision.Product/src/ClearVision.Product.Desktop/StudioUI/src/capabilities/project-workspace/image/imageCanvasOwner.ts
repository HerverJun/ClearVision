import { reactive, readonly, watch, type DeepReadonly } from 'vue';
import { ImagePixelProbe } from '@clearvision/canonical-image-pixel-probe';
import {
  createCanonicalImageCanvasHost,
  type CanonicalImageCanvasHost
} from '@/platform/canvas';
import type { PreviewOwner } from '../preview/previewOwner';
import type {
  WorkspaceCapabilityDiagnosticsLease,
  WorkspaceLifecycleDiagnosticsOwner,
  WorkspaceResourceSnapshot
} from '../workspaceLifecycleDiagnostics';
import {
  createPixelProbeProjectionModel,
  type PixelProbeProjection,
  type PixelProbeProjectionModel
} from './pixelProbeProjection';

export type ImageCanvasOwnerPhase = 'unmounted' | 'empty' | 'loading' | 'ready' | 'error' | 'disposed';

export interface ImageCanvasOwnerProjection {
  readonly phase: ImageCanvasOwnerPhase;
  readonly projectId: string;
  readonly imageIdentity: string | null;
  readonly imageGeneration: number;
  readonly width: number;
  readonly height: number;
  readonly scale: number;
  readonly offsetX: number;
  readonly offsetY: number;
  readonly dpr: number;
  readonly viewMode: 'fit' | 'actual' | 'custom';
  readonly roiEditing: boolean;
  readonly errorMessage: string | null;
  readonly pixelProbe: PixelProbeProjection;
}

type MutableImageCanvasOwnerProjection = {
  -readonly [Key in keyof ImageCanvasOwnerProjection]: ImageCanvasOwnerProjection[Key]
};

export interface ImageCanvasRoiPort {
  readonly projection: DeepReadonly<ImageCanvasOwnerProjection>;
  begin(geometry: unknown, onChanged: (geometry: unknown, phase: string) => void): boolean;
  replace(geometry: unknown, resetDraft?: boolean): boolean;
  cancelInteraction(): void;
  undo(): unknown;
  redo(): unknown;
  end(): void;
  showStatistics(geometry: unknown): void;
}

export interface ImageCanvasClick {
  readonly x: number;
  readonly y: number;
  readonly imageIdentity: string;
  readonly imageGeneration: number;
  readonly width: number;
  readonly height: number;
}

export interface ImageCanvasOwner {
  readonly projectId: string;
  readonly projection: DeepReadonly<ImageCanvasOwnerProjection>;
  readonly roi: ImageCanvasRoiPort;
  subscribeImageClick(listener: (click: ImageCanvasClick) => void): () => void;
  mount(canvasId: string): void;
  fit(): void;
  actualSize(): void;
  zoomIn(): void;
  zoomOut(): void;
  showArtifact(blob: Blob, identity: string): Promise<void>;
  restorePrimary(): Promise<void>;
  clearPixelLock(): void;
  dispose(reason?: string): void;
}

function zeroResources(): WorkspaceResourceSnapshot {
  return Object.freeze({
    activeSubscriptions: 0,
    activeTimers: 0,
    activeAnimationFrames: 0,
    activeObservers: 0,
    activeAbortControllers: 0,
    activeBlobUrls: 0,
    activePreviewArtifactIds: 0,
    activeHostSubscriptions: 0,
    inFlightReads: 0,
    inFlightWrites: 0,
    inFlightPreview: 0,
    inFlightExecute: 0
  });
}

function number(value: unknown, fallback = 0): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : fallback;
}

function geometryBounds(geometry: unknown): Readonly<{ x: number; y: number; width: number; height: number }> | null {
  if (!geometry || typeof geometry !== 'object') return null;
  const source = geometry as Readonly<Record<string, unknown>>;
  const kind = String(source.kind ?? source.type ?? 'rectangle');
  if (kind === 'circle') {
    const radius = Math.max(0, number(source.radius));
    return Object.freeze({
      x: number(source.centerX) - radius,
      y: number(source.centerY) - radius,
      width: radius * 2,
      height: radius * 2
    });
  }
  if (kind === 'annulus' || kind === 'arc' || kind === 'circleSearchV2') {
    const radius = Math.max(0, number(source.outerRadius ?? source.maxRadius ?? source.radius));
    return Object.freeze({
      x: number(source.centerX) - radius,
      y: number(source.centerY) - radius,
      width: radius * 2,
      height: radius * 2
    });
  }
  const points = Array.isArray(source.points) ? source.points : [];
  if (points.length > 0) {
    const xs = points.map(point => number((point as Readonly<Record<string, unknown>>).x));
    const ys = points.map(point => number((point as Readonly<Record<string, unknown>>).y));
    const minX = Math.min(...xs);
    const maxX = Math.max(...xs);
    const minY = Math.min(...ys);
    const maxY = Math.max(...ys);
    return Object.freeze({ x: minX, y: minY, width: maxX - minX, height: maxY - minY });
  }
  return Object.freeze({
    x: number(source.x),
    y: number(source.y),
    width: Math.max(0, number(source.width)),
    height: Math.max(0, number(source.height))
  });
}

export function createImageCanvasOwner(options: {
  readonly projectId: string;
  readonly previewOwner: PreviewOwner;
  readonly diagnostics: WorkspaceLifecycleDiagnosticsOwner;
}): ImageCanvasOwner {
  const lease: WorkspaceCapabilityDiagnosticsLease = options.diagnostics.reserveImageCanvas(options.projectId);
  const pixelModel: PixelProbeProjectionModel = createPixelProbeProjectionModel();
  const pixelReader = new ImagePixelProbe();
  const state = reactive<MutableImageCanvasOwnerProjection>({
    phase: 'unmounted',
    projectId: options.projectId,
    imageIdentity: null,
    imageGeneration: 0,
    width: 0,
    height: 0,
    scale: 1,
    offsetX: 0,
    offsetY: 0,
    dpr: globalThis.devicePixelRatio || 1,
    viewMode: 'fit',
    roiEditing: false,
    errorMessage: null,
    pixelProbe: pixelModel.getProjection()
  });
  let canvas: CanonicalImageCanvasHost | undefined;
  let canvasId: string | undefined;
  let disposed = false;
  let pendingSource: string | null = null;
  let pendingIdentity: string | null = null;
  let primarySource: string | null = null;
  let primaryIdentity: string | null = null;
  let roiChanged: ((geometry: unknown, phase: string) => void) | undefined;
  const imageClickListeners = new Set<(click: ImageCanvasClick) => void>();
  const domCleanup: Array<() => void> = [];

  function imageElement(): (CanvasImageSource & { readonly width: number; readonly height: number }) | null {
    return canvas?.getImageSnapshot()?.source ?? null;
  }

  function syncView(): void {
    if (!canvas || disposed) return;
    const view = canvas.getViewState();
    state.scale = number(view.scale, 1);
    state.offsetX = number(view.offset.x);
    state.offsetY = number(view.offset.y);
    state.dpr = globalThis.devicePixelRatio || 1;
    const image = imageElement();
    state.width = image?.width ?? 0;
    state.height = image?.height ?? 0;
    state.pixelProbe = pixelModel.setImageContext({
      identity: state.imageIdentity,
      status: state.phase === 'loading' ? 'loading' : state.phase === 'ready' ? 'ready' : 'no-image',
      width: state.width,
      height: state.height,
      scale: state.scale,
      worldSource: options.previewOwner.projection.outputData
    });
    syncDiagnostics();
  }

  function syncDiagnostics(): void {
    if (disposed) return;
    const resources = canvas?.getResourceDiagnostics();
    lease.update(Object.freeze({
      activeSubscriptions: 1 + domCleanup.length,
      activeTimers: 0,
      activeAnimationFrames: Number(resources?.animationFramePending === true) +
        Number(resources?.resizeFramePending === true),
      activeObservers: Number(resources?.resizeObserverActive === true),
      activeAbortControllers: 0,
      activeBlobUrls: number(resources?.currentBlobUrlCount) + number(resources?.pendingBlobUrlCount),
      activePreviewArtifactIds: 0,
      activeHostSubscriptions: 0,
      inFlightReads: state.phase === 'loading' ? 1 : 0,
      inFlightWrites: 0,
      inFlightPreview: 0,
      inFlightExecute: 0
    }));
  }

  async function loadPendingImage(): Promise<void> {
    if (!canvas || disposed) return;
    const source = pendingSource;
    const identity = pendingIdentity;
    const generation = ++state.imageGeneration;
    pixelReader.reset();
    state.imageIdentity = identity;
    state.errorMessage = null;
    if (!source || !identity) {
      canvas.clear();
      state.phase = 'empty';
      state.width = 0;
      state.height = 0;
      state.pixelProbe = pixelModel.setImageContext({ identity: null, status: 'no-image' });
      syncDiagnostics();
      return;
    }
    state.phase = 'loading';
    state.pixelProbe = pixelModel.setImageContext({ identity, status: 'loading' });
    syncDiagnostics();
    try {
      await canvas.load(source);
      if (disposed || generation !== state.imageGeneration || identity !== pendingIdentity) return;
      state.phase = 'ready';
      state.viewMode = 'fit';
      syncView();
    } catch (error) {
      if (disposed || generation !== state.imageGeneration) return;
      state.phase = 'error';
      state.errorMessage = error instanceof Error ? error.message : '图像加载失败。';
      state.pixelProbe = pixelModel.setImageContext({ identity: null, status: 'no-image' });
      syncDiagnostics();
    }
  }

  function handlePointerMove(event: PointerEvent): void {
    if (!canvas || state.phase !== 'ready' || state.roiEditing) return;
    const image = imageElement();
    if (!image) return;
    const point = canvas.getImagePoint(event);
    const x = Math.floor(point.x);
    const y = Math.floor(point.y);
    if (x < 0 || y < 0 || x >= image.width || y >= image.height) {
      state.pixelProbe = pixelModel.showOutside();
      return;
    }
    const sample = pixelReader.readPixel(image, x, y);
    if (sample.ok !== true || !sample.rgba) {
      state.pixelProbe = pixelModel.showUnreadable();
      return;
    }
    state.pixelProbe = pixelModel.showHover({ x, y, rgba: sample.rgba as ArrayLike<number> });
  }

  function handleClick(event: MouseEvent): void {
    if (!canvas || state.phase !== 'ready' || state.roiEditing) return;
    const image = imageElement();
    if (!image) return;
    const point = canvas.getImagePoint(event);
    const x = Math.floor(point.x);
    const y = Math.floor(point.y);
    if (x < 0 || y < 0 || x >= image.width || y >= image.height) return;
    const sample = pixelReader.readPixel(image, x, y);
    if (sample.ok !== true || !sample.rgba) return;
    const neighborhoods = Object.freeze({
      3: pixelReader.readNeighborhoodStats(image, x, y, 3) as unknown as { ok: boolean },
      5: pixelReader.readNeighborhoodStats(image, x, y, 5) as unknown as { ok: boolean }
    });
    state.pixelProbe = pixelModel.lockPixel({
      x,
      y,
      rgba: sample.rgba as ArrayLike<number>,
      neighborhoods
    });
    if (state.imageIdentity) {
      const click = Object.freeze({
        x,
        y,
        imageIdentity: state.imageIdentity,
        imageGeneration: state.imageGeneration,
        width: image.width,
        height: image.height
      });
      for (const listener of [...imageClickListeners]) listener(click);
    }
  }

  function bindCanvasEvents(element: HTMLCanvasElement): void {
    const sync = (): void => syncView();
    element.addEventListener('pointermove', handlePointerMove);
    element.addEventListener('click', handleClick);
    element.addEventListener('wheel', sync);
    element.addEventListener('mouseup', sync);
    element.addEventListener('pointerup', sync);
    domCleanup.push(
      () => element.removeEventListener('pointermove', handlePointerMove),
      () => element.removeEventListener('click', handleClick),
      () => element.removeEventListener('wheel', sync),
      () => element.removeEventListener('mouseup', sync),
      () => element.removeEventListener('pointerup', sync)
    );
  }

  function zoomBy(factor: number): void {
    if (!canvas || state.phase !== 'ready') return;
    const view = canvas.getViewState();
    const nextScale = Math.max(0.1, Math.min(10, view.scale * factor));
    const centerX = canvas.element.clientWidth / 2;
    const centerY = canvas.element.clientHeight / 2;
    const ratio = nextScale / view.scale;
    canvas.setViewState({
      scale: nextScale,
      offset: {
        x: centerX - (centerX - view.offset.x) * ratio,
        y: centerY - (centerY - view.offset.y) * ratio
      }
    });
    state.viewMode = 'custom';
    syncView();
  }

  const stopPreviewWatch = watch(
    () => [
      options.previewOwner.projection.requestIdentity?.requestKey ?? null,
      options.previewOwner.projection.outputImageSrc,
      options.previewOwner.projection.inputImageSrc,
      options.previewOwner.projection.isStale,
      options.previewOwner.projection.phase
    ] as const,
    ([requestKey, output, input, stale, phase]) => {
      if (disposed) return;
      pendingSource = stale || phase === 'loading' ? null : output || input;
      pendingIdentity = pendingSource && requestKey ? `${requestKey}:${output ? 'output' : 'input'}` : null;
      primarySource = pendingSource;
      primaryIdentity = pendingIdentity;
      void loadPendingImage();
    },
    { immediate: true }
  );

  const roiPort: ImageCanvasRoiPort = Object.freeze({
    projection: readonly(state),
    begin(geometry: unknown, onChanged: (next: unknown, phase: string) => void): boolean {
      if (!canvas || state.phase !== 'ready' || !geometry) return false;
      roiChanged = onChanged;
      state.roiEditing = true;
      pixelModel.reset();
      canvas.setRoiMode(true);
      canvas.setRoiChanged((next, phase) => roiChanged?.(next, phase));
      canvas.setEditableGeometry(geometry, true);
      syncDiagnostics();
      return true;
    },
    replace(geometry: unknown, resetDraft = false): boolean {
      if (!canvas || !state.roiEditing || !geometry) return false;
      canvas.setEditableGeometry(geometry, resetDraft);
      return true;
    },
    cancelInteraction(): void {
      canvas?.cancelRoiInteraction();
    },
    undo(): unknown {
      if (!canvas || !state.roiEditing) return null;
      const geometry = canvas.undoRoi();
      if (geometry) canvas.applyRoiHistory(geometry);
      return geometry;
    },
    redo(): unknown {
      if (!canvas || !state.roiEditing) return null;
      const geometry = canvas.redoRoi();
      if (geometry) canvas.applyRoiHistory(geometry);
      return geometry;
    },
    end(): void {
      if (!canvas) return;
      canvas.cancelRoiInteraction();
      canvas.setRoiChanged(null);
      canvas.clearEditableGeometry();
      canvas.setRoiMode(false);
      roiChanged = undefined;
      state.roiEditing = false;
      syncDiagnostics();
    },
    showStatistics(geometry: unknown): void {
      if (!canvas || state.phase !== 'ready') return;
      const image = imageElement();
      const bounds = geometryBounds(geometry);
      if (!image || !bounds) return;
      const result = pixelReader.readRoiStats(image, bounds);
      if (result.ok === true && result.roi && result.stats) {
        state.pixelProbe = pixelModel.showRoi({
          roi: result.roi as { x: number; y: number; width: number; height: number },
          stats: result.stats as { ok: boolean; gray?: { mean: number; min: number; max: number } }
        });
      }
    }
  });

  return Object.freeze({
    projectId: options.projectId,
    projection: readonly(state),
    roi: roiPort,
    subscribeImageClick(listener: (click: ImageCanvasClick) => void): () => void {
      if (disposed) return () => {};
      imageClickListeners.add(listener);
      return () => imageClickListeners.delete(listener);
    },
    mount(nextCanvasId: string): void {
      if (disposed) throw new Error('ImageCanvas owner has been disposed.');
      if (canvas) throw new Error(`ImageCanvas owner already mounted for project ${options.projectId}.`);
      canvasId = nextCanvasId;
      canvas = createCanonicalImageCanvasHost(canvasId, { interactionMode: 'legacy', enableRightButtonPan: false });
      bindCanvasEvents(canvas.element);
      state.phase = 'empty';
      syncDiagnostics();
      void loadPendingImage();
    },
    fit(): void {
      if (!canvas || state.phase !== 'ready') return;
      canvas.fit();
      state.viewMode = 'fit';
      syncView();
    },
    actualSize(): void {
      if (!canvas || state.phase !== 'ready') return;
      canvas.actualSize();
      state.viewMode = 'actual';
      syncView();
    },
    zoomIn(): void { zoomBy(1.2); },
    zoomOut(): void { zoomBy(1 / 1.2); },
    async showArtifact(blob: Blob, identity: string): Promise<void> {
      if (!canvas || disposed || !identity) return;
      const generation = ++state.imageGeneration;
      state.phase = 'loading';
      state.imageIdentity = identity;
      state.pixelProbe = pixelModel.setImageContext({ identity, status: 'loading' });
      syncDiagnostics();
      try {
        await canvas.load(blob);
        if (disposed || generation !== state.imageGeneration) return;
        state.phase = 'ready';
        state.viewMode = 'fit';
        syncView();
      } catch (error) {
        if (disposed || generation !== state.imageGeneration) return;
        state.phase = 'error';
        state.errorMessage = error instanceof Error ? error.message : '附加图像加载失败。';
        syncDiagnostics();
      }
    },
    restorePrimary(): Promise<void> {
      pendingSource = primarySource;
      pendingIdentity = primaryIdentity;
      return loadPendingImage();
    },
    clearPixelLock(): void {
      state.pixelProbe = pixelModel.reset();
    },
    dispose(reason = 'image-canvas-owner-disposed'): void {
      if (disposed) return;
      disposed = true;
      stopPreviewWatch();
      while (domCleanup.length > 0) domCleanup.pop()?.();
      roiChanged = undefined;
      imageClickListeners.clear();
      canvas?.setRoiChanged(null);
      canvas?.dispose();
      canvas = undefined;
      canvasId = undefined;
      pixelReader.reset();
      state.phase = 'disposed';
      state.imageIdentity = null;
      state.width = 0;
      state.height = 0;
      state.roiEditing = false;
      state.pixelProbe = pixelModel.setImageContext({ identity: null, status: 'no-image' });
      lease.update(zeroResources());
      lease.dispose(reason);
    }
  });
}
