declare module '@clearvision/canonical-preview-coordinator' {
  export interface CanonicalPreviewCoordinatorOptions {
    readonly getProjectId: () => string | null;
    readonly getFlowRevision: () => number;
    readonly getNodeById: (nodeId: string) => Readonly<Record<string, unknown>> | null;
    readonly getOperatorMetadata: (type: string) => Readonly<Record<string, unknown>> | null;
    readonly getInputImageBase64: () => string | null | Promise<string | null>;
    readonly getInputImageContext?: (
      node: Readonly<Record<string, unknown>>
    ) => unknown | Promise<unknown>;
    readonly previewExecutor: (
      nodeId: string,
      options: Readonly<Record<string, unknown>>
    ) => Promise<unknown>;
    readonly artifactClient: {
      getPreviewArtifactBlob(
        artifactId: string,
        options?: Readonly<{ signal?: AbortSignal }>
      ): Promise<Readonly<{ blob: Blob; headers?: Headers }>>;
      deletePreviewArtifact(artifactId: string): Promise<void>;
      getResourceDiagnostics?(): unknown;
    };
    readonly subscribeStructureState?: (listener: () => void) => () => void;
    readonly debounceMs?: number;
    readonly maxCacheEntries?: number;
    readonly maxCacheOutputImageBase64Chars?: number;
  }

  export class NodePreviewCoordinator {
    constructor(options: CanonicalPreviewCoordinatorOptions);
    getState(): Readonly<Record<string, unknown>>;
    subscribe(listener: (state: Readonly<Record<string, unknown>>) => void): () => void;
    setActiveNode(node: Readonly<Record<string, unknown>> | null, options?: Readonly<Record<string, unknown>>): void;
    requestActivePreview(options?: Readonly<Record<string, unknown>>): Promise<unknown>;
    invalidateActivePreview(options?: Readonly<Record<string, unknown>>): Promise<unknown>;
    cancelPreview(reason?: string): void;
    readArtifactForCurrentState(
      artifactId: string,
      expectedIdentity: Readonly<Record<string, unknown>>,
      options?: Readonly<{ signal?: AbortSignal; objectUrl?: boolean }>
    ): Promise<Readonly<{ artifact: unknown; blob: Blob; headers?: Headers; objectUrl: string | null }>>;
    getResourceDiagnostics(): Readonly<Record<string, number | boolean>>;
    destroy(): void;
  }

  export function getOperatorPreviewCostPolicy(
    node: Readonly<Record<string, unknown>>,
    metadata?: Readonly<Record<string, unknown>> | null
  ): Readonly<Record<string, unknown>>;
}

declare module '@clearvision/canonical-image-canvas' {
  export interface CanonicalImageCanvasResourceDiagnostics {
    readonly destroyed: boolean;
    readonly animationFramePending: boolean;
    readonly resizeFramePending: boolean;
    readonly resizeObserverActive: boolean;
    readonly currentBlobUrlCount: number;
    readonly pendingBlobUrlCount: number;
    readonly imageLoadGeneration: number;
    readonly pointerCaptureActive: boolean;
    readonly interactionActive: boolean;
    readonly overlayCount: number;
  }

  export class ImageCanvas {
    readonly canvas: HTMLCanvasElement;
    readonly image: CanvasImageSource & { readonly width: number; readonly height: number } | null;
    readonly scale: number;
    readonly offset: Readonly<{ x: number; y: number }>;
    constructor(canvasId: string, options?: Readonly<Record<string, unknown>>);
    loadImage(source: string | Blob | ArrayBuffer | Uint8Array): Promise<unknown>;
    clear(): void;
    resize(): void;
    fitToScreen(): void;
    fitToWindow(): void;
    actualSize(): void;
    getViewState(): Readonly<{ scale: number; offset: Readonly<{ x: number; y: number }> }>;
    setViewState(state: Readonly<{ scale: number; offset: Readonly<{ x: number; y: number }> }>): void;
    setInteractionMode(mode: string): void;
    setOverlayChangedCallback(callback: ((geometry: unknown, phase: string) => void) | null): void;
    setEditableGeometry(geometry: unknown, options?: Readonly<Record<string, unknown>>): unknown;
    clearEditableRectangle(): void;
    cancelActiveRoiInteraction(): boolean;
    undoGeometryDraft(): unknown;
    redoGeometryDraft(): unknown;
    applyRoiDraftHistory(geometry: unknown): boolean;
    getImagePointFromEvent(event: MouseEvent | PointerEvent): Readonly<{ x: number; y: number }>;
    getImageBounds(): Readonly<{ width: number; height: number }>;
    getResourceDiagnostics(): CanonicalImageCanvasResourceDiagnostics;
    destroy(): void;
  }
}

declare module '@clearvision/canonical-roi-support' {
  export function getOperatorRoiConfig(
    operator: Readonly<Record<string, unknown>>,
    options?: Readonly<Record<string, boolean>>
  ): Readonly<Record<string, unknown>>;
  export function geometryFromParams(
    values: Readonly<Record<string, unknown>>,
    config: Readonly<Record<string, unknown>>,
    bounds?: Readonly<{ width: number; height: number }> | null
  ): unknown;
  export function geometryToParams(
    geometry: unknown,
    config: Readonly<Record<string, unknown>>
  ): Readonly<Record<string, unknown>>;
}

declare module '@clearvision/canonical-roi-geometry' {
  export function screenToImagePoint(
    point: Readonly<{ x: number; y: number }>,
    viewport: Readonly<Record<string, unknown>>
  ): Readonly<{ x: number; y: number }>;
  export function imageToScreenPoint(
    point: Readonly<{ x: number; y: number }>,
    viewport: Readonly<Record<string, unknown>>
  ): Readonly<{ x: number; y: number }>;
}

declare module '@clearvision/canonical-image-pixel-probe' {
  export const PIXEL_PROBE_DEFAULT_MESSAGE: string;
  export const PIXEL_PROBE_OUTSIDE_MESSAGE: string;
  export const PIXEL_PROBE_NO_IMAGE_MESSAGE: string;
  export const PIXEL_PROBE_LOADING_MESSAGE: string;
  export const PIXEL_PROBE_UNREADABLE_MESSAGE: string;
  export const PIXEL_PROBE_NO_WORLD_MESSAGE: string;
  export function formatPixelProbeStatus(value: Readonly<Record<string, unknown>>): string;
  export function formatLockedPixelProbeStatus(value: Readonly<Record<string, unknown>>): string;
  export function formatRoiProbeStatus(value: Readonly<Record<string, unknown>>): string;
  export function resolvePixelWorldCoordinate(
    point: Readonly<{ x: number; y: number }>,
    previewState: unknown
  ): Readonly<Record<string, unknown>>;
  export class ImagePixelProbe {
    reset(): void;
    mapPoint(
      point: Readonly<{ x: number; y: number }>,
      imageElement: CanvasImageSource & { readonly width: number; readonly height: number },
      options?: Readonly<Record<string, unknown>>
    ): Readonly<Record<string, unknown>>;
    probePoint(point: Readonly<Record<string, unknown>>, imageElement: CanvasImageSource): Readonly<Record<string, unknown>>;
    createLockedPoint(
      mapped: Readonly<Record<string, unknown>>,
      imageElement: CanvasImageSource,
      previewState?: unknown
    ): Readonly<Record<string, unknown>>;
    createRoiSelection(
      roi: Readonly<Record<string, unknown>>,
      imageElement: CanvasImageSource,
      previewState?: unknown
    ): Readonly<Record<string, unknown>>;
    readPixel(imageElement: CanvasImageSource, x: number, y: number): Readonly<Record<string, unknown>>;
    readNeighborhoodStats(
      imageElement: CanvasImageSource,
      x: number,
      y: number,
      size?: number
    ): Readonly<Record<string, unknown>>;
    readRoiStats(imageElement: CanvasImageSource, roi: Readonly<Record<string, unknown>>): Readonly<Record<string, unknown>>;
  }
}
