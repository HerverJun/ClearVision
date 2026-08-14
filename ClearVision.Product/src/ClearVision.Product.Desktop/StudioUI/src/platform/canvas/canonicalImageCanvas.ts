import {
  ImageCanvas,
  type CanonicalImageCanvasResourceDiagnostics
} from '@clearvision/canonical-image-canvas';

export interface CanonicalImageSnapshot {
  readonly source: CanvasImageSource & { readonly width: number; readonly height: number };
  readonly width: number;
  readonly height: number;
}

export interface CanonicalImageCanvasViewState {
  readonly scale: number;
  readonly offset: Readonly<{ x: number; y: number }>;
}

export interface CanonicalImageCanvasHostOptions {
  readonly interactionMode?: string;
  readonly enableRightButtonPan?: boolean;
  readonly onViewChanged?: (view: CanonicalImageCanvasViewState) => void;
}

export interface CanonicalImageCanvasHost {
  readonly element: HTMLCanvasElement;
  load(source: string | Blob | ArrayBuffer | Uint8Array): Promise<unknown>;
  clear(): void;
  fit(): void;
  actualSize(): void;
  getViewState(): CanonicalImageCanvasViewState;
  setViewState(state: CanonicalImageCanvasViewState): void;
  getImagePoint(event: MouseEvent | PointerEvent): Readonly<{ x: number; y: number }>;
  getImageSnapshot(): CanonicalImageSnapshot | null;
  setRoiMode(enabled: boolean): void;
  setRoiChanged(callback: ((geometry: unknown, phase: string) => void) | null): void;
  setEditableGeometry(geometry: unknown, resetDraft?: boolean): void;
  clearEditableGeometry(): void;
  cancelRoiInteraction(): void;
  undoRoi(): unknown;
  redoRoi(): unknown;
  applyRoiHistory(geometry: unknown): boolean;
  getResourceDiagnostics(): CanonicalImageCanvasResourceDiagnostics;
  dispose(): void;
}

let activeImageCanvasToken: symbol | undefined;

export class CanonicalImageCanvasOwnerConflictError extends Error {
  constructor() {
    super('A canonical ImageCanvas owner is already mounted.');
    this.name = 'CanonicalImageCanvasOwnerConflictError';
  }
}

export function createCanonicalImageCanvasHost(
  canvasId: string,
  options: Readonly<CanonicalImageCanvasHostOptions> = {}
): CanonicalImageCanvasHost {
  if (activeImageCanvasToken) throw new CanonicalImageCanvasOwnerConflictError();
  const token = Symbol(`canonical-image-canvas:${canvasId}`);
  activeImageCanvasToken = token;
  let canvas: ImageCanvas;
  try {
    canvas = new ImageCanvas(canvasId, options);
  } catch (error) {
    if (activeImageCanvasToken === token) activeImageCanvasToken = undefined;
    throw error;
  }
  let disposed = false;

  function assertActive(): void {
    if (disposed) throw new Error('Canonical ImageCanvas host has been disposed.');
  }

  return Object.freeze({
    element: canvas.canvas,
    load(source: string | Blob | ArrayBuffer | Uint8Array): Promise<unknown> {
      assertActive();
      return canvas.loadImage(source);
    },
    clear(): void { assertActive(); canvas.clear(); },
    fit(): void { assertActive(); canvas.fitToWindow(); },
    actualSize(): void { assertActive(); canvas.actualSize(); },
    getViewState() { assertActive(); return canvas.getViewState(); },
    setViewState(state: CanonicalImageCanvasViewState) {
      assertActive();
      canvas.setViewState(state);
    },
    getImagePoint(event: MouseEvent | PointerEvent) {
      assertActive();
      return canvas.getImagePointFromEvent(event);
    },
    getImageSnapshot(): CanonicalImageSnapshot | null {
      assertActive();
      return canvas.image
        ? Object.freeze({ source: canvas.image, width: canvas.image.width, height: canvas.image.height })
        : null;
    },
    setRoiMode(enabled: boolean): void {
      assertActive();
      canvas.setInteractionMode(enabled ? 'roi-rect' : 'legacy');
    },
    setRoiChanged(callback: ((geometry: unknown, phase: string) => void) | null): void {
      assertActive();
      canvas.setOverlayChangedCallback(callback);
    },
    setEditableGeometry(geometry: unknown, resetDraft = false): void {
      assertActive();
      canvas.setEditableGeometry(geometry, { resetDraft });
    },
    clearEditableGeometry(): void { assertActive(); canvas.clearEditableRectangle(); },
    cancelRoiInteraction(): void { if (!disposed) canvas.cancelActiveRoiInteraction(); },
    undoRoi(): unknown { assertActive(); return canvas.undoGeometryDraft(); },
    redoRoi(): unknown { assertActive(); return canvas.redoGeometryDraft(); },
    applyRoiHistory(geometry: unknown): boolean { assertActive(); return canvas.applyRoiDraftHistory(geometry); },
    getResourceDiagnostics(): CanonicalImageCanvasResourceDiagnostics {
      return canvas.getResourceDiagnostics();
    },
    dispose(): void {
      if (disposed) return;
      disposed = true;
      try {
        canvas.destroy();
      } finally {
        if (activeImageCanvasToken === token) activeImageCanvasToken = undefined;
      }
    }
  });
}
