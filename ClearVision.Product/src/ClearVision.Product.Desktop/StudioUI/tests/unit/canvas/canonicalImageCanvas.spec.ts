import { afterEach, describe, expect, it, vi } from 'vitest';

const destroy = vi.hoisted(() => vi.fn());
const loadImage = vi.hoisted(() => vi.fn(async () => undefined));

vi.mock('@clearvision/canonical-image-canvas', () => ({
  ImageCanvas: class FakeImageCanvas {
    canvas: HTMLCanvasElement;
    image: (HTMLImageElement & { width: number; height: number }) | null = null;
    private view = { scale: 1, offset: { x: 0, y: 0 } };
    constructor(canvasId: string) {
      this.canvas = document.getElementById(canvasId) as HTMLCanvasElement;
    }
    loadImage = loadImage;
    clear() { this.image = null; }
    fitToWindow() { this.view = { scale: 0.5, offset: { x: 2, y: 3 } }; }
    actualSize() { this.view = { scale: 1, offset: { x: 0, y: 0 } }; }
    getViewState() { return this.view; }
    setViewState(value: typeof this.view) { this.view = value; }
    getImagePointFromEvent() { return { x: 4, y: 5 }; }
    setInteractionMode() {}
    setOverlayChangedCallback() {}
    setEditableGeometry() {}
    clearEditableRectangle() {}
    cancelActiveRoiInteraction() { return true; }
    undoGeometryDraft() { return null; }
    redoGeometryDraft() { return null; }
    applyRoiDraftHistory() { return false; }
    getResourceDiagnostics() {
      return {
        destroyed: false,
        animationFramePending: false,
        resizeFramePending: false,
        resizeObserverActive: true,
        currentBlobUrlCount: 0,
        pendingBlobUrlCount: 0,
        imageLoadGeneration: 1,
        pointerCaptureActive: false,
        interactionActive: false,
        overlayCount: 0
      };
    }
    destroy = destroy;
  }
}));

import {
  CanonicalImageCanvasOwnerConflictError,
  createCanonicalImageCanvasHost
} from '@/platform/canvas';

afterEach(() => {
  document.body.innerHTML = '';
  destroy.mockClear();
  loadImage.mockClear();
});

describe('canonical ImageCanvas production facade', () => {
  it('enforces one owner and exposes only narrow image/ROI commands', async () => {
    document.body.innerHTML = '<canvas id="image-one"></canvas><canvas id="image-two"></canvas>';
    const host = createCanonicalImageCanvasHost('image-one');
    expect(() => createCanonicalImageCanvasHost('image-two')).toThrow(CanonicalImageCanvasOwnerConflictError);
    await host.load(new Blob(['image'], { type: 'image/png' }));
    expect(loadImage).toHaveBeenCalledTimes(1);
    expect(host.getImagePoint(new MouseEvent('mousemove'))).toEqual({ x: 4, y: 5 });
    expect(host.getResourceDiagnostics()).toMatchObject({ resizeObserverActive: true });
    host.dispose();
    expect(destroy).toHaveBeenCalledTimes(1);

    const replacement = createCanonicalImageCanvasHost('image-two');
    replacement.dispose();
  });
});
