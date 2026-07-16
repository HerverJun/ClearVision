import { describe, expect, it } from 'vitest';
import {
  PIXEL_PROBE_STALE_MESSAGE,
  createPixelProbeProjectionModel
} from '@/capabilities/project-workspace/image/pixelProbeProjection';

const readyImage = Object.freeze({
  identity: 'project-a/node-a/revision-4/request-9/image-primary',
  status: 'ready' as const,
  width: 640,
  height: 480,
  scale: 1
});

describe('pixelProbeProjection', () => {
  it('projects explicit no-image, loading, stale, outside and unreadable states', () => {
    const model = createPixelProbeProjectionModel();

    expect(model.getProjection()).toMatchObject({ phase: 'no-image', canProbe: false });
    expect(model.setImageContext({ identity: 'image-a', status: 'loading' }))
      .toMatchObject({ phase: 'loading', canProbe: false });
    expect(model.setImageContext({ ...readyImage, status: 'stale' }))
      .toMatchObject({ phase: 'stale', message: PIXEL_PROBE_STALE_MESSAGE, canProbe: false });

    expect(model.setImageContext(readyImage)).toMatchObject({ phase: 'idle', canProbe: true });
    expect(model.showOutside()).toMatchObject({ phase: 'outside', canProbe: true });
    expect(model.showUnreadable()).toMatchObject({ phase: 'unreadable', canProbe: true });
  });

  it('formats hover pixels without owning an image element or canvas', () => {
    const model = createPixelProbeProjectionModel(readyImage);
    const projection = model.showHover({
      x: 12,
      y: 34,
      rgba: new Uint8ClampedArray([10, 20, 30, 255])
    });

    expect(projection).toMatchObject({
      identity: readyImage.identity,
      phase: 'hover',
      point: { x: 12, y: 34 },
      rgba: [10, 20, 30, 255],
      imageWidth: 640,
      imageHeight: 480
    });
    expect(projection.message).toContain('X: 12');
    expect(projection.message).toContain('RGB: 10,20,30');
  });

  it('formats locked pixels, neighborhoods and explicit pixel-to-world coordinates', () => {
    const model = createPixelProbeProjectionModel({
      ...readyImage,
      worldSource: {
        pixelToWorld: {
          matrix: [2, 0, 10, 0, 3, 20],
          unit: 'mm',
          frameId: 'camera-1'
        }
      }
    });
    const projection = model.lockPixel({
      x: 4,
      y: 5,
      rgba: [80, 80, 80, 255],
      neighborhoods: {
        3: { ok: true, count: 9, gray: { mean: 80, min: 70, max: 90 }, rgbMean: null },
        5: { ok: true, count: 25, gray: { mean: 81, min: 60, max: 100 }, rgbMean: null }
      }
    });

    expect(projection.phase).toBe('locked');
    expect(projection.world).toMatchObject({
      kind: 'world',
      x: 18,
      y: 35,
      unit: 'mm',
      frameId: 'camera-1'
    });
    expect(projection.message).toContain('3x3');
    expect(projection.message).toContain('世界: 18,35 mm @camera-1');
  });

  it('projects ROI statistics and explains unavailable world coordinates', () => {
    const model = createPixelProbeProjectionModel(readyImage);
    const projection = model.showRoi({
      roi: { x: 10, y: 20, width: 30, height: 40 },
      stats: {
        ok: true,
        count: 1200,
        gray: { mean: 42.5, min: 2, max: 240 },
        rgbMean: { r: 40, g: 42, b: 45 }
      }
    });

    expect(projection).toMatchObject({
      phase: 'roi',
      roi: { x: 10, y: 20, width: 30, height: 40 },
      stats: { ok: true, count: 1200 }
    });
    expect(projection.message).toContain('ROI x:10 y:20 w:30 h:40');
    expect(projection.message).toContain('未配置标定/暂无世界坐标');
  });

  it('preserves a lock across same-identity view updates and resets on identity change', () => {
    const model = createPixelProbeProjectionModel(readyImage);
    model.lockPixel({ x: 2, y: 3, rgba: [1, 2, 3, 255] });

    const zoomed = model.setImageContext({ ...readyImage, scale: 2 });
    expect(zoomed.phase).toBe('locked');
    expect(zoomed.message).toContain('缩放: 200%');

    const changed = model.setImageContext({
      ...readyImage,
      identity: 'project-a/node-b/revision-5/request-10/image-primary'
    });
    expect(changed).toMatchObject({ phase: 'idle', point: null, rgba: null, roi: null });
  });

  it('reset clears observations while retaining the current image state', () => {
    const model = createPixelProbeProjectionModel(readyImage);
    model.showHover({ x: 8, y: 9, rgba: [1, 1, 1, 255] });

    expect(model.reset()).toMatchObject({
      identity: readyImage.identity,
      phase: 'idle',
      canProbe: true,
      point: null,
      rgba: null,
      roi: null
    });

    model.setImageContext({ ...readyImage, status: 'stale' });
    expect(model.showHover({ x: 1, y: 1, rgba: [0, 0, 0, 255] }).phase).toBe('stale');
  });
});
