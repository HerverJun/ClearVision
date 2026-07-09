import test from 'node:test';
import assert from 'node:assert/strict';

import {
  ImagePixelProbe,
  PIXEL_PROBE_LOADING_MESSAGE,
  PIXEL_PROBE_NO_IMAGE_MESSAGE,
  PIXEL_PROBE_OUTSIDE_MESSAGE,
  clampImageRoi,
  createImageRoiFromPoints,
  mapImageRoiToStageRect,
  mapImagePixelToStagePoint,
  mapPointToImagePixel,
  resolvePixelWorldCoordinate,
  rgbToGray,
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/imagePixelProbe.mjs';

function fakeRect(left, top, width, height) {
  return {
    left,
    top,
    width,
    height,
    right: left + width,
    bottom: top + height,
  };
}

function fakeImage({
  naturalWidth = 100,
  naturalHeight = 50,
  rect = fakeRect(10, 20, 200, 100),
  src = 'data:image/png;base64,IMAGE_A',
  complete = true,
} = {}) {
  return {
    naturalWidth,
    naturalHeight,
    currentSrc: src,
    src,
    complete,
    getAttribute(name) {
      return name === 'src' ? src : null;
    },
    getBoundingClientRect() {
      return rect;
    },
  };
}

function createCanvasFactory(drawCalls, pixelFor = () => [10, 20, 30, 255]) {
  return (width, height) => ({
    width,
    height,
    getContext() {
      return {
        drawImage(...args) {
          drawCalls.push(args);
        },
        getImageData(x, y, width = 1, height = 1) {
          const data = [];
          for (let row = 0; row < height; row += 1) {
            for (let col = 0; col < width; col += 1) {
              data.push(...pixelFor(x + col, y + row));
            }
          }
          return {
            data: Uint8ClampedArray.from(data),
          };
        },
      };
    },
  });
}

test('image pixel probe maps the center point to original image coordinates', () => {
  const mapped = mapPointToImagePixel({
    clientX: 110,
    clientY: 70,
    naturalWidth: 100,
    naturalHeight: 50,
    elementRect: fakeRect(10, 20, 200, 100),
  });

  assert.equal(mapped.inside, true);
  assert.equal(mapped.x, 50);
  assert.equal(mapped.y, 25);
  assert.equal(mapped.width, 100);
  assert.equal(mapped.height, 50);
  assert.equal(mapped.scale, 2);
});

test('image pixel probe maps locked image pixels back to the stage coordinate system', () => {
  const image = fakeImage({
    naturalWidth: 100,
    naturalHeight: 50,
    rect: fakeRect(10, 20, 200, 100),
  });
  const stage = {
    scrollLeft: 7,
    scrollTop: 3,
    getBoundingClientRect() {
      return fakeRect(0, 0, 300, 200);
    },
  };

  const point = mapImagePixelToStagePoint({
    x: 50,
    y: 25,
    imageElement: image,
    stageElement: stage,
  });

  assert.equal(point.left, 118);
  assert.equal(point.top, 74);
});

test('image pixel probe samples clipped 3x3 and 5x5 grayscale neighborhoods', () => {
  const probe = new ImagePixelProbe({
    createCanvas: createCanvasFactory([], (x, y) => {
      const value = x + (y * 10);
      return [value, value, value, 255];
    }),
  });
  const image = fakeImage({ naturalWidth: 5, naturalHeight: 5 });

  const clipped = probe.readNeighborhoodStats(image, 0, 0, 3);
  assert.equal(clipped.ok, true);
  assert.deepEqual(clipped.roi, { x: 0, y: 0, width: 2, height: 2 });
  assert.equal(clipped.count, 4);
  assert.equal(clipped.gray.mean, 5.5);
  assert.equal(clipped.gray.min, 0);
  assert.equal(clipped.gray.max, 11);

  const full = probe.readNeighborhoodStats(image, 2, 2, 5);
  assert.equal(full.ok, true);
  assert.deepEqual(full.roi, { x: 0, y: 0, width: 5, height: 5 });
  assert.equal(full.count, 25);
  assert.equal(full.gray.mean, 22);
  assert.equal(full.gray.min, 0);
  assert.equal(full.gray.max, 44);
});

test('image pixel probe converts color neighborhood pixels to grayscale before statistics', () => {
  const colors = new Map([
    ['0,0', [255, 0, 0, 255]],
    ['1,0', [0, 255, 0, 255]],
    ['0,1', [0, 0, 255, 255]],
    ['1,1', [255, 255, 255, 255]],
  ]);
  const probe = new ImagePixelProbe({
    createCanvas: createCanvasFactory([], (x, y) => colors.get(`${x},${y}`) || [0, 0, 0, 255]),
  });

  const stats = probe.readNeighborhoodStats(fakeImage({ naturalWidth: 2, naturalHeight: 2 }), 0, 0, 3);
  const expected = [
    rgbToGray(255, 0, 0),
    rgbToGray(0, 255, 0),
    rgbToGray(0, 0, 255),
    rgbToGray(255, 255, 255),
  ];

  assert.equal(stats.count, 4);
  assert.equal(stats.gray.mean, expected.reduce((sum, value) => sum + value, 0) / expected.length);
  assert.equal(stats.gray.min, Math.min(...expected));
  assert.equal(stats.gray.max, Math.max(...expected));
});

test('image pixel probe reuses the canvas cache for pixel and neighborhood reads', () => {
  const drawCalls = [];
  const probe = new ImagePixelProbe({
    createCanvas: createCanvasFactory(drawCalls, (x, y) => [x, y, 0, 255]),
  });
  const image = fakeImage();

  probe.readPixel(image, 1, 1);
  probe.readNeighborhoodStats(image, 1, 1, 3);
  probe.readNeighborhoodStats(image, 2, 2, 5);
  probe.readRoiStats(image, { x: 0, y: 0, width: 2, height: 2 });

  assert.equal(drawCalls.length, 1);
});

test('image pixel probe clamps ROI coordinates and computes grayscale statistics', () => {
  const probe = new ImagePixelProbe({
    createCanvas: createCanvasFactory([], (x, y) => {
      const value = x + (y * 4);
      return [value, value, value, 255];
    }),
  });

  assert.deepEqual(clampImageRoi({ x: -1, y: -1, width: 3, height: 3 }, 4, 4), {
    x: 0,
    y: 0,
    width: 2,
    height: 2,
  });
  assert.deepEqual(createImageRoiFromPoints({ x: 3, y: 3, width: 4, height: 4 }, { x: 9, y: 9 }, 4, 4), {
    x: 3,
    y: 3,
    width: 1,
    height: 1,
  });

  const result = probe.readRoiStats(fakeImage({ naturalWidth: 4, naturalHeight: 4 }), {
    x: -1,
    y: -1,
    width: 3,
    height: 3,
  });

  assert.equal(result.ok, true);
  assert.deepEqual(result.roi, { x: 0, y: 0, width: 2, height: 2 });
  assert.equal(result.stats.count, 4);
  assert.equal(result.stats.gray.mean, 2.5);
  assert.equal(result.stats.gray.min, 0);
  assert.equal(result.stats.gray.max, 5);
});

test('image pixel probe includes RGB means for color ROI statistics', () => {
  const colors = new Map([
    ['0,0', [100, 0, 0, 255]],
    ['1,0', [0, 50, 0, 255]],
    ['0,1', [0, 0, 25, 255]],
    ['1,1', [100, 50, 25, 255]],
  ]);
  const probe = new ImagePixelProbe({
    createCanvas: createCanvasFactory([], (x, y) => colors.get(`${x},${y}`) || [0, 0, 0, 255]),
  });

  const result = probe.readRoiStats(fakeImage({ naturalWidth: 2, naturalHeight: 2 }), {
    x: 0,
    y: 0,
    width: 2,
    height: 2,
  });

  assert.equal(result.ok, true);
  assert.deepEqual(result.stats.rgbMean, { r: 50, g: 25, b: 12.5 });
});

test('image pixel probe maps ROI rectangles to stage coordinates with scroll offsets', () => {
  const image = fakeImage({
    naturalWidth: 100,
    naturalHeight: 50,
    rect: fakeRect(10, 20, 200, 100),
  });
  const stage = {
    scrollLeft: 5,
    scrollTop: 8,
    getBoundingClientRect() {
      return fakeRect(0, 0, 300, 200);
    },
  };

  const rect = mapImageRoiToStageRect({
    roi: { x: 10, y: 5, width: 20, height: 10 },
    imageElement: image,
    stageElement: stage,
  });

  assert.deepEqual(
    {
      left: rect.left,
      top: rect.top,
      width: rect.width,
      height: rect.height,
    },
    {
      left: 35,
      top: 38,
      width: 40,
      height: 20,
    }
  );
});

test('image pixel probe only computes world coordinates from explicit pixel-to-world mappings', () => {
  const missing = resolvePixelWorldCoordinate({ x: 2, y: 3 }, {
    observation: {
      visualScene: {
        coordinateSpace: 'world.2d.neutral-plane',
        frameId: 'world.2d',
        unit: 'mm',
      },
    },
  });
  assert.equal(missing.kind, 'unavailable');
  assert.match(missing.hint, /coordinateSpace=world\.2d\.neutral-plane/);

  const world = resolvePixelWorldCoordinate({ x: 2, y: 3 }, {
    spatialContext: {
      pixelToWorld: {
        matrix: [2, 0, 10, 0, 3, 20],
        unit: 'mm',
        frameId: 'robot',
      },
    },
  });

  assert.equal(world.kind, 'world');
  assert.equal(world.x, 14);
  assert.equal(world.y, 29);
  assert.equal(world.unit, 'mm');
  assert.equal(world.frameId, 'robot');
});

test('image pixel probe treats fit-mode letterbox space as outside the image', () => {
  const mapped = mapPointToImagePixel({
    clientX: 30,
    clientY: 50,
    naturalWidth: 100,
    naturalHeight: 100,
    elementRect: fakeRect(0, 0, 200, 100),
  });

  assert.equal(mapped.inside, false);
  assert.equal(mapped.reason, 'outside');
});

test('image pixel probe clamps coordinates at the image boundary', () => {
  const mapped = mapPointToImagePixel({
    clientX: 210,
    clientY: 120,
    naturalWidth: 100,
    naturalHeight: 50,
    elementRect: fakeRect(10, 20, 200, 100),
  });

  assert.equal(mapped.inside, true);
  assert.equal(mapped.x, 99);
  assert.equal(mapped.y, 49);
});

test('image pixel probe reads RGB and grayscale values from a cached canvas', () => {
  const drawCalls = [];
  const probe = new ImagePixelProbe({
    createCanvas: createCanvasFactory(drawCalls, (x, y) => [x, y, 30, 255]),
  });
  const image = fakeImage();

  const result = probe.probePoint({ clientX: 110, clientY: 70 }, image);
  assert.equal(result.kind, 'pixel');
  assert.match(result.message, /X: 50  Y: 25/);
  assert.match(result.message, /RGB: 50,25,30/);
  assert.match(result.message, /灰度≈/);
  assert.match(result.message, /图像: 100x50/);
  assert.match(result.message, /缩放: 200%/);
  assert.equal(drawCalls.length, 1);

  const second = probe.probePoint({ clientX: 112, clientY: 72 }, image);
  assert.equal(second.kind, 'pixel');
  assert.equal(drawCalls.length, 1);
});

test('image pixel probe displays grayscale when RGB channels match', () => {
  const probe = new ImagePixelProbe({
    createCanvas: createCanvasFactory([], () => [42, 42, 42, 255]),
  });

  const result = probe.probePoint({ clientX: 110, clientY: 70 }, fakeImage());

  assert.equal(result.kind, 'pixel');
  assert.match(result.message, /灰度: 42/);
  assert.doesNotMatch(result.message, /RGB:/);
});

test('image pixel probe invalidates the canvas cache when the image changes or cache is reset', () => {
  const drawCalls = [];
  const probe = new ImagePixelProbe({
    createCanvas: createCanvasFactory(drawCalls),
  });

  probe.probePoint({ clientX: 110, clientY: 70 }, fakeImage({ src: 'data:image/png;base64,A' }));
  probe.probePoint({ clientX: 110, clientY: 70 }, fakeImage({ src: 'data:image/png;base64,A' }));
  assert.equal(drawCalls.length, 1);

  probe.probePoint({ clientX: 110, clientY: 70 }, fakeImage({ src: 'data:image/png;base64,B' }));
  assert.equal(drawCalls.length, 2);

  probe.reset();
  probe.probePoint({ clientX: 110, clientY: 70 }, fakeImage({ src: 'data:image/png;base64,B' }));
  assert.equal(drawCalls.length, 3);
});

test('image pixel probe returns understandable statuses without a loaded image', () => {
  const probe = new ImagePixelProbe({
    createCanvas: createCanvasFactory([]),
  });

  assert.equal(probe.probePoint({ clientX: 1, clientY: 1 }, null).message, PIXEL_PROBE_NO_IMAGE_MESSAGE);
  assert.equal(
    probe.probePoint({ clientX: 1, clientY: 1 }, fakeImage({ complete: false })).message,
    PIXEL_PROBE_LOADING_MESSAGE
  );
  assert.equal(
    probe.probePoint({ clientX: 1, clientY: 1 }, fakeImage({ rect: fakeRect(10, 10, 100, 100) })).message,
    PIXEL_PROBE_OUTSIDE_MESSAGE
  );
});
