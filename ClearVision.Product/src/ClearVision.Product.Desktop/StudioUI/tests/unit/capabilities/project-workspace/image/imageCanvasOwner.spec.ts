import { nextTick, reactive } from 'vue';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const mocks = vi.hoisted(() => {
  let imageSource: { width: number; height: number } | null = null;
  let view = { scale: 1, offset: { x: 0, y: 0 } };
  let onViewChanged: ((next: typeof view) => void) | undefined;
  const clear = vi.fn(() => { imageSource = null; });
  const load = vi.fn(async () => { imageSource = { width: 100, height: 100 }; });
  const createCanonicalImageCanvasHost = vi.fn((_canvasId: string, options?: {
    onViewChanged?: (next: typeof view) => void;
  }) => {
    imageSource = null;
    view = { scale: 1, offset: { x: 0, y: 0 } };
    onViewChanged = options?.onViewChanged;
    return {
      element: document.createElement('canvas'),
      load,
      clear,
      fit: vi.fn(),
      actualSize: vi.fn(),
      getViewState: () => view,
      setViewState: vi.fn((next: typeof view) => { view = next; onViewChanged?.(next); }),
      getImagePoint: () => ({ x: 0, y: 0 }),
      getImageSnapshot: () => imageSource
        ? ({ source: imageSource, width: imageSource.width, height: imageSource.height })
        : null,
      setRoiMode: vi.fn(),
      setRoiChanged: vi.fn(),
      setEditableGeometry: vi.fn(),
      clearEditableGeometry: vi.fn(),
      cancelRoiInteraction: vi.fn(),
      undoRoi: vi.fn(),
      redoRoi: vi.fn(),
      applyRoiHistory: vi.fn(),
      getResourceDiagnostics: () => ({}),
      dispose: vi.fn()
    };
  });
  const emitView = (next: typeof view): void => {
    view = next;
    onViewChanged?.(next);
  };
  return { clear, load, createCanonicalImageCanvasHost, emitView };
});

vi.mock('@/platform/canvas', () => ({ createCanonicalImageCanvasHost: mocks.createCanonicalImageCanvasHost }));

import { createImageCanvasOwner } from '@/capabilities/project-workspace/image/imageCanvasOwner';

describe('ImageCanvas owner stale projection', () => {
  beforeEach(() => {
    mocks.clear.mockClear();
    mocks.load.mockClear();
    mocks.createCanonicalImageCanvasHost.mockClear();
  });

  it('keeps the last bitmap read-only until a fresh preview result arrives', async () => {
    const projection = reactive({
      requestIdentity: { requestKey: 'request-1' },
      outputImageSrc: 'blob:request-1',
      inputImageSrc: null,
      isStale: false,
      phase: 'success',
      outputData: null
    });
    const lease = { update: vi.fn(), dispose: vi.fn() };
    const owner = createImageCanvasOwner({
      projectId: 'project-1',
      previewOwner: { projection } as never,
      diagnostics: { reserveImageCanvas: () => lease } as never
    });
    owner.mount('image-canvas-test');
    await nextTick();
    await Promise.resolve();

    expect(owner.projection).toMatchObject({ phase: 'ready', width: 100, height: 100 });
    expect(mocks.load).toHaveBeenCalledWith('blob:request-1');

    mocks.emitView({ scale: 2.079, offset: { x: 54.55, y: 11.55 } });
    expect(owner.projection).toMatchObject({ scale: 2.079, offsetX: 54.55, offsetY: 11.55 });

    projection.isStale = true;
    await nextTick();
    expect(owner.projection).toMatchObject({ phase: 'stale', width: 100, height: 100 });
    expect(mocks.clear).not.toHaveBeenCalled();
    expect(owner.roi.begin({ kind: 'rectangle', x: 1, y: 1, width: 2, height: 2 }, vi.fn())).toBe(false);

    projection.requestIdentity = { requestKey: 'request-2' };
    projection.outputImageSrc = 'blob:request-2';
    projection.isStale = false;
    await nextTick();
    await Promise.resolve();
    expect(mocks.load).toHaveBeenLastCalledWith('blob:request-2');
    expect(owner.projection).toMatchObject({ phase: 'ready', imageIdentity: 'request-2:output' });
    owner.dispose();
  });

  it('does not load a stale source when no previous bitmap exists', async () => {
    const projection = reactive({
      requestIdentity: { requestKey: 'request-stale' },
      outputImageSrc: 'blob:request-stale',
      inputImageSrc: null,
      isStale: true,
      phase: 'success',
      outputData: null
    });
    const lease = { update: vi.fn(), dispose: vi.fn() };
    const owner = createImageCanvasOwner({
      projectId: 'project-stale',
      previewOwner: { projection } as never,
      diagnostics: { reserveImageCanvas: () => lease } as never
    });

    owner.mount('image-canvas-stale');
    await nextTick();
    await Promise.resolve();

    expect(mocks.load).not.toHaveBeenCalled();
    expect(mocks.clear).toHaveBeenCalledOnce();
    expect(owner.projection).toMatchObject({ phase: 'empty', imageIdentity: null, width: 0, height: 0 });
    owner.dispose();
  });
});
